using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Audio;
using Moq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioEffector.Infrastructure.Tests.Audio;

public sealed class AudioServicePlayTrackAsyncTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (string path in _tempFiles)
        {
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // テスト終了時のファイルハンドル解放遅延は無視する
                }
            }
        }
    }

    /// <summary>
    /// 予約キューに積まれた楽曲の再生開始前にトークンがキャンセルされた場合、予約キューと現在曲が維持され TrackChanged / PlaylistChanged が発火しないことを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_再生開始前キャンセル時_予約キューと現在曲が維持され通知されない()
    {
        // Arrange
        string wavPath = CreateSilentWavFile();
        var trackB = CreateTrack(wavPath, "Track B");
        using var cts = new CancellationTokenSource();

        var settingsService = new Mock<ISettingsService>();
        settingsService
            .Setup(s => s.LoadSettings())
            .Returns(() =>
            {
                // デコード後・出力デバイス開始前に期限切れ（キャンセル）が発生した状況を再現する
                cts.Cancel();
                return new AppSettings();
            });

        using var sut = new AudioService(ImmediateUiDispatcher.Instance, settingsService.Object);
        sut.EnqueueTracks([trackB], playNext: false);

        int trackChangedCount = 0;
        int playlistChangedCount = 0;
        sut.TrackChanged += _ => trackChangedCount++;
        sut.PlaylistChanged += _ => playlistChangedCount++;

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.PlayTrackAsync(trackB, cts.Token));
        Assert.Single(sut.UserQueue);
        Assert.Equal(trackB.FilePath, sut.UserQueue[0].FilePath);
        Assert.Equal(0, trackChangedCount);
        Assert.Equal(0, playlistChangedCount);
        Assert.False(sut.IsPlaying);
    }

    /// <summary>
    /// 先行の PlayTrackAsync が準備中に後続リクエストで上書きされた場合、先行リクエストは false で終了し後続リクエストの予約キューおよび現在曲状態を巻き戻さないことを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_準備中に後続要求が割り込んだ場合_後続状態を巻き戻さずfalseを返す()
    {
        // Arrange
        string wavPathB = CreateSilentWavFile();
        string wavPathC = CreateSilentWavFile();
        var trackB = CreateTrack(wavPathB, "Track B");
        var trackC = CreateTrack(wavPathC, "Track C");

        AudioService? sut = null;
        int loadCount = 0;
        var settingsService = new Mock<ISettingsService>();
        settingsService
            .Setup(s => s.LoadSettings())
            .Returns(() =>
            {
                if (Interlocked.Increment(ref loadCount) == 1)
                {
                    // 先行リクエスト B の準備中に後続リクエスト C を開始する
                    sut!.PlayTrack(trackC);
                }

                return new AppSettings();
            });

        sut = new AudioService(ImmediateUiDispatcher.Instance, settingsService.Object);
        using (sut)
        {
            sut.EnqueueTracks([trackB, trackC], playNext: false);

            var notifiedTracks = new List<Track?>();
            sut.TrackChanged += t => notifiedTracks.Add(t);

            // Act
            bool startedB = await sut.PlayTrackAsync(trackB);

            // Assert
            Assert.False(startedB);
            Assert.Single(sut.UserQueue);
            Assert.Equal(trackB.FilePath, sut.UserQueue[0].FilePath);
            Assert.Contains(notifiedTracks, t => t != null && string.Equals(t.FilePath, trackC.FilePath, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(notifiedTracks, t => t != null && string.Equals(t.FilePath, trackB.FilePath, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 破損した音声ファイルを PlayTrackAsync で再生しようとした場合、false を返し予約キューの楽曲が失われないことを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_破損ファイル指定時_予約キューを保持したままfalseを返す()
    {
        // Arrange
        string corruptPath = Path.Combine(Path.GetTempPath(), $"AudioEffectorCorrupt_{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(corruptPath, [0x01, 0x02, 0x03, 0x04]);
        _tempFiles.Add(corruptPath);

        var corruptTrack = CreateTrack(corruptPath, "Corrupt Track");
        using var sut = new AudioService(ImmediateUiDispatcher.Instance);
        sut.EnqueueTracks([corruptTrack], playNext: false);

        int trackChangedCount = 0;
        sut.TrackChanged += _ => trackChangedCount++;

        // Act
        bool started = await sut.PlayTrackAsync(corruptTrack);

        // Assert
        Assert.False(started);
        Assert.Single(sut.UserQueue);
        Assert.Equal(corruptTrack.FilePath, sut.UserQueue[0].FilePath);
        Assert.Equal(0, trackChangedCount);
    }

    /// <summary>
    /// 楽曲 A の再生中に破損した楽曲 B を PlayTrackAsync に渡した場合、false を返しつつ A の再生デバイスが破棄されず再生継続することを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_A再生中に破損ファイルB指定時_Aの再生を停止せずfalseを返す()
    {
        // Arrange
        string wavPathA = CreateSilentWavFile(durationSeconds: 1.5);
        string corruptPathB = Path.Combine(Path.GetTempPath(), $"AudioEffectorCorruptB_{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(corruptPathB, [0x01, 0x02, 0x03, 0x04]);
        _tempFiles.Add(corruptPathB);

        var trackA = CreateTrack(wavPathA, "Track A");
        var trackB = CreateTrack(corruptPathB, "Corrupt B");
        using var sut = new AudioService(ImmediateUiDispatcher.Instance);

        bool startedA = await sut.PlayTrackAsync(trackA);
        Assert.True(startedA);
        Assert.True(sut.IsPlaying);

        // Act
        bool startedB = await sut.PlayTrackAsync(trackB);

        // Assert
        Assert.False(startedB);
        Assert.True(sut.IsPlaying);
    }

    /// <summary>
    /// A 再生中に予約曲 B の PlayTrackAsync がキャンセルされても、A の自然終了時に次曲 B へ正常に自動送りされることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_後続要求Bキャンセル時_再生中Aの自然終了でBへ自動送りされる()
    {
        // Arrange
        string wavPathA = CreateSilentWavFile(durationSeconds: 0.3);
        string wavPathB = CreateSilentWavFile(durationSeconds: 0.5);
        var trackA = CreateTrack(wavPathA, "Track A");
        var trackB = CreateTrack(wavPathB, "Track B");
        using var cts = new CancellationTokenSource();

        int loadCall = 0;
        var settingsService = new Mock<ISettingsService>();
        settingsService
            .Setup(s => s.LoadSettings())
            .Returns(() =>
            {
                // 2回目（B の準備）でキャンセルする
                if (Interlocked.Increment(ref loadCall) == 2)
                {
                    cts.Cancel();
                }

                return new AppSettings();
            });

        using var sut = new AudioService(ImmediateUiDispatcher.Instance, settingsService.Object);
        var nextTrackStarted = new TaskCompletionSource<Track>(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.TrackChanged += t =>
        {
            if (t != null && string.Equals(t.FilePath, trackB.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                nextTrackStarted.TrySetResult(t);
            }
        };

        Assert.True(await sut.PlayTrackAsync(trackA));
        sut.EnqueueTracks([trackB], playNext: false);

        // Act
        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.PlayTrackAsync(trackB, cts.Token));

        // Assert: A が自然終了すると B がキューから消費されて再生へ遷移する
        Task completed = await Task.WhenAny(nextTrackStarted.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(nextTrackStarted.Task, completed);
        Assert.Empty(sut.UserQueue);
    }

    /// <summary>
    /// PlayTrackAsync の準備中に Stop が呼ばれた場合、先行要求が無効化され後から再生が始まらないことを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_準備中にStop呼び出し時_再生を開始せずfalseを返す()
    {
        // Arrange
        string wavPath = CreateSilentWavFile();
        var track = CreateTrack(wavPath, "Track Stopped");

        AudioService? sut = null;
        var settingsService = new Mock<ISettingsService>();
        settingsService
            .Setup(s => s.LoadSettings())
            .Returns(() =>
            {
                sut!.Stop();
                return new AppSettings();
            });

        sut = new AudioService(ImmediateUiDispatcher.Instance, settingsService.Object);
        using (sut)
        {
            // Act
            bool started = await sut.PlayTrackAsync(track);

            // Assert
            Assert.False(started);
            Assert.False(sut.IsPlaying);
        }
    }

    /// <summary>
    /// PlayTrackAsync の準備中に Volume を 0 へ変更した場合、切り替え後の新しい VolumeSampleProvider へ最新音量 0 が反映されることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_準備中にVolumeを0へ変更時_新プロバイダへ最新音量0が反映される()
    {
        // Arrange
        string wavPath = CreateSilentWavFile(durationSeconds: 1.0);
        var track = CreateTrack(wavPath, "Track Volume");

        AudioService? sut = null;
        var settingsService = new Mock<ISettingsService>();
        settingsService
            .Setup(s => s.LoadSettings())
            .Returns(() =>
            {
                sut!.Volume = 0f;
                return new AppSettings();
            });

        sut = new AudioService(ImmediateUiDispatcher.Instance, settingsService.Object);
        using (sut)
        {
            sut.Volume = 0.8f;

            // Act
            bool started = await sut.PlayTrackAsync(track);

            // Assert
            Assert.True(started);
            Assert.Equal(0f, sut.Volume);
            FieldInfo? providerField = typeof(AudioService).GetField("_masterVolumeProvider", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(providerField);
            var masterVolumeProvider = Assert.IsType<VolumeSampleProvider>(providerField.GetValue(sut));
            Assert.Equal(0f, masterVolumeProvider.Volume);
        }
    }

    /// <summary>
    /// PlayTrackAsync の確定後通知が IUiDispatcher 経由で配信され、配信直前に Stop が割り込んだ場合は古い TrackChanged が配信されないことを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_通知配信前にStopが割り込んだ場合_UiDispatcher経由で古い通知が抑制される()
    {
        // Arrange
        string wavPath = CreateSilentWavFile(durationSeconds: 1.0);
        var track = CreateTrack(wavPath, "Track Stale Notification");

        AudioService? sut = null;
        int invokeCount = 0;
        var dispatcher = new Mock<IUiDispatcher>();
        dispatcher
            .Setup(d => d.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action =>
            {
                invokeCount++;
                // 確定後の UI 通知が実行される直前に Stop() が割り込んだ状況を再現する
                sut!.Stop();
                action();
            });

        sut = new AudioService(dispatcher.Object);
        using (sut)
        {
            var notifiedTracks = new List<Track?>();
            sut.TrackChanged += t => notifiedTracks.Add(t);

            // Act
            bool started = await sut.PlayTrackAsync(track);

            // Assert
            Assert.True(started);
            Assert.Equal(1, invokeCount);
            Assert.DoesNotContain(notifiedTracks, t => t != null && string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// PlayPlaylistAsync でシャッフル・リピートを指定して再生した場合、開始曲の再生・AlbumQueue 展開・ShuffleChanged / RepeatChanged イベント発火が行われることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayPlaylistAsync_シャッフルとリピート指定時_キュー展開と設定変更イベントが発火される()
    {
        // Arrange
        var tracks = Enumerable.Range(1, 3)
            .Select(i => CreateTrack(CreateSilentWavFile(durationSeconds: 0.5), $"Track {i}"))
            .ToList();

        using var sut = new AudioService(ImmediateUiDispatcher.Instance);
        bool? notifiedShuffle = null;
        bool? notifiedRepeat = null;
        sut.ShuffleChanged += v => notifiedShuffle = v;
        sut.RepeatChanged += v => notifiedRepeat = v;

        // Act
        Track? started = await sut.PlayPlaylistAsync(tracks, startTrack: tracks[1], shuffle: true, repeat: true);

        // Assert
        Assert.NotNull(started);
        Assert.Equal(tracks[1].FilePath, started.FilePath);
        Assert.True(sut.IsPlaying);
        Assert.True(sut.IsShuffleEnabled);
        Assert.True(sut.IsRepeatEnabled);
        Assert.Equal(true, notifiedShuffle);
        Assert.Equal(true, notifiedRepeat);
        Assert.Equal(2, sut.AlbumQueue.Count);
        Assert.DoesNotContain(sut.AlbumQueue, t => string.Equals(t.FilePath, tracks[1].FilePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// PlayPlaylistAsync で shuffle=true かつ開始曲未指定の場合、コレクション内から開始曲が選出され残り曲が AlbumQueue に展開されることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayPlaylistAsync_シャッフル有効かつ開始曲未指定時_コレクション内から開始曲が選出される()
    {
        // Arrange
        var tracks = Enumerable.Range(1, 3)
            .Select(i => CreateTrack(CreateSilentWavFile(durationSeconds: 0.5), $"Shuffle Track {i}"))
            .ToList();

        using var sut = new AudioService(ImmediateUiDispatcher.Instance);

        // Act
        Track? started = await sut.PlayPlaylistAsync(tracks, startTrack: null, shuffle: true, repeat: null);

        // Assert
        Assert.NotNull(started);
        Assert.Contains(tracks, t => string.Equals(t.FilePath, started.FilePath, StringComparison.OrdinalIgnoreCase));
        Assert.True(sut.IsShuffleEnabled);
        Assert.False(sut.IsRepeatEnabled);
        Assert.Equal(2, sut.AlbumQueue.Count);
        Assert.DoesNotContain(sut.AlbumQueue, t => string.Equals(t.FilePath, started.FilePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// PlayPlaylistAsync で shuffle / repeat 未指定（null）の場合、本体の現在のシャッフル・リピート設定がそのまま維持されることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayPlaylistAsync_シャッフルとリピート未指定時_現在の本体設定を維持する()
    {
        // Arrange
        var tracks = Enumerable.Range(1, 2)
            .Select(i => CreateTrack(CreateSilentWavFile(durationSeconds: 0.5), $"Keep Track {i}"))
            .ToList();

        using var sut = new AudioService(ImmediateUiDispatcher.Instance);
        sut.IsShuffleEnabled = false;
        sut.IsRepeatEnabled = true;

        int shuffleEventCount = 0;
        int repeatEventCount = 0;
        sut.ShuffleChanged += _ => shuffleEventCount++;
        sut.RepeatChanged += _ => repeatEventCount++;

        // Act
        Track? started = await sut.PlayPlaylistAsync(tracks, startTrack: null, shuffle: null, repeat: null);

        // Assert
        Assert.NotNull(started);
        Assert.Equal(tracks[0].FilePath, started.FilePath);
        Assert.False(sut.IsShuffleEnabled);
        Assert.True(sut.IsRepeatEnabled);
        Assert.Equal(0, shuffleEventCount);
        Assert.Equal(0, repeatEventCount);
        Assert.Single(sut.AlbumQueue);
        Assert.Equal(tracks[1].FilePath, sut.AlbumQueue[0].FilePath);
    }

    /// <summary>
    /// IsShuffleEnabled / IsRepeatEnabled のプロパティ更新時に ShuffleChanged / RepeatChanged イベントが発火されることを検証します。
    /// </summary>
    [Fact]
    public void IsShuffleEnabledとIsRepeatEnabled_値変更時_変更通知イベントが発火される()
    {
        // Arrange
        using var sut = new AudioService(ImmediateUiDispatcher.Instance);
        var shuffleValues = new List<bool>();
        var repeatValues = new List<bool>();
        sut.ShuffleChanged += v => shuffleValues.Add(v);
        sut.RepeatChanged += v => repeatValues.Add(v);

        // Act
        sut.IsShuffleEnabled = true;
        sut.IsShuffleEnabled = true; // 同値では再発火しない
        sut.IsRepeatEnabled = true;
        sut.IsRepeatEnabled = false;

        // Assert
        Assert.Equal([true], shuffleValues);
        Assert.Equal([true, false], repeatValues);
    }

    /// <summary>
    /// PlayPlaylistAsync の通知配信直前に Stop が割り込んだ場合でも、曲通知は抑制しつつ最新のシャッフル・リピート状態は UI へ同期通知されることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayPlaylistAsync_通知配信前にStopが割り込んだ場合_曲通知は抑制しつつシャッフルとリピート最新状態は同期される()
    {
        // Arrange
        var track = CreateTrack(CreateSilentWavFile(durationSeconds: 0.5), "Stop Race Track");
        AudioService? sut = null;
        var dispatcher = new Mock<IUiDispatcher>();
        dispatcher
            .Setup(d => d.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action =>
            {
                sut!.Stop();
                action();
            });

        sut = new AudioService(dispatcher.Object);
        using (sut)
        {
            var notifiedTracks = new List<Track?>();
            var notifiedShuffles = new List<bool>();
            var notifiedRepeats = new List<bool>();
            sut.TrackChanged += t => notifiedTracks.Add(t);
            sut.ShuffleChanged += v => notifiedShuffles.Add(v);
            sut.RepeatChanged += v => notifiedRepeats.Add(v);

            // Act
            Track? started = await sut.PlayPlaylistAsync([track], shuffle: true, repeat: true, sourceName: "Race Playlist");

            // Assert
            Assert.NotNull(started);
            Assert.True(sut.IsShuffleEnabled);
            Assert.True(sut.IsRepeatEnabled);
            Assert.Equal([true], notifiedShuffles);
            Assert.Equal([true], notifiedRepeats);
            Assert.DoesNotContain(notifiedTracks, t => t != null && string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 同一トラックが重複する [A, B, A, C] のプレイリストを startTrackIndex=2・シャッフル無効で開始した場合、指定出現位置を保持し残り曲が [C] のみになることを検証します。
    /// </summary>
    [Fact]
    public async Task PlayPlaylistAsync_同一トラック重複リストでstartTrackIndex指定時_指定出現位置を保持し残り曲が後続のみになる()
    {
        // Arrange
        var trackA = CreateTrack(CreateSilentWavFile(durationSeconds: 0.5), "Track A");
        var trackB = CreateTrack(CreateSilentWavFile(durationSeconds: 0.5), "Track B");
        var trackC = CreateTrack(CreateSilentWavFile(durationSeconds: 0.5), "Track C");
        List<Track> playlist = [trackA, trackB, trackA, trackC];

        using var sut = new AudioService(ImmediateUiDispatcher.Instance);
        string? notifiedSourceName = null;
        sut.QueueSourceNameChanged += name => notifiedSourceName = name;

        // Act
        Track? started = await sut.PlayPlaylistAsync(
            playlist,
            startTrack: trackA,
            shuffle: false,
            repeat: false,
            sourceName: "Duplicate Playlist",
            startTrackIndex: 2);

        // Assert
        Assert.NotNull(started);
        Assert.Equal(trackA.FilePath, started.FilePath);
        Assert.Equal("Duplicate Playlist", notifiedSourceName);
        Assert.Single(sut.AlbumQueue);
        Assert.Equal(trackC.FilePath, sut.AlbumQueue[0].FilePath);
    }

    /// <summary>
    /// 先頭と末尾が同一曲のプレイリスト [A, B, A] を先頭からシャッフル・リピート無効で再生した場合、最初の A 終了で終了せず A → B → A と進行し最後の A で PlaylistEnded が発火することを検証します。
    /// </summary>
    [Fact]
    public async Task PlayPlaylistAsync_先頭と末尾が同一曲のリストで連続再生時_AからBを経て最後のAでPlaylistEndedが発火する()
    {
        // Arrange
        var trackA = CreateTrack(CreateSilentWavFile(durationSeconds: 1.0), "Track A");
        var trackB = CreateTrack(CreateSilentWavFile(durationSeconds: 1.0), "Track B");
        List<Track> playlist = [trackA, trackB, trackA];

        using var sut = new AudioService(ImmediateUiDispatcher.Instance);
        var notifiedTrackTitles = new List<string?>();
        int playlistEndedCount = 0;
        sut.TrackChanged += t => notifiedTrackTitles.Add(t?.Title);
        sut.PlaylistEnded += (_, _) => playlistEndedCount++;

        // Act & Assert 1: 先頭の A を再生開始し、残りキューが [B, A] であることを確認
        Track? started = await sut.PlayPlaylistAsync(playlist, startTrack: null, shuffle: false, repeat: false);
        Assert.NotNull(started);
        Assert.Equal(trackA.FilePath, started.FilePath);
        Assert.Equal([trackB.FilePath, trackA.FilePath], sut.AlbumQueue.Select(t => t.FilePath).ToArray());
        Assert.Equal(0, playlistEndedCount);

        // Act & Assert 2: 1 回目の Next() で B へ進み、PlaylistEnded は発火しない
        sut.Next();
        Assert.Equal(0, playlistEndedCount);
        Assert.Contains("Track B", notifiedTrackTitles);
        Assert.Equal([trackA.FilePath], sut.AlbumQueue.Select(t => t.FilePath).ToArray());

        // Act & Assert 3: 2 回目の Next() で末尾の A へ進み、残りキューが空になり、まだ PlaylistEnded は発火しない
        sut.Next();
        Assert.Equal(0, playlistEndedCount);
        Assert.Empty(sut.AlbumQueue);
        Assert.Equal(2, notifiedTrackTitles.Count(title => title == "Track A"));

        // Act & Assert 4: 3 回目の Next()（最後の A の終了）で PlaylistEnded が発火する
        sut.Next();
        Assert.Equal(1, playlistEndedCount);
    }

    /// <summary>
    /// [A, B, C] を先頭から開始した後に PlayTrackAsync(C) でキュー最終曲へ移動して Next() を実行した場合、残存する B へ進まず PlaylistEnded が発火することを検証します。
    /// </summary>
    [Fact]
    public async Task PlayTrackAsync_キュー最終曲へ単曲遷移後にNext実行時_残存曲へ進まずPlaylistEndedが発火する()
    {
        // Arrange
        var trackA = CreateTrack(CreateSilentWavFile(durationSeconds: 1.0), "Track A");
        var trackB = CreateTrack(CreateSilentWavFile(durationSeconds: 1.0), "Track B");
        var trackC = CreateTrack(CreateSilentWavFile(durationSeconds: 1.0), "Track C");

        using var sut = new AudioService(ImmediateUiDispatcher.Instance);
        var notifiedTrackTitles = new List<string?>();
        int playlistEndedCount = 0;
        sut.TrackChanged += t => notifiedTrackTitles.Add(t?.Title);
        sut.PlaylistEnded += (_, _) => playlistEndedCount++;

        // Act 1: [A, B, C] を先頭から再生（リピート・シャッフル無効）
        Track? started = await sut.PlayPlaylistAsync([trackA, trackB, trackC], startTrack: null, shuffle: false, repeat: false);
        Assert.NotNull(started);
        Assert.Equal(trackA.FilePath, started.FilePath);

        // Act 2: 単曲再生で末尾曲 C へ移動
        bool jumpedToC = await sut.PlayTrackAsync(trackC);
        Assert.True(jumpedToC);
        notifiedTrackTitles.Clear();

        // Act 3: 末尾曲 C の終了（Next）
        sut.Next();

        // Assert: B の TrackChanged は届かず PlaylistEnded が発火する
        Assert.Equal(1, playlistEndedCount);
        Assert.DoesNotContain("Track B", notifiedTrackTitles);
    }

    /// <summary>
    /// 初期状態・再生開始後・一時停止後・Stop後の各遷移において CurrentTrack・IsPlaying・IsPaused が正確に反映されることを検証します。
    /// </summary>
    [Fact]
    public async Task CurrentTrackとIsPaused_再生と一時停止と停止の状態遷移_期待される状態を返す()
    {
        // Arrange
        string wavPath = CreateSilentWavFile(durationSeconds: 1.5);
        var track = CreateTrack(wavPath, "State Transition Track");
        using var sut = new AudioService(ImmediateUiDispatcher.Instance);

        // Assert (初期状態: 停止)
        Assert.Null(sut.CurrentTrack);
        Assert.False(sut.IsPlaying);
        Assert.False(sut.IsPaused);

        // Act 1: 再生開始
        bool started = await sut.PlayTrackAsync(track);

        // Assert 1: 再生中
        Assert.True(started);
        Assert.NotNull(sut.CurrentTrack);
        Assert.Equal(track.FilePath, sut.CurrentTrack.FilePath);
        Assert.True(sut.IsPlaying);
        Assert.False(sut.IsPaused);

        // Act 2: 一時停止（シーク用の一時停止 API を利用して同期的に一時停止状態へ遷移）
        sut.PauseForSeek();

        // Assert 2: 一時停止中
        Assert.NotNull(sut.CurrentTrack);
        Assert.Equal(track.FilePath, sut.CurrentTrack.FilePath);
        Assert.False(sut.IsPlaying);
        Assert.True(sut.IsPaused);

        // Act 3: 完全停止
        sut.Stop();

        // Assert 3: 停止・トラック未選択
        Assert.Null(sut.CurrentTrack);
        Assert.False(sut.IsPlaying);
        Assert.False(sut.IsPaused);
    }

    private string CreateSilentWavFile(double durationSeconds = 0.5)
    {
        string path = Path.Combine(Path.GetTempPath(), $"AudioEffectorSilent_{Guid.NewGuid():N}.wav");
        var format = new WaveFormat(44100, 16, 2);
        using (var writer = new WaveFileWriter(path, format))
        {
            int byteCount = (int)(format.AverageBytesPerSecond * durationSeconds);
            byteCount -= byteCount % format.BlockAlign;
            byte[] silentBuffer = new byte[byteCount];
            writer.Write(silentBuffer, 0, silentBuffer.Length);
        }

        _tempFiles.Add(path);
        return path;
    }

    private static Track CreateTrack(string filePath, string title)
    {
        return new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(filePath),
            title: title,
            artist: "Test Artist",
            album: "Test Album",
            duration: TimeSpan.FromSeconds(0.5),
            year: 2026,
            trackNumber: 1,
            bitrate: 1411,
            sampleRate: 44100,
            bitsPerSample: 16,
            format: "WAV",
            genre: "Test");
    }
}
