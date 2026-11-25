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

namespace AudioEffector.Tests.Infrastructure.Audio;

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
