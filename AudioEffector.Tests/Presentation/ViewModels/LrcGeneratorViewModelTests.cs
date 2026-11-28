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
        public string TextToReturn { get; set; } = "一行目の歌詞\n二行目の歌詞";
        public string? LastSavedPath { get; private set; }
        public bool LastOverwriteFlag { get; private set; }

        public Task<TextEncodingInspectionResult> InspectAsync(string textFilePath, CancellationToken cancellationToken = default)
        {
            TextEncodingInspectionResult result = InspectionFactory is not null
                ? InspectionFactory(textFilePath)
                : new TextEncodingInspectionResult(textFilePath, true, false, "UTF-8", 2, 2);
            return Task.FromResult(result);
        }

        public Task<string> ReadAsUtf8TextAsync(string textFilePath, bool allowEncodingConversion, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TextToReturn);
        }

        public Task SaveLrcFileAsync(string destinationPath, string lrcContent, bool overwrite, CancellationToken cancellationToken = default)
        {
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
        public LyricsDocument Parse(string content) => LyricsDocument.Empty;

        public string Format(LyricsDocument document)
        {
            return string.Join(
                Environment.NewLine,
                System.Linq.Enumerable.Select(document.Lines, l => $"[{l.Timestamp?.TotalSeconds:00.00}]{l.Text}"));
        }
    }

    private sealed class StubLyricsProvider : ILyricsProvider
    {
        public Task<LyricsDocument?> LoadLyricsAsync(Track track, CancellationToken cancellationToken = default)
            => Task.FromResult<LyricsDocument?>(null);
    }

    private static (LrcGeneratorViewModel Sut, FakeAlignmentEngine Engine, FakeTextEncodingService TextSvc, FakeModelManager ModelMgr, LyricsApplicationService LyricsSvc) CreateSut(
        bool modelAvailable = true,
        Mock<IAudioService>? audioMock = null)
    {
        var engine = new FakeAlignmentEngine();
        var textSvc = new FakeTextEncodingService();
        var modelMgr = new FakeModelManager { IsAvailable = modelAvailable };
        var eventBus = new InMemoryEventBus();
        var lyricsSvc = new LyricsApplicationService(new StubLyricsProvider(), eventBus);
        var genSvc = new LrcGenerationApplicationService(engine, textSvc, modelMgr, new StubLrcParser(), lyricsSvc);

        audioMock ??= new Mock<IAudioService>();
        var sut = new LrcGeneratorViewModel(
            genSvc,
            audioMock.Object,
            new ImmediateDispatcherStub(),
            selectTextFileFunc: () => null,
            selectModelFileFunc: () => null,
            openFolderAction: _ => { });

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
}
