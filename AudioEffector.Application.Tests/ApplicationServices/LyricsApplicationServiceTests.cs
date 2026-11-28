using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using Xunit;

namespace AudioEffector.Application.Tests.ApplicationServices;

/// <summary>
/// LyricsApplicationService のキャッシュヒット・強制再読み込み・キャッシュ無効化と更新通知・キャンセル動作を検証するテストクラスです。
/// </summary>
public sealed class LyricsApplicationServiceTests
{
    private static Track CreateTrack(string filePath)
    {
        return new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(filePath),
            title: "Test Song",
            artist: "Test Artist",
            album: "Test Album",
            duration: TimeSpan.FromSeconds(180));
    }

    private static LyricsDocument CreateDocument(string text)
    {
        return new LyricsDocument(
            lines: [new LyricLine(0, TimeSpan.FromSeconds(5), text)],
            isSynced: true,
            sourceType: LyricsSourceType.LrcFile);
    }

    /// <summary>
    /// コンストラクタの必須引数に null を指定した場合、ArgumentNullException がスローされることを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_必須引数null指定_ArgumentNullExceptionをスローすること()
    {
        // Arrange
        var fakeProvider = new FakeLyricsProvider();
        var eventBus = new InMemoryEventBus();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LyricsApplicationService(null!, eventBus));
        Assert.Throws<ArgumentNullException>(() => new LyricsApplicationService(fakeProvider, null!));
    }

    /// <summary>
    /// 同一トラックに対する連続呼び出しではキャッシュがヒットし、ILyricsProvider の読み込みが 1 回のみ実行されることを検証します。
    /// </summary>
    [Fact]
    public async Task LoadForTrackAsync_同一トラック連続呼び出し_キャッシュヒットにより重複IOを回避すること()
    {
        // Arrange
        Track track = CreateTrack(@"C:\Music\cached_song.flac");
        LyricsDocument expected = CreateDocument("キャッシュ対象歌詞");
        var fakeProvider = new FakeLyricsProvider
        {
            Handler = (_, _) => Task.FromResult<LyricsDocument?>(expected),
        };
        var sut = new LyricsApplicationService(fakeProvider, new InMemoryEventBus());

        // Act
        LyricsDocument? first = await sut.LoadForTrackAsync(track);
        LyricsDocument? second = await sut.LoadForTrackAsync(track);

        // Assert
        Assert.Same(expected, first);
        Assert.Same(expected, second);
        Assert.Equal(1, fakeProvider.CallCount);
        Assert.True(sut.TryGetCached(track.FilePath, out LyricsDocument? cached));
        Assert.Same(expected, cached);
    }

    /// <summary>
    /// 歌詞が存在しない（null）結果もキャッシュされ、同じ曲の再要求時に重複探索を行わないことを検証します。
    /// </summary>
    [Fact]
    public async Task LoadForTrackAsync_歌詞未存在のnull結果_キャッシュされ再探索されないこと()
    {
        // Arrange
        Track track = CreateTrack(@"C:\Music\no_lyrics_song.mp3");
        var fakeProvider = new FakeLyricsProvider
        {
            Handler = (_, _) => Task.FromResult<LyricsDocument?>(null),
        };
        var sut = new LyricsApplicationService(fakeProvider, new InMemoryEventBus());

        // Act
        LyricsDocument? first = await sut.LoadForTrackAsync(track);
        LyricsDocument? second = await sut.LoadForTrackAsync(track);

        // Assert
        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, fakeProvider.CallCount);
        Assert.True(sut.TryGetCached(track.FilePath, out LyricsDocument? cached));
        Assert.Null(cached);
    }

    /// <summary>
    /// forceReload に true を指定した場合、キャッシュを無視して最新歌詞を再読み込みしキャッシュを更新することを検証します。
    /// </summary>
    [Fact]
    public async Task LoadForTrackAsync_forceReload指定_キャッシュを無視して再読み込みし更新すること()
    {
        // Arrange
        Track track = CreateTrack(@"C:\Music\reload_song.flac");
        LyricsDocument v1 = CreateDocument("更新前の歌詞");
        LyricsDocument v2 = CreateDocument("更新後の歌詞");
        LyricsDocument current = v1;

        var fakeProvider = new FakeLyricsProvider
        {
            Handler = (_, _) => Task.FromResult<LyricsDocument?>(current),
        };
        var sut = new LyricsApplicationService(fakeProvider, new InMemoryEventBus());

        // Act
        LyricsDocument? first = await sut.LoadForTrackAsync(track);
        current = v2;
        LyricsDocument? reloaded = await sut.LoadForTrackAsync(track, forceReload: true);
        LyricsDocument? afterReload = await sut.LoadForTrackAsync(track);

        // Assert
        Assert.Same(v1, first);
        Assert.Same(v2, reloaded);
        Assert.Same(v2, afterReload);
        Assert.Equal(2, fakeProvider.CallCount);
    }

    /// <summary>
    /// InvalidateCacheAsync を呼び出した場合、対象トラックのキャッシュのみ破棄され、イベントおよび IEventBus へ通知されることを検証します。
    /// </summary>
    [Fact]
    public async Task InvalidateCacheAsync_対象パス指定_キャッシュ破棄とイベントおよびEventBus通知が行われること()
    {
        // Arrange
        Track trackA = CreateTrack(@"C:\Music\song_a.flac");
        Track trackB = CreateTrack(@"C:\Music\song_b.flac");
        LyricsDocument docA1 = CreateDocument("曲A-v1");
        LyricsDocument docA2 = CreateDocument("曲A-v2");
        LyricsDocument docB = CreateDocument("曲B");

        var providerMap = new Dictionary<string, LyricsDocument?>(StringComparer.OrdinalIgnoreCase)
        {
            [trackA.FilePath] = docA1,
            [trackB.FilePath] = docB,
        };
        var fakeProvider = new FakeLyricsProvider
        {
            Handler = (t, _) => Task.FromResult(providerMap[t.FilePath]),
        };

        var eventBus = new InMemoryEventBus();
        var busEvents = new List<LyricsUpdatedEvent>();
        eventBus.Subscribe<LyricsUpdatedEvent>((e, _) =>
        {
            busEvents.Add(e);
            return Task.CompletedTask;
        });

        var sut = new LyricsApplicationService(fakeProvider, eventBus);
        var directEvents = new List<LyricsUpdatedEvent>();
        sut.LyricsUpdated += (_, e) => directEvents.Add(e);

        await sut.LoadForTrackAsync(trackA);
        await sut.LoadForTrackAsync(trackB);
        providerMap[trackA.FilePath] = docA2;

        // Act
        await sut.InvalidateCacheAsync(trackA.FilePath, docA2);
        LyricsDocument? reloadedA = await sut.LoadForTrackAsync(trackA);
        LyricsDocument? cachedB = await sut.LoadForTrackAsync(trackB);

        // Assert
        Assert.Same(docA2, reloadedA);
        Assert.Same(docB, cachedB);
        Assert.Equal(3, fakeProvider.CallCount); // A(初回), B(初回), A(再読込)
        Assert.Single(directEvents);
        Assert.Equal(trackA.FilePath, directEvents[0].TrackFilePath);
        Assert.Same(docA2, directEvents[0].Document);
        Assert.Single(busEvents);
        Assert.Equal(trackA.FilePath, busEvents[0].TrackFilePath);
        Assert.Same(docA2, busEvents[0].Document);
    }

    /// <summary>
    /// 同期 InvalidateCache を呼び出した場合、キャッシュが破棄され LyricsUpdated イベントおよび IEventBus へ通知されることを検証します。
    /// </summary>
    [Fact]
    public async Task InvalidateCache_対象パス指定_キャッシュが破棄され次回読み込みで最新化されること()
    {
        // Arrange
        Track track = CreateTrack(@"C:\Music\sync_invalidate.flac");
        LyricsDocument v1 = CreateDocument("旧歌詞");
        LyricsDocument v2 = CreateDocument("新歌詞");
        LyricsDocument current = v1;

        var fakeProvider = new FakeLyricsProvider
        {
            Handler = (_, _) => Task.FromResult<LyricsDocument?>(current),
        };
        var eventBus = new InMemoryEventBus();
        var publishedTcs = new TaskCompletionSource<LyricsUpdatedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        eventBus.Subscribe<LyricsUpdatedEvent>((e, _) =>
        {
            publishedTcs.TrySetResult(e);
            return Task.CompletedTask;
        });

        var sut = new LyricsApplicationService(fakeProvider, eventBus);
        LyricsUpdatedEvent? receivedDirect = null;
        sut.LyricsUpdated += (_, e) => receivedDirect = e;

        await sut.LoadForTrackAsync(track);
        current = v2;

        // Act
        sut.InvalidateCache(track.FilePath);
        LyricsUpdatedEvent busEvent = await publishedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        LyricsDocument? reloaded = await sut.LoadForTrackAsync(track);

        // Assert
        Assert.NotNull(receivedDirect);
        Assert.Equal(track.FilePath, receivedDirect!.TrackFilePath);
        Assert.Equal(track.FilePath, busEvent.TrackFilePath);
        Assert.Same(v2, reloaded);
        Assert.Equal(2, fakeProvider.CallCount);
    }

    /// <summary>
    /// 非同期読み込み途中でキャンセルされた場合、OperationCanceledException が伝播しキャッシュへ不正な値が登録されないことを検証します。
    /// </summary>
    [Fact]
    public async Task LoadForTrackAsync_読み込み中にキャンセル発生_キャンセルが伝播しキャッシュが汚染されないこと()
    {
        // Arrange
        Track track = CreateTrack(@"C:\Music\cancel_in_flight.flac");
        var startedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fakeProvider = new FakeLyricsProvider
        {
            Handler = async (_, ct) =>
            {
                startedTcs.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
                return CreateDocument("到達しない");
            },
        };
        var sut = new LyricsApplicationService(fakeProvider, new InMemoryEventBus());
        using var cts = new CancellationTokenSource();

        // Act
        Task<LyricsDocument?> loadTask = sut.LoadForTrackAsync(track, cancellationToken: cts.Token);
        await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cts.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loadTask);
        Assert.False(sut.TryGetCached(track.FilePath, out _));
    }

    /// <summary>
    /// キャッシュ上限件数を超えた場合、最も古く参照されたエントリが LRU 方式で退避されることを検証します。
    /// </summary>
    [Fact]
    public async Task LoadForTrackAsync_キャッシュ上限超過_最古エントリがLRUで退避されること()
    {
        // Arrange
        Track track1 = CreateTrack(@"C:\Music\track1.flac");
        Track track2 = CreateTrack(@"C:\Music\track2.flac");
        Track track3 = CreateTrack(@"C:\Music\track3.flac");
        var fakeProvider = new FakeLyricsProvider
        {
            Handler = (t, _) => Task.FromResult<LyricsDocument?>(CreateDocument(t.FilePath)),
        };
        var sut = new LyricsApplicationService(fakeProvider, new InMemoryEventBus(), maxCacheEntries: 2);

        // Act
        await sut.LoadForTrackAsync(track1);
        await sut.LoadForTrackAsync(track2);
        // track1 を再参照して最新化 → track2 が最古になる
        await sut.LoadForTrackAsync(track1);
        await sut.LoadForTrackAsync(track3);

        // Assert
        Assert.True(sut.TryGetCached(track1.FilePath, out _));
        Assert.False(sut.TryGetCached(track2.FilePath, out _));
        Assert.True(sut.TryGetCached(track3.FilePath, out _));
    }

    /// <summary>
    /// ClearCache を呼び出した場合、保持しているすべてのキャッシュが消去されることを検証します。
    /// </summary>
    [Fact]
    public async Task ClearCache_キャッシュ済み状態で呼び出し_全キャッシュが消去されること()
    {
        // Arrange
        Track track = CreateTrack(@"C:\Music\clear_target.flac");
        var fakeProvider = new FakeLyricsProvider
        {
            Handler = (_, _) => Task.FromResult<LyricsDocument?>(CreateDocument("歌詞")),
        };
        var sut = new LyricsApplicationService(fakeProvider, new InMemoryEventBus());
        await sut.LoadForTrackAsync(track);

        // Act
        sut.ClearCache();

        // Assert
        Assert.False(sut.TryGetCached(track.FilePath, out _));
    }

    /// <summary>
    /// 非同期読み込み実行中に InvalidateCache が呼び出された場合、古い読み込み結果でキャッシュが汚染されず次回呼び出しで最新データが読み込まれることを検証します。
    /// </summary>
    [Fact]
    public async Task LoadForTrackAsync_読み込み中にInvalidateCacheが発生_古い結果でキャッシュ汚染せず次回最新を読み込むこと()
    {
        // Arrange
        Track track = CreateTrack(@"C:\Music\race_invalidate.flac");
        LyricsDocument staleDoc = CreateDocument("古い歌詞");
        LyricsDocument freshDoc = CreateDocument("保存後の最新歌詞");
        var firstStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstTcs = new TaskCompletionSource<LyricsDocument?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int callNumber = 0;

        var fakeProvider = new FakeLyricsProvider
        {
            Handler = async (_, _) =>
            {
                int current = Interlocked.Increment(ref callNumber);
                if (current == 1)
                {
                    firstStartedTcs.TrySetResult(true);
                    return await releaseFirstTcs.Task;
                }

                return freshDoc;
            },
        };

        var sut = new LyricsApplicationService(fakeProvider, new InMemoryEventBus());

        // Act
        Task<LyricsDocument?> staleLoadTask = sut.LoadForTrackAsync(track);
        await firstStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // 1回目の非同期読み込み中に外部保存＋キャッシュ無効化が発生
        await sut.InvalidateCacheAsync(track.FilePath);

        // その後、1回目の古い読み込みが完了
        releaseFirstTcs.TrySetResult(staleDoc);
        LyricsDocument? firstResult = await staleLoadTask;

        // 次回呼び出しでは古い結果がキャッシュされていないため最新を読み込む
        LyricsDocument? nextResult = await sut.LoadForTrackAsync(track);

        // Assert
        Assert.Same(staleDoc, firstResult);
        Assert.Same(freshDoc, nextResult);
        Assert.Equal(2, fakeProvider.CallCount);
        Assert.True(sut.TryGetCached(track.FilePath, out LyricsDocument? cached));
        Assert.Same(freshDoc, cached);
    }

    /// <summary>
    /// 非同期読み込み実行中に ClearCache が呼び出された場合、完了した古い読み込み結果でキャッシュが汚染されないことを検証します。
    /// </summary>
    [Fact]
    public async Task LoadForTrackAsync_読み込み中にClearCacheが発生_古い結果でキャッシュが汚染されないこと()
    {
        // Arrange
        Track track = CreateTrack(@"C:\Music\race_clear.flac");
        LyricsDocument staleDoc = CreateDocument("クリア前の古い歌詞");
        var startedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTcs = new TaskCompletionSource<LyricsDocument?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var fakeProvider = new FakeLyricsProvider
        {
            Handler = async (_, _) =>
            {
                startedTcs.TrySetResult(true);
                return await releaseTcs.Task;
            },
        };

        var sut = new LyricsApplicationService(fakeProvider, new InMemoryEventBus());

        // Act
        Task<LyricsDocument?> loadTask = sut.LoadForTrackAsync(track);
        await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        sut.ClearCache();
        releaseTcs.TrySetResult(staleDoc);
        await loadTask;

        // Assert
        Assert.False(sut.TryGetCached(track.FilePath, out _));
    }

    /// <summary>
    /// LyricsUpdated イベントの先頭購読者が例外を送出しても、後続購読者および IEventBus へ通知が届き呼び出し元へ例外が波及しないことを検証します。
    /// </summary>
    [Fact]
    public async Task InvalidateCacheAsync_先頭LyricsUpdated購読者が例外送出_後続購読者とEventBusへ通知が届き例外が波及しないこと()
    {
        // Arrange
        Track track = CreateTrack(@"C:\Music\subscriber_error_async.flac");
        LyricsDocument updatedDoc = CreateDocument("更新済み歌詞");
        var eventBus = new InMemoryEventBus();
        var busEvents = new List<LyricsUpdatedEvent>();
        eventBus.Subscribe<LyricsUpdatedEvent>((e, _) =>
        {
            busEvents.Add(e);
            return Task.CompletedTask;
        });

        var sut = new LyricsApplicationService(new FakeLyricsProvider(), eventBus);
        LyricsUpdatedEvent? secondSubscriberReceived = null;

        sut.LyricsUpdated += (_, _) => throw new InvalidOperationException("先頭購読者の障害シミュレーション");
        sut.LyricsUpdated += (_, e) => secondSubscriberReceived = e;

        // Act
        Exception? ex = await Record.ExceptionAsync(() => sut.InvalidateCacheAsync(track.FilePath, updatedDoc));

        // Assert
        Assert.Null(ex);
        Assert.NotNull(secondSubscriberReceived);
        Assert.Equal(track.FilePath, secondSubscriberReceived!.TrackFilePath);
        Assert.Same(updatedDoc, secondSubscriberReceived.Document);
        Assert.Single(busEvents);
        Assert.Equal(track.FilePath, busEvents[0].TrackFilePath);
    }

    /// <summary>
    /// 同期 InvalidateCache で先頭購読者が例外を送出しても、後続購読者および IEventBus へ通知が届き呼び出し元へ例外が波及しないことを検証します。
    /// </summary>
    [Fact]
    public async Task InvalidateCache_先頭LyricsUpdated購読者が例外送出_後続購読者とEventBusへ通知が届き例外が波及しないこと()
    {
        // Arrange
        Track track = CreateTrack(@"C:\Music\subscriber_error_sync.flac");
        var eventBus = new InMemoryEventBus();
        var busReceivedTcs = new TaskCompletionSource<LyricsUpdatedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        eventBus.Subscribe<LyricsUpdatedEvent>((e, _) =>
        {
            busReceivedTcs.TrySetResult(e);
            return Task.CompletedTask;
        });

        var sut = new LyricsApplicationService(new FakeLyricsProvider(), eventBus);
        LyricsUpdatedEvent? secondSubscriberReceived = null;

        sut.LyricsUpdated += (_, _) => throw new InvalidOperationException("先頭購読者の障害シミュレーション");
        sut.LyricsUpdated += (_, e) => secondSubscriberReceived = e;

        // Act
        Exception? ex = Record.Exception(() => sut.InvalidateCache(track.FilePath));
        LyricsUpdatedEvent busEvent = await busReceivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // Assert
        Assert.Null(ex);
        Assert.NotNull(secondSubscriberReceived);
        Assert.Equal(track.FilePath, secondSubscriberReceived!.TrackFilePath);
        Assert.Equal(track.FilePath, busEvent.TrackFilePath);
    }

    private sealed class FakeLyricsProvider : ILyricsProvider
    {
        public int CallCount { get; private set; }

        public Func<Track, CancellationToken, Task<LyricsDocument?>> Handler { get; init; } =
            (_, _) => Task.FromResult<LyricsDocument?>(null);

        public Task<LyricsDocument?> LoadLyricsAsync(Track track, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Handler(track, cancellationToken);
        }
    }
}
