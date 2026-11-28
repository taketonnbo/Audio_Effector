using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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

    /// <summary>
    /// GenerateLrcAsync に未対応拡張子ファイルを直接渡した場合にエンジンを実行せず失敗 Result を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task GenerateLrcAsync_未対応拡張子ファイル_エンジンを実行せず失敗Resultを返す()
    {
        // Arrange
        var engine = new FakeAudioAlignmentEngine();
        LrcGenerationApplicationService sut = CreateSut(engine: engine);
        var track = new Track { FilePath = @"C:\Music\Song.flac" };

        // Act
        Result<AlignmentResult> result = await sut.GenerateLrcAsync(track, "binary.mp3", allowEncodingConversion: true);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(0, engine.CallCount);
    }

    /// <summary>
    /// SaveLrcAsync で既定以外の別フォルダへ保存した場合に LyricsApplicationService のキャッシュ更新通知が発行されないことを検証します。
    /// </summary>
    [Fact]
    public async Task SaveLrcAsync_既定パス以外の別保存先_ファイル保存は成功しキャッシュ更新通知は発行しない()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var lyricsService = new LyricsApplicationService(new StubLyricsProvider(), eventBus);
        var textService = new FakeTextEncodingService();
        LrcGenerationApplicationService sut = CreateSut(textService: textService, lyricsService: lyricsService);

        var track = new Track { FilePath = @"C:\Music\Album\01_Song.flac", Title = "01_Song" };
        var doc = new LyricsDocument([new LyricLine(0, TimeSpan.FromSeconds(2), "別名保存")], isSynced: true);
        bool eventFired = false;
        lyricsService.LyricsUpdated += (_, _) => eventFired = true;

        // Act
        Result<string> result = await sut.SaveLrcAsync(track, doc, destinationPath: @"C:\Export\custom_copy.lrc", overwrite: true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\Export\custom_copy.lrc", textService.LastSavedPath);
        Assert.False(eventFired);
    }

    /// <summary>
    /// 既定パスに既存 .lrc が存在する場合、LoadExistingLrcAsync が透過的に UTF-8 読み込み・パースし、LrcFile ソース付きドキュメントを返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LoadExistingLrcAsync_既存Lrcファイルが存在する場合_パース済みLyricsDocumentを返す()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"ae_app_load_lrc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string audioPath = Path.Combine(tempDir, "Song.flac");
        string lrcPath = Path.Combine(tempDir, "Song.lrc");
        File.WriteAllText(audioPath, "dummy");
        File.WriteAllText(lrcPath, "[00:05.00]既存歌詞一行目");

        try
        {
            var textService = new FakeTextEncodingService
            {
                TextContent = "[00:05.00]既存歌詞一行目\n[00:12.00]既存歌詞二行目",
            };
            var parser = new StubLrcParser
            {
                ParseHandler = _ => new LyricsDocument(
                    [
                        new LyricLine(0, TimeSpan.FromSeconds(5), "既存歌詞一行目"),
                        new LyricLine(1, TimeSpan.FromSeconds(12), "既存歌詞二行目"),
                    ],
                    isSynced: true,
                    title: "Song"),
            };
            LrcGenerationApplicationService sut = CreateSut(textService: textService, lrcParser: parser);
            var track = new Track { FilePath = audioPath, Title = "Song" };

            // Act
            Result<LyricsDocument?> result = await sut.LoadExistingLrcAsync(track);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(2, result.Value!.Lines.Count);
            Assert.Equal("既存歌詞一行目", result.Value.Lines[0].Text);
            Assert.Equal(LyricsSourceType.LrcFile, result.Value.SourceType);
            Assert.Equal(lrcPath, result.Value.SourcePath);
            Assert.True(textService.LastAllowEncodingConversion);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// 既定パスに .lrc が存在しない場合、LoadExistingLrcAsync が成功 Result(null) を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LoadExistingLrcAsync_既存Lrcファイルが存在しない場合_成功ResultでNullを返す()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"ae_app_nolrc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string audioPath = Path.Combine(tempDir, "NoLyrics.flac");
        File.WriteAllText(audioPath, "dummy");

        try
        {
            LrcGenerationApplicationService sut = CreateSut();
            var track = new Track { FilePath = audioPath, Title = "NoLyrics" };

            // Act
            Result<LyricsDocument?> result = await sut.LoadExistingLrcAsync(track);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Null(result.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// 既存 .lrc ファイルが空または有効な歌詞行を含まない場合、LoadExistingLrcAsync が安全に失敗 Result を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    [InlineData("[ti:HeaderOnly]")]
    public async Task LoadExistingLrcAsync_空または有効行なしの既存Lrc_失敗Resultを返す(string content)
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"ae_app_emptylrc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string audioPath = Path.Combine(tempDir, "Broken.flac");
        string lrcPath = Path.Combine(tempDir, "Broken.lrc");
        File.WriteAllText(audioPath, "dummy");
        File.WriteAllText(lrcPath, content);

        try
        {
            var textService = new FakeTextEncodingService { TextContent = content };
            LrcGenerationApplicationService sut = CreateSut(textService: textService);
            var track = new Track { FilePath = audioPath, Title = "Broken" };

            // Act
            Result<LyricsDocument?> result = await sut.LoadExistingLrcAsync(track);

            // Assert
            Assert.True(result.IsFailure);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// 既存 .lrc ファイルがタイムタグのないプレーン歌詞（IsSynced == false）または読み込み時 I/O 例外の場合、
    /// LoadExistingLrcAsync が安全に失敗 Result を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task LoadExistingLrcAsync_非同期歌詞および読み込み例外発生時_失敗Resultを返す()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"ae_app_unsynced_lrc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string audioPath = Path.Combine(tempDir, "Unsynced.flac");
        string lrcPath = Path.Combine(tempDir, "Unsynced.lrc");
        File.WriteAllText(audioPath, "dummy");
        File.WriteAllText(lrcPath, "タイムタグのない歌詞行");

        try
        {
            var plainParser = new StubLrcParser
            {
                ParseHandler = _ => new LyricsDocument(
                    [new LyricLine(0, null, "タイムタグのない歌詞行")],
                    isSynced: false),
            };
            var textService = new FakeTextEncodingService { TextContent = "タイムタグのない歌詞行" };
            LrcGenerationApplicationService sutUnsynced = CreateSut(textService: textService, lrcParser: plainParser);
            var track = new Track { FilePath = audioPath, Title = "Unsynced" };

            // Act 1: IsSynced == false のドキュメント
            Result<LyricsDocument?> unsyncedResult = await sutUnsynced.LoadExistingLrcAsync(track);

            // Assert 1
            Assert.True(unsyncedResult.IsFailure);

            // Act 2: ReadAsUtf8TextAsync で例外発生
            textService.ThrowOnRead = new IOException("読み込み失敗テスト");
            LrcGenerationApplicationService sutReadError = CreateSut(textService: textService, lrcParser: plainParser);
            Result<LyricsDocument?> errorResult = await sutReadError.LoadExistingLrcAsync(track);

            // Assert 2
            Assert.True(errorResult.IsFailure);
            Assert.Contains("読み込み失敗テスト", errorResult.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// 未ラップの英語・システム例外がエンジンから直接到達しても生メッセージを露出せず、
    /// ロガーへ記録したうえで日本語メッセージへ正規化し、既存日本語ドメインメッセージやラップ済み例外は維持することを検証します。
    /// </summary>
    [Fact]
    public async Task TranscribeAudioToLrcAsync_未ラップ英語例外とラップ済み例外とドメイン例外_期待されるメッセージとログ出力になる()
    {
        // Arrange
        var logger = new RecordingAppLogger();
        var engine = new FakeAudioAlignmentEngine();
        LrcGenerationApplicationService sut = CreateSut(engine: engine, logger: logger);
        var track = new Track { FilePath = @"E:\USB\Song.flac", Title = "Song" };

        // Act 1: 未ラップのネイティブライブラリ欠損（英語生メッセージ）
        engine.ExceptionToThrow = new FileNotFoundException(
            "Native Library not found in default paths.Verify you have have included the native Whisper library.");
        Result<AlignmentResult> nativeResult = await sut.TranscribeAudioToLrcAsync(track);

        // Act 2: 未ラップの未分類英語例外
        engine.ExceptionToThrow = new InvalidOperationException("Unexpected whisper internal failure.");
        Result<AlignmentResult> unknownResult = await sut.TranscribeAudioToLrcAsync(track);

        // Act 3: 既に NLog 記録済みの LrcGenerationException（二重ログにしない）
        engine.ExceptionToThrow = new LrcGenerationException(
            LrcGenerationErrorCategory.ModelLoadFailed,
            LrcGenerationErrorMessageResolver.MODEL_LOAD_FAILED_MESSAGE,
            new InvalidDataException("corrupt model"),
            isLogged: true);
        Result<AlignmentResult> wrappedResult = await sut.TranscribeAudioToLrcAsync(track);

        // Act 4: 既存の日本語ドメイン例外（無音・発話 0 件）
        engine.ExceptionToThrow = new InvalidOperationException(
            "発話・歌唱区間を検出できませんでした（無音またはインストゥルメンタル楽曲の可能性があります）。");
        Result<AlignmentResult> domainResult = await sut.TranscribeAudioToLrcAsync(track);

        // Assert
        Assert.True(nativeResult.IsFailure);
        Assert.Equal(LrcGenerationErrorMessageResolver.NATIVE_RUNTIME_UNAVAILABLE_MESSAGE, nativeResult.ErrorMessage);
        Assert.DoesNotContain("Native Library", nativeResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        Assert.True(unknownResult.IsFailure);
        Assert.Equal(LrcGenerationErrorMessageResolver.DEFAULT_GENERATION_ERROR_MESSAGE, unknownResult.ErrorMessage);
        Assert.DoesNotContain("Unexpected whisper", unknownResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        Assert.True(wrappedResult.IsFailure);
        Assert.Equal(LrcGenerationErrorMessageResolver.MODEL_LOAD_FAILED_MESSAGE, wrappedResult.ErrorMessage);

        Assert.True(domainResult.IsFailure);
        Assert.Contains("発話・歌唱区間を検出できませんでした", domainResult.ErrorMessage, StringComparison.Ordinal);

        // IsLogged == true の Act 3 は二重記録されず、Act 1・2・4 の 3 件のみ記録される
        Assert.Equal(3, logger.ErrorEntries.Count);
    }

    /// <summary>
    /// SaveLrcAsync で外部ドライブの書き込み保護・取り外し・空き容量不足が発生した場合に、
    /// 生の英語 OS メッセージやドライブパスを露出せず分かりやすい日本語案内を返すことを検証します。
    /// </summary>
    [Theory]
    [InlineData("AccessDenied", LrcGenerationErrorMessageResolver.SAVE_LRC_ACCESS_DENIED_MESSAGE)]
    [InlineData("UsbRemoved", LrcGenerationErrorMessageResolver.SAVE_LRC_IO_ERROR_MESSAGE)]
    [InlineData("DiskFull", LrcGenerationErrorMessageResolver.SAVE_LRC_DISK_FULL_MESSAGE)]
    public async Task SaveLrcAsync_外部ドライブ書き込み障害_日本語案内を返しログ記録する(string scenario, string expectedMessage)
    {
        // Arrange
        var logger = new RecordingAppLogger();
        var textService = new FakeTextEncodingService
        {
            ThrowOnSave = scenario switch
            {
                "AccessDenied" => new UnauthorizedAccessException("Access to the path 'E:\\Music\\Song.lrc' is denied."),
                "UsbRemoved" => new IOException("The device is not ready.", unchecked((int)0x80070015)),
                _ => new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)),
            },
        };
        LrcGenerationApplicationService sut = CreateSut(textService: textService, logger: logger);
        var track = new Track { FilePath = @"E:\Music\Song.flac", Title = "Song" };
        var doc = new LyricsDocument([new LyricLine(0, TimeSpan.FromSeconds(1), "歌詞")], isSynced: true);

        // Act
        Result<string> result = await sut.SaveLrcAsync(track, doc, overwrite: true);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(expectedMessage, result.ErrorMessage);
        Assert.DoesNotContain(@"E:\", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Access to the path", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(logger.ErrorEntries);
    }

    /// <summary>
    /// DownloadModelAsync でネットワーク障害・タイムアウトが発生した際に生英語を連結せず日本語案内を返し、
    /// ユーザーキャンセル時は OperationCanceledException をそのまま伝播することを検証します。
    /// </summary>
    [Fact]
    public async Task DownloadModelAsync_ネットワーク障害とタイムアウトとユーザーキャンセル_適切に処理する()
    {
        // Arrange
        var logger = new RecordingAppLogger();
        var modelManager = new FakeLocalSpeechModelManager();
        LrcGenerationApplicationService sut = CreateSut(modelManager: modelManager, logger: logger);

        // Act 1: ネットワーク障害
        modelManager.DownloadException = new HttpRequestException("No such host is known.");
        Result<LocalSpeechModelInfo> netResult = await sut.DownloadModelAsync("base");

        // Act 2: タイムアウト（未キャンセルのトークンで TaskCanceledException）
        modelManager.DownloadException = new TaskCanceledException("HttpClient.Timeout exceeded.");
        Result<LocalSpeechModelInfo> timeoutResult = await sut.DownloadModelAsync("base");

        // Assert 1 & 2
        Assert.True(netResult.IsFailure);
        Assert.Equal(LrcGenerationErrorMessageResolver.MODEL_DOWNLOAD_NETWORK_ERROR_MESSAGE, netResult.ErrorMessage);
        Assert.DoesNotContain("No such host", netResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        Assert.True(timeoutResult.IsFailure);
        Assert.Equal(LrcGenerationErrorMessageResolver.MODEL_DOWNLOAD_TIMEOUT_MESSAGE, timeoutResult.ErrorMessage);
        Assert.DoesNotContain("HttpClient", timeoutResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, logger.ErrorEntries.Count);

        // Act & Assert 3: ユーザーキャンセルは伝播する
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        modelManager.DownloadException = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.DownloadModelAsync("base", cancellationToken: cts.Token));
    }

    /// <summary>
    /// InspectLyricsTextAsync・SelectModel・SetCustomModelPath で英語生例外が発生しても露出させず日本語案内を返すことを検証します。
    /// </summary>
    [Fact]
    public async Task テキスト検査とモデル選択_英語システム例外発生時_生メッセージを露出しない()
    {
        // Arrange
        var logger = new RecordingAppLogger();
        var textService = new FakeTextEncodingService
        {
            ThrowOnInspect = new UnauthorizedAccessException("Access to the path 'E:\\Lyrics\\song.txt' is denied."),
        };
        var modelManager = new FakeLocalSpeechModelManager
        {
            CustomModelException = new IOException("The device is not ready.", unchecked((int)0x80070015)),
            SelectModelException = new InvalidOperationException("Unexpected model catalog failure."),
        };
        LrcGenerationApplicationService sut = CreateSut(textService: textService, modelManager: modelManager, logger: logger);

        // Act
        Result<TextEncodingInspectionResult> inspectResult = await sut.InspectLyricsTextAsync(@"E:\Lyrics\song.txt");
        Result customModelResult = sut.SetCustomModelPath(@"E:\Models\custom.bin");
        Result selectModelResult = sut.SelectModel("base");

        // Assert
        Assert.True(inspectResult.IsFailure);
        Assert.DoesNotContain("Access to the path", inspectResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(customModelResult.IsFailure);
        Assert.DoesNotContain("not ready", customModelResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(selectModelResult.IsFailure);
        Assert.DoesNotContain("Unexpected model", selectModelResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, logger.ErrorEntries.Count);
    }

    private static LrcGenerationApplicationService CreateSut(
        FakeAudioAlignmentEngine? engine = null,
        FakeTextEncodingService? textService = null,
        FakeLocalSpeechModelManager? modelManager = null,
        LyricsApplicationService? lyricsService = null,
        StubLrcParser? lrcParser = null,
        IAppLogger? logger = null)
    {
        return new LrcGenerationApplicationService(
            engine ?? new FakeAudioAlignmentEngine(),
            textService ?? new FakeTextEncodingService(),
            modelManager ?? new FakeLocalSpeechModelManager(),
            lrcParser ?? new StubLrcParser(),
            lyricsService ?? new LyricsApplicationService(new StubLyricsProvider(), new InMemoryEventBus()),
            logger);
    }

    private sealed class RecordingAppLogger : IAppLogger
    {
        public List<(string Message, Exception? Exception)> ErrorEntries { get; } = new();

        public void Info(string message) { }

        public void Warn(string message, Exception? exception = null) { }

        public void Error(string message, Exception? exception = null)
        {
            ErrorEntries.Add((message, exception));
        }
    }

    private sealed class FakeAudioAlignmentEngine : IAudioAlignmentEngine
    {
        public string EngineName => "FakeEngine";
        public int CallCount { get; private set; }
        public AlignmentRequest? LastRequest { get; private set; }
        public Exception? ExceptionToThrow { get; set; }

        public Task<AlignmentResult> GenerateAsync(
            AlignmentRequest request,
            IProgress<AlignmentProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastRequest = request;
            if (ExceptionToThrow is not null)
            {
                return Task.FromException<AlignmentResult>(ExceptionToThrow);
            }

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
        public Exception? ThrowOnSave { get; set; }
        public Exception? ThrowOnInspect { get; set; }
        public Exception? ThrowOnRead { get; set; }
        public string? LastSavedPath { get; private set; }
        public bool LastAllowEncodingConversion { get; private set; }

        public Task<TextEncodingInspectionResult> InspectAsync(string textFilePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnInspect is not null)
            {
                return Task.FromException<TextEncodingInspectionResult>(ThrowOnInspect);
            }

            TextEncodingInspectionResult result = InspectionFactory is not null
                ? InspectionFactory(textFilePath)
                : new TextEncodingInspectionResult(textFilePath, true, false, "UTF-8", 1, 1);
            return Task.FromResult(result);
        }

        public Task<string> ReadAsUtf8TextAsync(string textFilePath, bool allowEncodingConversion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastAllowEncodingConversion = allowEncodingConversion;
            if (ThrowOnRead is not null)
            {
                return Task.FromException<string>(ThrowOnRead);
            }

            return Task.FromResult(TextContent);
        }

        public Task SaveLrcFileAsync(string destinationPath, string lrcContent, bool overwrite, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnSave is not null)
            {
                return Task.FromException(ThrowOnSave);
            }

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

        public Exception? DownloadException { get; set; }
        public Exception? CustomModelException { get; set; }
        public Exception? SelectModelException { get; set; }

        public LocalSpeechModelInfo GetActiveModelInfo() => _info;
        public IReadOnlyList<LocalSpeechModelInfo> GetSupportedModels() => [_info];
        public void SelectModel(string modelId)
        {
            if (SelectModelException is not null)
            {
                throw SelectModelException;
            }
        }

        public void SetCustomModelPath(string? modelFilePath)
        {
            if (CustomModelException is not null)
            {
                throw CustomModelException;
            }
        }

        public Task<LocalSpeechModelInfo> DownloadModelAsync(
            string? modelId = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DownloadException is not null)
            {
                return Task.FromException<LocalSpeechModelInfo>(DownloadException);
            }

            progress?.Report(1.0);
            return Task.FromResult(_info);
        }
    }

    private sealed class StubLrcParser : ILrcParser
    {
        public Func<string, LyricsDocument>? ParseHandler { get; set; }

        public LyricsDocument Parse(string content) => ParseHandler is not null ? ParseHandler(content) : LyricsDocument.Empty;

        public string Format(LyricsDocument document) => "[00:01.00]formatted";

        public LrcOriginType ClassifyOrigin(string lrcContent) => LrcOriginType.ManualOrExternal;
    }

    private sealed class StubLyricsProvider : ILyricsProvider
    {
        public Task<LyricsDocument?> LoadLyricsAsync(Track track, CancellationToken cancellationToken = default)
            => Task.FromResult<LyricsDocument?>(null);
    }
}
