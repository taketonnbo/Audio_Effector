using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using Track = AudioEffector.Domain.Entities.Track;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="LyricsViewModel"/> の読み込み・レース防止・同期ハイライト・行クリックシーク・手動スクロール一時停止・イベント購読・解放を検証するテストクラス
/// </summary>
public sealed class LyricsViewModelTests
{
    private sealed class ImmediateDispatcherStub : IUiDispatcher
    {
        public void Invoke(Action action) => action();

        public void BeginInvoke(Action action) => action();
    }

    private sealed class ControlledLyricsProviderStub : ILyricsProvider
    {
        private readonly Func<Track, CancellationToken, Task<LyricsDocument?>> _handler;

        public int CallCount { get; private set; }

        public ControlledLyricsProviderStub(Func<Track, CancellationToken, Task<LyricsDocument?>> handler)
        {
            _handler = handler;
        }

        public Task<LyricsDocument?> LoadLyricsAsync(Track track, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _handler(track, cancellationToken);
        }
    }

    private static Track CreateTrack(string filePath, string title = "Sample Song")
    {
        return new Track(
            TrackId.New(),
            AudioPath.Create(filePath),
            title,
            "Sample Artist",
            "Sample Album",
            TimeSpan.FromMinutes(3));
    }

    private static LyricsDocument CreateSyncedDocument(
        LyricsSourceType sourceType = LyricsSourceType.LrcFile,
        TimeSpan offset = default)
    {
        var lines = new[]
        {
            new LyricLine(0, TimeSpan.FromSeconds(10), "First line"),
            new LyricLine(1, TimeSpan.FromSeconds(20), "Second line (EN)"),
            new LyricLine(2, TimeSpan.FromSeconds(20), "2行目（日本語訳）"),
            new LyricLine(3, TimeSpan.FromSeconds(30), string.Empty),
            new LyricLine(4, TimeSpan.FromSeconds(40), "After interlude"),
            new LyricLine(5, TimeSpan.FromSeconds(50), "Fifth line"),
            new LyricLine(6, TimeSpan.FromSeconds(60), "Sixth line"),
            new LyricLine(7, TimeSpan.FromSeconds(70), "Seventh line"),
        };

        return new LyricsDocument(
            lines,
            isSynced: true,
            offset: offset,
            sourceType: sourceType,
            sourcePath: @"C:\Music\sample.lrc");
    }

    private static LyricsDocument CreateUnsyncedDocument()
    {
        var lines = new[]
        {
            new LyricLine(0, null, "Plain line 1"),
            new LyricLine(1, null, "Plain line 2"),
        };

        return new LyricsDocument(
            lines,
            isSynced: false,
            sourceType: LyricsSourceType.EmbeddedTag,
            sourcePath: @"C:\Music\sample.flac");
    }

    private static Mock<IAudioService> CreateAudioServiceMock(
        Track? currentTrack = null,
        bool isPlaying = false,
        TimeSpan? currentTime = null,
        TimeSpan? totalTime = null)
    {
        var mock = new Mock<IAudioService>();
        mock.Setup(a => a.CurrentTrack).Returns(currentTrack);
        mock.Setup(a => a.IsPlaying).Returns(isPlaying);
        mock.Setup(a => a.CurrentTime).Returns(currentTime ?? TimeSpan.Zero);
        mock.Setup(a => a.TotalTime).Returns(totalTime ?? TimeSpan.FromSeconds(200));
        return mock;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(15);
        }

        return condition();
    }

    /// <summary>
    /// 再生中楽曲がない初期状態ではエンプティステートとなり、コマンド非活性かつ同期タイマー停止であることを検証します。
    /// </summary>
    [Fact]
    public void 初期化_再生中楽曲なしの場合_未再生エンプティ状態かつタイマー停止であること()
    {
        // Arrange
        var audioMock = CreateAudioServiceMock();
        var eventBus = new InMemoryEventBus();
        var provider = new ControlledLyricsProviderStub((_, _) => Task.FromResult<LyricsDocument?>(null));
        var appService = new LyricsApplicationService(provider, eventBus);

        // Act
        using var sut = new LyricsViewModel(audioMock.Object, eventBus, appService, new ImmediateDispatcherStub());

        // Assert
        Assert.False(sut.HasTrack);
        Assert.False(sut.HasLyrics);
        Assert.True(sut.IsNoTrackState);
        Assert.False(sut.IsLyricsNotFoundState);
        Assert.False(sut.IsLoading);
        Assert.False(sut.HasError);
        Assert.Equal("再生中の楽曲がありません", sut.EmptyStateTitle);
        Assert.False(sut.CanReloadLyrics);
        Assert.False(sut.CanGenerateLrc);
        Assert.False(sut.HasSourceBadge);
        Assert.False(sut.IsSyncTimerRunning);
        Assert.Equal(-1, sut.CurrentLineIndex);
    }

    /// <summary>
    /// 同期 LRC 歌詞を持つ楽曲へ切り替えた際、歌詞行・ソースバッジ・タイムスタンプ表示が正しく構築されることを検証します。
    /// </summary>
    [Fact]
    public async Task TrackChangedEvent_同期LRC歌詞あり_行リストとLRC同期バッジが構築されること()
    {
        // Arrange
        var track = CreateTrack(@"C:\Music\track01.flac");
        var document = CreateSyncedDocument(LyricsSourceType.LrcFile);
        var audioMock = CreateAudioServiceMock();
        var eventBus = new InMemoryEventBus();
        var provider = new ControlledLyricsProviderStub((_, _) => Task.FromResult<LyricsDocument?>(document));
        var appService = new LyricsApplicationService(provider, eventBus);
        using var sut = new LyricsViewModel(audioMock.Object, eventBus, appService, new ImmediateDispatcherStub());

        // Act
        await eventBus.PublishAsync(new TrackChangedEvent(track, TimeSpan.Zero));
        bool loaded = await WaitUntilAsync(() => !sut.IsLoading && sut.HasLyrics);

        // Assert
        Assert.True(loaded);
        Assert.True(sut.HasTrack);
        Assert.True(sut.HasLyrics);
        Assert.True(sut.IsSynced);
        Assert.False(sut.IsUnsynced);
        Assert.Equal(LyricsSourceType.LrcFile, sut.SourceType);
        Assert.Equal("LRC 同期", sut.SourceBadgeText);
        Assert.True(sut.HasSourceBadge);
        Assert.Equal(8, sut.Lines.Count);
        Assert.Equal("[00:10]", sut.Lines[0].TimestampDisplay);
        Assert.True(sut.Lines[0].IsSeekable);
        Assert.True(sut.Lines[3].IsInstrumental);
        Assert.False(sut.Lines[3].IsSeekable);
    }

    /// <summary>
    /// 非同期（プレーン）歌詞を読み込んだ際、同期なしバッジとなり全行がシーク対象外であることを検証します。
    /// </summary>
    [Fact]
    public async Task TrackChangedEvent_非同期プレーン歌詞の場合_同期なし表示かつ全行シーク不可であること()
    {
        // Arrange
        var track = CreateTrack(@"C:\Music\plain.flac");
        var audioMock = CreateAudioServiceMock();
        var eventBus = new InMemoryEventBus();
        var provider = new ControlledLyricsProviderStub((_, _) => Task.FromResult<LyricsDocument?>(CreateUnsyncedDocument()));
        var appService = new LyricsApplicationService(provider, eventBus);
        using var sut = new LyricsViewModel(audioMock.Object, eventBus, appService, new ImmediateDispatcherStub());

        // Act
        await eventBus.PublishAsync(new TrackChangedEvent(track, TimeSpan.Zero));
        bool loaded = await WaitUntilAsync(() => !sut.IsLoading && sut.HasLyrics);

        // Assert
        Assert.True(loaded);
        Assert.False(sut.IsSynced);
        Assert.True(sut.IsUnsynced);
        Assert.Equal("同期なし", sut.SourceBadgeText);
        Assert.All(sut.Lines, line => Assert.False(line.IsSeekable));
        Assert.False(sut.SeekToLineCommand.CanExecute(sut.Lines[0]));
    }

    /// <summary>
    /// 歌詞未検出時および読み込み例外発生時にそれぞれ適切なフォールバック状態へ遷移することを検証します。
    /// </summary>
    [Fact]
    public async Task LoadLyricsForTrackAsync_未検出および例外発生時_それぞれの状態へ安全に遷移すること()
    {
        // Arrange
        var trackNotFound = CreateTrack(@"C:\Music\missing.flac");
        var trackError = CreateTrack(@"C:\Music\broken.flac");
        var audioMock = CreateAudioServiceMock();
        var eventBus = new InMemoryEventBus();
        var provider = new ControlledLyricsProviderStub((track, _) =>
        {
            if (track.FilePath == trackError.FilePath)
            {
                throw new InvalidOperationException("Simulated I/O failure");
            }

            return Task.FromResult<LyricsDocument?>(null);
        });
        var appService = new LyricsApplicationService(provider, eventBus);
        using var sut = new LyricsViewModel(audioMock.Object, eventBus, appService, new ImmediateDispatcherStub());

        // Act 1: 歌詞未検出
        await eventBus.PublishAsync(new TrackChangedEvent(trackNotFound, TimeSpan.Zero));
        bool notFoundDone = await WaitUntilAsync(() => !sut.IsLoading && sut.IsLyricsNotFoundState);

        // Assert 1
        Assert.True(notFoundDone);
        Assert.Equal("歌詞が見つかりません", sut.EmptyStateTitle);
        Assert.False(sut.HasError);

        // Act 2: 例外発生
        await eventBus.PublishAsync(new TrackChangedEvent(trackError, TimeSpan.Zero));
        bool errorDone = await WaitUntilAsync(() => !sut.IsLoading && sut.HasError);

        // Assert 2
        Assert.True(errorDone);
        Assert.Equal("歌詞を読み込めませんでした", sut.EmptyStateTitle);
        Assert.False(sut.HasLyrics);
    }

    /// <summary>
    /// 連続した楽曲切り替えで旧楽曲の非同期読み込みが後から完了しても、最新楽曲の歌詞が上書きされないことを検証します。
    /// </summary>
    [Fact]
    public async Task TrackChangedEvent_連続切替で旧読込が遅延完了した場合_旧読込はキャンセルされ最新楽曲の歌詞が維持されること()
    {
        // Arrange
        var trackA = CreateTrack(@"C:\Music\songA.flac", "Song A");
        var trackB = CreateTrack(@"C:\Music\songB.flac", "Song B");
        var docA = new LyricsDocument(new[] { new LyricLine(0, TimeSpan.FromSeconds(1), "Old Lyrics A") }, true, sourceType: LyricsSourceType.LrcFile);
        var docB = new LyricsDocument(new[] { new LyricLine(0, TimeSpan.FromSeconds(1), "Latest Lyrics B") }, true, sourceType: LyricsSourceType.LrcFile);

        var tcsA = new TaskCompletionSource<LyricsDocument?>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool tokenACancelled = false;

        var provider = new ControlledLyricsProviderStub((track, ct) =>
        {
            if (track.FilePath == trackA.FilePath)
            {
                ct.Register(() => tokenACancelled = true);
                return tcsA.Task;
            }

            return Task.FromResult<LyricsDocument?>(docB);
        });

        var audioMock = CreateAudioServiceMock();
        var eventBus = new InMemoryEventBus();
        var appService = new LyricsApplicationService(provider, eventBus);
        using var sut = new LyricsViewModel(audioMock.Object, eventBus, appService, new ImmediateDispatcherStub());

        // Act
        await eventBus.PublishAsync(new TrackChangedEvent(trackA, TimeSpan.Zero));
        Assert.True(sut.IsLoading);

        await eventBus.PublishAsync(new TrackChangedEvent(trackB, TimeSpan.Zero));
        bool trackBLoaded = await WaitUntilAsync(() => !sut.IsLoading && sut.Lines.Count == 1 && sut.Lines[0].Text == "Latest Lyrics B");

        // 遅延して旧トラック A のタスクを完了させる
        tcsA.TrySetResult(docA);
        await Task.Delay(30);

        // Assert
        Assert.True(trackBLoaded);
        Assert.True(tokenACancelled);
        Assert.Equal(trackB, sut.CurrentTrack);
        Assert.Single(sut.Lines);
        Assert.Equal("Latest Lyrics B", sut.Lines[0].Text);
    }

    /// <summary>
    /// 再生位置の進行に応じて同一タイムスタンプ複数行の同時ハイライト・差分通知・間奏空行でのハイライト解除が行われることを検証します。
    /// </summary>
    [Fact]
    public async Task UpdatePlaybackPosition_イントロおよび同一時刻複数行および間奏空行_仕様通りハイライトとスクロール要求が行われること()
    {
        // Arrange
        var track = CreateTrack(@"C:\Music\sync.flac");
        var audioMock = CreateAudioServiceMock();
        var eventBus = new InMemoryEventBus();
        var provider = new ControlledLyricsProviderStub((_, _) => Task.FromResult<LyricsDocument?>(CreateSyncedDocument()));
        var appService = new LyricsApplicationService(provider, eventBus);
        using var sut = new LyricsViewModel(audioMock.Object, eventBus, appService, new ImmediateDispatcherStub());

        await eventBus.PublishAsync(new TrackChangedEvent(track, TimeSpan.Zero));
        Assert.True(await WaitUntilAsync(() => !sut.IsLoading && sut.HasLyrics));

        var scrollRequests = new List<int>();
        sut.ScrollToLineRequested += idx => scrollRequests.Add(idx);

        // Act & Assert 1: イントロ区間（先頭タイムタグ 10 秒より前）
        sut.UpdatePlaybackPosition(TimeSpan.FromSeconds(5));
        Assert.Equal(-1, sut.CurrentLineIndex);
        Assert.All(sut.Lines, l => Assert.False(l.IsActive));

        // Act & Assert 2: 同一タイムスタンプ複数行（20秒: index 1, 2 が同時ハイライト、スクロール基準は先頭 index 1）
        sut.UpdatePlaybackPosition(TimeSpan.FromSeconds(22));
        Assert.Equal(1, sut.CurrentLineIndex);
        Assert.Equal(2, sut.CurrentLineEndIndex);
        Assert.False(sut.Lines[0].IsActive);
        Assert.True(sut.Lines[0].IsNearActive);
        Assert.True(sut.Lines[1].IsActive);
        Assert.True(sut.Lines[2].IsActive);
        Assert.False(sut.Lines[3].IsActive);
        Assert.True(sut.Lines[4].IsNearActive);
        Assert.False(sut.Lines[7].IsNearActive);
        Assert.Equal(1, Assert.Single(scrollRequests));

        // Act & Assert 3: 同一アクティブ範囲内の進行では PropertyChanged もスクロールも再発火しない
        int linePropertyChanges = 0;
        PropertyChangedEventHandler handler = (_, _) => linePropertyChanges++;
        sut.Lines[1].PropertyChanged += handler;
        sut.UpdatePlaybackPosition(TimeSpan.FromSeconds(25));
        sut.Lines[1].PropertyChanged -= handler;
        Assert.Equal(0, linePropertyChanges);
        Assert.Single(scrollRequests);

        // Act & Assert 4: 間奏空行（30秒: index 3）到達時は全ハイライト解除
        sut.UpdatePlaybackPosition(TimeSpan.FromSeconds(35));
        Assert.Equal(-1, sut.CurrentLineIndex);
        Assert.Equal(-1, sut.CurrentLineEndIndex);
        Assert.All(sut.Lines, l => Assert.False(l.IsActive));
    }

    /// <summary>
    /// SeekToLineCommand 実行時、offset 補正後タイムスタンプと総時間から 0.0〜1.0 比率を計算して SeekTo を呼び出し、再生状態を変更しないことを検証します。
    /// </summary>
    [Fact]
    public async Task SeekToLineCommand_同期歌詞行クリック時_補正後時刻の比率でシークしクランプおよび一時停止維持が成立すること()
    {
        // Arrange
        var track = CreateTrack(@"C:\Music\offset.flac");
        // offset = +2秒 → raw 20秒の有効タイムスタンプは 18秒、TotalTime = 100秒 → 比率 0.18
        var document = CreateSyncedDocument(offset: TimeSpan.FromSeconds(2));
        var audioMock = CreateAudioServiceMock(isPlaying: false, totalTime: TimeSpan.FromSeconds(100));
        var eventBus = new InMemoryEventBus();
        var provider = new ControlledLyricsProviderStub((_, _) => Task.FromResult<LyricsDocument?>(document));
        var appService = new LyricsApplicationService(provider, eventBus);
        using var sut = new LyricsViewModel(audioMock.Object, eventBus, appService, new ImmediateDispatcherStub());

        await eventBus.PublishAsync(new TrackChangedEvent(track, TimeSpan.Zero));
        Assert.True(await WaitUntilAsync(() => !sut.IsLoading && sut.HasLyrics));

        sut.NotifyManualScroll();
        Assert.True(sut.IsAutoScrollPaused);

        // Act
        Assert.True(sut.SeekToLineCommand.CanExecute(sut.Lines[1]));
        Assert.False(sut.SeekToLineCommand.CanExecute(sut.Lines[3])); // 間奏空行は不可
        sut.SeekToLineCommand.Execute(sut.Lines[1]);

        // Assert
        audioMock.Verify(a => a.SeekTo(It.Is<double>(r => Math.Abs(r - 0.18) < 0.0001)), Times.Once);
        audioMock.Verify(a => a.TogglePlayPause(), Times.Never);
        audioMock.Verify(a => a.ResumeAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(sut.IsAutoScrollPaused);
        Assert.Equal(1, sut.CurrentLineIndex);
    }

    /// <summary>
    /// 手動スクロール一時停止から、ボタン復帰・自動タイマー復帰・再スクロールリセット・楽曲切替解除が仕様通り動作することを検証します。
    /// </summary>
    [Fact]
    public async Task ManualScroll_一時停止と自動復帰および即時復帰操作_4条件すべて仕様通り動作すること()
    {
        // Arrange
        var track1 = CreateTrack(@"C:\Music\scroll1.flac");
        var track2 = CreateTrack(@"C:\Music\scroll2.flac");
        var audioMock = CreateAudioServiceMock();
        var eventBus = new InMemoryEventBus();
        var provider = new ControlledLyricsProviderStub((_, _) => Task.FromResult<LyricsDocument?>(CreateSyncedDocument()));
        var appService = new LyricsApplicationService(provider, eventBus);
        using var sut = new LyricsViewModel(
            audioMock.Object,
            eventBus,
            appService,
            new ImmediateDispatcherStub(),
            syncInterval: TimeSpan.FromMilliseconds(50),
            autoScrollResumeDelay: TimeSpan.FromMilliseconds(80));

        await eventBus.PublishAsync(new TrackChangedEvent(track1, TimeSpan.Zero));
        Assert.True(await WaitUntilAsync(() => !sut.IsLoading && sut.HasLyrics));
        sut.UpdatePlaybackPosition(TimeSpan.FromSeconds(12));

        var scrollRequests = new List<int>();
        sut.ScrollToLineRequested += idx => scrollRequests.Add(idx);

        // Act & Assert 1: 手動スクロール中は自動スクロールが抑止され、ボタンで即時復帰する
        sut.NotifyManualScroll();
        Assert.True(sut.IsAutoScrollPaused);
        Assert.True(sut.ShowResumeAutoScrollButton);
        Assert.True(sut.IsAutoScrollResumeTimerRunning);

        sut.UpdatePlaybackPosition(TimeSpan.FromSeconds(42));
        Assert.Empty(scrollRequests);

        sut.ResumeAutoScrollCommand.Execute(null);
        Assert.False(sut.IsAutoScrollPaused);
        Assert.False(sut.IsAutoScrollResumeTimerRunning);
        Assert.Equal(4, Assert.Single(scrollRequests));

        // Act & Assert 2: 自動復帰タイマー満了で自動復帰する
        scrollRequests.Clear();
        sut.NotifyManualScroll();
        Assert.True(sut.IsAutoScrollPaused);
        bool autoResumed = await WaitUntilAsync(() => !sut.IsAutoScrollPaused, timeoutMs: 2000);
        Assert.True(autoResumed);
        Assert.Equal(4, Assert.Single(scrollRequests));

        // Act & Assert 3: 楽曲切替で即時解除される
        sut.NotifyManualScroll();
        Assert.True(sut.IsAutoScrollPaused);
        await eventBus.PublishAsync(new TrackChangedEvent(track2, TimeSpan.Zero));
        Assert.False(sut.IsAutoScrollPaused);
        Assert.False(sut.IsAutoScrollResumeTimerRunning);
    }

    /// <summary>
    /// IsActiveView と再生状態の組み合わせにより、表示中かつ再生中かつ同期歌詞のときのみ同期タイマーが稼働することを検証します。
    /// </summary>
    [Fact]
    public async Task IsActiveView_再生状態との連動_表示中かつ再生中のみ同期タイマーが稼働すること()
    {
        // Arrange
        var track = CreateTrack(@"C:\Music\timer.flac");
        var audioMock = CreateAudioServiceMock(isPlaying: true);
        var eventBus = new InMemoryEventBus();
        var provider = new ControlledLyricsProviderStub((_, _) => Task.FromResult<LyricsDocument?>(CreateSyncedDocument()));
        var appService = new LyricsApplicationService(provider, eventBus);
        using var sut = new LyricsViewModel(audioMock.Object, eventBus, appService, new ImmediateDispatcherStub());

        await eventBus.PublishAsync(new TrackChangedEvent(track, TimeSpan.Zero));
        Assert.True(await WaitUntilAsync(() => !sut.IsLoading && sut.HasLyrics));

        // Assert 1: 非表示時は再生中でもタイマー停止
        Assert.False(sut.IsActiveView);
        Assert.False(sut.IsSyncTimerRunning);

        // Act & Assert 2: 表示 ON で稼働開始
        sut.IsActiveView = true;
        Assert.True(sut.IsSyncTimerRunning);

        // Act & Assert 3: 一時停止で停止
        await eventBus.PublishAsync(new PlaybackStateChangedEvent(PlaybackState.Paused));
        Assert.False(sut.IsSyncTimerRunning);

        // Act & Assert 4: 再生再開で再稼働、非表示で停止
        await eventBus.PublishAsync(new PlaybackStateChangedEvent(PlaybackState.Playing));
        Assert.True(sut.IsSyncTimerRunning);

        sut.IsActiveView = false;
        Assert.False(sut.IsSyncTimerRunning);
    }

    /// <summary>
    /// LyricsUpdatedEvent 受信時、表示中なら即時再読み込み・非表示中なら再表示時に強制再読み込みされることを検証します。
    /// </summary>
    [Fact]
    public async Task LyricsUpdatedEvent_キャッシュ無効化受信時_表示状態に応じて最新歌詞へ再読み込みされること()
    {
        // Arrange
        var track = CreateTrack(@"C:\Music\updated.flac");
        int loadVersion = 0;
        var provider = new ControlledLyricsProviderStub((_, _) =>
        {
            loadVersion++;
            var doc = new LyricsDocument(
                new[] { new LyricLine(0, TimeSpan.FromSeconds(1), $"Version {loadVersion}") },
                isSynced: true,
                sourceType: LyricsSourceType.LrcFile);
            return Task.FromResult<LyricsDocument?>(doc);
        });

        var audioMock = CreateAudioServiceMock();
        var eventBus = new InMemoryEventBus();
        var appService = new LyricsApplicationService(provider, eventBus);
        using var sut = new LyricsViewModel(audioMock.Object, eventBus, appService, new ImmediateDispatcherStub());

        await eventBus.PublishAsync(new TrackChangedEvent(track, TimeSpan.Zero));
        Assert.True(await WaitUntilAsync(() => !sut.IsLoading && sut.Lines.Count == 1 && sut.Lines[0].Text == "Version 1"));

        // Act 1: 表示中にキャッシュ無効化イベント発行 → 即時再読み込み（二重購読されないため 1 回のみ増加）
        sut.IsActiveView = true;
        await appService.InvalidateCacheAsync(track.FilePath);
        Assert.True(await WaitUntilAsync(() => !sut.IsLoading && sut.Lines.Count == 1 && sut.Lines[0].Text == "Version 2"));
        Assert.Equal(2, provider.CallCount);

        // Act 2: 非表示中にキャッシュ無効化イベント発行 → 非表示中は読まず、再表示時に再読み込み
        sut.IsActiveView = false;
        await appService.InvalidateCacheAsync(track.FilePath);
        Assert.Equal(2, provider.CallCount);

        sut.IsActiveView = true;
        Assert.True(await WaitUntilAsync(() => !sut.IsLoading && sut.Lines.Count == 1 && sut.Lines[0].Text == "Version 3"));
        Assert.Equal(3, provider.CallCount);
    }

    /// <summary>
    /// ReloadLyricsCommand および RequestGenerateLrcCommand の活性条件とイベントフック動作を検証します。
    /// </summary>
    [Fact]
    public async Task Commands_再読み込みおよびLRC生成フック_ハンドラ有無に応じて安全に動作すること()
    {
        // Arrange
        var track = CreateTrack(@"C:\Music\cmd.flac");
        var provider = new ControlledLyricsProviderStub((_, _) => Task.FromResult<LyricsDocument?>(CreateSyncedDocument()));
        var audioMock = CreateAudioServiceMock();
        var eventBus = new InMemoryEventBus();
        var appService = new LyricsApplicationService(provider, eventBus);
        using var sut = new LyricsViewModel(audioMock.Object, eventBus, appService, new ImmediateDispatcherStub());

        await eventBus.PublishAsync(new TrackChangedEvent(track, TimeSpan.Zero));
        Assert.True(await WaitUntilAsync(() => !sut.IsLoading && sut.HasLyrics));

        // Assert 1: GenerateLrcRequested 未接続時は非活性かつ安全
        Assert.False(sut.CanGenerateLrc);
        Assert.False(sut.RequestGenerateLrcCommand.CanExecute(null));
        sut.RequestGenerateLrcCommand.Execute(null);

        // Act 2: ハンドラ接続後に実行
        Track? requestedTrack = null;
        sut.GenerateLrcRequested += t => requestedTrack = t;
        Assert.True(sut.CanGenerateLrc);
        Assert.True(sut.RequestGenerateLrcCommand.CanExecute(null));
        sut.RequestGenerateLrcCommand.Execute(null);
        Assert.Same(track, requestedTrack);

        // Act 3: ReloadLyricsCommand で強制再読み込み
        Assert.True(sut.CanReloadLyrics);
        sut.ReloadLyricsCommand.Execute(null);
        Assert.True(await WaitUntilAsync(() => provider.CallCount == 2 && !sut.IsLoading));
    }

    /// <summary>
    /// Dispose 呼び出し後、同期タイマーが停止し後続イベントを受信しても状態が変化しないことを検証します。
    /// </summary>
    [Fact]
    public async Task Dispose_実行後_タイマー停止および全イベント購読が解除されること()
    {
        // Arrange
        var track1 = CreateTrack(@"C:\Music\before_dispose.flac");
        var track2 = CreateTrack(@"C:\Music\after_dispose.flac");
        var provider = new ControlledLyricsProviderStub((_, _) => Task.FromResult<LyricsDocument?>(CreateSyncedDocument()));
        var audioMock = CreateAudioServiceMock(isPlaying: true);
        var eventBus = new InMemoryEventBus();
        var appService = new LyricsApplicationService(provider, eventBus);
        var sut = new LyricsViewModel(audioMock.Object, eventBus, appService, new ImmediateDispatcherStub());

        await eventBus.PublishAsync(new TrackChangedEvent(track1, TimeSpan.Zero));
        Assert.True(await WaitUntilAsync(() => !sut.IsLoading && sut.HasLyrics));
        sut.IsActiveView = true;
        Assert.True(sut.IsSyncTimerRunning);

        // Act
        sut.Dispose();
        await eventBus.PublishAsync(new TrackChangedEvent(track2, TimeSpan.Zero));
        audioMock.Raise(a => a.TrackChanged += null, track2);
        await appService.InvalidateCacheAsync(track1.FilePath);

        // Assert
        Assert.False(sut.IsSyncTimerRunning);
        Assert.Equal(track1, sut.CurrentTrack);
        Assert.Equal(1, provider.CallCount);
    }
}
