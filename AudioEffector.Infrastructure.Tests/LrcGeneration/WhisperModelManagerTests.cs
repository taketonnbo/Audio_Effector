using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
