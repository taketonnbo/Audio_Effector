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
    /// 通常ウィンドウ起動時に WPF 本体へ渡す CLI 引数（StartMinimized 設定を抑止して通常表示する）。
    /// </summary>
    public const string NORMAL_ARGUMENT = "--normal";

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
            "Audio Effector 本体はすでに起動済み（または起動進行中）のため、新規起動をスキップします。");

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
    private readonly Func<CancellationToken, Task<bool>> _ipcReadinessProbe;
    private readonly IAppLaunchCoordinator _launchCoordinator;
    private readonly TimeSpan _readyTimeout;
    private readonly TimeSpan _pollInterval;
    private readonly SemaphoreSlim _launchLock = new(1, 1);
    private ILaunchedProcess? _trackedProcess;
    private bool _disposed;

    /// <summary>
    /// <see cref="WpfAppLauncher"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="options">MCP サーバ起動オプション。</param>
    /// <param name="ipcReadinessProbe">WPF 本体の IPC 待受準備完了を確認するプローブ。</param>
    /// <param name="launchCoordinator">セッション内の複数 MCP サーバプロセス間で起動を調停するコーディネータ。</param>
    /// <param name="logger">診断ロガー。</param>
    public WpfAppLauncher(
        McpServerOptions options,
        IIpcReadinessProbe ipcReadinessProbe,
        IAppLaunchCoordinator launchCoordinator,
        ILogger<WpfAppLauncher> logger)
        : this(
            options,
            logger,
            AppContext.BaseDirectory,
            File.Exists,
            IsSingleInstanceMutexActive,
            StartSystemProcess,
            (ipcReadinessProbe ?? throw new ArgumentNullException(nameof(ipcReadinessProbe))).IsReadyAsync,
            launchCoordinator,
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
    /// <param name="ipcReadinessProbe">IPC 準備完了確認デリゲート。</param>
    /// <param name="launchCoordinator">セッション内プロセス間起動調停コーディネータ（任意）。</param>
    /// <param name="readyTimeout">起動待機タイムアウト時間（任意）。</param>
    /// <param name="pollInterval">起動待機ポーリング間隔（任意）。</param>
    internal WpfAppLauncher(
        McpServerOptions options,
        ILogger<WpfAppLauncher> logger,
        string baseDirectory,
        Func<string, bool> fileExists,
        Func<bool> isAlreadyRunning,
        Func<ProcessStartInfo, ILaunchedProcess?> processStarter,
        Func<CancellationToken, Task<bool>> ipcReadinessProbe,
        IAppLaunchCoordinator? launchCoordinator = null,
        TimeSpan? readyTimeout = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(baseDirectory);
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(isAlreadyRunning);
        ArgumentNullException.ThrowIfNull(processStarter);
        ArgumentNullException.ThrowIfNull(ipcReadinessProbe);

        _options = options;
        _logger = logger;
        _baseDirectory = baseDirectory;
        _fileExists = fileExists;
        _isAlreadyRunning = isAlreadyRunning;
        _processStarter = processStarter;
        _ipcReadinessProbe = ipcReadinessProbe;
        _launchCoordinator = launchCoordinator ?? new InProcessAppLaunchCoordinator();
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

        bool launchedByThisCall = false;
        string modeLabel = mode == AppLaunchMode.MiniPlayer ? MODE_MINI_PLAYER : MODE_NORMAL;
        ILaunchedProcess? localTrackedProcess = null;
        LaunchedProcessIdentity? sharedTrackedIdentity = null;

        // 起動判定とプロセス起動・状態記録のみをプロセス内＋セッション内ロックで直列化し、
        // IPC 待機はロック解放後に実施することで後続要求を先行起動へ即座に合流させる
        await _launchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using IDisposable sessionLock = await _launchCoordinator
                .AcquireLaunchLockAsync(cancellationToken)
                .ConfigureAwait(false);

            bool localTrackedAlive = IsTrackedProcessAlive();
            LaunchedProcessIdentity? activeSharedLaunch = await _launchCoordinator
                .TryGetActiveLaunchAsync(cancellationToken)
                .ConfigureAwait(false);

            if (_isAlreadyRunning() || localTrackedAlive || activeSharedLaunch is not null)
            {
                LogAlreadyRunningDelegate(_logger, null);
                localTrackedProcess = localTrackedAlive ? _trackedProcess : null;
                sharedTrackedIdentity = activeSharedLaunch;
            }
            else
            {
                string executablePath = ResolveExecutablePath();
                if (!_fileExists(executablePath))
                {
                    LogExecutableNotFoundDelegate(_logger, executablePath, null);
                    throw new FileNotFoundException("Audio Effector 本体の実行ファイルが見つかりません。", executablePath);
                }

                LogLaunchingAppDelegate(_logger, modeLabel, executablePath, null);

                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    WorkingDirectory = Path.GetDirectoryName(executablePath) ?? _baseDirectory,
                    UseShellExecute = false,
                };

                startInfo.ArgumentList.Add(mode == AppLaunchMode.MiniPlayer ? MINI_PLAYER_ARGUMENT : NORMAL_ARGUMENT);

                // 起動したプロセスは独立プロセスとして継続動作させ、Mutex 確立前の後続呼び出しを合流できるよう追跡する
                ILaunchedProcess? process = _processStarter(startInfo);
                if (process is null)
                {
                    throw new InvalidOperationException("Audio Effector 本体のプロセス起動に失敗しました。");
                }

                ReplaceTrackedProcess(process);
                localTrackedProcess = process;
                sharedTrackedIdentity = process.Identity;
                await _launchCoordinator
                    .RecordLaunchedProcessAsync(process.Identity, cancellationToken)
                    .ConfigureAwait(false);
                launchedByThisCall = true;
            }
        }
        finally
        {
            _launchLock.Release();
        }

        if (!waitForReady)
        {
            bool immediateIpcReady = await _ipcReadinessProbe(cancellationToken).ConfigureAwait(false);
            return new AppLaunchResult(
                Launched: launchedByThisCall,
                AlreadyRunning: !launchedByThisCall,
                Mode: launchedByThisCall ? modeLabel : MODE_EXISTING,
                IpcReady: immediateIpcReady);
        }

        bool readyIpc = await WaitUntilIpcReadyAsync(
            localTrackedProcess,
            sharedTrackedIdentity,
            cancellationToken).ConfigureAwait(false);

        return new AppLaunchResult(
            Launched: launchedByThisCall,
            AlreadyRunning: !launchedByThisCall,
            Mode: launchedByThisCall ? modeLabel : MODE_EXISTING,
            IpcReady: readyIpc);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReplaceTrackedProcess(null);
        _launchLock.Dispose();
    }

    private bool IsTrackedProcessAlive()
    {
        if (_trackedProcess is null)
        {
            return false;
        }

        if (_trackedProcess.HasExited)
        {
            ReplaceTrackedProcess(null);
            return false;
        }

        return true;
    }

    private void ReplaceTrackedProcess(ILaunchedProcess? nextProcess)
    {
        if (ReferenceEquals(_trackedProcess, nextProcess))
        {
            return;
        }

        _trackedProcess?.Dispose();
        _trackedProcess = nextProcess;
    }

    private async Task<bool> WaitUntilIpcReadyAsync(
        ILaunchedProcess? localTrackedProcess,
        LaunchedProcessIdentity? sharedTrackedIdentity,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _ipcReadinessProbe(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (localTrackedProcess is not null && localTrackedProcess.HasExited)
            {
                int exitCode = localTrackedProcess.ExitCode;
                await ClearExitedTrackedProcessAsync(localTrackedProcess, sharedTrackedIdentity, cancellationToken)
                    .ConfigureAwait(false);

                // 別経路で起動済みの既存 WPF が稼働している場合はエラーとせず IPC 待受を継続する
                if (!_isAlreadyRunning())
                {
                    throw new InvalidOperationException($"Audio Effector 本体プロセスが準備完了前に終了しました（ExitCode: {exitCode}）。");
                }

                localTrackedProcess = null;
                sharedTrackedIdentity = null;
            }
            else if (localTrackedProcess is null
                && sharedTrackedIdentity is not null
                && !_launchCoordinator.IsProcessAlive(sharedTrackedIdentity))
            {
                await _launchCoordinator
                    .ClearLaunchedProcessAsync(sharedTrackedIdentity, cancellationToken)
                    .ConfigureAwait(false);

                if (!_isAlreadyRunning())
                {
                    throw new InvalidOperationException("先行起動された Audio Effector 本体プロセスが準備完了前に終了しました。");
                }

                sharedTrackedIdentity = null;
            }

            TimeSpan remaining = _readyTimeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException($"Audio Effector 本体の IPC 接続準備待機がタイムアウトしました（TimeoutMs: {_readyTimeout.TotalMilliseconds:F0}）。");
            }

            TimeSpan delay = remaining < _pollInterval ? remaining : _pollInterval;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ClearExitedTrackedProcessAsync(
        ILaunchedProcess exitedProcess,
        LaunchedProcessIdentity? sharedTrackedIdentity,
        CancellationToken cancellationToken)
    {
        await _launchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(_trackedProcess, exitedProcess))
            {
                ReplaceTrackedProcess(null);
            }
        }
        finally
        {
            _launchLock.Release();
        }

        if (sharedTrackedIdentity is not null)
        {
            await _launchCoordinator
                .ClearLaunchedProcessAsync(sharedTrackedIdentity, cancellationToken)
                .ConfigureAwait(false);
        }
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
        /// プロセスを一意に識別する情報を取得します。
        /// </summary>
        LaunchedProcessIdentity Identity { get; }

        /// <summary>
        /// プロセスが終了しているかどうかを取得します。
        /// </summary>
        bool HasExited { get; }

        /// <summary>
        /// プロセスの終了コードを取得します。
        /// </summary>
        int ExitCode { get; }
    }

    private sealed class SystemLaunchedProcess : ILaunchedProcess
    {
        private readonly Process _process;

        public SystemLaunchedProcess(Process process)
        {
            _process = process;
            long startTicks = 0;
            try
            {
                startTicks = process.StartTime.ToUniversalTime().Ticks;
            }
            catch (Exception)
            {
                // 起動直後に終了した場合等は 0 を許容する
            }

            Identity = new LaunchedProcessIdentity(process.Id, startTicks);
        }

        public LaunchedProcessIdentity Identity { get; }

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.ExitCode;

        public void Dispose() => _process.Dispose();
    }

    private sealed class InProcessAppLaunchCoordinator : IAppLaunchCoordinator
    {
        public Task<IDisposable> AcquireLaunchLockAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IDisposable>(NoOpDisposable.Instance);

        public Task<LaunchedProcessIdentity?> TryGetActiveLaunchAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<LaunchedProcessIdentity?>(null);

        public Task RecordLaunchedProcessAsync(LaunchedProcessIdentity processIdentity, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ClearLaunchedProcessAsync(LaunchedProcessIdentity? expectedProcess = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public bool IsProcessAlive(LaunchedProcessIdentity processIdentity) => false;

        private sealed class NoOpDisposable : IDisposable
        {
            public static readonly NoOpDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
