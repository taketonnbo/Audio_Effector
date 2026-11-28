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
    /// テキスト強制アライメントモードで進捗フェーズが逆転せず順に通知され、LRC 形式の結果が生成されることを検証します。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_テキスト強制アライメントモード_進捗フェーズが単調順で通知されLRC生成が完了する()
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
            transcribeFunc: (_, _, _, onModelLoaded, onProgress, _) =>
            {
                onModelLoaded?.Invoke();
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
        Assert.Contains(AlignmentPhase.LoadingModel, phases);
        Assert.Contains(AlignmentPhase.Transcribing, phases);
        Assert.Contains(AlignmentPhase.AligningLyrics, phases);
        Assert.Contains(AlignmentPhase.FormattingLrc, phases);
        Assert.Equal(AlignmentPhase.Completed, phases[^1]);
        for (int i = 1; i < phases.Count; i++)
        {
            Assert.True(phases[i] >= phases[i - 1], $"フェーズ順序が逆転しました: {phases[i - 1]} -> {phases[i]}");
        }
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
            transcribeFunc: (_, _, _, onModelLoaded, _, _) =>
            {
                onModelLoaded?.Invoke();
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
            transcribeFunc: (_, _, _, _, _, _) => Task.FromResult<IReadOnlyList<AlignmentSegment>>(Array.Empty<AlignmentSegment>()));

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
            transcribeFunc: (_, _, _, _, _, _) => Task.FromResult<IReadOnlyList<AlignmentSegment>>(Array.Empty<AlignmentSegment>()));

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
