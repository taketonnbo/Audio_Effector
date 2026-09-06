using System.Collections.Generic;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Audio;
using AudioEffector.Infrastructure.Library;
using AudioEffector.Presentation.ViewModels;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="LibraryViewModel"/> のアルバム収録曲展開（ソケット表示）および排他制御を検証するテストクラス。
/// </summary>
public sealed class LibraryViewModelTests
{
    private readonly Mock<ITrackRepository> _trackRepoMock = new();
    private readonly Mock<IFavoriteRepository> _favoriteRepoMock = new();
    private readonly TagLibMetadataExtractor _metadataExtractor = new();
    private readonly Mock<IAudioService> _audioServiceMock = new();
    private readonly Mock<ISettingsService> _settingsServiceMock = new();
    private readonly InMemoryEventBus _eventBus = new();

    private readonly LibraryApplicationService _libraryAppService;

    public LibraryViewModelTests()
    {
        _libraryAppService = new LibraryApplicationService(
            _trackRepoMock.Object,
            _favoriteRepoMock.Object,
            _metadataExtractor,
            _eventBus);

        _settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());
    }

    private LibraryViewModel CreateViewModel()
    {
        return new LibraryViewModel(
            _libraryAppService,
            _audioServiceMock.Object,
            _settingsServiceMock.Object);
    }

    [Fact]
    public void Album_IsTracksExpanded_プロパティ変更時にPropertyChangedが発火する()
    {
        // Arrange
        var album = new Album("Test Album", "Test Artist");
        var propertyChangedFired = false;
        album.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Album.IsTracksExpanded))
            {
                propertyChangedFired = true;
            }
        };

        // Act
        album.IsTracksExpanded = true;

        // Assert
        Assert.True(propertyChangedFired);
        Assert.True(album.IsTracksExpanded);
    }

    [Fact]
    public void ToggleAlbumTracksCommand_未展開アルバムに対して実行_展開状態となりExpandedAlbumに設定される()
    {
        // Arrange
        using var vm = CreateViewModel();
        var album = new Album("Album 1", "Artist 1");

        // Act
        vm.ToggleAlbumTracksCommand.Execute(album);

        // Assert
        Assert.True(album.IsTracksExpanded);
        Assert.Same(album, vm.ExpandedAlbum);
    }

    [Fact]
    public void ToggleAlbumTracksCommand_展開中アルバムに対して再実行_折りたたまれExpandedAlbumがnullになる()
    {
        // Arrange
        using var vm = CreateViewModel();
        var album = new Album("Album 1", "Artist 1");
        vm.ToggleAlbumTracksCommand.Execute(album);
        Assert.True(album.IsTracksExpanded);

        // Act
        vm.ToggleAlbumTracksCommand.Execute(album);

        // Assert
        Assert.False(album.IsTracksExpanded);
        Assert.Null(vm.ExpandedAlbum);
    }

    [Fact]
    public void ToggleAlbumTracksCommand_別アルバムが展開中に実行_前のアルバムが閉じ新規アルバムが開く排他制御が動作する()
    {
        // Arrange
        using var vm = CreateViewModel();
        var album1 = new Album("Album 1", "Artist 1");
        var album2 = new Album("Album 2", "Artist 2");

        // Act 1: Album 1 を展開
        vm.ToggleAlbumTracksCommand.Execute(album1);
        Assert.True(album1.IsTracksExpanded);
        Assert.Same(album1, vm.ExpandedAlbum);

        // Act 2: Album 2 を展開
        vm.ToggleAlbumTracksCommand.Execute(album2);

        // Assert: Album 1 は閉じられ、Album 2 が開く
        Assert.False(album1.IsTracksExpanded);
        Assert.True(album2.IsTracksExpanded);
        Assert.Same(album2, vm.ExpandedAlbum);
    }

    [Fact]
    public void CloseExpandedAlbum_展開中アルバムがある場合_安全に閉じられる()
    {
        // Arrange
        using var vm = CreateViewModel();
        var album = new Album("Album 1", "Artist 1");
        vm.ToggleAlbumTracksCommand.Execute(album);
        Assert.True(album.IsTracksExpanded);

        // Act
        vm.CloseExpandedAlbum();

        // Assert
        Assert.False(album.IsTracksExpanded);
        Assert.Null(vm.ExpandedAlbum);
    }

    [Fact]
    public void ShowAlbumInfoCommand_正常に初期化されアルバムを渡して実行できる()
    {
        // Arrange
        using var vm = CreateViewModel();
        var album = new Album("Test Album", "Test Artist");

        // Assert 1: コマンドが初期化されている
        Assert.NotNull(vm.ShowAlbumInfoCommand);
        Assert.True(vm.ShowAlbumInfoCommand.CanExecute(album));

        // Act & Assert 2: 例外なく実行できる
        var exception = Record.Exception(() => vm.ShowAlbumInfoCommand.Execute(album));
        Assert.Null(exception);
    }

    /// <summary>
    /// シャッフルON時にPlayAlbumを実行した際、アルバム収録曲の中からランダムな1曲が
    /// startTrackとして選定されてSetPlaylistおよびPlayTrackに渡されることを検証します。
    /// </summary>
    [Fact]
    public void PlayAlbum_シャッフルON時_収録曲からランダムな1曲がstartTrackとして選ばれSetPlaylistとPlayTrackに渡される()
    {
        // Arrange
        using var vm = CreateViewModel();
        _audioServiceMock.Setup(a => a.IsShuffleEnabled).Returns(true);

        var album = new Album("Album Shuffle", "Artist Shuffle");
        var track1 = new Track { FilePath = @"C:\Music\s1.mp3", Title = "S1" };
        var track2 = new Track { FilePath = @"C:\Music\s2.mp3", Title = "S2" };
        var track3 = new Track { FilePath = @"C:\Music\s3.mp3", Title = "S3" };
        album.Tracks.Add(track1);
        album.Tracks.Add(track2);
        album.Tracks.Add(track3);

        Track? passedStartTrack = null;
        _audioServiceMock
            .Setup(a => a.SetPlaylist(It.IsAny<List<Track>>(), It.IsAny<Track?>()))
            .Callback<List<Track>, Track?>((tracks, start) => passedStartTrack = start);

        Track? playedTrack = null;
        _audioServiceMock
            .Setup(a => a.PlayTrack(It.IsAny<Track>()))
            .Callback<Track>(t => playedTrack = t);

        // Act
        vm.PlayAlbum(album);

        // Assert
        Assert.NotNull(passedStartTrack);
        Assert.Contains(passedStartTrack, album.Tracks);
        Assert.Same(passedStartTrack, playedTrack);
        _audioServiceMock.Verify(a => a.SetPlaylist(It.Is<List<Track>>(l => l.Count == 3), passedStartTrack), Times.Once);
        _audioServiceMock.Verify(a => a.PlayTrack(passedStartTrack), Times.Once);
    }

    /// <summary>
    /// シャッフルOFF時にPlayAlbumを実行した際、先頭トラックがstartTrackとして選定されて
    /// SetPlaylistおよびPlayTrackに渡されることを検証します。
    /// </summary>
    [Fact]
    public void PlayAlbum_シャッフルOFF時_先頭トラックがstartTrackとして選ばれSetPlaylistとPlayTrackに渡される()
    {
        // Arrange
        using var vm = CreateViewModel();
        _audioServiceMock.Setup(a => a.IsShuffleEnabled).Returns(false);

        var album = new Album("Album Normal", "Artist Normal");
        var track1 = new Track { FilePath = @"C:\Music\n1.mp3", Title = "N1" };
        var track2 = new Track { FilePath = @"C:\Music\n2.mp3", Title = "N2" };
        album.Tracks.Add(track1);
        album.Tracks.Add(track2);

        Track? passedStartTrack = null;
        _audioServiceMock
            .Setup(a => a.SetPlaylist(It.IsAny<List<Track>>(), It.IsAny<Track?>()))
            .Callback<List<Track>, Track?>((tracks, start) => passedStartTrack = start);

        Track? playedTrack = null;
        _audioServiceMock
            .Setup(a => a.PlayTrack(It.IsAny<Track>()))
            .Callback<Track>(t => playedTrack = t);

        // Act
        vm.PlayAlbum(album);

        // Assert
        Assert.NotNull(passedStartTrack);
        Assert.Same(track1, passedStartTrack);
        Assert.Same(track1, playedTrack);
        _audioServiceMock.Verify(a => a.SetPlaylist(It.Is<List<Track>>(l => l.Count == 2), track1), Times.Once);
        _audioServiceMock.Verify(a => a.PlayTrack(track1), Times.Once);
    }
}
