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
using AudioEffector.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

public sealed class LrcGeneratorViewModelTests
{
    private sealed class ImmediateDispatcherStub : IUiDispatcher
    {
        public void Invoke(Action action) => action();

        public void BeginInvoke(Action action) => action();
    }

    private sealed class QueuedBeginInvokeDispatcherStub : IUiDispatcher
    {
        private readonly Queue<Action> _pendingBeginInvokes = new();

        public void Invoke(Action action) => action();

        public void BeginInvoke(Action action) => _pendingBeginInvokes.Enqueue(action);

        public void DrainBeginInvokes()
        {
            while (_pendingBeginInvokes.Count > 0)
            {
                _pendingBeginInvokes.Dequeue()();
            }
        }
    }

    private sealed class FakeAlignmentEngine : IAudioAlignmentEngine
    {
        public string EngineName => "FakeEngine";
        public AlignmentRequest? LastRequest { get; private set; }
        public Func<AlignmentRequest, IProgress<AlignmentProgress>?, CancellationToken, Task<AlignmentResult>>? Handler { get; set; }

        public Task<AlignmentResult> GenerateAsync(
            AlignmentRequest request,
            IProgress<AlignmentProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (Handler is not null)
            {
                return Handler(request, progress, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AlignmentProgress(AlignmentPhase.Transcribing, 0.5, "解析中"));
            progress?.Report(new AlignmentProgress(AlignmentPhase.Completed, 1.0, "完了"));

            var doc = new LyricsDocument(
                [
                    new LyricLine(0, TimeSpan.FromSeconds(5), "一行目の歌詞"),
                    new LyricLine(1, TimeSpan.FromSeconds(12), "二行目の歌詞"),
                ],
                isSynced: true,
                title: request.Title);

            return Task.FromResult(new AlignmentResult(
                doc,
                "[00:05.00]一行目の歌詞\r\n[00:12.00]二行目の歌詞",
                [new AlignmentSegment(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), "一行目の歌詞")],
                request.Mode,
                EngineName));
        }
    }

    private sealed class FakeTextEncodingService : ITextEncodingService
    {
        public Func<string, TextEncodingInspectionResult>? InspectionFactory { get; set; }
        public Func<string, bool, CancellationToken, Task<string>>? ReadHandler { get; set; }
        public string TextToReturn { get; set; } = "一行目の歌詞\n二行目の歌詞";
        public string? LastSavedPath { get; private set; }
        public bool LastOverwriteFlag { get; private set; }
        public Exception? SaveException { get; set; }

        public Task<TextEncodingInspectionResult> InspectAsync(string textFilePath, CancellationToken cancellationToken = default)
        {
            TextEncodingInspectionResult result = InspectionFactory is not null
                ? InspectionFactory(textFilePath)
                : new TextEncodingInspectionResult(textFilePath, true, false, "UTF-8", 2, 2);
            return Task.FromResult(result);
        }

        public Task<string> ReadAsUtf8TextAsync(string textFilePath, bool allowEncodingConversion, CancellationToken cancellationToken = default)
        {
            if (ReadHandler is not null)
            {
                return ReadHandler(textFilePath, allowEncodingConversion, cancellationToken);
            }

            return Task.FromResult(TextToReturn);
        }

        public Task SaveLrcFileAsync(string destinationPath, string lrcContent, bool overwrite, CancellationToken cancellationToken = default)
        {
            if (SaveException is not null)
            {
                return Task.FromException(SaveException);
            }

            LastSavedPath = destinationPath;
            LastOverwriteFlag = overwrite;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeModelManager : ILocalSpeechModelManager
    {
        public bool IsAvailable { get; set; } = true;
        public Func<IProgress<double>?, CancellationToken, Task<LocalSpeechModelInfo>>? DownloadHandler { get; set; }

        public LocalSpeechModelInfo GetActiveModelInfo()
            => new("base", "Whisper (base)", "ggml-base.bin", @"C:\models\ggml-base.bin", IsAvailable, 140_000_000);

        public IReadOnlyList<LocalSpeechModelInfo> GetSupportedModels() => [GetActiveModelInfo()];

        public void SelectModel(string modelId) { }

        public void SetCustomModelPath(string? modelFilePath)
        {
            IsAvailable = !string.IsNullOrWhiteSpace(modelFilePath);
        }

        public Task<LocalSpeechModelInfo> DownloadModelAsync(
            string? modelId = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (DownloadHandler is not null)
            {
                return DownloadHandler(progress, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(1.0);
            IsAvailable = true;
            return Task.FromResult(GetActiveModelInfo());
        }
    }

    private sealed class StubLrcParser : ILrcParser
    {
        public Func<string, LyricsDocument>? ParseHandler { get; set; }

        public LyricsDocument Parse(string content) => ParseHandler is not null ? ParseHandler(content) : LyricsDocument.Empty;

        public string Format(LyricsDocument document)
        {
            return string.Join(
                Environment.NewLine,
                System.Linq.Enumerable.Select(document.Lines, l => $"[{l.Timestamp?.TotalSeconds:00.00}]{l.Text}"));
        }

        public LrcOriginType ClassifyOrigin(string lrcContent) => LrcOriginType.ManualOrExternal;
    }

    private sealed class StubLyricsProvider : ILyricsProvider
    {
        public Task<LyricsDocument?> LoadLyricsAsync(Track track, CancellationToken cancellationToken = default)
            => Task.FromResult<LyricsDocument?>(null);
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        private readonly object _sync = new();
        private AppSettings _settings;

        public FakeSettingsService(bool hasAcceptedLrcCopyrightGuideline = true)
        {
            _settings = new AppSettings
            {
                HasAcceptedLrcCopyrightGuideline = hasAcceptedLrcCopyrightGuideline,
            };
        }

        public int UpdateSettingsAsyncCallCount { get; private set; }

        public bool StoredHasAcceptedLrcCopyrightGuideline
        {
            get
            {
                lock (_sync)
                {
                    return _settings.HasAcceptedLrcCopyrightGuideline;
                }
            }
        }

        public AppSettings LoadSettings()
        {
            lock (_sync)
            {
                return new AppSettings
                {
                    HasAcceptedLrcCopyrightGuideline = _settings.HasAcceptedLrcCopyrightGuideline,
                };
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            lock (_sync)
            {
                _settings = settings;
            }
        }

        public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(LoadSettings());

        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            SaveSettings(settings);
            return Task.CompletedTask;
        }

        public void UpdateSettings(Action<AppSettings> update)
        {
            lock (_sync)
            {
                update(_settings);
            }
        }

        public Task UpdateSettingsAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                UpdateSettingsAsyncCallCount++;
                update(_settings);
            }

            return Task.CompletedTask;
        }
    }

    private static (LrcGeneratorViewModel Sut, FakeAlignmentEngine Engine, FakeTextEncodingService TextSvc, FakeModelManager ModelMgr, LyricsApplicationService LyricsSvc) CreateSut(
        bool modelAvailable = true,
        Mock<IAudioService>? audioMock = null,
        IUiDispatcher? dispatcher = null,
        Func<string, bool>? fileExistsFunc = null,
        StubLrcParser? lrcParser = null,
        bool acceptedCopyrightGuideline = true,
        ISettingsService? settingsService = null)
    {
        var engine = new FakeAlignmentEngine();
        var textSvc = new FakeTextEncodingService();
        var modelMgr = new FakeModelManager { IsAvailable = modelAvailable };
        var eventBus = new InMemoryEventBus();
        var lyricsSvc = new LyricsApplicationService(new StubLyricsProvider(), eventBus);
        var genSvc = new LrcGenerationApplicationService(engine, textSvc, modelMgr, lrcParser ?? new StubLrcParser(), lyricsSvc);

        audioMock ??= new Mock<IAudioService>();
        ISettingsService resolvedSettings = settingsService ?? new FakeSettingsService(acceptedCopyrightGuideline);
        var sut = new LrcGeneratorViewModel(
            genSvc,
            audioMock.Object,
            dispatcher ?? new ImmediateDispatcherStub(),
            selectTextFileFunc: () => null,
            selectModelFileFunc: () => null,
            openFolderAction: _ => { },
            fileExistsFunc: fileExistsFunc ?? (_ => true),
            settingsService: resolvedSettings);

        return (sut, engine, textSvc, modelMgr, lyricsSvc);
    }

    /// <summary>
    /// UTF-8 テキストファイル選択時に警告が表示されず、即座にテキスト生成コマンドが実行可能になることを検証します。
    /// </summary>
    [Fact]
    public async Task InspectAndSelectTextFileAsync_Utf8ファイル_警告なしで即時生成可能状態へ遷移する()
    {
        // Arrange
        var (sut, _, _, _, _) = CreateSut();
        using (sut)
        {
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\Song.flac", Title = "Song" });

            // Act
            await sut.InspectAndSelectTextFileAsync(@"C:\Lyrics\utf8.txt");

            // Assert
            Assert.True(sut.HasInputTextFile);
            Assert.False(sut.IsEncodingWarningVisible);
            Assert.Equal("UTF-8", sut.DetectedEncodingName);
            Assert.True(sut.GenerateLrcCommand.CanExecute(null));
            Assert.True(sut.TranscribeAudioCommand.CanExecute(null));
        }
    }

    /// <summary>
    /// Shift-JIS 非 UTF-8 ファイル選択時に警告バーが表示され、テキスト生成のみ無効化（音声文字起こしは実行可能維持）となり、承諾で有効化・キャンセルで選択解除されることを検証します。
    /// </summary>
    [Fact]
    public async Task InspectAndSelectTextFileAsync_ShiftJisファイル_警告表示と承諾およびキャンセル状態遷移が正しく動作する()
    {
        // Arrange
        var (sut, _, textSvc, _, _) = CreateSut();
        textSvc.InspectionFactory = path => new TextEncodingInspectionResult(path, false, false, "Shift-JIS (CP932)", 3, 3);

        using (sut)
        {
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\Song.flac", Title = "Song" });

            // Act 1: Shift-JIS 選択
            await sut.InspectAndSelectTextFileAsync(@"C:\Lyrics\sjis.txt");

            // Assert 1: 警告表示・テキスト生成は非活性だが音声直接文字起こしは引き続き活性
            Assert.True(sut.IsEncodingWarningVisible);
            Assert.Equal("Shift-JIS (CP932)", sut.DetectedEncodingName);
            Assert.False(sut.GenerateLrcCommand.CanExecute(null));
            Assert.True(sut.TranscribeAudioCommand.CanExecute(null));

            // Act 2: 承諾
            sut.ConfirmEncodingConversionCommand.Execute(null);

            // Assert 2: 警告が閉じテキスト生成可能に
            Assert.False(sut.IsEncodingWarningVisible);
            Assert.True(sut.GenerateLrcCommand.CanExecute(null));

            // Act 3: 再度 Shift-JIS 選択後にキャンセル
            await sut.InspectAndSelectTextFileAsync(@"C:\Lyrics\sjis.txt");
            sut.CancelEncodingConversionCommand.Execute(null);

            // Assert 3: 選択解除され待機状態へ戻る
            Assert.False(sut.IsEncodingWarningVisible);
            Assert.False(sut.HasInputTextFile);
            Assert.False(sut.GenerateLrcCommand.CanExecute(null));
        }
    }

    /// <summary>
    /// テキストファイル未選択のまま「音声から文字起こしして LRC を生成」を実行でき、プレビュー状態へ遷移することを検証します。
    /// </summary>
    [Fact]
    public async Task TranscribeFromAudioAsync_テキスト未選択状態_音声直接文字起こしでプレビュー状態へ遷移する()
    {
        // Arrange
        var (sut, engine, _, _, _) = CreateSut();
        using (sut)
        {
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\Voice.mp3", Title = "Voice" });

            // Act
            Assert.False(sut.HasInputTextFile);
            Assert.False(sut.GenerateLrcCommand.CanExecute(null));
            Assert.True(sut.TranscribeAudioCommand.CanExecute(null));
            await sut.TranscribeFromAudioAsync();

            // Assert
            Assert.NotNull(engine.LastRequest);
            Assert.Equal(LrcGenerationMode.AudioTranscription, engine.LastRequest!.Mode);
            Assert.True(sut.IsPreviewState);
            Assert.True(sut.HasResult);
            Assert.Equal(2, sut.PreviewLines.Count);
        }
    }

    /// <summary>
    /// 生成実行中に IsGenerating / IsBusy が true となり戻る・閉じるコマンドがガードされ、キャンセル操作で待機状態へ安全に復帰することを検証します。
    /// </summary>
    [Fact]
    public async Task GenerateFromTextAsync_実行中ガードとキャンセル操作_安全に中断し入力状態へ戻る()
    {
        // Arrange
        var (sut, engine, _, _, _) = CreateSut();
        var startedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.Handler = async (_, progress, ct) =>
        {
            progress?.Report(new AlignmentProgress(AlignmentPhase.Transcribing, 0.3, "解析中"));
            startedTcs.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        };

        using (sut)
        {
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\Long.flac", Title = "Long" });
            await sut.InspectAndSelectTextFileAsync(@"C:\Lyrics\utf8.txt");

            // Act
            Task runningTask = sut.GenerateFromTextAsync();
            await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

            // Assert (実行中)
            Assert.True(sut.IsGenerating);
            Assert.True(sut.IsBusy);
            Assert.False(sut.BackToLyricsCommand.CanExecute(null));
            Assert.False(sut.CloseGeneratorCommand.CanExecute(null));
            Assert.True(sut.CancelGenerationCommand.CanExecute(null));

            // Act (キャンセル)
            sut.CancelGenerationCommand.Execute(null);
            await runningTask.WaitAsync(TimeSpan.FromSeconds(3));

            // Assert (中断後)
            Assert.False(sut.IsGenerating);
            Assert.False(sut.IsBusy);
            Assert.True(sut.IsInputState);
            Assert.True(sut.BackToLyricsCommand.CanExecute(null));
            Assert.True(sut.CloseGeneratorCommand.CanExecute(null));
        }
    }

    /// <summary>
    /// プレビュー結果に対する全体オフセット調整・リセットおよび個別行インライン編集（正常確定・不正時刻エラー）を検証します。
    /// </summary>
    [Fact]
    public async Task PreviewEditing_全体オフセット調整と個別行インライン編集_期待通りタイムスタンプと本文が更新される()
    {
        // Arrange
        var (sut, _, _, _, _) = CreateSut();
        using (sut)
        {
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\Song.flac", Title = "Song" });
            await sut.TranscribeFromAudioAsync();

            // Act 1: +0.5s オフセット調整
            sut.AdjustOffsetCommand.Execute("0.5");

            // Assert 1
            Assert.Equal(0.5, sut.TotalOffsetSeconds, precision: 2);
            Assert.Equal(TimeSpan.FromSeconds(5.5), sut.PreviewLines[0].Timestamp);

            // Act 2: リセット
            sut.ResetOffsetCommand.Execute(null);

            // Assert 2
            Assert.Equal(0.0, sut.TotalOffsetSeconds, precision: 2);
            Assert.Equal(TimeSpan.FromSeconds(5.0), sut.PreviewLines[0].Timestamp);

            // Act 3: 個別行編集（不正書式は拒否、正常書式は反映）
            LrcPreviewLineViewModel firstLine = sut.PreviewLines[0];
            firstLine.BeginEditCommand.Execute(null);
            firstLine.EditingTimestampText = "invalid";
            firstLine.CommitEditCommand.Execute(null);
            Assert.True(firstLine.HasEditError);
            Assert.True(firstLine.IsEditing);

            firstLine.EditingTimestampText = "00:07.25";
            firstLine.EditingLyricText = "修正後の歌詞";
            firstLine.CommitEditCommand.Execute(null);

            // Assert 3
            Assert.False(firstLine.IsEditing);
            Assert.False(firstLine.HasEditError);
            Assert.Equal(TimeSpan.FromSeconds(7.25), firstLine.Timestamp);
            Assert.Equal("修正後の歌詞", firstLine.Text);
        }
    }

    /// <summary>
    /// LRC 保存成功時に保存先パスと LrcSaved イベントおよび LyricsApplicationService への更新通知が発火することを検証します。
    /// </summary>
    [Fact]
    public async Task ExecuteSaveAsync_保存成功時_保存イベントと歌詞表示キャッシュ更新通知が発火する()
    {
        // Arrange
        var (sut, _, textSvc, _, lyricsSvc) = CreateSut();
        string? savedEventPath = null;
        LyricsUpdatedEvent? updatedEvent = null;
        sut.LrcSaved += path => savedEventPath = path;
        lyricsSvc.LyricsUpdated += (_, e) => updatedEvent = e;

        using (sut)
        {
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\SaveTest.flac", Title = "SaveTest" });
            await sut.TranscribeFromAudioAsync();

            // Act
            await sut.ExecuteSaveAsync(overwrite: true);

            // Assert
            Assert.Equal(@"C:\Music\SaveTest.lrc", sut.SavedFilePath);
            Assert.Equal(@"C:\Music\SaveTest.lrc", savedEventPath);
            Assert.Equal(@"C:\Music\SaveTest.lrc", textSvc.LastSavedPath);
            Assert.NotNull(updatedEvent);
            Assert.Equal(@"C:\Music\SaveTest.flac", updatedEvent!.TrackFilePath);
            Assert.True(sut.OpenSavedFolderCommand.CanExecute(null));
        }
    }

    /// <summary>
    /// モデル未配置状態からダウンロード成功後に自動的に入力待機状態へ遷移することを検証します。
    /// </summary>
    [Fact]
    public async Task DownloadDefaultModelAsync_未配置状態からDL成功_入力待機状態へ遷移する()
    {
        // Arrange
        var (sut, _, _, _, _) = CreateSut(modelAvailable: false);
        using (sut)
        {
            Assert.True(sut.IsModelMissingState);

            // Act
            await sut.DownloadDefaultModelAsync();

            // Assert
            Assert.True(sut.IsModelAvailable);
            Assert.True(sut.IsInputState);
            Assert.False(sut.IsModelMissingState);
        }
    }

    /// <summary>
    /// LRC 保存失敗時に SaveErrorMessage / HasSaveError が設定され、再保存成功および再生成でクリアされることを検証します。
    /// </summary>
    [Fact]
    public async Task ExecuteSaveAsync_保存失敗時_SaveErrorMessageが表示され再保存成功および再生成でクリアされる()
    {
        // Arrange
        var (sut, _, textSvc, _, _) = CreateSut();
        using (sut)
        {
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\SaveFail.flac", Title = "SaveFail" });
            await sut.TranscribeFromAudioAsync();
            textSvc.SaveException = new UnauthorizedAccessException("書き込み権限がありません。");

            // Act 1: 保存失敗
            await sut.ExecuteSaveAsync(overwrite: true);

            // Assert 1
            Assert.True(sut.IsPreviewState);
            Assert.True(sut.HasSaveError);
            Assert.Contains("書き込み権限", sut.SaveErrorMessage, StringComparison.Ordinal);
            Assert.Null(sut.SavedFilePath);

            // Act 2: 失敗後に再保存成功
            textSvc.SaveException = null;
            await sut.ExecuteSaveAsync(overwrite: true);

            // Assert 2
            Assert.False(sut.HasSaveError);
            Assert.Equal(string.Empty, sut.SaveErrorMessage);
            Assert.Equal(@"C:\Music\SaveFail.lrc", sut.SavedFilePath);

            // Act 3: 再度失敗させた後に「再生成」で入力画面へ戻るとクリアされる
            textSvc.SaveException = new IOException("ファイルがロックされています。");
            await sut.ExecuteSaveAsync(overwrite: true);
            Assert.True(sut.HasSaveError);

            sut.RegenerateCommand.Execute(null);
            Assert.False(sut.HasSaveError);
            Assert.True(sut.IsInputState);
        }
    }

    /// <summary>
    /// 全体オフセット適用後に個別行をインライン編集しても、後続の全体オフセット調整およびリセットで二重加算・ズレが起きないことを検証します。
    /// </summary>
    [Fact]
    public async Task PreviewEditing_オフセット適用後の個別行編集_後続オフセット調整とリセットで二重加算されない()
    {
        // Arrange
        var (sut, _, _, _, _) = CreateSut();
        using (sut)
        {
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\OffsetEdit.flac", Title = "OffsetEdit" });
            await sut.TranscribeFromAudioAsync();

            // Act 1: +0.5s オフセット適用（行0: 00:05.50, 行1: 00:12.50）
            sut.AdjustOffsetCommand.Execute("0.5");
            Assert.Equal(TimeSpan.FromSeconds(5.5), sut.PreviewLines[0].Timestamp);
            Assert.Equal(TimeSpan.FromSeconds(12.5), sut.PreviewLines[1].Timestamp);

            // Act 2: 行 0 の歌詞本文のみインライン編集して確定（時刻は 00:05.50 のまま）
            LrcPreviewLineViewModel line0 = sut.PreviewLines[0];
            line0.BeginEditCommand.Execute(null);
            line0.EditingLyricText = "誤字修正後の歌詞";
            line0.CommitEditCommand.Execute(null);
            Assert.Equal(TimeSpan.FromSeconds(5.5), line0.Timestamp);

            // Act 3: さらに +0.1s 追加調整（累計 +0.6s → 行0 は 00:05.60、行1 は 00:12.60 であるべき）
            sut.AdjustOffsetCommand.Execute("0.1");
            Assert.Equal(TimeSpan.FromSeconds(5.6), sut.PreviewLines[0].Timestamp);
            Assert.Equal(TimeSpan.FromSeconds(12.6), sut.PreviewLines[1].Timestamp);

            // Act 4: リセット（累計 0.0s → 行0 は 00:05.00、行1 は 00:12.00 へ一致復帰）
            sut.ResetOffsetCommand.Execute(null);
            Assert.Equal(TimeSpan.FromSeconds(5.0), sut.PreviewLines[0].Timestamp);
            Assert.Equal(TimeSpan.FromSeconds(12.0), sut.PreviewLines[1].Timestamp);
            Assert.Equal("誤字修正後の歌詞", sut.PreviewLines[0].Text);
        }
    }

    /// <summary>
    /// 遅延 BeginInvoke の進捗通知が完了・キャンセル・Dispose 後に進捗を上書きせず、未処理例外発生時も IsBusy が固着せずエラー状態へ復帰することを検証します。
    /// </summary>
    [Fact]
    public async Task ExecuteGenerationAsync_遅延BeginInvokeと未処理例外とDispose後_古い進捗破棄およびIsBusy固着しない()
    {
        // Arrange 1: 遅延 BeginInvoke キューで完了後の古い進捗が破棄されること
        var queuedDispatcher = new QueuedBeginInvokeDispatcherStub();
        var (sut, engine, _, _, _) = CreateSut(dispatcher: queuedDispatcher);
        var startedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.Handler = async (_, progress, ct) =>
        {
            progress?.Report(new AlignmentProgress(AlignmentPhase.Transcribing, 0.42, "古い進捗"));
            startedTcs.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        };

        using (sut)
        {
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\RaceTest.flac", Title = "RaceTest" });
            Task running = sut.TranscribeFromAudioAsync();
            await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
            sut.CancelGenerationCommand.Execute(null);
            await running.WaitAsync(TimeSpan.FromSeconds(3));

            // キャンセル完了後に遅延 BeginInvoke をドレインしても Progress が 0 のまま上書きされないこと
            queuedDispatcher.DrainBeginInvokes();
            Assert.False(sut.IsGenerating);
            Assert.Equal(0.0, sut.Progress);
            Assert.Equal(string.Empty, sut.StatusMessage);

            // Arrange 2: 想定外の一般例外発生時に IsBusy が固着せずエラー状態へ遷移すること
            engine.Handler = (_, _, _) => throw new InvalidOperationException("予期しないネイティブエラー");
            await sut.TranscribeFromAudioAsync();

            Assert.False(sut.IsGenerating);
            Assert.False(sut.IsBusy);
            Assert.True(sut.IsErrorState);
            Assert.Contains("予期しないネイティブエラー", sut.ErrorMessage, StringComparison.Ordinal);
        }

        // Dispose 後に遅延キューを実行しても例外を投げず安全であること
        queuedDispatcher.DrainBeginInvokes();
    }

    /// <summary>
    /// 音声ファイル欠損時は両生成ボタンが非活性かつ .txt 選択後も欠損警告が維持され、別楽曲切替時に前楽曲の .txt 選択と承諾状態がクリアされることを検証します。
    /// </summary>
    [Fact]
    public async Task SetTargetTrack_音声ファイル欠損と別楽曲切替_生成コマンド非活性とテキスト選択クリアが正しく動作する()
    {
        // Arrange
        var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Music\Exists.flac" };
        var (sut, _, textSvc, _, _) = CreateSut(fileExistsFunc: p => existingPaths.Contains(p));
        textSvc.InspectionFactory = path => new TextEncodingInspectionResult(path, false, false, "Shift-JIS (CP932)", 2, 2);

        using (sut)
        {
            // Act 1: 音声ファイル欠損トラックを設定
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\Missing.flac", Title = "Missing" });

            // Assert 1: 欠損メッセージ表示＆両生成コマンド非活性
            Assert.True(sut.IsTargetAudioMissing);
            Assert.NotEmpty(sut.TargetAudioMissingMessage);
            Assert.False(sut.GenerateLrcCommand.CanExecute(null));
            Assert.False(sut.TranscribeAudioCommand.CanExecute(null));

            // Act 2: .txt を選択しても欠損警告が消えず、生成コマンドも非活性のまま
            await sut.InspectAndSelectTextFileAsync(@"C:\Lyrics\sjis.txt");
            sut.ConfirmEncodingConversionCommand.Execute(null);
            Assert.True(sut.HasInputTextFile);
            Assert.True(sut.IsTargetAudioMissing);
            Assert.NotEmpty(sut.TargetAudioMissingMessage);
            Assert.False(sut.GenerateLrcCommand.CanExecute(null));
            Assert.False(sut.TranscribeAudioCommand.CanExecute(null));

            // Act 3: 別の実在トラックへ切替 → 前楽曲の .txt 選択と承諾状態がクリアされ、欠損も解除
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\Exists.flac", Title = "Exists" });

            // Assert 3
            Assert.False(sut.IsTargetAudioMissing);
            Assert.Equal(string.Empty, sut.TargetAudioMissingMessage);
            Assert.False(sut.HasInputTextFile);
            Assert.False(sut.IsEncodingWarningVisible);
            Assert.False(sut.GenerateLrcCommand.CanExecute(null));
            Assert.True(sut.TranscribeAudioCommand.CanExecute(null));
        }
    }

    /// <summary>
    /// テキスト生成エラー後の再試行でテキスト未選択・未承諾の場合は音声直接文字起こしへフォールバックせず入力画面へ案内表示することを検証します。
    /// </summary>
    [Fact]
    public async Task RetryErrorCommand_テキスト生成失敗後にテキスト未選択状態_音声文字起こしへフォールバックしない()
    {
        // Arrange
        int callCount = 0;
        var (sut, engine, _, _, _) = CreateSut();
        engine.Handler = (_, _, _) =>
        {
            callCount++;
            throw new InvalidOperationException("一時エラー");
        };

        using (sut)
        {
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\RetryTest.flac", Title = "RetryTest" });
            await sut.InspectAndSelectTextFileAsync(@"C:\Lyrics\utf8.txt");
            await sut.GenerateFromTextAsync();
            Assert.True(sut.IsErrorState);
            Assert.Equal(1, callCount);

            // テキスト選択を解除してから再試行を実行
            sut.ClearTextFileCommand.Execute(null);
            sut.RetryErrorCommand.Execute(null);
            await Task.Delay(50);

            // Assert: 音声文字起こしへ誤フォールバックせず入力状態へ戻る
            Assert.Equal(1, callCount);
            Assert.True(sut.IsInputState);
            Assert.True(sut.HasValidationError);
        }
    }

    /// <summary>
    /// 同名の既存 .lrc ファイルが存在する場合は上書き確認インラインバーを表示し、キャンセル・上書き保存確定の両経路が正しく動作することを検証します。
    /// </summary>
    [Fact]
    public async Task SaveLrcRequestedAsync_既存Lrcファイルが存在する場合_上書き確認バー表示とキャンセルおよび上書き確定が動作する()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"ae_lrc_overwrite_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string audioPath = Path.Combine(tempDir, "Song.flac");
        string lrcPath = Path.Combine(tempDir, "Song.lrc");
        File.WriteAllText(audioPath, "dummy");
        File.WriteAllText(lrcPath, "[00:01.00]old");

        try
        {
            var (sut, _, textSvc, _, _) = CreateSut(fileExistsFunc: File.Exists);
            using (sut)
            {
                sut.SetTargetTrack(new Track { FilePath = audioPath, Title = "Song" });
                await sut.TranscribeFromAudioAsync();

                // Act 1: 既存 .lrc があるため上書き確認バー表示（まだ保存されない）
                await sut.SaveLrcRequestedAsync();
                Assert.True(sut.IsOverwriteConfirmVisible);
                Assert.Contains("Song.lrc", sut.OverwriteWarningMessage, StringComparison.Ordinal);
                Assert.Null(textSvc.LastSavedPath);
                Assert.False(sut.SaveLrcCommand.CanExecute(null));
                Assert.True(sut.ConfirmOverwriteSaveCommand.CanExecute(null));

                // Act 2: キャンセル
                sut.CancelOverwriteSaveCommand.Execute(null);
                Assert.False(sut.IsOverwriteConfirmVisible);
                Assert.True(sut.SaveLrcCommand.CanExecute(null));

                // Act 3: 再度保存要求 → 上書き確定
                await sut.SaveLrcRequestedAsync();
                await sut.ExecuteSaveAsync(overwrite: true);
                Assert.False(sut.IsOverwriteConfirmVisible);
                Assert.Equal(lrcPath, textSvc.LastSavedPath);
                Assert.True(textSvc.LastOverwriteFlag);
            }
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
    /// プレビュー行クリックシークが対象楽曲再生中のみ SeekTo を呼び出し、インライン編集中や別楽曲再生中は抑止することを検証します。
    /// </summary>
    [Fact]
    public async Task SeekToPreviewLineCommand_通常時はシーク実行し編集中および別楽曲再生時は抑止する()
    {
        // Arrange
        var targetTrack = new Track { FilePath = @"C:\Music\SeekTarget.flac", Title = "SeekTarget" };
        var otherTrack = new Track { FilePath = @"C:\Music\Other.flac", Title = "Other" };
        var audioMock = new Mock<IAudioService>();
        audioMock.Setup(a => a.CurrentTrack).Returns(targetTrack);
        audioMock.Setup(a => a.TotalTime).Returns(TimeSpan.FromSeconds(100));

        var (sut, _, _, _, _) = CreateSut(audioMock: audioMock);
        using (sut)
        {
            sut.SetTargetTrack(targetTrack);
            await sut.TranscribeFromAudioAsync();
            LrcPreviewLineViewModel firstLine = sut.PreviewLines[0];

            // Act 1: 通常表示時のクリックシーク（5s / 100s = 0.05）
            sut.SeekToPreviewLineCommand.Execute(firstLine);
            audioMock.Verify(a => a.SeekTo(It.Is<double>(r => Math.Abs(r - 0.05) < 0.001)), Times.Once);

            // Act 2: 編集モード中はシーク抑止
            firstLine.BeginEditCommand.Execute(null);
            sut.SeekToPreviewLineCommand.Execute(firstLine);
            audioMock.Verify(a => a.SeekTo(It.IsAny<double>()), Times.Once);
            firstLine.CancelEditCommand.Execute(null);

            // Act 3: 別楽曲再生中はシーク抑止
            audioMock.Setup(a => a.CurrentTrack).Returns(otherTrack);
            sut.SeekToPreviewLineCommand.Execute(firstLine);
            audioMock.Verify(a => a.SeekTo(It.IsAny<double>()), Times.Once);
        }
    }

    /// <summary>
    /// モデル読み込みフェーズで IsProgressIndeterminate が true となり、文字起こし進捗フェーズで false へ切り替わることを検証します。
    /// </summary>
    [Fact]
    public async Task ExecuteGenerationAsync_モデル読み込みフェーズで不定進捗となり文字起こしフェーズで確定進捗へ切り替わる()
    {
        // Arrange
        var (sut, engine, _, _, _) = CreateSut();
        bool? indeterminateAtLoading = null;
        bool? indeterminateAtTranscribing = null;

        engine.Handler = (req, progress, _) =>
        {
            progress?.Report(new AlignmentProgress(AlignmentPhase.LoadingModel, 0.2, "モデル読み込み中"));
            indeterminateAtLoading = sut.IsProgressIndeterminate;
            progress?.Report(new AlignmentProgress(AlignmentPhase.Transcribing, 0.6, "文字起こし中"));
            indeterminateAtTranscribing = sut.IsProgressIndeterminate;

            var doc = new LyricsDocument([new LyricLine(0, TimeSpan.FromSeconds(1), "ok")], isSynced: true, title: req.Title);
            return Task.FromResult(new AlignmentResult(doc, "[00:01.00]ok", [], req.Mode, "Fake"));
        };

        using (sut)
        {
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\Phase.flac", Title = "Phase" });
            await sut.TranscribeFromAudioAsync();

            Assert.True(indeterminateAtLoading);
            Assert.False(indeterminateAtTranscribing);
            Assert.False(sut.IsProgressIndeterminate);
        }
    }

    /// <summary>
    /// テキスト強制アライメントおよび音声直接文字起こしの結果に対して、オフセット調整や個別行編集を行っても OriginType が維持されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(LrcGenerationMode.TextAlignment, LrcOriginType.AutoGeneratedTextAlignment)]
    [InlineData(LrcGenerationMode.AudioTranscription, LrcOriginType.AutoGeneratedAudioTranscription)]
    public async Task PreviewEditingおよびOffset調整_生成方式ごとのOriginTypeが欠落せず維持されること(
        LrcGenerationMode mode,
        LrcOriginType expectedOrigin)
    {
        // Arrange
        var (sut, engine, _, _, _) = CreateSut();
        engine.Handler = (req, _, _) =>
        {
            var doc = new LyricsDocument(
                lines:
                [
                    new LyricLine(0, TimeSpan.FromSeconds(5), "1行目"),
                    new LyricLine(1, TimeSpan.FromSeconds(10), "2行目"),
                ],
                isSynced: true,
                title: req.Title,
                originType: expectedOrigin);
            return Task.FromResult(new AlignmentResult(doc, "[00:05.00]1行目", [], req.Mode, "Fake"));
        };

        using (sut)
        {
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\OriginKeep.flac", Title = "OriginKeep" });

            if (mode == LrcGenerationMode.TextAlignment)
            {
                await sut.InspectAndSelectTextFileAsync(@"C:\Lyrics\origin.txt");
                await sut.GenerateFromTextAsync();
            }
            else
            {
                await sut.TranscribeFromAudioAsync();
            }

            Assert.Equal(expectedOrigin, sut.GeneratedDocument?.OriginType);

            // Act 1: オフセット調整
            sut.AdjustOffsetCommand.Execute("0.2");
            Assert.Equal(expectedOrigin, sut.GeneratedDocument?.OriginType);

            // Act 2: 個別行編集
            LrcPreviewLineViewModel line0 = sut.PreviewLines[0];
            line0.BeginEditCommand.Execute(null);
            line0.EditingLyricText = "修正後テキスト";
            line0.CommitEditCommand.Execute(null);

            // Assert
            Assert.NotNull(sut.GeneratedDocument);
            Assert.Equal(expectedOrigin, sut.GeneratedDocument!.OriginType);
            Assert.Equal("修正後テキスト", sut.GeneratedDocument.Lines[0].Text);
        }
    }

    /// <summary>
    /// 自動文字起こし実行・.lrc 保存後に同一楽曲で再作成を開始すると入力状態へ復帰し、
    /// テキストファイル（.txt）からの LRC 生成・上書き確認・保存・歌詞更新通知まで完了できることを検証します。
    /// </summary>
    [Fact]
    public async Task SetTargetTrack_自動文字起こし保存後に同一楽曲で再作成開始_入力状態へ復帰しテキスト生成と上書き保存が成功する()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"ae_lrc_recreate_txt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string audioPath = Path.Combine(tempDir, "RecreateSong.flac");
        string lrcPath = Path.Combine(tempDir, "RecreateSong.lrc");
        File.WriteAllText(audioPath, "dummy");

        try
        {
            var (sut, engine, textSvc, _, lyricsSvc) = CreateSut(fileExistsFunc: File.Exists);
            var track = new Track { FilePath = audioPath, Title = "RecreateSong", Artist = "Artist" };
            int updatedCount = 0;
            lyricsSvc.LyricsUpdated += (_, _) => updatedCount++;

            using (sut)
            {
                // 初回: 自動文字起こし → .lrc 保存
                sut.SetTargetTrack(track);
                await sut.TranscribeFromAudioAsync();
                Assert.True(sut.IsPreviewState);
                await sut.SaveLrcRequestedAsync();
                File.WriteAllText(lrcPath, "[00:05.00]transcribed");
                Assert.True(sut.HasSavedFile);
                Assert.Equal(1, updatedCount);

                // Act 1: 同一楽曲で再作成開始 → 入力・待機状態へ復帰
                sut.SetTargetTrack(track);

                // Assert 1: 前回の結果・保存状態がクリアされ入力状態へ遷移
                Assert.True(sut.IsInputState);
                Assert.False(sut.HasResult);
                Assert.False(sut.HasSavedFile);
                Assert.Empty(sut.PreviewLines);
                Assert.False(sut.IsOverwriteConfirmVisible);
                Assert.True(sut.SelectTextFileCommand.CanExecute(null));
                Assert.True(sut.TranscribeAudioCommand.CanExecute(null));
                Assert.False(sut.GenerateLrcCommand.CanExecute(null));

                // Act 2: .txt 選択 → テキストからの強制アライメント生成 → 上書き確認 → 上書き保存
                await sut.InspectAndSelectTextFileAsync(@"C:\Lyrics\RecreateSong.txt");
                Assert.True(sut.GenerateLrcCommand.CanExecute(null));

                await sut.GenerateFromTextAsync();
                Assert.True(sut.IsPreviewState);
                Assert.NotNull(engine.LastRequest);
                Assert.Equal(LrcGenerationMode.TextAlignment, engine.LastRequest!.Mode);

                await sut.SaveLrcRequestedAsync();
                Assert.True(sut.IsOverwriteConfirmVisible);

                await sut.ExecuteSaveAsync(overwrite: true);

                // Assert 2: 既存 .lrc の上書き保存および歌詞更新通知が完了
                Assert.False(sut.IsOverwriteConfirmVisible);
                Assert.Equal(lrcPath, textSvc.LastSavedPath);
                Assert.True(textSvc.LastOverwriteFlag);
                Assert.True(sut.HasSavedFile);
                Assert.Equal(2, updatedCount);
            }
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
    /// 自動文字起こし実行・.lrc 保存後に同一楽曲で再作成を開始した場合に、
    /// 再度「音声から直接文字起こし」を実行して上書き保存できることを検証します。
    /// </summary>
    [Fact]
    public async Task SetTargetTrack_自動文字起こし保存後に同一楽曲で再作成開始_自動文字起こしの再実行と上書き保存が成功する()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"ae_lrc_recreate_audio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string audioPath = Path.Combine(tempDir, "VoiceSong.flac");
        string lrcPath = Path.Combine(tempDir, "VoiceSong.lrc");
        File.WriteAllText(audioPath, "dummy");

        try
        {
            var (sut, engine, textSvc, _, _) = CreateSut(fileExistsFunc: File.Exists);
            var track = new Track { FilePath = audioPath, Title = "VoiceSong", Artist = "Artist" };

            using (sut)
            {
                sut.SetTargetTrack(track);
                await sut.TranscribeFromAudioAsync();
                await sut.SaveLrcRequestedAsync();
                File.WriteAllText(lrcPath, "[00:05.00]first");
                Assert.True(sut.HasSavedFile);

                // Act: 同一楽曲で再作成開始 → 再度自動文字起こし → 上書き保存
                sut.SetTargetTrack(track);
                Assert.True(sut.IsInputState);
                Assert.True(sut.TranscribeAudioCommand.CanExecute(null));

                await sut.TranscribeFromAudioAsync();
                Assert.True(sut.IsPreviewState);
                Assert.Equal(LrcGenerationMode.AudioTranscription, engine.LastRequest!.Mode);

                await sut.SaveLrcRequestedAsync();
                Assert.True(sut.IsOverwriteConfirmVisible);

                await sut.ExecuteSaveAsync(overwrite: true);

                // Assert
                Assert.False(sut.IsOverwriteConfirmVisible);
                Assert.Equal(lrcPath, textSvc.LastSavedPath);
                Assert.True(textSvc.LastOverwriteFlag);
                Assert.True(sut.HasSavedFile);
            }
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
    /// 生成処理中（IsBusy == true）に同一楽曲または別楽曲で SetTargetTrack が呼ばれた場合、
    /// 進行中の処理が中断・リセットされないことを検証します。
    /// </summary>
    [Fact]
    public async Task SetTargetTrack_生成処理中に同一楽曲または別楽曲を指定_進行中の処理が中断およびリセットされない()
    {
        // Arrange
        var (sut, engine, _, _, _) = CreateSut();
        var gate = new TaskCompletionSource<AlignmentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.Handler = (_, _, _) => gate.Task;
        var originalTrack = new Track { FilePath = @"C:\Music\Running.flac", Title = "Running" };
        var otherTrack = new Track { FilePath = @"C:\Music\Other.flac", Title = "Other" };

        using (sut)
        {
            sut.SetTargetTrack(originalTrack);
            Task runTask = sut.TranscribeFromAudioAsync();
            Assert.True(sut.IsBusy);
            Assert.True(sut.IsGeneratingState);

            // Act: 処理中に同一楽曲（resetCompletedResult 含む）および別楽曲で SetTargetTrack を呼び出す
            sut.SetTargetTrack(originalTrack, resetCompletedResult: true);
            sut.SetTargetTrack(otherTrack, resetCompletedResult: true);

            // Assert: 対象楽曲・実行状態とも維持される
            Assert.Same(originalTrack, sut.TargetTrack);
            Assert.True(sut.IsBusy);
            Assert.True(sut.IsGeneratingState);

            var doc = new LyricsDocument([new LyricLine(0, TimeSpan.FromSeconds(2), "done")], isSynced: true, title: "Running");
            gate.TrySetResult(new AlignmentResult(doc, "[00:02.00]done", [], LrcGenerationMode.AudioTranscription, "Fake"));
            await runTask;

            Assert.True(sut.IsPreviewState);
            Assert.Same(originalTrack, sut.TargetTrack);
        }
    }

    /// <summary>
    /// プレビューの「再生成」ボタンからの復帰・同一テキストパスの再選択・未保存結果の保持および明示リセットが正しく動作することを検証します。
    /// </summary>
    [Fact]
    public async Task RegenerateCommandとInspectAndSelectTextFileAsync_再生成復帰と同一パス再選択_入力サマリと両生成モードが正常に動作する()
    {
        // Arrange
        int lineCount = 2;
        var (sut, _, textSvc, _, _) = CreateSut();
        textSvc.InspectionFactory = path => new TextEncodingInspectionResult(path, true, false, "UTF-8", lineCount, lineCount);
        var track = new Track { FilePath = @"C:\Music\SamePath.flac", Title = "SamePath" };

        using (sut)
        {
            sut.SetTargetTrack(track);
            await sut.InspectAndSelectTextFileAsync(@"C:\Lyrics\same.txt");
            Assert.Equal("UTF-8 / 2 行", sut.InputTextSummary);

            await sut.TranscribeFromAudioAsync();
            Assert.True(sut.IsPreviewState);
            Assert.False(sut.HasSavedFile);

            // 未保存結果のまま同一楽曲で SetTargetTrack(resetCompletedResult: false) → 完了結果を維持
            sut.SetTargetTrack(track);
            Assert.True(sut.IsPreviewState);

            // Act 1: プレビューの「再生成」ボタンで入力状態へ戻る
            sut.RegenerateCommand.Execute(null);

            // Assert 1: 入力状態へ復帰し、テキスト生成・自動文字起こし双方とも利用可能
            Assert.True(sut.IsInputState);
            Assert.True(sut.GenerateLrcCommand.CanExecute(null));
            Assert.True(sut.TranscribeAudioCommand.CanExecute(null));

            // Act 2: 同一パスの .txt を編集後（行数変化）に再選択
            lineCount = 5;
            var changedProps = new List<string?>();
            sut.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName);
            await sut.InspectAndSelectTextFileAsync(@"C:\Lyrics\same.txt");

            // Assert 2: 同一パスでも InputTextSummary が通知・更新され、コマンドも利用可能
            Assert.Equal("UTF-8 / 5 行", sut.InputTextSummary);
            Assert.Contains(nameof(LrcGeneratorViewModel.InputTextSummary), changedProps);
            Assert.True(sut.GenerateLrcCommand.CanExecute(null));

            // Act 3: 未保存結果に対して resetCompletedResult: true を明示指定した場合はリセットされる
            await sut.TranscribeFromAudioAsync();
            Assert.True(sut.IsPreviewState);
            sut.SetTargetTrack(track, resetCompletedResult: true);
            Assert.True(sut.IsInputState);
        }
    }

    /// <summary>
    /// 既存 .lrc が存在する楽曲で SetTargetTrackAsync を呼ぶと .lrc 手動編集画面がメイン表示され、
    /// オフセット調整・個別行編集・上書き保存・LyricsUpdated 通知が動作し、
    /// 「↺ 再生成」押下時のみ入力・待機画面へ遷移してテキスト生成および自動文字起こしを実行できることを検証します。
    /// </summary>
    [Fact]
    public async Task SetTargetTrackAsync_既存Lrcが存在する楽曲を開く_Lrc編集画面をメイン表示し再生成押下時のみ一から生成画面へ遷移する()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"ae_lrc_editor_main_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string audioPath = Path.Combine(tempDir, "Editable.flac");
        string lrcPath = Path.Combine(tempDir, "Editable.lrc");
        File.WriteAllText(audioPath, "dummy");
        File.WriteAllText(lrcPath, "[00:10.00]既存一行目\r\n[00:20.00]既存二行目");

        try
        {
            var parser = new StubLrcParser
            {
                ParseHandler = _ => new LyricsDocument(
                    [
                        new LyricLine(0, TimeSpan.FromSeconds(10), "既存一行目"),
                        new LyricLine(1, TimeSpan.FromSeconds(20), "既存二行目"),
                    ],
                    isSynced: true,
                    title: "Editable"),
            };
            var (sut, engine, textSvc, _, lyricsSvc) = CreateSut(fileExistsFunc: File.Exists, lrcParser: parser);
            textSvc.TextToReturn = "[00:10.00]既存一行目\n[00:20.00]既存二行目";
            var track = new Track { FilePath = audioPath, Title = "Editable", Artist = "Artist" };
            int updatedCount = 0;
            lyricsSvc.LyricsUpdated += (_, _) => updatedCount++;

            using (sut)
            {
                // Act 1: 既存 .lrc がある楽曲を開く → .lrc 編集画面がメイン表示される
                await sut.SetTargetTrackAsync(track);

                // Assert 1
                Assert.True(sut.IsPreviewState);
                Assert.False(sut.IsInputState);
                Assert.Equal(2, sut.PreviewLines.Count);
                Assert.Equal("既存一行目", sut.PreviewLines[0].Text);
                Assert.True(sut.HasSavedFile);
                Assert.True(sut.OpenSavedFolderCommand.CanExecute(null));

                // Act 2: オフセット調整 + 個別行編集 → 上書き確認 → 上書き保存
                sut.AdjustOffsetCommand.Execute("0.5");
                sut.PreviewLines[0].BeginEditCommand.Execute(null);
                sut.PreviewLines[0].EditingLyricText = "手動修正済み一行目";
                sut.PreviewLines[0].CommitEditCommand.Execute(null);

                // 未保存の手動編集がある状態で同一楽曲へ再オープンしても編集内容が破棄されない
                await sut.SetTargetTrackAsync(track);
                Assert.True(sut.IsPreviewState);
                Assert.Equal("手動修正済み一行目", sut.PreviewLines[0].Text);

                await sut.SaveLrcRequestedAsync();
                Assert.True(sut.IsOverwriteConfirmVisible);
                await sut.ExecuteSaveAsync(overwrite: true);

                // Assert 2: 保存と LyricsUpdated 通知成功
                Assert.False(sut.IsOverwriteConfirmVisible);
                Assert.Equal(lrcPath, textSvc.LastSavedPath);
                Assert.True(textSvc.LastOverwriteFlag);
                Assert.Equal(1, updatedCount);

                // Act 3: 保存後に同一楽曲を再オープン → 再び .lrc 編集画面がメイン表示される
                await sut.SetTargetTrackAsync(track);
                Assert.True(sut.IsPreviewState);

                // Act 4: 「↺ 再生成」ボタン押下時のみ入力・待機画面へ遷移し、テキスト生成・自動文字起こし双方を利用できる
                sut.RegenerateCommand.Execute(null);
                Assert.True(sut.IsInputState);
                Assert.True(sut.TranscribeAudioCommand.CanExecute(null));

                await sut.InspectAndSelectTextFileAsync(@"C:\Lyrics\Editable.txt");
                Assert.True(sut.GenerateLrcCommand.CanExecute(null));
                await sut.GenerateFromTextAsync();
                Assert.True(sut.IsPreviewState);
                Assert.Equal(LrcGenerationMode.TextAlignment, engine.LastRequest!.Mode);

                // Act 5: 再度「↺ 再生成」から音声直接文字起こしを実行し、上書き確認〜上書き保存まで完了できる
                sut.RegenerateCommand.Execute(null);
                Assert.True(sut.IsInputState);
                await sut.TranscribeFromAudioAsync();
                Assert.True(sut.IsPreviewState);
                Assert.Equal(LrcGenerationMode.AudioTranscription, engine.LastRequest!.Mode);

                await sut.SaveLrcRequestedAsync();
                Assert.True(sut.IsOverwriteConfirmVisible);
                await sut.ExecuteSaveAsync(overwrite: true);
                Assert.False(sut.IsOverwriteConfirmVisible);
                Assert.Equal(lrcPath, textSvc.LastSavedPath);
                Assert.True(textSvc.LastOverwriteFlag);
                Assert.Equal(2, updatedCount);
            }
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
    /// [offset:] タグを含む既存 .lrc を読み込んだ際に、実効タイムスタンプへ正規化されてプレビュー表示・シーク・再保存され、
    /// [offset:] 消失によるタイミングズレが発生しないことを検証します。
    /// </summary>
    [Fact]
    public async Task SetTargetTrackAsync_Offset付き既存Lrcを読み込む_実効タイムスタンプへ正規化され保存後もタイミングを維持する()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"ae_lrc_offset_load_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string audioPath = Path.Combine(tempDir, "OffsetSong.flac");
        string lrcPath = Path.Combine(tempDir, "OffsetSong.lrc");
        File.WriteAllText(audioPath, "dummy");
        File.WriteAllText(lrcPath, "[offset:2000]\r\n[00:10.00]オフセット行");

        try
        {
            // Offset = +2秒（GetEffectiveTimestamp は 10s - 2s = 8s または 0s クランプ）
            var offsetDoc = new LyricsDocument(
                [
                    new LyricLine(0, TimeSpan.FromSeconds(1), "先頭クランプ行"),
                    new LyricLine(1, TimeSpan.FromSeconds(10), "オフセット行"),
                ],
                isSynced: true,
                title: "OffsetSong",
                offset: TimeSpan.FromSeconds(2));
            TimeSpan expectedFirst = offsetDoc.GetEffectiveTimestamp(0)!.Value;
            TimeSpan expectedSecond = offsetDoc.GetEffectiveTimestamp(1)!.Value;

            var parser = new StubLrcParser { ParseHandler = _ => offsetDoc };
            var (sut, _, textSvc, _, _) = CreateSut(fileExistsFunc: File.Exists, lrcParser: parser);
            textSvc.TextToReturn = "[offset:2000]\n[00:10.00]オフセット行";
            var track = new Track { FilePath = audioPath, Title = "OffsetSong" };

            using (sut)
            {
                // Act
                await sut.SetTargetTrackAsync(track);

                // Assert: PreviewLines と GeneratedDocument の両方が Offset 適用済みの実効タイムスタンプへ正規化される
                Assert.True(sut.IsPreviewState);
                Assert.Equal(expectedFirst, sut.PreviewLines[0].Timestamp);
                Assert.Equal(expectedSecond, sut.PreviewLines[1].Timestamp);
                Assert.NotNull(sut.GeneratedDocument);
                Assert.Equal(TimeSpan.Zero, sut.GeneratedDocument!.Offset);
                Assert.Equal(expectedFirst, sut.GeneratedDocument.Lines[0].Timestamp);
                Assert.Equal(expectedSecond, sut.GeneratedDocument.Lines[1].Timestamp);
            }
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
    /// 既存 .lrc の読み込みに失敗した場合に ValidationErrorMessage が設定され、
    /// モデル配置済み時は入力・待機画面、モデル未配置時はモデル未配置ガイドへ安全にフォールバックすることを検証します。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetTargetTrackAsync_既存Lrc読み込み失敗時_案内表示のうえ適切な待機画面へフォールバックする(bool modelAvailable)
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"ae_lrc_load_fail_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string audioPath = Path.Combine(tempDir, "Corrupt.flac");
        string lrcPath = Path.Combine(tempDir, "Corrupt.lrc");
        File.WriteAllText(audioPath, "dummy");
        File.WriteAllText(lrcPath, "broken");

        try
        {
            var (sut, _, textSvc, _, _) = CreateSut(modelAvailable: modelAvailable, fileExistsFunc: File.Exists);
            textSvc.ReadHandler = (_, _, _) => Task.FromException<string>(new IOException("ファイル破損テスト"));
            var track = new Track { FilePath = audioPath, Title = "Corrupt" };

            using (sut)
            {
                // Act
                await sut.SetTargetTrackAsync(track);

                // Assert
                Assert.False(sut.IsPreviewState);
                Assert.True(sut.HasValidationError);
                Assert.Contains("ファイル破損テスト", sut.ValidationErrorMessage, StringComparison.Ordinal);
                Assert.Equal(modelAvailable, sut.IsInputState);
                Assert.Equal(!modelAvailable, sut.IsModelMissingState);
            }
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
    /// ローカルモデル未配置の状態でも、既存 .lrc がある楽曲なら手動編集画面で編集・保存でき、
    /// 「↺ 再生成」クリック時にモデル未配置ガイドへ遷移することを検証します。
    /// </summary>
    [Fact]
    public async Task SetTargetTrackAsync_モデル未配置かつ既存Lrcあり_編集画面で手動修正でき再生成押下でモデル未配置ガイドへ遷移する()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"ae_lrc_nomodel_edit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string audioPath = Path.Combine(tempDir, "NoModelSong.flac");
        string lrcPath = Path.Combine(tempDir, "NoModelSong.lrc");
        File.WriteAllText(audioPath, "dummy");
        File.WriteAllText(lrcPath, "[00:05.00]モデル未配置でも編集可能");

        try
        {
            var parser = new StubLrcParser
            {
                ParseHandler = _ => new LyricsDocument(
                    [new LyricLine(0, TimeSpan.FromSeconds(5), "モデル未配置でも編集可能")],
                    isSynced: true,
                    title: "NoModelSong"),
            };
            var (sut, _, textSvc, _, _) = CreateSut(modelAvailable: false, fileExistsFunc: File.Exists, lrcParser: parser);
            textSvc.TextToReturn = "[00:05.00]モデル未配置でも編集可能";
            var track = new Track { FilePath = audioPath, Title = "NoModelSong" };

            using (sut)
            {
                // Act 1: モデル未配置でも既存 .lrc を読み込んで編集画面を表示
                await sut.SetTargetTrackAsync(track);

                // Assert 1
                Assert.True(sut.IsPreviewState);
                Assert.False(sut.IsModelMissingState);
                Assert.Single(sut.PreviewLines);
                Assert.True(sut.SaveLrcCommand.CanExecute(null));

                // Act 2: 保存実行成功
                await sut.ExecuteSaveAsync(overwrite: true);
                Assert.Equal(lrcPath, textSvc.LastSavedPath);

                // Act 3: 「↺ 再生成」押下でモデル未配置ガイドへ遷移
                sut.RegenerateCommand.Execute(null);
                Assert.True(sut.IsModelMissingState);
                Assert.False(sut.IsPreviewState);
            }
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
    /// 楽曲を連続切替した際、古い楽曲の既存 .lrc 非同期読込が遅れて完了しても最新楽曲の状態を上書きしないことを検証します。
    /// </summary>
    [Fact]
    public async Task SetTargetTrackAsync_連続楽曲切替時に古い既存Lrc読込が遅延完了_最新楽曲の状態を上書きしない()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"ae_lrc_race_load_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string oldAudio = Path.Combine(tempDir, "OldSong.flac");
        string oldLrc = Path.Combine(tempDir, "OldSong.lrc");
        string newAudio = Path.Combine(tempDir, "NewSongNoLrc.flac");
        File.WriteAllText(oldAudio, "dummy");
        File.WriteAllText(oldLrc, "[00:01.00]古い曲の歌詞");
        File.WriteAllText(newAudio, "dummy");

        try
        {
            var oldReadGate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var parser = new StubLrcParser
            {
                ParseHandler = text => new LyricsDocument(
                    [new LyricLine(0, TimeSpan.FromSeconds(1), text)],
                    isSynced: true),
            };
            var (sut, _, textSvc, _, _) = CreateSut(fileExistsFunc: File.Exists, lrcParser: parser);
            textSvc.ReadHandler = (path, _, _) => path.EndsWith("OldSong.lrc", StringComparison.OrdinalIgnoreCase)
                ? oldReadGate.Task
                : Task.FromResult(string.Empty);

            var oldTrack = new Track { FilePath = oldAudio, Title = "OldSong" };
            var newTrack = new Track { FilePath = newAudio, Title = "NewSongNoLrc" };

            using (sut)
            {
                // Act: 古い楽曲の既存 .lrc 読込中に、.lrc が存在しない新しい楽曲へ切替
                Task oldLoadTask = sut.SetTargetTrackAsync(oldTrack);
                await sut.SetTargetTrackAsync(newTrack);
                Assert.True(sut.IsInputState);
                Assert.Same(newTrack, sut.TargetTrack);

                // 古い読込を遅延完了させる
                oldReadGate.TrySetResult("[00:01.00]古い曲の歌詞");
                await oldLoadTask;

                // Assert: 最新楽曲の入力・待機状態が保持され、古い .lrc で上書きされない
                Assert.Same(newTrack, sut.TargetTrack);
                Assert.True(sut.IsInputState);
                Assert.False(sut.IsPreviewState);
                Assert.Empty(sut.PreviewLines);
            }
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
    /// 著作権・利用ガイドライン未承諾の初期状態では、モデル準備済み・楽曲設定済み・UTF-8 テキスト選択済みであっても
    /// 両生成コマンドが非活性であり、直接メソッド呼び出しでも生成エンジンが実行されないことを検証します。
    /// </summary>
    [Fact]
    public async Task 著作権ガイドライン未承諾時_モデル準備済みとテキスト選択済みでも生成コマンドは非活性で直接呼び出しも実行されない()
    {
        // Arrange
        var settings = new FakeSettingsService(hasAcceptedLrcCopyrightGuideline: false);
        var (sut, engine, _, _, _) = CreateSut(modelAvailable: true, settingsService: settings);

        using (sut)
        {
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\Unaccepted.flac", Title = "Unaccepted" });
            await sut.InspectAndSelectTextFileAsync(@"C:\Lyrics\utf8.txt");

            // Act
            await sut.GenerateFromTextAsync();
            await sut.TranscribeFromAudioAsync();

            // Assert
            Assert.False(sut.HasAcceptedCopyrightGuideline);
            Assert.True(sut.NeedsCopyrightGuidelineAcceptance);
            Assert.False(sut.IsCopyrightGuidelineExpanded);
            Assert.True(sut.AcceptCopyrightGuidelineCommand.CanExecute(null));
            Assert.False(sut.ToggleCopyrightGuidelineCommand.CanExecute(null));
            Assert.False(sut.GenerateLrcCommand.CanExecute(null));
            Assert.False(sut.TranscribeAudioCommand.CanExecute(null));
            Assert.Null(engine.LastRequest);
            Assert.False(sut.IsPreviewState);
        }
    }

    /// <summary>
    /// AcceptCopyrightGuidelineCommand 実行後に承諾済み状態へ遷移して設定へ永続化され、
    /// 両生成コマンドが即座に活性化して実際に LRC 生成を実行できることを検証します。
    /// </summary>
    [Fact]
    public async Task AcceptCopyrightGuidelineCommand_実行後_承諾状態へ遷移して設定永続化され両生成コマンドが活性および実行可能になる()
    {
        // Arrange
        var settings = new FakeSettingsService(hasAcceptedLrcCopyrightGuideline: false);
        var (sut, engine, _, _, _) = CreateSut(modelAvailable: true, settingsService: settings);
        var changedProps = new List<string?>();
        sut.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName);

        using (sut)
        {
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\AcceptFlow.flac", Title = "AcceptFlow" });
            await sut.InspectAndSelectTextFileAsync(@"C:\Lyrics\utf8.txt");
            Assert.False(sut.GenerateLrcCommand.CanExecute(null));
            Assert.False(sut.TranscribeAudioCommand.CanExecute(null));

            // Act
            sut.AcceptCopyrightGuidelineCommand.Execute(null);
            await sut.AcceptCopyrightGuidelineAsync();

            // Assert (承諾反映・永続化・コマンド活性)
            Assert.True(sut.HasAcceptedCopyrightGuideline);
            Assert.False(sut.NeedsCopyrightGuidelineAcceptance);
            Assert.True(settings.StoredHasAcceptedLrcCopyrightGuideline);
            Assert.Equal(1, settings.UpdateSettingsAsyncCallCount);
            Assert.Contains(nameof(LrcGeneratorViewModel.HasAcceptedCopyrightGuideline), changedProps);
            Assert.Contains(nameof(LrcGeneratorViewModel.NeedsCopyrightGuidelineAcceptance), changedProps);
            Assert.False(sut.AcceptCopyrightGuidelineCommand.CanExecute(null));
            Assert.True(sut.ToggleCopyrightGuidelineCommand.CanExecute(null));
            Assert.True(sut.GenerateLrcCommand.CanExecute(null));
            Assert.True(sut.TranscribeAudioCommand.CanExecute(null));

            // Act 2: 生成実行が正常に完了する
            await sut.GenerateFromTextAsync();

            // Assert 2
            Assert.NotNull(engine.LastRequest);
            Assert.Equal(LrcGenerationMode.TextAlignment, engine.LastRequest!.Mode);
            Assert.True(sut.IsPreviewState);
        }
    }

    /// <summary>
    /// 設定に HasAcceptedLrcCopyrightGuideline = true が保存済みの状態で初期化した場合、
    /// 初回承諾操作なしで即座に両生成コマンドが利用可能であることを検証します。
    /// </summary>
    [Fact]
    public async Task コンストラクタ_設定に承諾済みが保存されている場合_初回承諾なしで即座に生成コマンドが利用可能である()
    {
        // Arrange
        var settings = new FakeSettingsService(hasAcceptedLrcCopyrightGuideline: true);
        var (sut, _, _, _, _) = CreateSut(modelAvailable: true, settingsService: settings);

        using (sut)
        {
            // Act
            sut.SetTargetTrack(new Track { FilePath = @"C:\Music\PreAccepted.flac", Title = "PreAccepted" });
            await sut.InspectAndSelectTextFileAsync(@"C:\Lyrics\utf8.txt");

            // Assert
            Assert.True(sut.HasAcceptedCopyrightGuideline);
            Assert.False(sut.NeedsCopyrightGuidelineAcceptance);
            Assert.False(sut.AcceptCopyrightGuidelineCommand.CanExecute(null));
            Assert.True(sut.ToggleCopyrightGuidelineCommand.CanExecute(null));
            Assert.True(sut.GenerateLrcCommand.CanExecute(null));
            Assert.True(sut.TranscribeAudioCommand.CanExecute(null));
        }
    }

    /// <summary>
    /// ToggleCopyrightGuidelineCommand で IsCopyrightGuidelineExpanded が正しく展開・折りたたみトグルされることを検証します。
    /// </summary>
    [Fact]
    public void ToggleCopyrightGuidelineCommand_承諾済み状態で実行_IsCopyrightGuidelineExpandedが正しく開閉トグルされる()
    {
        // Arrange
        var (sut, _, _, _, _) = CreateSut(acceptedCopyrightGuideline: true);
        var changedProps = new List<string?>();
        sut.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName);

        using (sut)
        {
            Assert.False(sut.IsCopyrightGuidelineExpanded);

            // Act & Assert 1: 展開
            sut.ToggleCopyrightGuidelineCommand.Execute(null);
            Assert.True(sut.IsCopyrightGuidelineExpanded);
            Assert.Contains(nameof(LrcGeneratorViewModel.IsCopyrightGuidelineExpanded), changedProps);

            // Act & Assert 2: 折りたたみ
            sut.ToggleCopyrightGuidelineCommand.Execute(null);
            Assert.False(sut.IsCopyrightGuidelineExpanded);
        }
    }

    /// <summary>
    /// DI コンテナから LrcGeneratorViewModel を解決した際に ISettingsService が自動注入され、
    /// 著作権ガイドラインの承諾状態が正しく読み書きされることを検証します。
    /// </summary>
    [Fact]
    public async Task DIコンテナ解決_ISettingsService登録済みの場合_設定サービスが自動注入され承諾状態が連動する()
    {
        // Arrange
        var settings = new FakeSettingsService(hasAcceptedLrcCopyrightGuideline: false);
        var services = new ServiceCollection();
        var engine = new FakeAlignmentEngine();
        var textSvc = new FakeTextEncodingService();
        var modelMgr = new FakeModelManager { IsAvailable = true };
        var lyricsSvc = new LyricsApplicationService(new StubLyricsProvider(), new InMemoryEventBus());
        var genSvc = new LrcGenerationApplicationService(engine, textSvc, modelMgr, new StubLrcParser(), lyricsSvc);

        services.AddSingleton(genSvc);
        services.AddSingleton(new Mock<IAudioService>().Object);
        services.AddSingleton<IUiDispatcher>(new ImmediateDispatcherStub());
        services.AddSingleton<ISettingsService>(settings);
        services.AddSingleton<LrcGeneratorViewModel>();

        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        using LrcGeneratorViewModel sut = provider.GetRequiredService<LrcGeneratorViewModel>();
        bool initialAccepted = sut.HasAcceptedCopyrightGuideline;
        await sut.AcceptCopyrightGuidelineAsync();

        // Assert
        Assert.False(initialAccepted);
        Assert.True(sut.HasAcceptedCopyrightGuideline);
        Assert.True(settings.StoredHasAcceptedLrcCopyrightGuideline);
        Assert.Equal(1, settings.UpdateSettingsAsyncCallCount);
    }
}
