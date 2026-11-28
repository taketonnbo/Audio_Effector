using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.LrcGeneration;

namespace AudioEffector.Infrastructure.Tests.LrcGeneration;

public sealed class WhisperModelManagerTests : IDisposable
{
    private readonly string _tempDir;

    public WhisperModelManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffector_ModelMgrTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    /// <summary>
    /// モデルダウンロード成功時にファイルが配置され一時ファイルが残らないこと、およびカスタムモデル指定が機能することを検証します。
    /// </summary>
    [Fact]
    public async Task DownloadAndCustomModel_正常ストリームとカスタムファイル_状態が正しく遷移する()
    {
        // Arrange
        byte[] dummyModelBytes = new byte[1024];
        new Random(42).NextBytes(dummyModelBytes);
        var sut = new WhisperModelManager(
            _tempDir,
            downloaderFunc: (_, _) => Task.FromResult<Stream>(new MemoryStream(dummyModelBytes)));

        var reportedProgress = new List<double>();
        var progress = new SynchronousProgress<double>(p => reportedProgress.Add(p));

        // Act
        LocalSpeechModelInfo beforeDownload = sut.GetActiveModelInfo();
        LocalSpeechModelInfo afterDownload = await sut.DownloadModelAsync("base", progress);

        string customPath = Path.Combine(_tempDir, "custom.bin");
        await File.WriteAllBytesAsync(customPath, [1, 2, 3, 4]);
        sut.SetCustomModelPath(customPath);
        LocalSpeechModelInfo customInfo = sut.GetActiveModelInfo();

        // Assert
        Assert.False(beforeDownload.IsAvailable);
        Assert.True(afterDownload.IsAvailable);
        Assert.True(File.Exists(afterDownload.FilePath));
        Assert.Empty(Directory.GetFiles(_tempDir, "*.download"));
        Assert.Contains(1.0, reportedProgress);
        Assert.Equal("custom", customInfo.ModelId);
        Assert.True(customInfo.IsAvailable);
    }

    /// <summary>
    /// ダウンロードストリームが 0 バイトの場合に InvalidDataException を送出し壊れたファイルや一時ファイルを残さないことを検証します。
    /// </summary>
    [Fact]
    public async Task DownloadModelAsync_0バイトストリーム_InvalidDataExceptionを送出しファイルを残さない()
    {
        // Arrange
        var sut = new WhisperModelManager(
            _tempDir,
            downloaderFunc: (_, _) => Task.FromResult<Stream>(new MemoryStream(Array.Empty<byte>())));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() => sut.DownloadModelAsync("base"));
        Assert.False(sut.GetActiveModelInfo().IsAvailable);
        Assert.Empty(Directory.GetFiles(_tempDir, "*.download"));
        Assert.Empty(Directory.GetFiles(_tempDir, "*.bin"));
    }

    /// <summary>
    /// 0 バイトの壊れたモデルファイルが存在する場合は利用不可と判定され、カスタムモデル指定でも拒否されることを検証します。
    /// </summary>
    [Fact]
    public async Task GetActiveModelInfoAndSetCustomModelPath_0バイトファイル_利用不可と判定し指定を拒否する()
    {
        // Arrange
        string brokenDefault = Path.Combine(_tempDir, "ggml-base.bin");
        string brokenCustom = Path.Combine(_tempDir, "empty-custom.bin");
        await File.WriteAllBytesAsync(brokenDefault, Array.Empty<byte>());
        await File.WriteAllBytesAsync(brokenCustom, Array.Empty<byte>());
        var sut = new WhisperModelManager(_tempDir);

        // Act
        LocalSpeechModelInfo activeInfo = sut.GetActiveModelInfo();

        // Assert
        Assert.False(activeInfo.IsAvailable);
        Assert.Throws<InvalidDataException>(() => sut.SetCustomModelPath(brokenCustom));
    }

    /// <summary>
    /// モデルダウンロード時のネットワーク障害・タイムアウト・ディスク容量不足・権限不足が
    /// 日本語メッセージの LrcGenerationException へラップされ一時ファイルを残さないことを検証します。
    /// </summary>
    [Theory]
    [InlineData("Network", LrcGenerationErrorMessageResolver.MODEL_DOWNLOAD_NETWORK_ERROR_MESSAGE)]
    [InlineData("Timeout", LrcGenerationErrorMessageResolver.MODEL_DOWNLOAD_TIMEOUT_MESSAGE)]
    [InlineData("DiskFull", LrcGenerationErrorMessageResolver.MODEL_DOWNLOAD_DISK_FULL_MESSAGE)]
    [InlineData("AccessDenied", LrcGenerationErrorMessageResolver.MODEL_DOWNLOAD_ACCESS_DENIED_MESSAGE)]
    public async Task DownloadModelAsync_各種ダウンロード障害_日本語LrcGenerationExceptionへラップする(string scenario, string expectedMessage)
    {
        // Arrange
        Exception injected = scenario switch
        {
            "Network" => new HttpRequestException("The remote name could not be resolved."),
            "Timeout" => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."),
            "DiskFull" => new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)),
            _ => new UnauthorizedAccessException("Access to the path is denied."),
        };

        var sut = new WhisperModelManager(
            _tempDir,
            downloaderFunc: (_, _) => Task.FromException<Stream>(injected));

        // Act
        LrcGenerationException ex = await Assert.ThrowsAsync<LrcGenerationException>(() => sut.DownloadModelAsync("base"));

        // Assert
        Assert.Equal(LrcGenerationErrorCategory.ModelDownloadFailed, ex.Category);
        Assert.Equal(expectedMessage, ex.Message);
        Assert.Same(injected, ex.InnerException);
        Assert.True(ex.IsLogged);
        Assert.Empty(Directory.GetFiles(_tempDir, "*.download"));
    }

    /// <summary>
    /// モデルダウンロード中にユーザーキャンセルが要求された場合はラップせず OperationCanceledException を再送出することを検証します。
    /// </summary>
    [Fact]
    public async Task DownloadModelAsync_ユーザーキャンセル要求時_OperationCanceledExceptionをそのまま伝播する()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var sut = new WhisperModelManager(
            _tempDir,
            downloaderFunc: (_, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<Stream>(new MemoryStream());
            });

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.DownloadModelAsync("base", cancellationToken: cts.Token));
        Assert.Empty(Directory.GetFiles(_tempDir, "*.download"));
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
