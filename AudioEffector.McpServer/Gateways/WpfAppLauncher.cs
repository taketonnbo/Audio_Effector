using System.Diagnostics;
using AudioEffector.McpServer.Configuration;
using Microsoft.Extensions.Logging;

namespace AudioEffector.McpServer.Gateways;

/// <summary>
/// Audio Effector（WPF 本体）実行ファイルの解決・起動・起動待機を担うランチャー実装です。
/// </summary>
public sealed class WpfAppLauncher : IAppLauncher, IDisposable
{
    /// <summary>
    /// 同一ユーザーセッション内で WPF 本体の単一インスタンス制御および起動検知に使用する名前付き Mutex 名。
    /// </summary>
    public const string SINGLE_INSTANCE_MUTEX_NAME = @"Local\AudioEffector.SingleInstance";

    /// <summary>
    /// 既定の WPF 本体実行ファイル名。
    /// </summary>
    public const string DEFAULT_WPF_EXE_NAME = "AudioEffector.exe";

    /// <summary>
    /// ミニプレイヤー起動時に WPF 本体へ渡す CLI 引数。
    /// </summary>
    public const string MINI_PLAYER_ARGUMENT = "--mini-player";

    /// <summary>
    /// ミニプレイヤー起動モードを表す応答文字列。
    /// </summary>
    public const string MODE_MINI_PLAYER = "mini_player";

    /// <summary>
    /// 通常起動モードを表す応答文字列。
    /// </summary>
    public const string MODE_NORMAL = "normal";

    /// <summary>
    /// すでに起動済みだった場合のモード応答文字列。
    /// </summary>
    public const string MODE_EXISTING = "existing";

    /// <summary>
    /// 起動完了待機の既定タイムアウト時間。
    /// </summary>
    public static readonly TimeSpan DefaultReadyTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 起動完了待機の既定ポーリング間隔。
    /// </summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

    private static readonly Action<ILogger, string, string, Exception?> LogLaunchingAppDelegate =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(2001, "WpfAppLaunching"),
            "Audio Effector 本体を起動します（Mode: {Mode}, ExecutablePath: {ExecutablePath}）。");

    private static readonly Action<ILogger, Exception?> LogAlreadyRunningDelegate =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2002, "WpfAppAlreadyRunning"),
            "Audio Effector 本体はすでに起動済みのため、新規起動をスキップします。");

    private static readonly Action<ILogger, string, Exception?> LogExecutableNotFoundDelegate =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2003, "WpfAppExecutableNotFound"),
            "Audio Effector 本体の実行ファイルが見つかりません（Path: {ExecutablePath}）。");

    private readonly McpServerOptions _options;
    private readonly ILogger<WpfAppLauncher> _logger;
    private readonly string _baseDirectory;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<bool> _isAlreadyRunning;
    private readonly Func<ProcessStartInfo, ILaunchedProcess?> _processStarter;
    private readonly Func<CancellationToken, Task<bool>>? _ipcReadinessProbe;
    private readonly TimeSpan _readyTimeout;
    private readonly TimeSpan _pollInterval;
    private readonly SemaphoreSlim _launchLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// <see cref="WpfAppLauncher"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="options">MCP サーバ起動オプション。</param>
    /// <param name="logger">診断ロガー。</param>
    public WpfAppLauncher(McpServerOptions options, ILogger<WpfAppLauncher> logger)
        : this(
            options,
            logger,
            AppContext.BaseDirectory,
            File.Exists,
            IsSingleInstanceMutexActive,
            StartSystemProcess,
            ipcReadinessProbe: null,
            DefaultReadyTimeout,
            DefaultPollInterval)
    {
    }

    /// <summary>
    /// 単体テスト用に外部依存を差し替え可能な内部コンストラクタです。
    /// </summary>
    /// <param name="options">MCP サーバ起動オプション。</param>
    /// <param name="logger">診断ロガー。</param>
    /// <param name="baseDirectory">既定の実行ファイル検索ベースディレクトリ。</param>
    /// <param name="fileExists">実行ファイル存在確認デリゲート。</param>
    /// <param name="isAlreadyRunning">起動済み判定デリゲート。</param>
    /// <param name="processStarter">プロセス起動デリゲート。</param>
    /// <param name="ipcReadinessProbe">IPC 準備完了確認デリゲート（任意）。</param>
    /// <param name="readyTimeout">起動待機タイムアウト時間（任意）。</param>
    /// <param name="pollInterval">起動待機ポーリング間隔（任意）。</param>
    internal WpfAppLauncher(
        McpServerOptions options,
        ILogger<WpfAppLauncher> logger,
        string baseDirectory,
        Func<string, bool> fileExists,
        Func<bool> isAlreadyRunning,
        Func<ProcessStartInfo, ILaunchedProcess?> processStarter,
        Func<CancellationToken, Task<bool>>? ipcReadinessProbe = null,
        TimeSpan? readyTimeout = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(baseDirectory);
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(isAlreadyRunning);
        ArgumentNullException.ThrowIfNull(processStarter);

        _options = options;
        _logger = logger;
        _baseDirectory = baseDirectory;
        _fileExists = fileExists;
        _isAlreadyRunning = isAlreadyRunning;
        _processStarter = processStarter;
        _ipcReadinessProbe = ipcReadinessProbe;
        _readyTimeout = readyTimeout ?? DefaultReadyTimeout;
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    /// <summary>
    /// 実行対象の <c>AudioEffector.exe</c> パスを解決します。
    /// </summary>
    /// <returns>解決された実行ファイルパス。</returns>
    public string ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.WpfAppPath))
        {
            return Path.GetFullPath(_options.WpfAppPath.Trim());
        }

        return Path.Combine(_baseDirectory, DEFAULT_WPF_EXE_NAME);
    }

    /// <inheritdoc />
    public async Task<AppLaunchResult> LaunchAsync(
        AppLaunchMode mode,
        bool waitForReady,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _launchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isAlreadyRunning())
            {
                LogAlreadyRunningDelegate(_logger, null);
                bool ipcReady = await CheckIpcReadyAsync(cancellationToken).ConfigureAwait(false);
                return new AppLaunchResult(
                    Launched: false,
                    AlreadyRunning: true,
                    Mode: MODE_EXISTING,
                    IpcReady: ipcReady);
            }

            string executablePath = ResolveExecutablePath();
            if (!_fileExists(executablePath))
            {
                LogExecutableNotFoundDelegate(_logger, executablePath, null);
                throw new FileNotFoundException("Audio Effector 本体の実行ファイルが見つかりません。", executablePath);
            }

            string modeLabel = mode == AppLaunchMode.MiniPlayer ? MODE_MINI_PLAYER : MODE_NORMAL;
            LogLaunchingAppDelegate(_logger, modeLabel, executablePath, null);

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? _baseDirectory,
                UseShellExecute = false,
            };

            if (mode == AppLaunchMode.MiniPlayer)
            {
                startInfo.ArgumentList.Add(MINI_PLAYER_ARGUMENT);
            }

            // 起動したプロセスは独立プロセスとして継続動作させる（Process ラッパーの Dispose ではプロセスを終了しない）
            using ILaunchedProcess? process = _processStarter(startInfo);
            if (process is null)
            {
                throw new InvalidOperationException("Audio Effector 本体のプロセス起動に失敗しました。");
            }

            if (!waitForReady)
            {
                bool immediateIpcReady = await CheckIpcReadyAsync(cancellationToken).ConfigureAwait(false);
                return new AppLaunchResult(
                    Launched: true,
                    AlreadyRunning: false,
                    Mode: modeLabel,
                    IpcReady: immediateIpcReady);
            }

            bool readyIpc = await WaitUntilReadyAsync(process, cancellationToken).ConfigureAwait(false);
            return new AppLaunchResult(
                Launched: true,
                AlreadyRunning: false,
                Mode: modeLabel,
                IpcReady: readyIpc);
        }
        finally
        {
            _launchLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _launchLock.Dispose();
    }

    private async Task<bool> WaitUntilReadyAsync(ILaunchedProcess process, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                throw new InvalidOperationException($"Audio Effector 本体プロセスが準備完了前に終了しました（ExitCode: {process.ExitCode}）。");
            }

            if (_ipcReadinessProbe is not null)
            {
                if (await _ipcReadinessProbe(cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }
            }
            else if (_isAlreadyRunning())
            {
                return false;
            }

            TimeSpan remaining = _readyTimeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException($"Audio Effector 本体の起動待機がタイムアウトしました（TimeoutMs: {_readyTimeout.TotalMilliseconds:F0}）。");
            }

            TimeSpan delay = remaining < _pollInterval ? remaining : _pollInterval;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> CheckIpcReadyAsync(CancellationToken cancellationToken)
    {
        if (_ipcReadinessProbe is null)
        {
            return false;
        }

        return await _ipcReadinessProbe(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSingleInstanceMutexActive()
    {
        if (Mutex.TryOpenExisting(SINGLE_INSTANCE_MUTEX_NAME, out Mutex? existing))
        {
            existing.Dispose();
            return true;
        }

        return false;
    }

    private static ILaunchedProcess? StartSystemProcess(ProcessStartInfo startInfo)
    {
        Process? process = Process.Start(startInfo);
        return process is null ? null : new SystemLaunchedProcess(process);
    }

    /// <summary>
    /// 起動済みプロセス状態のテスト容易化用ラッパーインターフェースです。
    /// </summary>
    internal interface ILaunchedProcess : IDisposable
    {
        /// <summary>
        /// プロセスが終了しているかどうかを取得します。
        /// </summary>
        bool HasExited { get; }

        /// <summary>
        /// プロセスの終了コードを取得します。
        /// </summary>
        int ExitCode { get; }
    }

    private sealed class SystemLaunchedProcess(Process process) : ILaunchedProcess
    {
        public bool HasExited => process.HasExited;

        public int ExitCode => process.ExitCode;

        public void Dispose() => process.Dispose();
    }
}
