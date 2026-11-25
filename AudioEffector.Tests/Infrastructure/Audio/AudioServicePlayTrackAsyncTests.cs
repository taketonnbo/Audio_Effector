using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Audio;
using Moq;
using NAudio.Wave;

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

    private string CreateSilentWavFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"AudioEffectorSilent_{Guid.NewGuid():N}.wav");
        var format = new WaveFormat(44100, 16, 2);
        using (var writer = new WaveFileWriter(path, format))
        {
            byte[] silentBuffer = new byte[format.AverageBytesPerSecond / 2];
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
