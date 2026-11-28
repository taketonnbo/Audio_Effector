using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.LrcGeneration;
using AudioEffector.Infrastructure.Lyrics;

namespace AudioEffector.Infrastructure.Tests.LrcGeneration;

public sealed class WhisperLocalAlignmentEngineTests : IDisposable
{
    private readonly string _tempDir;

    public WhisperLocalAlignmentEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"AudioEffector_WhisperTests_{Guid.NewGuid():N}");
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
    /// WhisperModelManager のモデルダウンロード成功時にファイルが配置され一時ファイルが残らないこと、およびカスタムモデル指定が機能することを検証します。
    /// </summary>
    [Fact]
    public async Task WhisperModelManager_ダウンロードおよびカスタムモデル指定_状態が正しく遷移する()
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
    /// テキスト強制アライメントモードで進捗フェーズが順に通知され、LRC 形式の結果が生成されることを検証します。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_テキスト強制アライメントモード_進捗通知とLRC生成が正常に完了する()
    {
        // Arrange
        string audioPath = Path.Combine(_tempDir, "track.wav");
        string modelPath = Path.Combine(_tempDir, "ggml-base.bin");
        await File.WriteAllBytesAsync(audioPath, [0, 1, 2]);
        await File.WriteAllBytesAsync(modelPath, [0, 1, 2]);

        var modelManager = new WhisperModelManager(_tempDir);
        var sut = new WhisperLocalAlignmentEngine(
            modelManager,
            new LrcParser(),
            new LyricsForcedAligner(),
            new StubPcmDecoder(new float[16000], TimeSpan.FromSeconds(30)),
            transcribeFunc: (_, _, _, onProgress, _) =>
            {
                onProgress?.Invoke(50);
                onProgress?.Invoke(100);
                IReadOnlyList<AlignmentSegment> segments =
                [
                    new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(9), "最初のフレーズ"),
                    new(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(16), "二番目のフレーズ"),
                ];
                return Task.FromResult(segments);
            });

        var phases = new List<AlignmentPhase>();
        var progress = new SynchronousProgress<AlignmentProgress>(p => phases.Add(p.Phase));
        var request = new AlignmentRequest(
            audioFilePath: audioPath,
            mode: LrcGenerationMode.TextAlignment,
            lyricsLines: ["最初のフレーズ", "二番目のフレーズ"],
            title: "TestSong");

        // Act
        AlignmentResult result = await sut.GenerateAsync(request, progress);

        // Assert
        Assert.Equal(LrcGenerationMode.TextAlignment, result.Mode);
        Assert.Equal(2, result.Document.Lines.Count);
        Assert.Contains("[ti:TestSong]", result.LrcContent);
        Assert.Contains("[00:05.00]最初のフレーズ", result.LrcContent);
        Assert.Contains(AlignmentPhase.DecodingAudio, phases);
        Assert.Contains(AlignmentPhase.Transcribing, phases);
        Assert.Contains(AlignmentPhase.Completed, phases);
    }

    /// <summary>
    /// 音声直接文字起こしモードで歌詞行なしでもセグメントから同期 LRC が構築されることを検証します。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_音声直接文字起こしモード_歌詞入力なしで同期LRCを生成する()
    {
        // Arrange
        string audioPath = Path.Combine(_tempDir, "voice.wav");
        string modelPath = Path.Combine(_tempDir, "ggml-base.bin");
        await File.WriteAllBytesAsync(audioPath, [0, 1, 2]);
        await File.WriteAllBytesAsync(modelPath, [0, 1, 2]);

        var modelManager = new WhisperModelManager(_tempDir);
        var sut = new WhisperLocalAlignmentEngine(
            modelManager,
            new LrcParser(),
            new LyricsForcedAligner(),
            new StubPcmDecoder(new float[16000], TimeSpan.FromSeconds(20)),
            transcribeFunc: (_, _, _, _, _) =>
            {
                IReadOnlyList<AlignmentSegment> segments =
                [
                    new(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(6), "文字起こしされた一句"),
                ];
                return Task.FromResult(segments);
            });

        var request = new AlignmentRequest(
            audioFilePath: audioPath,
            mode: LrcGenerationMode.AudioTranscription,
            languageCode: "ja");

        // Act
        AlignmentResult result = await sut.GenerateAsync(request);

        // Assert
        Assert.Equal(LrcGenerationMode.AudioTranscription, result.Mode);
        Assert.Single(result.Document.Lines);
        Assert.Contains("[00:02.50]文字起こしされた一句", result.LrcContent);
    }

    /// <summary>
    /// 発話区間が 0 件（無音またはインスト楽曲）の場合に InvalidOperationException を送出することを検証します。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_発話区間0件_InvalidOperationExceptionを送出する()
    {
        // Arrange
        string audioPath = Path.Combine(_tempDir, "inst.wav");
        string modelPath = Path.Combine(_tempDir, "ggml-base.bin");
        await File.WriteAllBytesAsync(audioPath, [0, 1, 2]);
        await File.WriteAllBytesAsync(modelPath, [0, 1, 2]);

        var sut = new WhisperLocalAlignmentEngine(
            new WhisperModelManager(_tempDir),
            new LrcParser(),
            new LyricsForcedAligner(),
            new StubPcmDecoder(new float[16000], TimeSpan.FromSeconds(10)),
            transcribeFunc: (_, _, _, _, _) => Task.FromResult<IReadOnlyList<AlignmentSegment>>(Array.Empty<AlignmentSegment>()));

        var request = new AlignmentRequest(audioPath, LrcGenerationMode.AudioTranscription);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GenerateAsync(request));
    }

    /// <summary>
    /// ローカルモデルが未配置の場合に FileNotFoundException を送出することを検証します。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_モデル未配置_FileNotFoundExceptionを送出する()
    {
        // Arrange
        string audioPath = Path.Combine(_tempDir, "song.wav");
        await File.WriteAllBytesAsync(audioPath, [0, 1, 2]);

        var sut = new WhisperLocalAlignmentEngine(
            new WhisperModelManager(_tempDir),
            new LrcParser(),
            new LyricsForcedAligner(),
            new StubPcmDecoder(new float[16000], TimeSpan.FromSeconds(10)),
            transcribeFunc: (_, _, _, _, _) => Task.FromResult<IReadOnlyList<AlignmentSegment>>(Array.Empty<AlignmentSegment>()));

        var request = new AlignmentRequest(audioPath, LrcGenerationMode.AudioTranscription);

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => sut.GenerateAsync(request));
    }

    private sealed class StubPcmDecoder(float[] samples, TimeSpan duration) : IAudioPcmDecoder
    {
        public Task<DecodedAudioPcm> DecodeToMono16kHzAsync(
            string audioFilePath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(1.0);
            return Task.FromResult(new DecodedAudioPcm(samples, duration));
        }
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
