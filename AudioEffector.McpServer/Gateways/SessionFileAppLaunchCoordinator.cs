using System.Diagnostics;
using System.Text.Json;
using AudioEffector.Infrastructure.Ipc;

namespace AudioEffector.McpServer.Gateways;

/// <summary>
/// Windows セッション単位の OS ファイルロックと共有状態ファイルにより、複数 MCP サーバプロセス間の WPF 起動を調停する実装です。
/// </summary>
public sealed class SessionFileAppLaunchCoordinator : IAppLaunchCoordinator
{
    /// <summary>
    /// 起動調停ロック取得の既定タイムアウト時間。
    /// </summary>
    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan LockRetryInterval = TimeSpan.FromMilliseconds(25);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _lockFilePath;
    private readonly string _stateFilePath;
    private readonly Func<LaunchedProcessIdentity, bool> _isProcessAlive;
    private readonly TimeSpan _lockTimeout;

    /// <summary>
    /// 現在のユーザーおよび Windows セッションに対応する既定パス（<c>LocalApplicationData\AudioEffector\ipc</c>）で <see cref="SessionFileAppLaunchCoordinator"/> を初期化します。
    /// </summary>
    public SessionFileAppLaunchCoordinator()
        : this(
            GetDefaultCoordinatorDirectory(),
            IpcPipeNameResolver.GetDefaultPipeName(),
            IsSystemProcessAlive,
            DefaultLockTimeout)
    {
    }

    /// <summary>
    /// クライアント固有の <c>TMP</c>/<c>TEMP</c> 環境変数に依存しない、ユーザー共通の起動調停ファイル配置ディレクトリを返します。
    /// </summary>
    /// <returns>既定の調停ファイル配置ディレクトリパス。</returns>
    internal static string GetDefaultCoordinatorDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "AudioEffector", "ipc");
        }

        return Path.Combine(Path.GetTempPath(), "AudioEffector", "ipc");
    }

    /// <summary>
    /// テスト用に保存先ディレクトリ・スコープキー・プロセス生存確認デリゲートを差し替え可能な内部コンストラクタです。
    /// </summary>
    /// <param name="directory">ロックファイルおよび状態ファイルの配置ディレクトリ。</param>
    /// <param name="scopeKey">セッション単位の一意キー。</param>
    /// <param name="isProcessAlive">プロセス生存判定デリゲート。</param>
    /// <param name="lockTimeout">ロック取得タイムアウト時間（任意）。</param>
    internal SessionFileAppLaunchCoordinator(
        string directory,
        string scopeKey,
        Func<LaunchedProcessIdentity, bool> isProcessAlive,
        TimeSpan? lockTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        ArgumentNullException.ThrowIfNull(isProcessAlive);

        Directory.CreateDirectory(directory);
        _lockFilePath = Path.Combine(directory, $"{scopeKey}.launch.lock");
        _stateFilePath = Path.Combine(directory, $"{scopeKey}.launch.state");
        _isProcessAlive = isProcessAlive;
        _lockTimeout = lockTimeout ?? DefaultLockTimeout;
    }

    /// <inheritdoc />
    public async Task<IDisposable> AcquireLaunchLockAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var stream = new FileStream(
                    _lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
                return stream;
            }
            catch (IOException)
            {
                TimeSpan remaining = _lockTimeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException($"起動調停ロックの取得がタイムアウトしました（TimeoutMs: {_lockTimeout.TotalMilliseconds:F0}）。");
                }

                TimeSpan delay = remaining < LockRetryInterval ? remaining : LockRetryInterval;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task<LaunchedProcessIdentity?> TryGetActiveLaunchAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_stateFilePath))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(_stateFilePath, cancellationToken).ConfigureAwait(false);
            LaunchedProcessIdentity? recorded = JsonSerializer.Deserialize<LaunchedProcessIdentity>(json, JsonOptions);
            if (recorded is null)
            {
                TryDeleteStateFile();
                return null;
            }

            if (!_isProcessAlive(recorded))
            {
                TryDeleteStateFile();
                return null;
            }

            return recorded;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task RecordLaunchedProcessAsync(
        LaunchedProcessIdentity processIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processIdentity);

        string json = JsonSerializer.Serialize(processIdentity, JsonOptions);
        string tempPath = $"{_stateFilePath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, _stateFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // 一時ファイル削除失敗は無視する
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task ClearLaunchedProcessAsync(
        LaunchedProcessIdentity? expectedProcess = null,
        CancellationToken cancellationToken = default)
    {
        using IDisposable sessionLock = await AcquireLaunchLockAsync(cancellationToken).ConfigureAwait(false);

        if (!File.Exists(_stateFilePath))
        {
            return;
        }

        if (expectedProcess is not null)
        {
            try
            {
                string json = await File.ReadAllTextAsync(_stateFilePath, cancellationToken).ConfigureAwait(false);
                LaunchedProcessIdentity? recorded = JsonSerializer.Deserialize<LaunchedProcessIdentity>(json, JsonOptions);
                if (recorded != expectedProcess)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return;
            }
        }

        TryDeleteStateFile();
    }

    /// <inheritdoc />
    public bool IsProcessAlive(LaunchedProcessIdentity processIdentity)
    {
        ArgumentNullException.ThrowIfNull(processIdentity);
        return _isProcessAlive(processIdentity);
    }

    private void TryDeleteStateFile()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                File.Delete(_stateFilePath);
            }
        }
        catch (IOException)
        {
            // 削除競合時は次回参照時に上書きまたはクリアされるため無視する
        }
    }

    private static bool IsSystemProcessAlive(LaunchedProcessIdentity identity)
    {
        try
        {
            using Process process = Process.GetProcessById(identity.ProcessId);
            if (process.HasExited)
            {
                return false;
            }

            if (identity.StartTimeUtcTicks > 0)
            {
                long actualStartTicks = process.StartTime.ToUniversalTime().Ticks;
                if (Math.Abs(actualStartTicks - identity.StartTimeUtcTicks) > TimeSpan.FromSeconds(1).Ticks)
                {
                    return false;
                }
            }

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 別権限等で詳細参照できない場合も PID 自体は存在する可能性があるため、安全側で false ではなく確認不能時は false とする
            return false;
        }
    }
}
