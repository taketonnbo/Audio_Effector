using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Application.Tests.ApplicationServices;

public sealed class LrcGenerationApplicationServiceTests
{
    /// <summary>
    /// テキスト事前検査で未対応拡張子および空ファイルが失敗 Result を返し、有効ファイルで検査結果が返ることを検証します。
    /// </summary>
    [Fact]
    public async Task InspectLyricsTextAsync_未対応拡張子と空ファイルと有効ファイル_期待される成否を返す()
    {
        // Arrange
        var textService = new FakeTextEncodingService
        {
            InspectionFactory = path => path.Contains("empty", StringComparison.Ordinal)
                ? new TextEncodingInspectionResult(path, true, false, "UTF-8", 1, 0)
                : new TextEncodingInspectionResult(path, false, false, "Shift-JIS (CP932)", 3, 3),
        };
        LrcGenerationApplicationService sut = CreateSut(textService: textService);

        // Act
        Result<TextEncodingInspectionResult> invalidExt = await sut.InspectLyricsTextAsync("song.mp3");
        Result<TextEncodingInspectionResult> emptyResult = await sut.InspectLyricsTextAsync("empty.txt");
        Result<TextEncodingInspectionResult> sjisResult = await sut.InspectLyricsTextAsync("lyrics.txt");

        // Assert
        Assert.True(invalidExt.IsFailure);
        Assert.True(emptyResult.IsFailure);
        Assert.True(sjisResult.IsSuccess);
        Assert.True(sjisResult.Value.RequiresConversion);
        Assert.Equal("Shift-JIS (CP932)", sjisResult.Value.DetectedEncodingName);
    }

    /// <summary>
    /// 非 UTF-8 テキストの生成で変換未承諾時はエンジンを実行せず拒否し、承諾時は TextAlignment モードで生成成功することを検証します。
    /// </summary>
    [Fact]
    public async Task GenerateLrcAsync_非Utf8ファイルの未承諾と承諾_未承諾は拒否し承諾時のみエンジンを実行する()
    {
        // Arrange
        var engine = new FakeAudioAlignmentEngine();
        var textService = new FakeTextEncodingService
        {
            InspectionFactory = path => new TextEncodingInspectionResult(path, false, false, "Shift-JIS (CP932)", 2, 2),
            TextContent = "一行目の歌詞\n二行目の歌詞",
        };
        LrcGenerationApplicationService sut = CreateSut(engine: engine, textService: textService);
        var track = new Track { FilePath = @"C:\Music\Song.flac", Title = "Song", Artist = "Artist" };

        // Act
        Result<AlignmentResult> rejected = await sut.GenerateLrcAsync(track, "lyrics.txt", allowEncodingConversion: false);
        Result<AlignmentResult> accepted = await sut.GenerateLrcAsync(track, "lyrics.txt", allowEncodingConversion: true);

        // Assert
        Assert.True(rejected.IsFailure);
        Assert.True(accepted.IsSuccess);
        Assert.Equal(1, engine.CallCount);
        Assert.NotNull(engine.LastRequest);
        Assert.Equal(LrcGenerationMode.TextAlignment, engine.LastRequest!.Mode);
        Assert.Equal(2, engine.LastRequest.LyricsLines.Count);
    }

    /// <summary>
    /// 音声直接文字起こしモードがテキストファイルなしで AudioTranscription リクエストをエンジンへ委譲することを検証します。
    /// </summary>
    [Fact]
    public async Task TranscribeAudioToLrcAsync_有効トラック_AudioTranscriptionモードで生成成功する()
    {
        // Arrange
        var engine = new FakeAudioAlignmentEngine();
        LrcGenerationApplicationService sut = CreateSut(engine: engine);
        var track = new Track { FilePath = @"C:\Music\Voice.mp3", Title = "Voice" };

        // Act
        Result<AlignmentResult> result = await sut.TranscribeAudioToLrcAsync(track, languageCode: "ja");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(engine.LastRequest);
        Assert.Equal(LrcGenerationMode.AudioTranscription, engine.LastRequest!.Mode);
        Assert.Empty(engine.LastRequest.LyricsLines);
        Assert.Equal("ja", engine.LastRequest.LanguageCode);
    }

    /// <summary>
    /// 生成処理中にキャンセルされた場合に OperationCanceledException がそのまま伝播することを検証します。
    /// </summary>
    [Fact]
    public async Task TranscribeAudioToLrcAsync_キャンセル要求時_OperationCanceledExceptionを伝播する()
    {
        // Arrange
        var engine = new FakeAudioAlignmentEngine();
        LrcGenerationApplicationService sut = CreateSut(engine: engine);
        var track = new Track { FilePath = @"C:\Music\Cancel.flac" };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.TranscribeAudioToLrcAsync(track, cancellationToken: cts.Token));
    }

    /// <summary>
    /// LRC 保存成功時に既定パスへ保存され、LyricsApplicationService のキャッシュ無効化および更新通知イベントが発行されることを検証します。
    /// </summary>
    [Fact]
    public async Task SaveLrcAsync_保存成功時_ファイル保存後にLyricsApplicationServiceへ更新通知を発行する()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var lyricsService = new LyricsApplicationService(new StubLyricsProvider(), eventBus);
        var textService = new FakeTextEncodingService();
        LrcGenerationApplicationService sut = CreateSut(textService: textService, lyricsService: lyricsService);

        var track = new Track { FilePath = @"C:\Music\Album\01_Song.flac", Title = "01_Song" };
        var doc = new LyricsDocument(
            [new LyricLine(0, TimeSpan.FromSeconds(3), "サビの歌詞")],
            isSynced: true,
            title: "01_Song");

        LyricsUpdatedEvent? receivedEvent = null;
        lyricsService.LyricsUpdated += (_, e) => receivedEvent = e;

        // Act
        Result<string> result = await sut.SaveLrcAsync(track, doc, destinationPath: null, overwrite: true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\Music\Album\01_Song.lrc", result.Value);
        Assert.Equal(@"C:\Music\Album\01_Song.lrc", textService.LastSavedPath);
        Assert.NotNull(receivedEvent);
        Assert.Equal(track.FilePath, receivedEvent!.TrackFilePath);
        Assert.NotNull(receivedEvent.Document);
        Assert.Equal(LyricsSourceType.LrcFile, receivedEvent.Document!.SourceType);
    }

    /// <summary>
    /// 既存ファイルがあり上書き未承諾で保存サービスが IOException を投げた場合に失敗 Result が返り通知が発行されないことを検証します。
    /// </summary>
    [Fact]
    public async Task SaveLrcAsync_上書き未承諾でIOException発生時_失敗Resultを返し更新通知を発行しない()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var lyricsService = new LyricsApplicationService(new StubLyricsProvider(), eventBus);
        var textService = new FakeTextEncodingService { ThrowIoOnSave = true };
        LrcGenerationApplicationService sut = CreateSut(textService: textService, lyricsService: lyricsService);

        var track = new Track { FilePath = @"C:\Music\Existing.flac" };
        var doc = new LyricsDocument([new LyricLine(0, TimeSpan.FromSeconds(1), "Test")], isSynced: true);
        bool eventFired = false;
        lyricsService.LyricsUpdated += (_, _) => eventFired = true;

        // Act
        Result<string> result = await sut.SaveLrcAsync(track, doc, overwrite: false);

        // Assert
        Assert.True(result.IsFailure);
        Assert.False(eventFired);
    }

    private static LrcGenerationApplicationService CreateSut(
        FakeAudioAlignmentEngine? engine = null,
        FakeTextEncodingService? textService = null,
        FakeLocalSpeechModelManager? modelManager = null,
        LyricsApplicationService? lyricsService = null)
    {
        return new LrcGenerationApplicationService(
            engine ?? new FakeAudioAlignmentEngine(),
            textService ?? new FakeTextEncodingService(),
            modelManager ?? new FakeLocalSpeechModelManager(),
            new StubLrcParser(),
            lyricsService ?? new LyricsApplicationService(new StubLyricsProvider(), new InMemoryEventBus()));
    }

    private sealed class FakeAudioAlignmentEngine : IAudioAlignmentEngine
    {
        public string EngineName => "FakeEngine";
        public int CallCount { get; private set; }
        public AlignmentRequest? LastRequest { get; private set; }

        public Task<AlignmentResult> GenerateAsync(
            AlignmentRequest request,
            IProgress<AlignmentProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastRequest = request;
            progress?.Report(new AlignmentProgress(AlignmentPhase.Completed, 1.0, "完了"));

            var doc = new LyricsDocument(
                [new LyricLine(0, TimeSpan.FromSeconds(1), "生成歌詞")],
                isSynced: true,
                title: request.Title);
            return Task.FromResult(new AlignmentResult(
                doc,
                "[00:01.00]生成歌詞",
                [new AlignmentSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "生成歌詞")],
                request.Mode,
                EngineName));
        }
    }

    private sealed class FakeTextEncodingService : ITextEncodingService
    {
        public Func<string, TextEncodingInspectionResult>? InspectionFactory { get; set; }
        public string TextContent { get; set; } = "デフォルト歌詞";
        public bool ThrowIoOnSave { get; set; }
        public string? LastSavedPath { get; private set; }

        public Task<TextEncodingInspectionResult> InspectAsync(string textFilePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TextEncodingInspectionResult result = InspectionFactory is not null
                ? InspectionFactory(textFilePath)
                : new TextEncodingInspectionResult(textFilePath, true, false, "UTF-8", 1, 1);
            return Task.FromResult(result);
        }

        public Task<string> ReadAsUtf8TextAsync(string textFilePath, bool allowEncodingConversion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(TextContent);
        }

        public Task SaveLrcFileAsync(string destinationPath, string lrcContent, bool overwrite, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowIoOnSave)
            {
                throw new IOException("ファイルが既に存在します。");
            }

            LastSavedPath = destinationPath;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLocalSpeechModelManager : ILocalSpeechModelManager
    {
        private readonly LocalSpeechModelInfo _info = new("base", "Whisper (base)", "ggml-base.bin", @"C:\models\ggml-base.bin", true, 1000);

        public LocalSpeechModelInfo GetActiveModelInfo() => _info;
        public IReadOnlyList<LocalSpeechModelInfo> GetSupportedModels() => [_info];
        public void SelectModel(string modelId) { }
        public void SetCustomModelPath(string? modelFilePath) { }

        public Task<LocalSpeechModelInfo> DownloadModelAsync(
            string? modelId = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(1.0);
            return Task.FromResult(_info);
        }
    }

    private sealed class StubLrcParser : ILrcParser
    {
        public LyricsDocument Parse(string content) => LyricsDocument.Empty;

        public string Format(LyricsDocument document) => "[00:01.00]formatted";
    }

    private sealed class StubLyricsProvider : ILyricsProvider
    {
        public Task<LyricsDocument?> LoadLyricsAsync(Track track, CancellationToken cancellationToken = default)
            => Task.FromResult<LyricsDocument?>(null);
    }
}
