using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;

namespace AudioEffector.Infrastructure.Repository;

/// <summary>
/// OS ファイルロック（.lock）、一意一時ファイルによる原子的書き込み、および更新メタデータ比較を提供する共通ヘルパー
/// </summary>
public sealed class JsonFileLockHelper : IJsonFileLockHelper
{
    /// <summary>
    /// ロック取得待機の既定タイムアウト時間（5000ms）
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(5000);

    private const int MinRetryDelayMs = 10;
    private const int MaxRetryDelayMs = 35;
    private const int MaxMoveRetryCount = 5;

    private readonly TimeSpan _defaultTimeout;

    /// <summary>
    /// 既定タイムアウト（5000ms）を使用する共有インスタンスを取得します
    /// </summary>
    public static JsonFileLockHelper Default { get; } = new();

    /// <summary>
    /// 指定された既定タイムアウトで <see cref="JsonFileLockHelper"/> を初期化します
    /// </summary>
    /// <param name="defaultTimeout">ロック取得の既定タイムアウト時間（未指定時は 5000ms）</param>
    public JsonFileLockHelper(TimeSpan? defaultTimeout = null)
    {
        TimeSpan resolved = defaultTimeout ?? DefaultTimeout;
        if (resolved <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultTimeout), "タイムアウト時間は正の値を指定してください。");
        }

        _defaultTimeout = resolved;
    }

    /// <summary>
    /// 指定ファイルパスに対応するロックファイルパス（{filePath}.lock）を返します
    /// </summary>
    /// <param name="filePath">保護対象のデータファイルパス</param>
    /// <returns>ロックファイルパス</returns>
    public static string GetLockFilePath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return $"{filePath}.lock";
    }

    /// <inheritdoc />
    public async Task<IDisposable> AcquireLockAsync(
        string filePath,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        TimeSpan effectiveTimeout = ResolveTimeout(timeout);
        string lockPath = GetLockFilePath(filePath);
        EnsureParentDirectoryExists(lockPath);

        var stopwatch = Stopwatch.StartNew();
        Exception? lastException = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryOpenLockStream(lockPath, out var stream, out var ioEx))
            {
                return new FileLockHandle(stream!);
            }

            lastException = ioEx;
            TimeSpan remaining = effectiveTimeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                throw new FileLockTimeoutException(filePath, effectiveTimeout, lastException);
            }

            int jitterMs = Random.Shared.Next(MinRetryDelayMs, MaxRetryDelayMs);
            TimeSpan delay = remaining < TimeSpan.FromMilliseconds(jitterMs)
                ? remaining
                : TimeSpan.FromMilliseconds(jitterMs);

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public IDisposable AcquireLock(string filePath, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        TimeSpan effectiveTimeout = ResolveTimeout(timeout);
        string lockPath = GetLockFilePath(filePath);
        EnsureParentDirectoryExists(lockPath);

        var stopwatch = Stopwatch.StartNew();
        Exception? lastException = null;

        while (true)
        {
            if (TryOpenLockStream(lockPath, out var stream, out var ioEx))
            {
                return new FileLockHandle(stream!);
            }

            lastException = ioEx;
            TimeSpan remaining = effectiveTimeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                throw new FileLockTimeoutException(filePath, effectiveTimeout, lastException);
            }

            int jitterMs = Random.Shared.Next(MinRetryDelayMs, MaxRetryDelayMs);
            int sleepMs = (int)Math.Min(remaining.TotalMilliseconds, jitterMs);
            Thread.Sleep(Math.Max(1, sleepMs));
        }
    }

    /// <inheritdoc />
    public async Task WriteAllTextAtomicAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(content);

        EnsureParentDirectoryExists(filePath);
        string tempPath = CreateUniqueTempFilePath(filePath);

        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
            await MoveFileWithRetryAsync(tempPath, filePath, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    /// <inheritdoc />
    public void WriteAllTextAtomic(string filePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(content);

        EnsureParentDirectoryExists(filePath);
        string tempPath = CreateUniqueTempFilePath(filePath);

        try
        {
            File.WriteAllText(tempPath, content);
            MoveFileWithRetry(tempPath, filePath);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    /// <inheritdoc />
    public FileMetadataSnapshot GetFileMetadata(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var info = new FileInfo(filePath);
        if (!info.Exists)
        {
            return FileMetadataSnapshot.Missing;
        }

        return new FileMetadataSnapshot(true, info.LastWriteTimeUtc, info.Length);
    }

    private TimeSpan ResolveTimeout(TimeSpan? timeout)
    {
        TimeSpan effectiveTimeout = timeout ?? _defaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "タイムアウト時間は正の値を指定してください。");
        }

        return effectiveTimeout;
    }

    private static string CreateUniqueTempFilePath(string filePath)
    {
        return $"{filePath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
    }

    private static void EnsureParentDirectoryExists(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static bool TryOpenLockStream(string lockPath, out FileStream? stream, out IOException? ioException)
    {
        try
        {
            stream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
            ioException = null;
            return true;
        }
        catch (IOException ex) when (ex is not DirectoryNotFoundException and not PathTooLongException and not FileNotFoundException)
        {
            stream = null;
            ioException = ex;
            return false;
        }
    }

    private static async Task MoveFileWithRetryAsync(string tempPath, string targetPath, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(tempPath, targetPath, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < MaxMoveRetryCount && IsTransientMoveException(ex))
            {
                await Task.Delay(Random.Shared.Next(MinRetryDelayMs, MaxRetryDelayMs), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void MoveFileWithRetry(string tempPath, string targetPath)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(tempPath, targetPath, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < MaxMoveRetryCount && IsTransientMoveException(ex))
            {
                Thread.Sleep(Random.Shared.Next(MinRetryDelayMs, MaxRetryDelayMs));
            }
        }
    }

    private static bool IsTransientMoveException(Exception ex)
    {
        return ex is IOException and not DirectoryNotFoundException and not PathTooLongException
            || ex is UnauthorizedAccessException;
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // 一時ファイル削除失敗時は元の例外を優先する
        }
    }

    private sealed class FileLockHandle : IDisposable
    {
        private FileStream? _stream;

        public FileLockHandle(FileStream stream)
        {
            _stream = stream;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _stream, null)?.Dispose();
        }
    }
}
