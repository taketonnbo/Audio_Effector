using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Audio;
using AudioEffector.Infrastructure.Library;
using AudioEffector.Infrastructure.Repository;
using AudioEffector.Presentation.ViewModels;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="LibraryViewModel"/> のアルバム収録曲展開（ソケット表示）および排他制御を検証するテストクラス。
/// </summary>
public sealed class LibraryViewModelTests : IDisposable
{
    private readonly DirectoryInfo _tempDirectory = Directory.CreateTempSubdirectory();

    public void Dispose() => _tempDirectory.Delete(true);

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

    private LibraryViewModel CreateViewModel(IFavoriteRepository? favorites = null)
    {
        return new LibraryViewModel(
            favorites == null ? _libraryAppService : new LibraryApplicationService(_trackRepoMock.Object, favorites, _metadataExtractor, _eventBus),
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

    /// <summary>
    /// ShowAlbumInfoCommand実行時に展開中トレイが閉じられ、AlbumDetailRequestedイベントが発火することを検証します
    /// </summary>
    [Fact]
    public void ShowAlbumInfoCommand_展開中アルバムありで実行_トレイが閉じられAlbumDetailRequestedが発火する()
    {
        // Arrange
        using var sut = CreateViewModel();
        var expandedAlbum = new Album("Expanded Album", "Artist 1");
        var targetAlbum = new Album("Test Album", "Test Artist");
        sut.ToggleAlbumTracksCommand.Execute(expandedAlbum);
        Assert.True(expandedAlbum.IsTracksExpanded);

        Album? requestedAlbum = null;
        sut.AlbumDetailRequested += album => requestedAlbum = album;

        // Act
        sut.ShowAlbumInfoCommand.Execute(targetAlbum);

        // Assert
        Assert.False(expandedAlbum.IsTracksExpanded);
        Assert.Null(sut.ExpandedAlbum);
        Assert.Same(targetAlbum, requestedAlbum);
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

    /// <summary>
    /// アルバムの途中曲からPlayTrackCommandを実行した際、PlaybackRequestedにはアルバム全収録曲が渡され、
    /// AudioService.SetPlaylistには選択曲以降の楽曲のみが渡されることを検証します。
    /// </summary>
    [Fact]
    public void PlayTrackCommand_アルバム途中曲を指定して実行_PlaybackRequestedには全収録曲が渡されSetPlaylistには選択曲以降のみが渡される()
    {
        // Arrange
        using var sut = CreateViewModel();
        var track1 = new Track { FilePath = @"C:\Music\t1.mp3", Title = "Track 1", Album = "Album Mid", Artist = "Artist Mid", TrackNumber = 1 };
        var track2 = new Track { FilePath = @"C:\Music\t2.mp3", Title = "Track 2", Album = "Album Mid", Artist = "Artist Mid", TrackNumber = 2 };
        var track3 = new Track { FilePath = @"C:\Music\t3.mp3", Title = "Track 3", Album = "Album Mid", Artist = "Artist Mid", TrackNumber = 3 };
        var album = new Album("Album Mid", "Artist Mid", 2024, [track1, track2, track3]);
        sut.Albums.Add(album);

        IReadOnlyList<Track>? requestedTracks = null;
        Track? requestedStartTrack = null;
        string? requestedTitle = null;
        string? requestedSubtitle = null;
        sut.PlaybackRequested += (tracks, start, title, subtitle) =>
        {
            requestedTracks = tracks;
            requestedStartTrack = start;
            requestedTitle = title;
            requestedSubtitle = subtitle;
        };

        // Act
        sut.PlayTrackCommand.Execute(track2);

        // Assert
        Assert.NotNull(requestedTracks);
        Assert.Equal([track1, track2, track3], requestedTracks);
        Assert.Same(track2, requestedStartTrack);
        Assert.Equal("Album Mid", requestedTitle);
        Assert.Equal("Artist Mid", requestedSubtitle);
        _audioServiceMock.Verify(
            a => a.SetPlaylist(It.Is<List<Track>>(list => list.SequenceEqual(new[] { track2, track3 })), track2),
            Times.Once);
        _audioServiceMock.Verify(a => a.PlayTrack(track2), Times.Once);
    }

    /// <summary>画面を開いた後の外部追加・解除を、画面からの切り替えと再照合が保持することを検証します</summary>
    [Fact]
    public async Task お気に入り_画面初期化後の外部変更を保持する()
    {
        // Arrange
        var path = Path.Combine(_tempDirectory.FullName, "favorites.json");
        using var first = new JsonFavoriteRepository(path);
        using var second = new JsonFavoriteRepository(path);
        var moved = new Track { Title = "Moved", Artist = "Artist", FilePath = "C:/Music/old.flac" };
        var removed = new Track { Title = "Removed", Artist = "Artist", FilePath = "C:/Music/removed.flac" };
        var external = new Track { Title = "External", FilePath = "C:/Music/external.flac" };
        var local = new Track { Title = "Local", FilePath = "C:/Music/local.flac" };
        await first.AddOrUpdateAsync(FavoriteTrackEntry.FromTrack(moved));
        await first.AddOrUpdateAsync(FavoriteTrackEntry.FromTrack(removed));
        using var sut = CreateViewModel(first);
        await second.AddOrUpdateAsync(FavoriteTrackEntry.FromTrack(external));
        await second.RemoveAsync(removed);

        // Act
        sut.ToggleFavorite(local);
        Assert.True(await second.ContainsAsync(external));
        Assert.True(await second.ContainsAsync(local));
        sut.ToggleFavorite(local);
        moved.FilePath = "C:/Music/new.flac";
        removed.Title = "Changed after removal";
        sut.Tracks.Add(moved);
        sut.Tracks.Add(removed);
        sut.SyncFavoritesWithLibraryTracks();

        // Assert
        var saved = await second.GetAllAsync();
        Assert.Equal(2, saved.Count);
        Assert.Contains(saved, e => e.LastKnownFilePath == external.FilePath);
        Assert.Contains(saved, e => e.LastKnownFilePath == moved.FilePath);
    }

    /// <summary>
    /// フォルダ移動や拡張子変更後でも、メタデータ一致でお気に入りが維持されGetFavoriteTracksで解決かつLastKnownFilePathが自動更新されることを検証します。
    /// </summary>
    [Fact]
    public void SyncFavoritesWithLibraryTracksおよびGetFavoriteTracks_フォルダ移動および拡張子変更後_メタデータ一致でお気に入り復元およびパス自動更新されること()
    {
        // Arrange
        List<FavoriteTrackEntry>? savedEntries = null;
        _favoriteRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FavoriteTrackEntry("Midnight City", "M83", "Hurry Up, We're Dreaming", 2, 243.5, @"C:\Old\02_Midnight_City.mp3", DateTime.Now)
            });
        _favoriteRepoMock.Setup(r => r.UpdateMetadataAsync(It.IsAny<IReadOnlyList<FavoriteMetadataChange>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<FavoriteMetadataChange>, CancellationToken>((changes, _) => savedEntries = changes.Select(c => c.Updated).ToList())
            .Returns(Task.CompletedTask);

        using var sut = CreateViewModel();
        var movedFlacTrack = new Track
        {
            FilePath = @"D:\Music\M83\02_Midnight_City.flac",
            Title = "Midnight City",
            Artist = "M83",
            Album = "Hurry Up, We're Dreaming",
            TrackNumber = 2,
            Duration = TimeSpan.FromSeconds(243.8)
        };
        sut.Tracks.Add(movedFlacTrack);

        // Act
        sut.SyncFavoritesWithLibraryTracks();
        var favoriteTracks = sut.GetFavoriteTracks();

        // Assert
        Assert.True(movedFlacTrack.IsFavorite);
        Assert.Single(favoriteTracks);
        Assert.Same(movedFlacTrack, favoriteTracks[0]);
        Assert.NotNull(savedEntries);
        Assert.Single(savedEntries);
        Assert.Equal(@"D:\Music\M83\02_Midnight_City.flac", savedEntries[0].LastKnownFilePath);
    }

    /// <summary>
    /// タグ未設定曲やタグ編集直後の楽曲でも、LastKnownFilePathフォールバックによりお気に入り登録が維持されメタデータが最新化されることを検証します。
    /// </summary>
    [Fact]
    public void SyncFavoritesWithLibraryTracks_タグ編集直後およびタグ未設定曲_LastKnownFilePathフォールバックでお気に入りが維持されること()
    {
        // Arrange
        List<FavoriteTrackEntry>? savedEntries = null;
        _favoriteRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FavoriteTrackEntry("Old Title", "Old Artist", "Old Album", 1, 200.0, @"D:\Music\Edited.flac", DateTime.Now),
                new FavoriteTrackEntry("Track01", "Unknown Artist", "Unknown Album", 0, 150.0, @"D:\Music\Untagged\Track01.mp3", DateTime.Now)
            });
        _favoriteRepoMock.Setup(r => r.UpdateMetadataAsync(It.IsAny<IReadOnlyList<FavoriteMetadataChange>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<FavoriteMetadataChange>, CancellationToken>((changes, _) => savedEntries = changes.Select(c => c.Updated).ToList())
            .Returns(Task.CompletedTask);

        using var sut = CreateViewModel();
        var editedTrack = new Track
        {
            FilePath = @"D:\Music\Edited.flac",
            Title = "Renamed Title",
            Artist = "Renamed Artist",
            Album = "Renamed Album",
            TrackNumber = 1,
            Duration = TimeSpan.FromSeconds(200.0)
        };
        var untaggedTrack = new Track
        {
            FilePath = @"D:\Music\Untagged\Track01.mp3",
            Title = "Track01",
            Artist = "Unknown Artist",
            Album = "Unknown Album",
            Duration = TimeSpan.FromSeconds(150.0)
        };
        sut.Tracks.Add(editedTrack);
        sut.Tracks.Add(untaggedTrack);

        // Act
        sut.SyncFavoritesWithLibraryTracks();

        // Assert
        Assert.True(editedTrack.IsFavorite);
        Assert.True(untaggedTrack.IsFavorite);
        Assert.NotNull(savedEntries);
        Assert.Equal("Renamed Title", savedEntries[0].Title);
    }

    /// <summary>
    /// ToggleFavoriteの追加・解除および重複登録防止が正しく動作し、メタデータ付きエントリが保存されることを検証します。
    /// </summary>
    [Fact]
    public async Task ToggleFavorite_追加と解除のトグルおよび重複登録防止_メタデータ付きエントリが正しく保存および削除されること()
    {
        // Arrange
        using var repository = new JsonFavoriteRepository(Path.Combine(_tempDirectory.FullName, "favorites.json"));
        using var sut = CreateViewModel(repository);
        var track = new Track
        {
            FilePath = @"D:\Music\Song.flac",
            Title = "Starboy",
            Artist = "The Weeknd",
            Album = "Starboy",
            TrackNumber = 1,
            Duration = TimeSpan.FromSeconds(230.0)
        };
        var duplicateInstance = new Track
        {
            FilePath = @"E:\Backup\Song.mp3",
            Title = "Starboy",
            Artist = "The Weeknd",
            Album = "Starboy",
            TrackNumber = 1,
            Duration = TimeSpan.FromSeconds(230.0)
        };
        sut.Tracks.Add(track);

        // Act 1: 追加
        sut.ToggleFavorite(track);

        // Assert 1
        Assert.True(track.IsFavorite);
        Assert.Single(await repository.GetAllAsync());
        var latestSaved = await repository.GetAllAsync();
        Assert.Equal("Starboy", latestSaved[0].Title);
        Assert.Equal("The Weeknd", latestSaved[0].Artist);
        Assert.Equal("Starboy", latestSaved[0].Album);
        Assert.Equal(1u, latestSaved[0].TrackNumber);
        Assert.Equal(230.0, latestSaved[0].DurationSeconds);

        // Act 2: 別インスタンスで再度お気に入り追加しても重複しないこと
        sut.ToggleFavorite(duplicateInstance);

        // Assert 2
        Assert.True(duplicateInstance.IsFavorite);
        Assert.Single(await repository.GetAllAsync());

        // Act 3: 解除
        sut.ToggleFavorite(track);

        // Assert 3
        Assert.False(track.IsFavorite);
        Assert.Empty(await repository.GetAllAsync());
    }

    /// <summary>
    /// 別フォルダに存在する同名・同再生時間のタグ未設定曲2件を同時にお気に入り登録でき、片方のみ解除しても他方が維持されることを検証します。
    /// </summary>
    [Fact]
    public async Task ToggleFavorite_別フォルダの同名未タグ曲2件_同時登録および片方のみ解除できること()
    {
        // Arrange
        using var repository = new JsonFavoriteRepository(Path.Combine(_tempDirectory.FullName, "favorites.json"));
        using var sut = CreateViewModel(repository);
        var untaggedA = new Track
        {
            FilePath = @"D:\Music\FolderA\Track01.mp3",
            Title = "Track01",
            Artist = "Unknown Artist",
            Album = "Unknown Album",
            Duration = TimeSpan.FromSeconds(200.0)
        };
        var untaggedB = new Track
        {
            FilePath = @"D:\Music\FolderB\Track01.mp3",
            Title = "Track01",
            Artist = "Unknown Artist",
            Album = "Unknown Album",
            Duration = TimeSpan.FromSeconds(200.0)
        };
        sut.Tracks.Add(untaggedA);
        sut.Tracks.Add(untaggedB);

        // Act 1: 2件ともお気に入り登録
        sut.ToggleFavorite(untaggedA);
        sut.ToggleFavorite(untaggedB);

        // Assert 1: 互いに上書きされず2件保持されること
        Assert.True(untaggedA.IsFavorite);
        Assert.True(untaggedB.IsFavorite);
        Assert.Equal(2, (await repository.GetAllAsync()).Count);

        // Act 2: FolderA のみお気に入り解除
        sut.ToggleFavorite(untaggedA);

        // Assert 2: FolderB はお気に入りのまま維持されること
        Assert.False(untaggedA.IsFavorite);
        Assert.True(untaggedB.IsFavorite);
        Assert.Single(await repository.GetAllAsync());
        Assert.Equal(@"D:\Music\FolderB\Track01.mp3", (await repository.GetAllAsync())[0].LastKnownFilePath);
    }

    /// <summary>
    /// 指定済みフォルダの差分再スキャン時に、新規追加ファイル・削除ファイル・空になったアルバムの除去が
    /// 既存TrackおよびAlbumインスタンスの参照を維持したまま正確に同期されることを検証します
    /// </summary>
    [Fact]
    public async Task LoadLibraryAsync_差分再スキャンで曲追加と曲削除および空アルバム除去が発生した場合_既存インスタンスを維持しつつ正確に同期されること()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_LibSync", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string trackA1Path = Path.Combine(tempDir, "a1.mp3");
            string trackB1Path = Path.Combine(tempDir, "b1.mp3");
            File.WriteAllText(trackA1Path, "a1");
            File.WriteAllText(trackB1Path, "b1");

            var metadataMap = new Dictionary<string, (string Title, string Artist, string Album, uint TrackNo)>(StringComparer.OrdinalIgnoreCase)
            {
                [trackA1Path] = ("Song A1", "Artist A", "Album A", 1),
                [trackB1Path] = ("Song B1", "Artist B", "Album B", 1),
            };

            using var sut = new LibraryViewModel(
                _libraryAppService,
                _audioServiceMock.Object,
                _settingsServiceMock.Object,
                playlistViewModel: null,
                trackMetadataLoader: path =>
                {
                    var info = metadataMap[path];
                    return new Track
                    {
                        FilePath = path,
                        Title = info.Title,
                        Artist = info.Artist,
                        Album = info.Album,
                        TrackNumber = info.TrackNo
                    };
                });

            // 初回スキャン
            var initialResult = await sut.LoadLibraryAsync(tempDir);
            Assert.Equal(2, initialResult.AddedCount);
            Assert.Equal(2, sut.Tracks.Count);
            Assert.Equal(2, sut.Albums.Count);

            var originalTrackA1 = sut.Tracks.Single(t => t.FilePath == trackA1Path);
            var originalAlbumA = sut.Albums.Single(a => a.Title == "Album A");
            var originalAlbumB = sut.Albums.Single(a => a.Title == "Album B");

            // Album B を展開状態にしておく
            sut.ToggleAlbumTracks(originalAlbumB);
            Assert.Same(originalAlbumB, sut.ExpandedAlbum);

            // Album A に新曲 a2.mp3 を追加、新規 Album C の c1.mp3 を追加、Album B の b1.mp3 を削除
            string trackA2Path = Path.Combine(tempDir, "a2.mp3");
            string trackC1Path = Path.Combine(tempDir, "c1.mp3");
            File.WriteAllText(trackA2Path, "a2");
            File.WriteAllText(trackC1Path, "c1");
            File.Delete(trackB1Path);

            metadataMap[trackA2Path] = ("Song A2", "Artist A", "Album A", 2);
            metadataMap[trackC1Path] = ("Song C1", "Artist C", "Album C", 1);

            var removedTracks = new List<Track>();
            var removedAlbums = new List<Album>();
            sut.TrackRemoved += t => removedTracks.Add(t);
            sut.AlbumRemoved += a => removedAlbums.Add(a);

            // Act: 再スキャン（差分同期）
            var rescanResult = await sut.RescanCurrentFolderAsync();

            // Assert
            Assert.Equal(2, rescanResult.AddedCount);
            Assert.Equal(1, rescanResult.RemovedCount);
            Assert.Equal(3, sut.Tracks.Count);
            Assert.Equal(2, sut.Albums.Count);

            // 既存インスタンス参照が維持されていること
            Assert.Same(originalTrackA1, sut.Tracks.Single(t => t.FilePath == trackA1Path));
            Assert.Same(originalAlbumA, sut.Albums.Single(a => a.Title == "Album A"));
            Assert.Equal(2, originalAlbumA.TrackCount);

            // 収録曲が0になった Album B が除去され、ExpandedAlbum も安全にクリアされていること
            Assert.DoesNotContain(sut.Albums, a => a.Title == "Album B");
            Assert.Null(sut.ExpandedAlbum);
            Assert.Single(removedTracks);
            Assert.Equal(trackB1Path, removedTracks[0].FilePath);
            Assert.Single(removedAlbums);
            Assert.Same(originalAlbumB, removedAlbums[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// DetectMetadataChangesOnRescan 設定の ON/OFF に応じて、最終更新日時が変化した既存ファイルの
    /// メタデータ再解析スキップおよび再読み込み反映が切り替わることを検証します
    /// </summary>
    [Fact]
    public async Task LoadLibraryAsync_DetectMetadataChangesOnRescan設定切替時_OFFでは再解析をスキップしONでは更新ファイルを再解析すること()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_ModSync", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string trackPath = Path.Combine(tempDir, "track.mp3");
            File.WriteAllText(trackPath, "v1");
            var initialTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(trackPath, initialTime);

            string currentTitle = "Initial Title";
            int loaderCallCount = 0;
            var settings = new AppSettings
            {
                LastLibraryPath = tempDir,
                DetectMetadataChangesOnRescan = false
            };
            _settingsServiceMock.Setup(s => s.LoadSettings()).Returns(settings);

            using var sut = new LibraryViewModel(
                _libraryAppService,
                _audioServiceMock.Object,
                _settingsServiceMock.Object,
                playlistViewModel: null,
                trackMetadataLoader: path =>
                {
                    Interlocked.Increment(ref loaderCallCount);
                    return new Track
                    {
                        FilePath = path,
                        Title = currentTitle,
                        Artist = "Artist",
                        Album = "Album"
                    };
                });

            await sut.LoadLibraryAsync(tempDir);
            Assert.Equal(1, loaderCallCount);
            Assert.Equal("Initial Title", sut.Tracks[0].Title);

            // ファイルの更新日時とタグ内容を変更
            currentTitle = "Updated Title";
            File.SetLastWriteTimeUtc(trackPath, initialTime.AddMinutes(10));

            // Act 1: 設定 OFF のまま再スキャン -> 再解析されない
            var offResult = await sut.RescanCurrentFolderAsync();

            // Assert 1
            Assert.Equal(0, offResult.UpdatedCount);
            Assert.Equal(1, loaderCallCount);
            Assert.Equal("Initial Title", sut.Tracks[0].Title);

            // Act 2: 設定 ON にして再スキャン -> 更新日時差分を検知して再解析・反映される
            settings.DetectMetadataChangesOnRescan = true;
            var onResult = await sut.RescanCurrentFolderAsync();

            // Assert 2
            Assert.Equal(1, onResult.UpdatedCount);
            Assert.Equal(2, loaderCallCount);
            Assert.Equal("Updated Title", sut.Tracks[0].Title);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// お気に入り登録済みの楽曲が別サブフォルダや新ファイルパスへ移動した後に再スキャンした際、
    /// メタデータ一致でお気に入り状態が維持され、favorites.json の LastKnownFilePath が新しいパスへ自動更新されることを検証します
    /// </summary>
    [Fact]
    public async Task LoadLibraryAsync_お気に入り楽曲のパスが変更された後に再スキャンした場合_メタデータ一致で該当楽曲のパスに自動更新されること()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_FavRescan", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string oldPath = Path.Combine(tempDir, "OldFolder", "01_FavoriteSong.mp3");
            string newDir = Path.Combine(tempDir, "NewFolder");
            Directory.CreateDirectory(newDir);
            string newPath = Path.Combine(newDir, "01_FavoriteSong.flac");
            File.WriteAllText(newPath, "flac");

            List<FavoriteTrackEntry>? savedEntries = null;
            _favoriteRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[]
                {
                    new FavoriteTrackEntry("Favorite Song", "Fav Artist", "Fav Album", 1, 210.0, oldPath, DateTime.Now)
                });
            _favoriteRepoMock.Setup(r => r.UpdateMetadataAsync(It.IsAny<IReadOnlyList<FavoriteMetadataChange>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<FavoriteMetadataChange>, CancellationToken>((changes, _) => savedEntries = changes.Select(c => c.Updated).ToList())
                .Returns(Task.CompletedTask);

            using var sut = new LibraryViewModel(
                _libraryAppService,
                _audioServiceMock.Object,
                _settingsServiceMock.Object,
                playlistViewModel: null,
                trackMetadataLoader: path => new Track
                {
                    FilePath = path,
                    Title = "Favorite Song",
                    Artist = "Fav Artist",
                    Album = "Fav Album",
                    TrackNumber = 1,
                    Duration = TimeSpan.FromSeconds(210.0)
                });

            // Act
            await sut.LoadLibraryAsync(tempDir);

            // Assert
            var loadedTrack = Assert.Single(sut.Tracks);
            Assert.True(loadedTrack.IsFavorite);
            Assert.NotNull(savedEntries);
            Assert.Single(savedEntries);
            Assert.Equal(newPath, savedEntries[0].LastKnownFilePath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// ファイルの追加・削除がなく既存ファイルのメタデータのみが更新された再スキャン時に、
    /// LibrarySynced イベント経由で AllSongsViewModel および ArtistsViewModel の集約・ソート結果が自動再構築されることを検証します
    /// </summary>
    [Fact]
    public async Task LoadLibraryAsync_既存ファイルのみメタデータ更新時_AllSongsViewModelとArtistsViewModelが自動再構築されること()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_SyncProp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string trackPath = Path.Combine(tempDir, "track.mp3");
            File.WriteAllText(trackPath, "v1");
            var initialTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(trackPath, initialTime);

            string currentArtist = "Old Artist";
            string currentTitle = "Zeta Song";
            var settings = new AppSettings
            {
                LastLibraryPath = tempDir,
                DetectMetadataChangesOnRescan = true
            };
            _settingsServiceMock.Setup(s => s.LoadSettings()).Returns(settings);

            using var sut = new LibraryViewModel(
                _libraryAppService,
                _audioServiceMock.Object,
                _settingsServiceMock.Object,
                playlistViewModel: null,
                trackMetadataLoader: path => new Track
                {
                    FilePath = path,
                    Title = currentTitle,
                    Artist = currentArtist,
                    Album = "Shared Album",
                    Duration = TimeSpan.FromMinutes(3)
                });

            using var allSongsVm = new AllSongsViewModel(sut, _audioServiceMock.Object, TimeSpan.Zero);
            using var artistsVm = new ArtistsViewModel(sut, _audioServiceMock.Object, TimeSpan.Zero);

            await sut.LoadLibraryAsync(tempDir);
            Assert.Equal("Old Artist", Assert.Single(artistsVm.FilteredArtists).Name);
            Assert.Equal("Zeta Song", Assert.Single(allSongsVm.FilteredTracks).Title);

            // 既存ファイルのタグ（アーティスト名・タイトル）と更新日時のみを変更
            currentArtist = "New Artist";
            currentTitle = "Alpha Song";
            File.SetLastWriteTimeUtc(trackPath, initialTime.AddMinutes(5));

            // Act
            var syncResult = await sut.RescanCurrentFolderAsync();

            // Assert
            Assert.Equal(0, syncResult.AddedCount);
            Assert.Equal(0, syncResult.RemovedCount);
            Assert.Equal(1, syncResult.UpdatedCount);
            Assert.Equal("New Artist", Assert.Single(artistsVm.FilteredArtists).Name);
            Assert.Equal("Alpha Song", Assert.Single(allSongsVm.FilteredTracks).Title);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// SortLibrary 実行時、同一アーティストが複数のアルバムを持つ場合にアルバムタイトルを第2ソートキー（タイブレーカー）として
    /// 大文字小文字無視で決定論的にソートされることを検証します
    /// </summary>
    [Fact]
    public void SortLibrary_同一アーティストの複数アルバムが存在する場合_アルバム名を第2キーとして決定論的にソートされること()
    {
        // Arrange
        using var sut = CreateViewModel();
        sut.Albums.Add(new Album("Zebra Album", "Daft Punk"));
        sut.Albums.Add(new Album("alpha Album", "daft punk"));
        sut.Albums.Add(new Album("Middle Album", "Daft Punk"));
        sut.SelectedSortOption = "Artist";
        sut.IsAscending = true;

        // Act
        sut.SortLibrary();

        // Assert
        Assert.Equal("alpha Album", sut.Albums[0].Title);
        Assert.Equal("Middle Album", sut.Albums[1].Title);
        Assert.Equal("Zebra Album", sut.Albums[2].Title);
    }

    /// <summary>
    /// LoadLibraryAsync にキャンセル済みトークンが渡された場合、ScanProgress が 0.0 にリセットされ IsLoading が false に戻ることを検証します
    /// </summary>
    [Fact]
    public async Task LoadLibraryAsync_キャンセルされた場合_ScanProgressが0にリセットされIsLoadingがfalseに戻ること()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_Cancel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "s1.mp3"), "s1");
            using var sut = CreateViewModel();
            sut.ScanProgress = 0.5;
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            await sut.LoadLibraryAsync(tempDir, cts.Token);

            // Assert
            Assert.False(sut.IsLoading);
            Assert.Equal(0.0, sut.ScanProgress);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// 登録済みの楽曲が存在する状態でフォルダー列挙が失敗（例: UnauthorizedAccessException）した場合、
    /// UI コレクション・永続化データ（tracks.json）・既存 TrackId がすべて維持され、次回の正常スキャンでも TrackId が引き継がれることを検証します。
    /// </summary>
    [Fact]
    public async Task LoadLibraryAsync_登録済み楽曲がある状態でフォルダー列挙に失敗した場合_UIと永続データとTrackIdが維持されること()
    {
        // Arrange
        string musicDir = Path.Combine(_tempDirectory.FullName, "music_enum_fail");
        string dataDir = Path.Combine(_tempDirectory.FullName, "data_enum_fail");
        Directory.CreateDirectory(musicDir);
        Directory.CreateDirectory(dataDir);
        string songPath = Path.Combine(musicDir, "keep_me.mp3");
        await File.WriteAllTextAsync(songPath, "dummy audio");

        string tracksJsonPath = Path.Combine(dataDir, "tracks.json");
        using var realTrackRepo = new JsonTrackRepository(tracksJsonPath);
        var appService = new LibraryApplicationService(realTrackRepo, _favoriteRepoMock.Object, _metadataExtractor, _eventBus);

        bool shouldFailEnumeration = false;
        using var sut = new LibraryViewModel(
            appService,
            _audioServiceMock.Object,
            _settingsServiceMock.Object,
            playlistViewModel: null,
            trackMetadataLoader: file => new Track
            {
                FilePath = file,
                Title = Path.GetFileNameWithoutExtension(file),
                Artist = "Keep Artist",
                Album = "Keep Album",
            },
            fileEnumerator: folder =>
            {
                if (shouldFailEnumeration)
                {
                    throw new UnauthorizedAccessException("サブフォルダーへのアクセスが拒否されました。");
                }

                return Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);
            });

        // 初回の正常スキャンで UI と tracks.json に 1 曲を登録
        await sut.LoadLibraryAsync(musicDir);
        Track initialTrack = Assert.Single(sut.Tracks);
        TrackId originalTrackId = initialTrack.Id;

        // Act: フォルダー列挙が例外で失敗する状態で再スキャン
        shouldFailEnumeration = true;
        await sut.LoadLibraryAsync(musicDir);

        // Assert: UI・永続データ・TrackId がすべて維持されていること
        Assert.False(sut.IsLoading);
        Track afterFailUiTrack = Assert.Single(sut.Tracks);
        Assert.Equal(originalTrackId, afterFailUiTrack.Id);
        Assert.Single(sut.Albums);

        using (var verifyRepo = new JsonTrackRepository(tracksJsonPath))
        {
            IReadOnlyList<Track> persisted = await verifyRepo.GetAllAsync();
            Track persistedTrack = Assert.Single(persisted);
            Assert.Equal(originalTrackId, persistedTrack.Id);
            Assert.Equal(songPath, persistedTrack.FilePath);
        }

        // さらに別インスタンスで正常再スキャンしても元の TrackId が引き継がれること
        shouldFailEnumeration = false;
        using var reloadedSut = new LibraryViewModel(
            appService,
            _audioServiceMock.Object,
            _settingsServiceMock.Object,
            playlistViewModel: null,
            trackMetadataLoader: file => new Track
            {
                FilePath = file,
                Title = Path.GetFileNameWithoutExtension(file),
                Artist = "Keep Artist",
                Album = "Keep Album",
            });
        await reloadedSut.LoadLibraryAsync(musicDir);
        Assert.Equal(originalTrackId, Assert.Single(reloadedSut.Tracks).Id);
    }

    /// <summary>
    /// 正常に走査した空フォルダーを読み込んだ場合は、列挙失敗と区別して UI および永続データが 0 件へ同期されることを検証します。
    /// </summary>
    [Fact]
    public async Task LoadLibraryAsync_正常に走査した空フォルダーの場合_0件としてUIと永続データへ同期されること()
    {
        // Arrange
        string musicDir = Path.Combine(_tempDirectory.FullName, "music_empty_ok");
        string dataDir = Path.Combine(_tempDirectory.FullName, "data_empty_ok");
        Directory.CreateDirectory(musicDir);
        Directory.CreateDirectory(dataDir);
        string songPath = Path.Combine(musicDir, "remove_me.mp3");
        await File.WriteAllTextAsync(songPath, "dummy audio");

        string tracksJsonPath = Path.Combine(dataDir, "tracks.json");
        using var realTrackRepo = new JsonTrackRepository(tracksJsonPath);
        var appService = new LibraryApplicationService(realTrackRepo, _favoriteRepoMock.Object, _metadataExtractor, _eventBus);

        using var sut = new LibraryViewModel(
            appService,
            _audioServiceMock.Object,
            _settingsServiceMock.Object,
            playlistViewModel: null,
            trackMetadataLoader: file => new Track
            {
                FilePath = file,
                Title = Path.GetFileNameWithoutExtension(file),
                Artist = "Artist",
                Album = "Album",
            });

        await sut.LoadLibraryAsync(musicDir);
        Assert.Single(sut.Tracks);

        // Act: ファイルを削除して正常な空フォルダーとして再スキャン
        File.Delete(songPath);
        await sut.LoadLibraryAsync(musicDir);

        // Assert
        Assert.Empty(sut.Tracks);
        using var verifyRepo = new JsonTrackRepository(tracksJsonPath);
        Assert.Empty(await verifyRepo.GetAllAsync());
    }

    /// <summary>
    /// 同名アルバム・別アーティストの楽曲を読み込んだ際、LibraryViewModelのアルバム集約とLibraryApplicationService.GetAlbumByIdAsyncの収録曲が完全に一致し、片方削除時も他方が維持されることを検証します。
    /// </summary>
    [Fact]
    public async Task LoadLibraryAsync_同名アルバム別アーティスト混在時_WPF表示とGetAlbumByIdAsyncの収録曲が一致し片方削除時も他方が維持されること()
    {
        // Arrange
        string musicDir = Path.Combine(_tempDirectory.FullName, "music_same_album_title");
        string dataDir = Path.Combine(_tempDirectory.FullName, "data_same_album_title");
        Directory.CreateDirectory(musicDir);
        Directory.CreateDirectory(dataDir);
        string songA = Path.Combine(musicDir, "artist_a.mp3");
        string songB = Path.Combine(musicDir, "artist_b.mp3");
        await File.WriteAllTextAsync(songA, "dummy audio a");
        await File.WriteAllTextAsync(songB, "dummy audio b");

        string tracksJsonPath = Path.Combine(dataDir, "tracks.json");
        using var realTrackRepo = new JsonTrackRepository(tracksJsonPath);
        _favoriteRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());
        var appService = new LibraryApplicationService(realTrackRepo, _favoriteRepoMock.Object, _metadataExtractor, _eventBus);

        using var sut = new LibraryViewModel(
            appService,
            _audioServiceMock.Object,
            _settingsServiceMock.Object,
            playlistViewModel: null,
            trackMetadataLoader: file =>
            {
                bool isArtistA = string.Equals(file, songA, StringComparison.OrdinalIgnoreCase);
                return new Track
                {
                    FilePath = file,
                    Title = isArtistA ? "Track A" : "Track B",
                    Artist = isArtistA ? "Artist A" : "Artist B",
                    Album = "Best Of",
                    TrackNumber = 1,
                };
            });

        // Act
        await sut.LoadLibraryAsync(musicDir);

        // Assert: WPF 側も 2 枚に分離され、各 Album.Id で GetAlbumByIdAsync を呼ぶと収録曲が一致する
        Assert.Equal(2, sut.Albums.Count);
        var wpfAlbumA = Assert.Single(sut.Albums, a => a.Artist == "Artist A");
        var wpfAlbumB = Assert.Single(sut.Albums, a => a.Artist == "Artist B");
        Assert.NotEqual(wpfAlbumA.Id, wpfAlbumB.Id);

        var serviceAlbumA = await appService.GetAlbumByIdAsync(wpfAlbumA.Id);
        var serviceAlbumB = await appService.GetAlbumByIdAsync(wpfAlbumB.Id);
        Assert.NotNull(serviceAlbumA);
        Assert.NotNull(serviceAlbumB);
        Assert.Equal(wpfAlbumA.Tracks.Select(t => t.Id), serviceAlbumA.Tracks.Select(t => t.Id));
        Assert.Equal(wpfAlbumB.Tracks.Select(t => t.Id), serviceAlbumB.Tracks.Select(t => t.Id));

        // Act 2: Artist A の曲を削除して再スキャンした場合、Artist B の同名アルバムは維持される
        File.Delete(songA);
        await sut.LoadLibraryAsync(musicDir);

        // Assert 2
        var remainingAlbum = Assert.Single(sut.Albums);
        Assert.Equal(wpfAlbumB.Id, remainingAlbum.Id);
        Assert.Equal("Artist B", remainingAlbum.Artist);
        Assert.Null(await appService.GetAlbumByIdAsync(wpfAlbumA.Id));
        var remainingServiceAlbum = await appService.GetAlbumByIdAsync(wpfAlbumB.Id);
        Assert.NotNull(remainingServiceAlbum);
        Assert.Equal(remainingAlbum.Tracks.Select(t => t.Id), remainingServiceAlbum.Tracks.Select(t => t.Id));
    }

    /// <summary>
    /// DetectMetadataChangesOnRescan有効時に1曲アルバムのアーティスト名のみをAからBへ変更して再スキャンした場合、
    /// 旧アルバムに対するAlbumRemovedは発火しつつも、同一TrackIdでライブラリに残存する曲は表示中プレイリストおよび再生トラック一覧から削除されないことを検証します。
    /// </summary>
    [Fact]
    public async Task LoadLibraryAsync_1曲アルバムのアーティスト名変更再スキャン時_旧アルバムは除去されつつ残存曲は表示から削除されないこと()
    {
        // Arrange
        string musicDir = Path.Combine(_tempDirectory.FullName, "music_artist_rename");
        string dataDir = Path.Combine(_tempDirectory.FullName, "data_artist_rename");
        Directory.CreateDirectory(musicDir);
        Directory.CreateDirectory(dataDir);
        string songPath = Path.Combine(musicDir, "solo.mp3");
        await File.WriteAllTextAsync(songPath, "v1");
        File.SetLastWriteTimeUtc(songPath, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        _settingsServiceMock.Setup(s => s.LoadSettings()).Returns(new AppSettings
        {
            DetectMetadataChangesOnRescan = true,
        });

        string tracksJsonPath = Path.Combine(dataDir, "tracks.json");
        using var realTrackRepo = new JsonTrackRepository(tracksJsonPath);
        _favoriteRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());
        var appService = new LibraryApplicationService(realTrackRepo, _favoriteRepoMock.Object, _metadataExtractor, _eventBus);
        var playlistService = new PlaylistApplicationService(new Mock<IPlaylistRepository>().Object, realTrackRepo, _eventBus);
        using var playlistVm = new PlaylistViewModel(playlistService, appService, _audioServiceMock.Object, _eventBus);
        var playbackListTracks = new ObservableCollection<Track>();

        string currentArtist = "Artist A";
        using var sut = new LibraryViewModel(
            appService,
            _audioServiceMock.Object,
            _settingsServiceMock.Object,
            playlistViewModel: playlistVm,
            trackMetadataLoader: file => new Track
            {
                FilePath = file,
                Title = "Solo Track",
                Artist = currentArtist,
                Album = "Solo Album",
                TrackNumber = 1,
            });

        var removedTracks = new List<Track>();
        var removedAlbums = new List<Album>();
        sut.TrackRemoved += track =>
        {
            removedTracks.Add(track);
            playlistVm.RemoveDisplayedTrack(track);
            playbackListTracks.Remove(track);
        };
        sut.AlbumRemoved += album =>
        {
            removedAlbums.Add(album);
            MainViewModel.RemoveTracksNotInLibrary(album, sut.Tracks, playlistVm, playbackListTracks);
        };

        await sut.LoadLibraryAsync(musicDir);
        Track initialTrack = Assert.Single(sut.Tracks);
        TrackId originalTrackId = initialTrack.Id;
        AlbumId originalAlbumId = Assert.Single(sut.Albums).Id;
        playlistVm.PlaylistTracks.Add(initialTrack);
        playbackListTracks.Add(initialTrack);

        // Act: アーティスト名を A → B へ変更し更新日時を進めて再スキャン
        currentArtist = "Artist B";
        File.SetLastWriteTimeUtc(songPath, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        await sut.LoadLibraryAsync(musicDir);

        // Assert: アーティスト変更で旧アルバムのIDが引き継がれ、表示中プレイリストおよび再生一覧に同じ TrackId の曲が維持される
        Assert.Empty(removedTracks);

        Track remainingLibraryTrack = Assert.Single(sut.Tracks);
        Assert.Equal(originalTrackId, remainingLibraryTrack.Id);
        Assert.Equal("Artist B", remainingLibraryTrack.Artist);

        Album updatedAlbum = Assert.Single(sut.Albums);
        Assert.Equal("Artist B", updatedAlbum.Artist);
        Assert.Equal(originalAlbumId, updatedAlbum.Id);
        Assert.Equal(remainingLibraryTrack.AlbumId, updatedAlbum.Id);

        Track displayedPlaylistTrack = Assert.Single(playlistVm.PlaylistTracks);
        Assert.Equal(originalTrackId, displayedPlaylistTrack.Id);
        Track displayedPlaybackTrack = Assert.Single(playbackListTracks);
        Assert.Equal(originalTrackId, displayedPlaybackTrack.Id);

        // Act 2: 実ファイル削除による再スキャン時は表示中プレイリスト・再生一覧から正しく除去される
        File.Delete(songPath);
        await sut.LoadLibraryAsync(musicDir);

        // Assert 2
        Assert.Single(removedTracks);
        Assert.Empty(sut.Tracks);
        Assert.Empty(sut.Albums);
        Assert.Empty(playlistVm.PlaylistTracks);
        Assert.Empty(playbackListTracks);
    }

    /// <summary>
    /// 共通作品に属する異なる演奏者の2曲（AlbumArtist共通およびAlbumArtist未設定コンピレーション）が一覧で1枚にまとまり、
    /// そのAlbumIdで両曲を取得・再生できる一方、別作品の同名アルバムは別カードに残ることを検証します。
    /// </summary>
    [Fact]
    public async Task LoadLibraryAsync_コンピレーションおよび客演曲と別作品同名アルバム混在時_共通作品は1枚で両曲取得再生でき別作品は別カードに残ること()
    {
        // Arrange
        string rootDir = Path.Combine(_tempDirectory.FullName, "music_compilation_and_distinct");
        string blueNoteDir = Path.Combine(rootDir, "BlueNote");
        string compNoTagDir = Path.Combine(rootDir, "CompilationNoTag");
        string otherArtistDir = Path.Combine(rootDir, "OtherArtist");
        string dataDir = Path.Combine(_tempDirectory.FullName, "data_compilation_and_distinct");
        Directory.CreateDirectory(blueNoteDir);
        Directory.CreateDirectory(compNoTagDir);
        Directory.CreateDirectory(otherArtistDir);
        Directory.CreateDirectory(dataDir);

        string bn1 = Path.Combine(blueNoteDir, "01_clifford.flac");
        string bn2 = Path.Combine(blueNoteDir, "02_herbie.flac");
        string noTag1 = Path.Combine(compNoTagDir, "01_a.flac");
        string noTag2 = Path.Combine(compNoTagDir, "02_b.flac");
        string distinctSameName = Path.Combine(otherArtistDir, "01_other.flac");
        await File.WriteAllTextAsync(bn1, "bn1");
        await File.WriteAllTextAsync(bn2, "bn2");
        await File.WriteAllTextAsync(noTag1, "nt1");
        await File.WriteAllTextAsync(noTag2, "nt2");
        await File.WriteAllTextAsync(distinctSameName, "other");

        string tracksJsonPath = Path.Combine(dataDir, "tracks.json");
        using var realTrackRepo = new JsonTrackRepository(tracksJsonPath);
        _favoriteRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());
        var appService = new LibraryApplicationService(realTrackRepo, _favoriteRepoMock.Object, _metadataExtractor, _eventBus);

        List<Track>? queuedTracks = null;
        _audioServiceMock
            .Setup(a => a.SetPlaylist(It.IsAny<List<Track>>(), It.IsAny<Track?>()))
            .Callback<List<Track>, Track?>((tracks, _) => queuedTracks = tracks);

        using var sut = new LibraryViewModel(
            appService,
            _audioServiceMock.Object,
            _settingsServiceMock.Object,
            playlistViewModel: null,
            trackMetadataLoader: file =>
            {
                if (string.Equals(file, bn1, StringComparison.OrdinalIgnoreCase))
                {
                    return new Track { FilePath = file, Title = "Moanin'", Artist = "Clifford Brown", AlbumArtist = "Blue Note", Album = "Blue Note X Blue Giant", TrackNumber = 1 };
                }

                if (string.Equals(file, bn2, StringComparison.OrdinalIgnoreCase))
                {
                    return new Track { FilePath = file, Title = "Maiden Voyage", Artist = "Herbie Hancock", AlbumArtist = "Blue Note", Album = "Blue Note X Blue Giant", TrackNumber = 2 };
                }

                if (string.Equals(file, noTag1, StringComparison.OrdinalIgnoreCase))
                {
                    return new Track { FilePath = file, Title = "Autumn Leaves", Artist = "Bill Evans", Album = "Jazz Essentials", TrackNumber = 1 };
                }

                if (string.Equals(file, noTag2, StringComparison.OrdinalIgnoreCase))
                {
                    return new Track { FilePath = file, Title = "So What", Artist = "Miles Davis", Album = "Jazz Essentials", TrackNumber = 2 };
                }

                return new Track { FilePath = file, Title = "Different Work", Artist = "Solo Artist", Album = "Blue Note X Blue Giant", TrackNumber = 1 };
            });

        // Act
        await sut.LoadLibraryAsync(rootDir);

        // Assert: 3 枚（Blue Note 共通作品 / AlbumArtist未設定コンピレーション / 別作品の同名アルバム）
        Assert.Equal(3, sut.Albums.Count);

        var blueNoteAlbum = Assert.Single(sut.Albums, a => a.Title == "Blue Note X Blue Giant" && a.Artist == "Blue Note");
        Assert.Equal(2, blueNoteAlbum.Tracks.Count);
        Assert.Equal(["Clifford Brown", "Herbie Hancock"], blueNoteAlbum.Tracks.Select(t => t.Artist).ToArray());

        var noTagCompAlbum = Assert.Single(sut.Albums, a => a.Title == "Jazz Essentials");
        Assert.Equal("Various Artists", noTagCompAlbum.Artist);
        Assert.Equal(2, noTagCompAlbum.Tracks.Count);
        Assert.Equal(["Bill Evans", "Miles Davis"], noTagCompAlbum.Tracks.Select(t => t.Artist).ToArray());

        var distinctAlbum = Assert.Single(sut.Albums, a => a.Title == "Blue Note X Blue Giant" && a.Artist == "Solo Artist");
        Assert.Single(distinctAlbum.Tracks);
        Assert.NotEqual(blueNoteAlbum.Id, distinctAlbum.Id);

        // Assert: GetAlbumByIdAsync でも共通作品の ID で両曲を取得できる
        var fetchedBlueNote = await appService.GetAlbumByIdAsync(blueNoteAlbum.Id);
        Assert.NotNull(fetchedBlueNote);
        Assert.Equal(2, fetchedBlueNote.Tracks.Count);
        Assert.Equal(blueNoteAlbum.Tracks.Select(t => t.Id), fetchedBlueNote.Tracks.Select(t => t.Id));

        var fetchedNoTagComp = await appService.GetAlbumByIdAsync(noTagCompAlbum.Id);
        Assert.NotNull(fetchedNoTagComp);
        Assert.Equal(2, fetchedNoTagComp.Tracks.Count);
        Assert.Equal(noTagCompAlbum.Tracks.Select(t => t.Id), fetchedNoTagComp.Tracks.Select(t => t.Id));

        // Assert: PlayAlbumCommand で両曲が再生キューへ渡される
        sut.PlayAlbumCommand.Execute(blueNoteAlbum);
        Assert.NotNull(queuedTracks);
        Assert.Equal(2, queuedTracks.Count);
        Assert.Equal(blueNoteAlbum.Tracks.Select(t => t.Id), queuedTracks.Select(t => t.Id));
    }

    /// <summary>
    /// 1曲を登録・永続化した後、新しいLibraryViewModelインスタンスを作り直して同一フォルダーを再読み込みした場合、
    /// アルバムカードのId・収録トラックのAlbumId・GetAlbumByIdAsyncの結果がいずれも初回保存済みのAlbumIdと一致することを検証します。
    /// </summary>
    [Fact]
    public async Task LoadLibraryAsync_再起動後に新しいLibraryViewModelで再読込_アルバムカードのIdと保存済みAlbumIdおよびGetAlbumByIdAsyncが完全一致すること()
    {
        // Arrange
        string musicDir = Path.Combine(_tempDirectory.FullName, "music_restart_album_id");
        string dataDir = Path.Combine(_tempDirectory.FullName, "data_restart_album_id");
        Directory.CreateDirectory(musicDir);
        Directory.CreateDirectory(dataDir);
        string songPath = Path.Combine(musicDir, "track1.flac");
        await File.WriteAllTextAsync(songPath, "dummy audio");

        string tracksJsonPath = Path.Combine(dataDir, "tracks.json");
        _favoriteRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());

        AlbumId initialAlbumId;
        using (var initialRepo = new JsonTrackRepository(tracksJsonPath))
        {
            var initialService = new LibraryApplicationService(initialRepo, _favoriteRepoMock.Object, _metadataExtractor, _eventBus);
            using var initialVm = new LibraryViewModel(
                initialService,
                _audioServiceMock.Object,
                _settingsServiceMock.Object,
                playlistViewModel: null,
                trackMetadataLoader: file => new Track
                {
                    FilePath = file,
                    Title = "Restart Track",
                    Artist = "Restart Artist",
                    Album = "Restart Album",
                    TrackNumber = 1,
                });

            await initialVm.LoadLibraryAsync(musicDir);
            initialAlbumId = Assert.Single(initialVm.Albums).Id;
        }

        // Act: アプリ再起動相当（新しい JsonTrackRepository と新しい LibraryViewModel で同じフォルダーを読み込む）
        using var restartedRepo = new JsonTrackRepository(tracksJsonPath);
        var restartedService = new LibraryApplicationService(restartedRepo, _favoriteRepoMock.Object, _metadataExtractor, _eventBus);
        using var restartedVm = new LibraryViewModel(
            restartedService,
            _audioServiceMock.Object,
            _settingsServiceMock.Object,
            playlistViewModel: null,
            trackMetadataLoader: file => new Track
            {
                FilePath = file,
                Title = "Restart Track",
                Artist = "Restart Artist",
                Album = "Restart Album",
                TrackNumber = 1,
            });

        await restartedVm.LoadLibraryAsync(musicDir);

        // Assert
        Album restartedAlbum = Assert.Single(restartedVm.Albums);
        Track restartedTrack = Assert.Single(restartedAlbum.Tracks);
        Assert.Equal(initialAlbumId, restartedAlbum.Id);
        Assert.Equal(restartedAlbum.Id, restartedTrack.AlbumId);

        Album? fetched = await restartedService.GetAlbumByIdAsync(restartedAlbum.Id);
        Assert.NotNull(fetched);
        Assert.Equal(restartedAlbum.Id, fetched.Id);
        Assert.Equal(restartedTrack.Id, Assert.Single(fetched.Tracks).Id);
    }
}
