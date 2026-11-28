using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Entities.DataTransfer;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using Moq;
using Xunit;

namespace AudioEffector.Application.Tests.ApplicationServices;

/// <summary>
/// DataTransferApplicationServiceのデバイス接続確認、ファイル転送、進捗通知、削除を検証するテストクラス
/// </summary>
public sealed class DataTransferApplicationServiceTests
{
    private static Track CreateTestTrack(string title = "Song 1")
    {
        return new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create($@"C:\Music\{title}.mp3"),
            title: title,
            artist: "Artist",
            album: "Album",
            duration: TimeSpan.FromMinutes(3));
    }

    /// <summary>
    /// コンストラクタ引数にnullを指定した場合、ArgumentNullExceptionがスローされるかを検証します。
    /// </summary>
    [Fact]
    public void コンストラクタ_必須引数null指定_ArgumentNullExceptionをスローすること()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DataTransferApplicationService(null!));
    }

    /// <summary>
    /// IsDeviceConnectedAsync呼び出し時に、IDataTransferRepositoryの結果をそのまま返すかを検証します。
    /// </summary>
    [Fact]
    public async Task IsDeviceConnectedAsync_呼び出し時_IDataTransferRepositoryの結果を返すこと()
    {
        // Arrange
        var mockRepo = new Mock<IDataTransferRepository>();
        mockRepo.Setup(r => r.IsDeviceConnectedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new DataTransferApplicationService(mockRepo.Object);

        // Act
        var actual = await sut.IsDeviceConnectedAsync();

        // Assert
        Assert.True(actual);
    }

    /// <summary>
    /// GetDeviceTracksAsyncおよびGetDeviceAlbumsAsync呼び出し時に、リポジトリの戻り値をそのまま返すかを検証します。
    /// </summary>
    [Fact]
    public async Task GetDeviceTracksAsyncおよびGetDeviceAlbumsAsync_呼び出し時_リポジトリの戻り値を返すこと()
    {
        // Arrange
        var tracks = new List<DeviceTrack>
        {
            new("DeviceSong", "DeviceArtist", "DeviceAlbum", @"E:\Music\DeviceSong.mp3", 1024)
        };
        var albums = new List<DeviceAlbum>
        {
            new("DeviceAlbum", "WALKMAN", @"E:\Music\DeviceAlbum")
        };

        var mockRepo = new Mock<IDataTransferRepository>();
        mockRepo.Setup(r => r.GetDeviceTracksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tracks);
        mockRepo.Setup(r => r.GetDeviceAlbumsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(albums);

        var sut = new DataTransferApplicationService(mockRepo.Object);

        // Act
        var actualTracks = await sut.GetDeviceTracksAsync();
        var actualAlbums = await sut.GetDeviceAlbumsAsync();

        // Assert
        Assert.Single(actualTracks);
        Assert.Equal("DeviceSong", actualTracks[0].Title);
        Assert.Single(actualAlbums);
        Assert.Equal("DeviceAlbum", actualAlbums[0].Title);
    }

    /// <summary>
    /// 複数トラックを指定してTransferTracksAsyncを呼び出した際、順次転送が実行され成功件数が返るかを検証します。
    /// </summary>
    [Fact]
    public async Task TransferTracksAsync_複数トラック指定_順次転送が呼び出され成功件数を返すこと()
    {
        // Arrange
        var t1 = CreateTestTrack("Track1");
        var t2 = CreateTestTrack("Track2");
        var t3 = CreateTestTrack("Track3");

        var mockRepo = new Mock<IDataTransferRepository>();
        mockRepo.Setup(r => r.TransferTrackAsync(t1.FilePath, @"E:\Music", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mockRepo.Setup(r => r.TransferTrackAsync(t2.FilePath, @"E:\Music", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // 2曲目は失敗
        mockRepo.Setup(r => r.TransferTrackAsync(t3.FilePath, @"E:\Music", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new DataTransferApplicationService(mockRepo.Object);

        // Act
        int successCount = await sut.TransferTracksAsync([t1, t2, t3], @"E:\Music");

        // Assert
        Assert.Equal(2, successCount);
        mockRepo.Verify(r => r.TransferTrackAsync(It.IsAny<AudioPath>(), @"E:\Music", null, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    /// <summary>
    /// 空コレクションを指定してTransferTracksAsyncを呼び出した際、転送を行わず0を返すかを検証します。
    /// </summary>
    [Fact]
    public async Task TransferTracksAsync_空コレクション指定_転送を実行せず0を返すこと()
    {
        // Arrange
        var mockRepo = new Mock<IDataTransferRepository>();
        var sut = new DataTransferApplicationService(mockRepo.Object);

        // Act
        int count = await sut.TransferTracksAsync([], @"E:\Music");

        // Assert
        Assert.Equal(0, count);
        mockRepo.Verify(r => r.TransferTrackAsync(It.IsAny<AudioPath>(), It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// 転送中にIProgressに進捗率が通知されるかを検証します。
    /// </summary>
    [Fact]
    public async Task TransferTracksAsync_進捗通知_IProgressに進捗率が通知されること()
    {
        // Arrange
        var t1 = CreateTestTrack("T1");
        var t2 = CreateTestTrack("T2");

        var mockRepo = new Mock<IDataTransferRepository>();
        mockRepo.Setup(r => r.TransferTrackAsync(It.IsAny<AudioPath>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new DataTransferApplicationService(mockRepo.Object);
        var progressList = new List<double>();
        var progress = new Progress<double>(val => progressList.Add(val));

        // Act
        // Progress<T>の非同期通知を確実に受けるため、SyncProgressラッパー等で検証
        var syncProgress = new Mock<IProgress<double>>();

        await sut.TransferTracksAsync([t1, t2], @"E:\Music", syncProgress.Object);

        // Assert: 1曲目（0.5）と2曲目（1.0）が通知されること
        syncProgress.Verify(p => p.Report(0.5), Times.Once);
        syncProgress.Verify(p => p.Report(1.0), Times.Once);
    }

    /// <summary>
    /// DeleteDeviceTrackAsyncを呼び出した際、リポジトリの削除メソッドが実行され結果を返すかを検証します。
    /// </summary>
    [Fact]
    public async Task DeleteDeviceTrackAsync_指定パス_リポジトリの削除メソッドを呼び出し結果を返すこと()
    {
        // Arrange
        var mockRepo = new Mock<IDataTransferRepository>();
        mockRepo.Setup(r => r.DeleteDeviceTrackAsync(@"E:\Music\delete_me.mp3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = new DataTransferApplicationService(mockRepo.Object);

        // Act
        bool actual = await sut.DeleteDeviceTrackAsync(@"E:\Music\delete_me.mp3");

        // Assert
        Assert.True(actual);
        mockRepo.Verify(r => r.DeleteDeviceTrackAsync(@"E:\Music\delete_me.mp3", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// ConvertMp4ToM4aが有効な場合、MP4トラックが変換サービス経由で一時M4Aファイルとして転送され、転送後に一時ファイルが自動削除されることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferTracksAsync_ConvertMp4ToM4a有効時_一時M4Aファイルが転送され転送後に削除されること()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AE_AppTranscode_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string tempM4a = Path.Combine(tempDir, "Song.m4a");
        File.WriteAllText(tempM4a, "m4a-data");

        var mp4Track = new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(@"C:\Music\Song.mp4"),
            title: "Song",
            artist: "Artist",
            album: "Album",
            duration: TimeSpan.FromMinutes(3));

        var mockRepo = new Mock<IDataTransferRepository>();
        mockRepo.Setup(r => r.TransferTrackAsync(It.Is<AudioPath>(p => p.Value == tempM4a), @"E:\Music", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.LoadSettings()).Returns(new AppSettings { ConvertMp4ToM4a = true });

        var mockTranscoder = new Mock<IAudioTranscodeService>();
        mockTranscoder.Setup(t => t.NeedsTranscode(mp4Track.FilePath, It.Is<AudioTranscodeOptions>(o => o.ConvertMp4ToM4a)))
            .Returns(true);
        using var transcodedResult = new TranscodedAudioResult(
            mp4Track.FilePath,
            tempM4a,
            "Song.m4a",
            wasTranscoded: true,
            temporaryDirectoryPath: tempDir);
        mockTranscoder.Setup(t => t.PrepareForTransferAsync(mp4Track.FilePath, It.Is<AudioTranscodeOptions>(o => o.ConvertMp4ToM4a), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcodedResult);

        var sut = new DataTransferApplicationService(mockRepo.Object, mockTranscoder.Object, mockSettings.Object);

        // Act
        int count = await sut.TransferTracksAsync([mp4Track], @"E:\Music");

        // Assert
        Assert.Equal(1, count);
        mockRepo.Verify(r => r.TransferTrackAsync(It.Is<AudioPath>(p => p.Value == tempM4a), @"E:\Music", null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(File.Exists(tempM4a));
        Assert.False(Directory.Exists(tempDir));
    }

    /// <summary>
    /// ConvertMp4ToM4aが無効な場合、MP4トラックは変換サービスを介さず元パスのまま転送されることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferTracksAsync_ConvertMp4ToM4a無効時_変換せず元MP4ファイルが転送されること()
    {
        // Arrange
        var mp4Track = new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(@"C:\Music\Song.mp4"),
            title: "Song",
            artist: "Artist",
            album: "Album",
            duration: TimeSpan.FromMinutes(3));

        var mockRepo = new Mock<IDataTransferRepository>();
        mockRepo.Setup(r => r.TransferTrackAsync(mp4Track.FilePath, @"E:\Music", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.LoadSettings()).Returns(new AppSettings { ConvertMp4ToM4a = false });

        var mockTranscoder = new Mock<IAudioTranscodeService>();
        mockTranscoder.Setup(t => t.NeedsTranscode(mp4Track.FilePath, It.Is<AudioTranscodeOptions>(o => !o.ConvertMp4ToM4a)))
            .Returns(false);

        var sut = new DataTransferApplicationService(mockRepo.Object, mockTranscoder.Object, mockSettings.Object);

        // Act
        int count = await sut.TransferTracksAsync([mp4Track], @"E:\Music");

        // Assert
        Assert.Equal(1, count);
        mockTranscoder.Verify(t => t.PrepareForTransferAsync(It.IsAny<string>(), It.IsAny<AudioTranscodeOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        mockRepo.Verify(r => r.TransferTrackAsync(mp4Track.FilePath, @"E:\Music", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// ConvertFlacToMp3が有効な場合、FLACトラックが変換サービス経由で一時MP3ファイルとして転送され、転送後に一時ファイルが自動削除されることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferTracksAsync_ConvertFlacToMp3有効時_一時MP3ファイルが転送され転送後に削除されること()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"AE_AppFlac_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string tempMp3 = Path.Combine(tempDir, "Lossless.mp3");
        File.WriteAllText(tempMp3, "mp3-data");

        var flacTrack = new Track(
            id: TrackId.New(),
            filePath: AudioPath.Create(@"C:\Music\Lossless.flac"),
            title: "Lossless",
            artist: "Artist",
            album: "Album",
            duration: TimeSpan.FromMinutes(4));

        var mockRepo = new Mock<IDataTransferRepository>();
        mockRepo.Setup(r => r.TransferTrackAsync(It.Is<AudioPath>(p => p.Value == tempMp3), @"E:\Music", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.LoadSettings()).Returns(new AppSettings { ConvertFlacToMp3 = true });

        var mockTranscoder = new Mock<IAudioTranscodeService>();
        mockTranscoder.Setup(t => t.NeedsTranscode(flacTrack.FilePath, It.Is<AudioTranscodeOptions>(o => o.ConvertFlacToMp3)))
            .Returns(true);
        using var transcodedResult = new TranscodedAudioResult(
            flacTrack.FilePath,
            tempMp3,
            "Lossless.mp3",
            wasTranscoded: true,
            temporaryDirectoryPath: tempDir);
        mockTranscoder.Setup(t => t.PrepareForTransferAsync(flacTrack.FilePath, It.Is<AudioTranscodeOptions>(o => o.ConvertFlacToMp3), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcodedResult);

        var sut = new DataTransferApplicationService(mockRepo.Object, mockTranscoder.Object, mockSettings.Object);

        // Act
        int count = await sut.TransferTracksAsync([flacTrack], @"E:\Music");

        // Assert
        Assert.Equal(1, count);
        mockRepo.Verify(r => r.TransferTrackAsync(It.Is<AudioPath>(p => p.Value == tempMp3), @"E:\Music", null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(File.Exists(tempMp3));
        Assert.False(Directory.Exists(tempDir));
    }

    /// <summary>
    /// LRC 同梱設定が有効な種別の .lrc のみが変換後ファイル名ベースで TransferCompanionFileAsync へ渡され、無効な種別はスキップされることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferTracksAsync_Lrc同梱設定有効時_該当種別のみ変換後同名ベースで同梱転送されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_AppLrc_{Guid.NewGuid():N}");
        string sourceDir = Path.Combine(tempRoot, "Source");
        string transcodeDir = Path.Combine(tempRoot, "Transcoded");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(transcodeDir);

        string manualAudio = Path.Combine(sourceDir, "01_Manual.mp3");
        string manualLrc = Path.Combine(sourceDir, "01_Manual.lrc");
        string alignedAudio = Path.Combine(sourceDir, "02_Aligned.flac");
        string alignedLrc = Path.Combine(sourceDir, "02_Aligned.lrc");
        string transcribedAudio = Path.Combine(sourceDir, "03_Transcribed.mp3");
        string transcribedLrc = Path.Combine(sourceDir, "03_Transcribed.lrc");
        string transcodedMp3 = Path.Combine(transcodeDir, "02_Aligned.mp3");

        File.WriteAllText(manualAudio, "audio");
        File.WriteAllText(manualLrc, "[00:01.00]手動歌詞");
        File.WriteAllText(alignedAudio, "flac");
        File.WriteAllText(alignedLrc, "[re:Audio Effector (TextAlignment)]\n[00:01.00]テキスト生成");
        File.WriteAllText(transcribedAudio, "audio");
        File.WriteAllText(transcribedLrc, "[re:Audio Effector (AudioTranscription)]\n[00:01.00]文字起こし");
        File.WriteAllText(transcodedMp3, "mp3");

        try
        {
            var t1 = new Track(TrackId.New(), AudioPath.Create(manualAudio), "Manual", "Artist", "Album", TimeSpan.FromMinutes(3));
            var t2 = new Track(TrackId.New(), AudioPath.Create(alignedAudio), "Aligned", "Artist", "Album", TimeSpan.FromMinutes(3));
            var t3 = new Track(TrackId.New(), AudioPath.Create(transcribedAudio), "Transcribed", "Artist", "Album", TimeSpan.FromMinutes(3));

            var mockRepo = new Mock<IDataTransferRepository>();
            mockRepo.Setup(r => r.TransferTrackAsync(It.IsAny<AudioPath>(), @"E:\Music", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            mockRepo.Setup(r => r.TransferCompanionFileAsync(It.IsAny<string>(), @"E:\Music", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.LoadSettings()).Returns(new AppSettings
            {
                ConvertFlacToMp3 = true,
                TransferManualLrcFiles = true,
                TransferTextAlignedLrcFiles = true,
                TransferTranscribedLrcFiles = false
            });

            var mockTranscoder = new Mock<IAudioTranscodeService>();
            mockTranscoder.Setup(t => t.NeedsTranscode(It.IsAny<string>(), It.IsAny<AudioTranscodeOptions>()))
                .Returns<string, AudioTranscodeOptions>((p, o) => o.ConvertFlacToMp3 && p.EndsWith(".flac", StringComparison.OrdinalIgnoreCase));
            using var transcodedResult = new TranscodedAudioResult(alignedAudio, transcodedMp3, "02_Aligned.mp3", wasTranscoded: true, temporaryDirectoryPath: transcodeDir);
            mockTranscoder.Setup(t => t.PrepareForTransferAsync(alignedAudio, It.IsAny<AudioTranscodeOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(transcodedResult);

            var mockParser = new Mock<AudioEffector.Domain.Services.ILrcParser>();
            mockParser.Setup(p => p.ClassifyOrigin(It.Is<string>(s => s.Contains("TextAlignment", StringComparison.Ordinal))))
                .Returns(LrcOriginType.AutoGeneratedTextAlignment);
            mockParser.Setup(p => p.ClassifyOrigin(It.Is<string>(s => s.Contains("AudioTranscription", StringComparison.Ordinal))))
                .Returns(LrcOriginType.AutoGeneratedAudioTranscription);
            mockParser.Setup(p => p.ClassifyOrigin(It.Is<string>(s => !s.Contains("TextAlignment", StringComparison.Ordinal) && !s.Contains("AudioTranscription", StringComparison.Ordinal))))
                .Returns(LrcOriginType.ManualOrExternal);

            var sut = new DataTransferApplicationService(mockRepo.Object, mockTranscoder.Object, mockSettings.Object, mockParser.Object);

            // Act
            int count = await sut.TransferTracksAsync([t1, t2, t3], @"E:\Music");

            // Assert
            Assert.Equal(3, count);
            mockRepo.Verify(r => r.TransferCompanionFileAsync(manualLrc, @"E:\Music", "01_Manual.lrc", It.IsAny<CancellationToken>()), Times.Once);
            mockRepo.Verify(r => r.TransferCompanionFileAsync(alignedLrc, @"E:\Music", "02_Aligned.lrc", It.IsAny<CancellationToken>()), Times.Once);
            mockRepo.Verify(r => r.TransferCompanionFileAsync(transcribedLrc, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// 同梱 .lrc の転送処理で例外が発生しても音声トラックの転送成功件数および後続トラック転送が妨げられないことを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferTracksAsync_Lrc同梱転送で例外発生時_音声転送成功と後続トラック転送が継続されること()
    {
        // Arrange
        string tempRoot = Path.Combine(Path.GetTempPath(), $"AE_AppLrcFail_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string audio1 = Path.Combine(tempRoot, "01.mp3");
        string lrc1 = Path.Combine(tempRoot, "01.lrc");
        string audio2 = Path.Combine(tempRoot, "02.mp3");
        File.WriteAllText(audio1, "a1");
        File.WriteAllText(lrc1, "[00:01.00]lyric");
        File.WriteAllText(audio2, "a2");

        try
        {
            var t1 = new Track(TrackId.New(), AudioPath.Create(audio1), "T1", "Artist", "Album", TimeSpan.FromMinutes(3));
            var t2 = new Track(TrackId.New(), AudioPath.Create(audio2), "T2", "Artist", "Album", TimeSpan.FromMinutes(3));

            var mockRepo = new Mock<IDataTransferRepository>();
            mockRepo.Setup(r => r.TransferTrackAsync(It.IsAny<AudioPath>(), @"E:\Music", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            mockRepo.Setup(r => r.TransferCompanionFileAsync(lrc1, @"E:\Music", "01.lrc", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("LRC 書き込み失敗"));

            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.LoadSettings()).Returns(new AppSettings { TransferManualLrcFiles = true });

            var mockParser = new Mock<AudioEffector.Domain.Services.ILrcParser>();
            mockParser.Setup(p => p.ClassifyOrigin(It.IsAny<string>())).Returns(LrcOriginType.ManualOrExternal);

            var sut = new DataTransferApplicationService(mockRepo.Object, null, mockSettings.Object, mockParser.Object);

            // Act
            int count = await sut.TransferTracksAsync([t1, t2], @"E:\Music");

            // Assert
            Assert.Equal(2, count);
            mockRepo.Verify(r => r.TransferTrackAsync(t2.FilePath, @"E:\Music", null, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// ISettingsService.LoadSettings が例外送出または null を返却した場合でも、既定オプションへフォールバックして音声転送が継続されることを検証します。
    /// </summary>
    /// <returns>非同期タスク</returns>
    [Fact]
    public async Task TransferTracksAsync_設定読み込み例外またはNull返却時_既定値へフォールバックし音声転送を継続すること()
    {
        // Arrange
        var track = CreateTestTrack("FallbackSong");
        var mockRepo = new Mock<IDataTransferRepository>();
        mockRepo.Setup(r => r.TransferTrackAsync(track.FilePath, @"E:\Music", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var throwingSettings = new Mock<ISettingsService>();
        throwingSettings.Setup(s => s.LoadSettings()).Throws(new IOException("settings read failed"));

        var nullSettings = new Mock<ISettingsService>();
        nullSettings.Setup(s => s.LoadSettings()).Returns((AppSettings)null!);

        var sutThrow = new DataTransferApplicationService(mockRepo.Object, null, throwingSettings.Object);
        var sutNull = new DataTransferApplicationService(mockRepo.Object, null, nullSettings.Object);

        // Act
        int countFromThrow = await sutThrow.TransferTracksAsync([track], @"E:\Music");
        int countFromNull = await sutNull.TransferTracksAsync([track], @"E:\Music");

        // Assert
        Assert.Equal(1, countFromThrow);
        Assert.Equal(1, countFromNull);
        mockRepo.Verify(r => r.TransferCompanionFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
