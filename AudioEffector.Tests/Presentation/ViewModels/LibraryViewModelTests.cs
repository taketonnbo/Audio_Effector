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
}
