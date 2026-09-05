using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Entities.DataTransfer;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Application.ApplicationServices;

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
}
