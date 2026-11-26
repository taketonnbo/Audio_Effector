using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Infrastructure.Repository;
using Xunit;

namespace AudioEffector.Infrastructure.Tests.Repository;

/// <summary>
/// JsonFileLockHelperのプロセス間ロック取得、タイムアウト、原子的書き込み、およびメタデータ検知を検証するテストクラス
/// </summary>
public sealed class JsonFileLockHelperTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _targetFilePath;

    public JsonFileLockHelperTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_FileLock", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _targetFilePath = Path.Combine(_tempDirectory, "sample.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // クリーンアップエラーは無視
        }
    }

    /// <summary>
    /// ロックファイルが未保持の状態では、非同期ロック取得と解放が正常に完了することを検証します。
    /// </summary>
    [Fact]
    public async Task AcquireLockAsync_ロック未保持時_正常にロック取得および解放できること()
    {
        // Arrange
        var sut = new JsonFileLockHelper(TimeSpan.FromMilliseconds(500));

        // Act
        using (IDisposable firstLock = await sut.AcquireLockAsync(_targetFilePath))
        {
            Assert.NotNull(firstLock);
            Assert.True(File.Exists(JsonFileLockHelper.GetLockFilePath(_targetFilePath)));
        }

        using IDisposable secondLock = await sut.AcquireLockAsync(_targetFilePath);

        // Assert
        Assert.NotNull(secondLock);
    }

    /// <summary>
    /// 先行ロックが短時間で解放される場合、待機側がリトライによってロックを取得できることを検証します。
    /// </summary>
    [Fact]
    public async Task AcquireLockAsync_別ハンドルがロック保持中に解放された場合_リトライ後に取得できること()
    {
        // Arrange
        var sut = new JsonFileLockHelper(TimeSpan.FromSeconds(2));
        IDisposable firstLock = await sut.AcquireLockAsync(_targetFilePath);

        // Act
        Task releaseTask = Task.Run(async () =>
        {
            await Task.Delay(80);
            firstLock.Dispose();
        });

        using IDisposable secondLock = await sut.AcquireLockAsync(_targetFilePath);
        await releaseTask;

        // Assert
        Assert.NotNull(secondLock);
    }

    /// <summary>
    /// ロックファイルが保持され続けた場合、非同期取得がFileLockTimeoutExceptionを送出することを検証します。
    /// </summary>
    [Fact]
    public async Task AcquireLockAsync_ロックが保持され続けた場合_FileLockTimeoutExceptionが送出されること()
    {
        // Arrange
        var sut = new JsonFileLockHelper(TimeSpan.FromMilliseconds(150));
        using IDisposable heldLock = await sut.AcquireLockAsync(_targetFilePath);

        // Act
        var ex = await Assert.ThrowsAsync<FileLockTimeoutException>(() => sut.AcquireLockAsync(_targetFilePath));

        // Assert
        Assert.Equal(_targetFilePath, ex.FilePath);
        Assert.Equal(TimeSpan.FromMilliseconds(150), ex.Timeout);
    }

    /// <summary>
    /// ロックファイルが保持され続けた場合、同期取得がFileLockTimeoutExceptionを送出することを検証します。
    /// </summary>
    [Fact]
    public void AcquireLock_ロックが保持され続けた場合_FileLockTimeoutExceptionが送出されること()
    {
        // Arrange
        var sut = new JsonFileLockHelper(TimeSpan.FromMilliseconds(150));
        using IDisposable heldLock = sut.AcquireLock(_targetFilePath);

        // Act
        var ex = Assert.Throws<FileLockTimeoutException>(() => sut.AcquireLock(_targetFilePath));

        // Assert
        Assert.Equal(_targetFilePath, ex.FilePath);
        Assert.Equal(TimeSpan.FromMilliseconds(150), ex.Timeout);
    }

    /// <summary>
    /// ロック待機中にキャンセレーショントークンがキャンセルされた場合、OperationCanceledExceptionが送出されることを検証します。
    /// </summary>
    [Fact]
    public async Task AcquireLockAsync_待機中にキャンセルされた場合_OperationCanceledExceptionが送出されること()
    {
        // Arrange
        var sut = new JsonFileLockHelper(TimeSpan.FromSeconds(5));
        using IDisposable heldLock = await sut.AcquireLockAsync(_targetFilePath);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.AcquireLockAsync(_targetFilePath, cancellationToken: cts.Token));
    }

    /// <summary>
    /// 複数タスクから原子的書き込みを並行実行しても一時ファイルが残存せず、有効なファイル内容が保たれることを検証します。
    /// </summary>
    [Fact]
    public async Task WriteAllTextAtomicAsync_並行書き込み時_一時ファイルが残存せず有効なファイルが維持されること()
    {
        // Arrange
        var sut = new JsonFileLockHelper();
        const int writerCount = 12;

        // Act
        Task[] tasks = Enumerable.Range(0, writerCount)
            .Select(async i =>
            {
                using IDisposable fileLock = await sut.AcquireLockAsync(_targetFilePath);
                await sut.WriteAllTextAtomicAsync(_targetFilePath, $"{{\"writer\":{i}}}");
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        Assert.True(File.Exists(_targetFilePath));
        string finalText = await File.ReadAllTextAsync(_targetFilePath);
        Assert.StartsWith("{\"writer\":", finalText);
        string[] leftoverTemps = Directory.GetFiles(_tempDirectory, "*.tmp");
        Assert.Empty(leftoverTemps);
    }

    /// <summary>
    /// ファイルの作成・内容更新・削除に応じてGetFileMetadataのスナップショットが変化することを検証します。
    /// </summary>
    [Fact]
    public async Task GetFileMetadata_ファイル作成と更新と削除_メタデータ変化が正確に検知されること()
    {
        // Arrange
        var sut = new JsonFileLockHelper();

        // Act
        FileMetadataSnapshot beforeCreate = sut.GetFileMetadata(_targetFilePath);

        await sut.WriteAllTextAtomicAsync(_targetFilePath, "{\"v\":1}");
        FileMetadataSnapshot afterCreate = sut.GetFileMetadata(_targetFilePath);

        await sut.WriteAllTextAtomicAsync(_targetFilePath, "{\"v\":123456}");
        FileMetadataSnapshot afterUpdate = sut.GetFileMetadata(_targetFilePath);

        File.Delete(_targetFilePath);
        FileMetadataSnapshot afterDelete = sut.GetFileMetadata(_targetFilePath);

        // Assert
        Assert.False(beforeCreate.Exists);
        Assert.Equal(FileMetadataSnapshot.Missing, beforeCreate);

        Assert.True(afterCreate.Exists);
        Assert.True(afterCreate.Length > 0);
        Assert.NotEqual(beforeCreate, afterCreate);

        Assert.True(afterUpdate.Exists);
        Assert.NotEqual(afterCreate, afterUpdate);

        Assert.False(afterDelete.Exists);
        Assert.Equal(FileMetadataSnapshot.Missing, afterDelete);
    }
}
