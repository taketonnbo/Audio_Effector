using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.LrcGeneration;
using AudioEffector.Infrastructure.Lyrics;
using NAudio;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace AudioEffector.Infrastructure.Tests.LrcGeneration;

[SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "外部ライブラリや OS 起因の予約済み例外の分類・日本語ラップ動作を単体テストで検証するため")]
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

    /// <summary>
    /// Whisper.net のネイティブライブラリ欠損（Native Library not found / DllNotFoundException / TypeInitializationException）時に
    /// 英語生メッセージを露出せず NativeRuntimeUnavailable の LrcGenerationException へラップし NLog へ記録することを検証します。
    /// </summary>
    [Theory]
    [InlineData("FileNotFoundNative")]
    [InlineData("DllNotFound")]
    [InlineData("TypeInitialization")]
    public async Task GenerateAsync_ネイティブライブラリ欠損例外_日本語エラーへラップしNLogへ記録する(string scenario)
    {
        // Arrange
        string audioPath = Path.Combine(_tempDir, "usb_song.flac");
        string modelPath = Path.Combine(_tempDir, "ggml-base.bin");
        await File.WriteAllBytesAsync(audioPath, [0, 1, 2]);
        await File.WriteAllBytesAsync(modelPath, [0, 1, 2]);

        Exception injected = scenario switch
        {
            "FileNotFoundNative" => new FileNotFoundException(
                "Native Library not found in default paths.Verify you have have included the native Whisper library in your application, or install the default libraries with the Whisper.net.Runtime NuGet."),
            "DllNotFound" => new DllNotFoundException("Unable to load DLL 'whisper.dll' or one of its dependencies."),
            _ => new TypeInitializationException("Whisper.net.Native.NativeMethods", new BadImageFormatException("Bad image format for whisper.dll")),
        };

        var sut = new WhisperLocalAlignmentEngine(
            new WhisperModelManager(_tempDir),
            new LrcParser(),
            new LyricsForcedAligner(),
            new StubPcmDecoder(new float[16000], TimeSpan.FromSeconds(10)),
            transcribeFunc: (_, _, _, _, _, _) => Task.FromException<IReadOnlyList<AlignmentSegment>>(injected));

        var request = new AlignmentRequest(audioPath, LrcGenerationMode.AudioTranscription);

        LoggingConfiguration? previousConfig = LogManager.Configuration;
        using var memoryTarget = new MemoryTarget("issue465_test_target")
        {
            Layout = "${level:uppercase=true}|${message}|${exception:format=tostring}",
        };
        var testConfig = new LoggingConfiguration();
        testConfig.AddRule(LogLevel.Error, LogLevel.Fatal, memoryTarget);
        LogManager.Configuration = testConfig;

        try
        {
            // Act
            LrcGenerationException ex = await Assert.ThrowsAsync<LrcGenerationException>(() => sut.GenerateAsync(request));

            // Assert
            Assert.Equal(LrcGenerationErrorCategory.NativeRuntimeUnavailable, ex.Category);
            Assert.Equal(LrcGenerationErrorMessageResolver.NATIVE_RUNTIME_UNAVAILABLE_MESSAGE, ex.Message);
            Assert.DoesNotContain("Native Library", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Whisper.net", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Same(injected, ex.InnerException);
            Assert.True(ex.IsLogged);
            Assert.NotEmpty(memoryTarget.Logs);
            Assert.Contains(injected.GetType().FullName!, memoryTarget.Logs[0], StringComparison.Ordinal);
        }
        finally
        {
            LogManager.Configuration = previousConfig;
        }
    }

    /// <summary>
    /// 音声デコード時の USB 取り外し・権限不足（IOException / UnauthorizedAccessException / DirectoryNotFoundException）が
    /// AudioFileAccessFailed へ分類されることを検証します。
    /// </summary>
    [Theory]
    [InlineData("IO")]
    [InlineData("Unauthorized")]
    [InlineData("DirectoryNotFound")]
    public async Task GenerateAsync_音声ファイルアクセス失敗_AudioFileAccessFailedへ分類する(string scenario)
    {
        // Arrange
        string audioPath = Path.Combine(_tempDir, "usb_track.mp3");
        string modelPath = Path.Combine(_tempDir, "ggml-base.bin");
        await File.WriteAllBytesAsync(audioPath, [0, 1, 2]);
        await File.WriteAllBytesAsync(modelPath, [0, 1, 2]);

        Exception injected = scenario switch
        {
            "IO" => new IOException("The device is not ready.", unchecked((int)0x80070015)),
            "Unauthorized" => new UnauthorizedAccessException("Access to the path 'E:\\Music\\track.mp3' is denied."),
            _ => new DirectoryNotFoundException("Could not find a part of the path 'E:\\Music'."),
        };

        var sut = new WhisperLocalAlignmentEngine(
            new WhisperModelManager(_tempDir),
            new LrcParser(),
            new LyricsForcedAligner(),
            new ThrowingPcmDecoder(injected),
            transcribeFunc: (_, _, _, _, _, _) => Task.FromResult<IReadOnlyList<AlignmentSegment>>(Array.Empty<AlignmentSegment>()));

        var request = new AlignmentRequest(audioPath, LrcGenerationMode.AudioTranscription);

        // Act
        LrcGenerationException ex = await Assert.ThrowsAsync<LrcGenerationException>(() => sut.GenerateAsync(request));

        // Assert
        Assert.Equal(LrcGenerationErrorCategory.AudioFileAccessFailed, ex.Category);
        Assert.Equal(LrcGenerationErrorMessageResolver.AUDIO_FILE_ACCESS_FAILED_MESSAGE, ex.Message);
        Assert.Same(injected, ex.InnerException);
    }

    /// <summary>
    /// 音声デコード時の破損・未対応形式例外（FormatException / InvalidDataException / COMException / MmException）が
    /// AudioDecodeFailed へ分類されることを検証します。
    /// </summary>
    [Theory]
    [InlineData("Format")]
    [InlineData("InvalidData")]
    [InlineData("COM")]
    [InlineData("MmException")]
    public async Task GenerateAsync_音声デコード失敗_AudioDecodeFailedへ分類する(string scenario)
    {
        // Arrange
        string audioPath = Path.Combine(_tempDir, "broken.flac");
        string modelPath = Path.Combine(_tempDir, "ggml-base.bin");
        await File.WriteAllBytesAsync(audioPath, [0, 1, 2]);
        await File.WriteAllBytesAsync(modelPath, [0, 1, 2]);

        Exception injected = scenario switch
        {
            "Format" => new FormatException("Not a WAVE file - no RIFF header"),
            "InvalidData" => new InvalidDataException("Invalid FLAC frame header"),
            "COM" => new COMException("MF_E_UNSUPPORTED_BYTESTREAM_TYPE", unchecked((int)0xC00D36C4)),
            _ => new MmException(MmResult.AcmNotPossible, "acmStreamOpen"),
        };

        var sut = new WhisperLocalAlignmentEngine(
            new WhisperModelManager(_tempDir),
            new LrcParser(),
            new LyricsForcedAligner(),
            new ThrowingPcmDecoder(injected),
            transcribeFunc: (_, _, _, _, _, _) => Task.FromResult<IReadOnlyList<AlignmentSegment>>(Array.Empty<AlignmentSegment>()));

        var request = new AlignmentRequest(audioPath, LrcGenerationMode.AudioTranscription);

        // Act
        LrcGenerationException ex = await Assert.ThrowsAsync<LrcGenerationException>(() => sut.GenerateAsync(request));

        // Assert
        Assert.Equal(LrcGenerationErrorCategory.AudioDecodeFailed, ex.Category);
        Assert.Equal(LrcGenerationErrorMessageResolver.AUDIO_DECODE_FAILED_MESSAGE, ex.Message);
        Assert.Same(injected, ex.InnerException);
    }

    /// <summary>
    /// モデル読込失敗・メモリ不足・未分類英語例外のそれぞれが期待するカテゴリと日本語メッセージへ変換されることを検証します。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_モデル破損とメモリ不足と未分類英語例外_期待するカテゴリへ分類する()
    {
        // Arrange
        string audioPath = Path.Combine(_tempDir, "song.wav");
        string modelPath = Path.Combine(_tempDir, "ggml-base.bin");
        await File.WriteAllBytesAsync(audioPath, [0, 1, 2]);
        await File.WriteAllBytesAsync(modelPath, [0, 1, 2]);
        var request = new AlignmentRequest(audioPath, LrcGenerationMode.AudioTranscription);

        var modelBrokenSut = new WhisperLocalAlignmentEngine(
            new WhisperModelManager(_tempDir),
            new LrcParser(),
            new LyricsForcedAligner(),
            new StubPcmDecoder(new float[16000], TimeSpan.FromSeconds(10)),
            transcribeFunc: (_, _, _, _, _, _) => Task.FromException<IReadOnlyList<AlignmentSegment>>(
                new InvalidDataException("Failed to load ggml model file: invalid magic.")));

        var oomSut = new WhisperLocalAlignmentEngine(
            new WhisperModelManager(_tempDir),
            new LrcParser(),
            new LyricsForcedAligner(),
            new StubPcmDecoder(new float[16000], TimeSpan.FromSeconds(10)),
            transcribeFunc: (_, _, _, _, _, _) => Task.FromException<IReadOnlyList<AlignmentSegment>>(
                new OutOfMemoryException("Out of memory during whisper_full.")));

        var unexpectedSut = new WhisperLocalAlignmentEngine(
            new WhisperModelManager(_tempDir),
            new LrcParser(),
            new LyricsForcedAligner(),
            new StubPcmDecoder(new float[16000], TimeSpan.FromSeconds(10)),
            transcribeFunc: (_, _, _, onModelLoaded, _, _) =>
            {
                onModelLoaded?.Invoke();
                return Task.FromException<IReadOnlyList<AlignmentSegment>>(
                    new InvalidOperationException("Unexpected internal state error."));
            });

        // Act
        LrcGenerationException modelEx = await Assert.ThrowsAsync<LrcGenerationException>(() => modelBrokenSut.GenerateAsync(request));
        LrcGenerationException oomEx = await Assert.ThrowsAsync<LrcGenerationException>(() => oomSut.GenerateAsync(request));
        LrcGenerationException unexpectedEx = await Assert.ThrowsAsync<LrcGenerationException>(() => unexpectedSut.GenerateAsync(request));

        // Assert
        Assert.Equal(LrcGenerationErrorCategory.ModelLoadFailed, modelEx.Category);
        Assert.Equal(LrcGenerationErrorMessageResolver.MODEL_LOAD_FAILED_MESSAGE, modelEx.Message);

        Assert.Equal(LrcGenerationErrorCategory.OutOfMemory, oomEx.Category);
        Assert.Equal(LrcGenerationErrorMessageResolver.OUT_OF_MEMORY_MESSAGE, oomEx.Message);

        Assert.Equal(LrcGenerationErrorCategory.Unexpected, unexpectedEx.Category);
        Assert.Equal(LrcGenerationErrorMessageResolver.DEFAULT_GENERATION_ERROR_MESSAGE, unexpectedEx.Message);
        Assert.DoesNotContain("Unexpected internal", unexpectedEx.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ユーザーキャンセル要求時に OperationCanceledException がラップされずそのまま再送出されることを検証します。
    /// </summary>
    [Fact]
    public async Task GenerateAsync_ユーザーキャンセル要求時_OperationCanceledExceptionをそのまま伝播する()
    {
        // Arrange
        string audioPath = Path.Combine(_tempDir, "cancel.wav");
        string modelPath = Path.Combine(_tempDir, "ggml-base.bin");
        await File.WriteAllBytesAsync(audioPath, [0, 1, 2]);
        await File.WriteAllBytesAsync(modelPath, [0, 1, 2]);

        using var cts = new CancellationTokenSource();
        var sut = new WhisperLocalAlignmentEngine(
            new WhisperModelManager(_tempDir),
            new LrcParser(),
            new LyricsForcedAligner(),
            new StubPcmDecoder(new float[16000], TimeSpan.FromSeconds(10)),
            transcribeFunc: (_, _, _, _, _, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<AlignmentSegment>>(Array.Empty<AlignmentSegment>());
            });

        var request = new AlignmentRequest(audioPath, LrcGenerationMode.AudioTranscription);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.GenerateAsync(request, cancellationToken: cts.Token));
    }

    private sealed class ThrowingPcmDecoder(Exception exception) : IAudioPcmDecoder
    {
        public Task<DecodedAudioPcm> DecodeToMono16kHzAsync(
            string audioFilePath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<DecodedAudioPcm>(exception);
        }
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
