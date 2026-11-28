using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Library;
using AudioEffector.Presentation.Converters;
using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using AudioEffector.Tests.Presentation.Diagnostics;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// ArtistsViewModelのアーティスト集約・検索・ソート・再生コマンド・破棄およびViewバインディングを検証するテストクラス
/// </summary>
public sealed class ArtistsViewModelTests
{
    private static Track CreateTrack(string title, string artist, string album, uint year = 2020)
    {
        return new Track
        {
            FilePath = $@"C:\music\{artist}\{album}\{title}.flac",
            Title = title,
            Artist = artist,
            Album = album,
            Year = year,
            Duration = TimeSpan.FromMinutes(3),
        };
    }

    private static (ArtistsViewModel Sut, LibraryViewModel Library, StubAudioService Audio) CreateSut(TimeSpan? searchDebounceDelay = null)
    {
        var libraryService = new LibraryApplicationService(
            new InMemoryTrackRepository(),
            new InMemoryFavoriteRepository(),
            new TagLibMetadataExtractor(),
            new InMemoryEventBus());
        var audio = new StubAudioService();
        var settings = new StubSettingsService();
        var library = new LibraryViewModel(libraryService, audio, settings);
        var sut = searchDebounceDelay.HasValue
            ? new ArtistsViewModel(library, audio, searchDebounceDelay.Value)
            : new ArtistsViewModel(library, audio, TimeSpan.Zero);
        return (sut, library, audio);
    }

    /// <summary>
    /// 複数楽曲をライブラリに追加した際、アーティスト単位でアルバムと楽曲が正しく集約されることを検証します
    /// </summary>
    [Fact]
    public void RefreshArtists_複数アーティストの楽曲追加時_アーティスト別に集約される()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;

        // Act
        library.Tracks.Add(CreateTrack("Get Lucky", "Daft Punk", "RAM", 2013));
        library.Tracks.Add(CreateTrack("One More Time", "Daft Punk", "Discovery", 2001));
        library.Tracks.Add(CreateTrack("Midnight City", "M83", "Hurry Up", 2011));

        // Assert
        Assert.Equal(2, sut.FilteredArtists.Count);
        var daftPunk = sut.FilteredArtists.Single(a => a.Name == "Daft Punk");
        Assert.Equal(2, daftPunk.AlbumCount);
        Assert.Equal(2, daftPunk.TrackCount);
        Assert.NotNull(sut.SelectedArtist);
        Assert.False(sut.IsEmpty);
        Assert.Equal("全 2 アーティスト", sut.SummaryText);
    }

    /// <summary>
    /// アーティスト名が空または空白の楽曲はUnknown Artistへ集約され、大文字小文字が異なる同一アーティスト名は1件に統合されることを検証します
    /// </summary>
    [Fact]
    public void RefreshArtists_空アーティスト名と大文字小文字揺れがある場合_正しく正規化統合される()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;

        // Act
        library.Tracks.Add(CreateTrack("No Artist 1", string.Empty, "Misc"));
        library.Tracks.Add(CreateTrack("No Artist 2", "   ", "Misc"));
        library.Tracks.Add(CreateTrack("Track A", "Daft Punk", "RAM"));
        library.Tracks.Add(CreateTrack("Track B", "daft punk", "Discovery"));

        // Assert
        Assert.Equal(2, sut.FilteredArtists.Count);
        var unknown = sut.FilteredArtists.Single(a => a.Name == "Unknown Artist");
        Assert.Equal(2, unknown.TrackCount);
        var daftPunk = sut.FilteredArtists.Single(a => string.Equals(a.Name, "Daft Punk", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, daftPunk.TrackCount);
        Assert.Equal(2, daftPunk.AlbumCount);
    }

    /// <summary>
    /// 検索キーワードを入力した際、アーティスト名・アルバム名・曲名のいずれかに一致するアーティストのみに絞り込まれることを検証します
    /// </summary>
    [Theory]
    [InlineData("m83", "M83")]
    [InlineData("discovery", "Daft Punk")]
    [InlineData("midnight", "M83")]
    public void SearchKeyword_アーティスト名アルバム名曲名で検索時_該当アーティストのみに絞り込まれる(string keyword, string expectedArtist)
    {
        // Arrange
        var (sut, library, _) = CreateSut(TimeSpan.Zero);
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("One More Time", "Daft Punk", "Discovery"));
        library.Tracks.Add(CreateTrack("Midnight City", "M83", "Hurry Up"));

        // Act
        sut.SearchKeyword = keyword;

        // Assert
        Assert.Single(sut.FilteredArtists);
        Assert.Equal(expectedArtist, sut.FilteredArtists[0].Name);
    }

    /// <summary>
    /// デバウンス有効時に連続して検索キーワードを入力した際、待機後に最新キーワードの結果のみ反映されることを検証します
    /// </summary>
    [Fact]
    public async Task SearchKeyword_デバウンス有効時の連続入力_待機後に最新キーワードの結果のみ反映される()
    {
        // Arrange
        var (sut, library, _) = CreateSut(TimeSpan.FromMilliseconds(80));
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("Get Lucky", "Daft Punk", "RAM"));
        library.Tracks.Add(CreateTrack("Midnight City", "M83", "Hurry Up"));

        // Act
        sut.SearchKeyword = "m";
        sut.SearchKeyword = "m8";
        sut.SearchKeyword = "m83";

        // デバウンス待機直後はまだ全件表示
        Assert.Equal(2, sut.FilteredArtists.Count);

        await Task.Delay(180);

        // Assert
        Assert.Single(sut.FilteredArtists);
        Assert.Equal("M83", sut.FilteredArtists[0].Name);
    }

    /// <summary>
    /// ClearSearchCommandを実行した際、デバウンス待機なしで即座に全件表示へ戻ることを検証します
    /// </summary>
    [Fact]
    public async Task ClearSearchCommand_実行時_デバウンス待機せず即座に全件表示へ戻る()
    {
        // Arrange
        var (sut, library, _) = CreateSut(TimeSpan.FromMilliseconds(60));
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("Get Lucky", "Daft Punk", "RAM"));
        library.Tracks.Add(CreateTrack("Midnight City", "M83", "Hurry Up"));
        sut.SearchKeyword = "m83";
        await Task.Delay(150);
        Assert.Single(sut.FilteredArtists);

        // Act
        sut.ClearSearchCommand.Execute(null);

        // Assert
        Assert.Equal(string.Empty, sut.SearchKeyword);
        Assert.Equal(2, sut.FilteredArtists.Count);
    }

    /// <summary>
    /// ソート選択肢に内部キーと日本語表示ラベルのペアが提供されることを検証します
    /// </summary>
    [Fact]
    public void SortOptions_初期化時_日本語表示ラベルを持つ選択肢が提供される()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;

        // Act
        var options = sut.SortOptions;

        // Assert
        Assert.Equal(3, options.Count);
        Assert.Contains(options, o => o.Key == "Name" && o.DisplayName == "名前順");
        Assert.Contains(options, o => o.Key == "TrackCount" && o.DisplayName == "曲数順");
        Assert.Contains(options, o => o.Key == "AlbumCount" && o.DisplayName == "アルバム数順");
    }

    /// <summary>
    /// 曲数降順でソートした際、所属楽曲数が多いアーティスト順に並ぶことを検証します
    /// </summary>
    [Fact]
    public void SelectedSortOption_曲数降順指定時_曲数が多い順に並ぶ()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("S1", "Artist A", "A1"));
        library.Tracks.Add(CreateTrack("S2", "Artist B", "B1"));
        library.Tracks.Add(CreateTrack("S3", "Artist B", "B1"));

        // Act
        sut.SelectedSortOption = "TrackCount";
        sut.IsAscending = false;

        // Assert
        Assert.Equal("曲数順", sut.SelectedSortOptionItem.DisplayName);
        Assert.Equal(new[] { "Artist B", "Artist A" }, sut.FilteredArtists.Select(a => a.Name).ToArray());
    }

    /// <summary>
    /// SelectedSortOptionItemでアルバム数順を選択しToggleSortDirectionCommandで降順へ切り替えた際、アルバム数が多い順に並ぶことを検証します
    /// </summary>
    [Fact]
    public void SelectedSortOptionItem_アルバム数順と昇降順切替時_アルバム数順に並び替わる()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("T1", "One Album Artist", "Alb1"));
        library.Tracks.Add(CreateTrack("T2", "Three Album Artist", "Alb1"));
        library.Tracks.Add(CreateTrack("T3", "Three Album Artist", "Alb2"));
        library.Tracks.Add(CreateTrack("T4", "Three Album Artist", "Alb3"));
        library.Tracks.Add(CreateTrack("T5", "Two Album Artist", "Alb1"));
        library.Tracks.Add(CreateTrack("T6", "Two Album Artist", "Alb2"));

        // Act
        sut.SelectedSortOptionItem = sut.SortOptions.Single(o => o.Key == "AlbumCount");
        sut.ToggleSortDirectionCommand.Execute(null);

        // Assert
        Assert.Equal("AlbumCount", sut.SelectedSortOption);
        Assert.False(sut.IsAscending);
        Assert.Equal(
            new[] { "Three Album Artist", "Two Album Artist", "One Album Artist" },
            sut.FilteredArtists.Select(a => a.Name).ToArray());
    }

    /// <summary>
    /// SelectedArtistを別アーティストへ切り替えた際、前アーティストのSelectedTrack参照がnullにリセットされることを検証します
    /// </summary>
    [Fact]
    public void SelectedArtist_別アーティストへ切替時_SelectedTrackがnullにリセットされる()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        var trackA = CreateTrack("Song A", "Artist A", "Album A");
        var trackB = CreateTrack("Song B", "Artist B", "Album B");
        library.Tracks.Add(trackA);
        library.Tracks.Add(trackB);
        var artistA = sut.FilteredArtists.Single(a => a.Name == "Artist A");
        var artistB = sut.FilteredArtists.Single(a => a.Name == "Artist B");
        sut.SelectedArtist = artistA;
        sut.SelectedTrack = trackA;

        // Act
        sut.SelectedArtist = artistB;

        // Assert
        Assert.Null(sut.SelectedTrack);
    }

    /// <summary>
    /// SelectedArtistがnullや楽曲0件のときはアーティスト再生系コマンドが実行不可となり、楽曲ありのアーティストでは実行可能となることを検証します
    /// </summary>
    [Fact]
    public void CanExecute_アーティスト選択状態と楽曲数に応じて_再生系コマンドの実行可否が切り替わる()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        var emptyArtist = new ArtistGroup("Empty Artist", Array.Empty<Album>(), Array.Empty<Track>());

        // Act & Assert (未選択・空アーティスト)
        Assert.False(sut.PlayArtistCommand.CanExecute(null));
        Assert.False(sut.ShuffleArtistCommand.CanExecute(null));
        Assert.False(sut.PlayNextArtistCommand.CanExecute(null));
        Assert.False(sut.EnqueueArtistCommand.CanExecute(null));
        Assert.False(sut.PlayArtistCommand.CanExecute(emptyArtist));

        // Act (楽曲ありアーティスト追加)
        library.Tracks.Add(CreateTrack("T1", "Daft Punk", "RAM"));

        // Assert
        Assert.True(sut.PlayArtistCommand.CanExecute(null));
        Assert.True(sut.ShuffleArtistCommand.CanExecute(null));
        Assert.True(sut.PlayNextArtistCommand.CanExecute(null));
        Assert.True(sut.EnqueueArtistCommand.CanExecute(null));
    }

    /// <summary>
    /// PlayArtistを実行した際、該当アーティストの全楽曲がプレイリストに設定され再生開始されることを検証します
    /// </summary>
    [Fact]
    public void PlayArtist_アーティスト指定時_全曲がプレイリストにセットされ再生される()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("T1", "Daft Punk", "RAM"));
        library.Tracks.Add(CreateTrack("T2", "Daft Punk", "Discovery"));
        var target = sut.FilteredArtists.Single();
        IReadOnlyList<Track>? requestedTracks = null;
        sut.PlaybackRequested += (tracks, _, _, _) => requestedTracks = tracks;

        // Act
        sut.PlayArtist(target);

        // Assert
        Assert.NotNull(requestedTracks);
        Assert.Equal(2, requestedTracks!.Count);
        Assert.Equal(1, audio.SetPlaylistCallCount);
        Assert.Equal(2, audio.LastPlaylist.Count);
        Assert.True(audio.PlayCalled);
    }

    /// <summary>
    /// ShuffleArtistを実行した際、該当アーティストの全楽曲が含まれたプレイリストで再生開始されることを検証します
    /// </summary>
    [Fact]
    public void ShuffleArtist_アーティスト指定時_全曲を含めてプレイリスト設定し再生開始する()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        var t1 = CreateTrack("T1", "Daft Punk", "RAM");
        var t2 = CreateTrack("T2", "Daft Punk", "Discovery");
        var t3 = CreateTrack("T3", "Daft Punk", "Homework");
        library.Tracks.Add(t1);
        library.Tracks.Add(t2);
        library.Tracks.Add(t3);
        var target = sut.FilteredArtists.Single();
        string? requestedTitle = null;
        IReadOnlyList<Track>? requestedTracks = null;
        sut.PlaybackRequested += (tracks, _, title, _) =>
        {
            requestedTracks = tracks;
            requestedTitle = title;
        };

        // Act
        sut.ShuffleArtist(target);

        // Assert
        Assert.Equal("Daft Punk（シャッフル）", requestedTitle);
        Assert.NotNull(requestedTracks);
        Assert.Equal(3, requestedTracks!.Count);
        Assert.Equal(new[] { t1, t2, t3 }.OrderBy(t => t.Title), requestedTracks.OrderBy(t => t.Title));
        Assert.Equal(1, audio.SetPlaylistCallCount);
        Assert.True(audio.PlayCalled);
    }

    /// <summary>
    /// PlayAlbumを実行した際、指定アルバムの収録曲のみがプレイリストに設定され再生開始されることを検証します
    /// </summary>
    [Fact]
    public void PlayAlbum_アルバム指定時_アルバム収録曲がプレイリストに設定され再生される()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("Get Lucky", "Daft Punk", "RAM", 2013));
        library.Tracks.Add(CreateTrack("One More Time", "Daft Punk", "Discovery", 2001));
        var discovery = sut.FilteredArtists.Single().Albums.Single(a => a.Title == "Discovery");
        string? requestedName = null;
        sut.PlaybackRequested += (_, _, name, _) => requestedName = name;

        // Act
        sut.PlayAlbum(discovery);

        // Assert
        Assert.Equal("Discovery", requestedName);
        Assert.Single(audio.LastPlaylist);
        Assert.Equal("One More Time", audio.LastStartTrack?.Title);
        Assert.True(audio.PlayCalled);
    }

    /// <summary>
    /// PlayTrackを実行した際、選択アーティストの全曲コンテキストで対象楽曲が開始曲として再生されることを検証します
    /// </summary>
    [Fact]
    public void PlayTrack_楽曲指定時_アーティスト全曲コンテキストで対象楽曲が再生される()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        var t1 = CreateTrack("Get Lucky", "Daft Punk", "RAM");
        var t2 = CreateTrack("One More Time", "Daft Punk", "Discovery");
        library.Tracks.Add(t1);
        library.Tracks.Add(t2);

        // Act
        sut.PlayTrack(t2);

        // Assert
        Assert.Equal(2, audio.LastPlaylist.Count);
        Assert.Equal(t2, audio.LastStartTrack);
        Assert.True(audio.PlayCalled);
    }

    /// <summary>
    /// EnqueueRequestedが購読されている場合、PlayNextArtistCommandとEnqueueArtistCommandが正しいplayNextフラグでイベント発火することを検証します
    /// </summary>
    [Fact]
    public void EnqueueArtistCommands_EnqueueRequested購読ありの場合_イベント経由で通知される()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("T1", "Daft Punk", "RAM"));
        library.Tracks.Add(CreateTrack("T2", "Daft Punk", "Discovery"));
        var target = sut.FilteredArtists.Single();
        var events = new List<(IReadOnlyList<Track> Tracks, bool PlayNext)>();
        sut.EnqueueRequested += (tracks, playNext) => events.Add((tracks, playNext));

        // Act
        sut.PlayNextArtistCommand.Execute(target);
        sut.EnqueueArtistCommand.Execute(target);

        // Assert
        Assert.Equal(2, events.Count);
        Assert.True(events[0].PlayNext);
        Assert.Equal(2, events[0].Tracks.Count);
        Assert.False(events[1].PlayNext);
        Assert.Equal(2, events[1].Tracks.Count);
        Assert.Empty(audio.EnqueuedCalls);
    }

    /// <summary>
    /// EnqueueRequestedが未購読の場合、PlayNextArtistCommandとEnqueueArtistCommandがAudioServiceへ直接委譲されることを検証します
    /// </summary>
    [Fact]
    public void EnqueueArtistCommands_EnqueueRequested購読なしの場合_AudioServiceへ直接委譲される()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("T1", "Daft Punk", "RAM"));
        var target = sut.FilteredArtists.Single();

        // Act
        sut.PlayNextArtistCommand.Execute(target);
        sut.EnqueueArtistCommand.Execute(target);

        // Assert
        Assert.Equal(2, audio.EnqueuedCalls.Count);
        Assert.True(audio.EnqueuedCalls[0].PlayNext);
        Assert.False(audio.EnqueuedCalls[1].PlayNext);
    }

    /// <summary>
    /// Tracksが存在する場合はAlbumsのCollectionChangedでは再構築が発火せず、Tracksが空のフォールバック時のみAlbums変更が反映されることを検証します
    /// </summary>
    [Fact]
    public void AlbumsCollectionChanged_Tracksが空の場合のみフォールバック反映される()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        var fallbackTrack = CreateTrack("Fallback Song", "Fallback Artist", "Fallback Album");

        // Act (Tracksが空の状態でAlbumsへ追加)
        library.Albums.Add(new Album("Fallback Album", "Fallback Artist", 2022, new[] { fallbackTrack }));

        // Assert
        Assert.Single(sut.FilteredArtists);
        Assert.Equal("Fallback Artist", sut.FilteredArtists[0].Name);

        // Act (Tracksへ楽曲を追加した後にAlbumsをクリアしてもTracksを正として維持される)
        library.Tracks.Add(CreateTrack("Primary Song", "Primary Artist", "Primary Album"));
        int summaryChangeCount = 0;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ArtistsViewModel.IsEmpty))
            {
                summaryChangeCount++;
            }
        };
        library.Albums.Clear();

        // Assert
        Assert.Equal(0, summaryChangeCount);
        Assert.Single(sut.FilteredArtists);
        Assert.Equal("Primary Artist", sut.FilteredArtists[0].Name);
    }

    /// <summary>
    /// Dispose後にライブラリへ楽曲が追加されても、FilteredArtistsが更新されないことを検証します
    /// </summary>
    [Fact]
    public void Dispose_破棄後にライブラリへ楽曲追加した場合_FilteredArtistsが更新されない()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        library.Tracks.Add(CreateTrack("Before Dispose", "Artist A", "Album A"));
        Assert.Single(sut.FilteredArtists);

        // Act
        sut.Dispose();
        library.Tracks.Add(CreateTrack("After Dispose", "Artist B", "Album B"));

        // Assert
        Assert.Single(sut.FilteredArtists);
        Assert.Equal("Artist A", sut.FilteredArtists[0].Name);
    }

    /// <summary>
    /// ArtistsViewの実体へArtistsViewModelをバインドしてソートComboBoxを切り替えた際、表示ラベルが更新されバインディングエラーも0件であることを検証します
    /// </summary>
    [Fact]
    public void ArtistsView_実体へバインドおよびソート変更した場合_バインディングエラーが0件でComboBox表示が更新されること()
    {
        RunInStaThread(() =>
        {
            // Arrange
            System.Threading.SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Threading.DispatcherSynchronizationContext(
                    System.Windows.Threading.Dispatcher.CurrentDispatcher));

            var (viewModel, library, _) = CreateSut(TimeSpan.Zero);
            using var _lib = library;
            using var _vm = viewModel;
            var t1 = CreateTrack("Get Lucky", "Daft Punk", "RAM", 2013);
            var t2 = CreateTrack("Midnight City", "M83", "Hurry Up", 2011);
            var t3 = CreateTrack("Wait", "M83", "Hurry Up", 2011);
            t1.FilePath = string.Empty;
            t2.FilePath = string.Empty;
            t3.FilePath = string.Empty;
            library.Tracks.Add(t1);
            library.Tracks.Add(t2);
            library.Tracks.Add(t3);

            using var listener = new BindingErrorTraceListener();
            EnsureApplicationResources();
            var view = new ArtistsView();
            AttachRequiredResources(view.Resources);
            view.DataContext = viewModel;
            view.Measure(new Size(900, 600));
            view.Arrange(new Rect(0, 0, 900, 600));
            view.UpdateLayout();

            var comboBox = Assert.IsType<System.Windows.Controls.ComboBox>(view.FindName("SortComboBox"));
            var presenter = Assert.IsType<System.Windows.Controls.ContentPresenter>(comboBox.Template.FindName("contentPresenter", comboBox));
            Assert.Equal("名前順", FindFirstTextBlockText(presenter));

            // Act
            comboBox.SelectedItem = viewModel.SortOptions.Single(o => o.Key == "TrackCount");
            view.UpdateLayout();

            // Assert
            Assert.Equal("TrackCount", viewModel.SelectedSortOption);
            var selectedBox = Assert.IsType<ArtistsSortOption>(comboBox.SelectionBoxItem);
            Assert.Equal("曲数順", selectedBox.DisplayName);
            Assert.Equal("曲数順", FindFirstTextBlockText(presenter));
            Assert.False(listener.HasErrors, $"ArtistsView でバインディングエラーが検出されました: {string.Join("; ", listener.Errors)}");
            Assert.Equal(0, listener.ErrorCount);
        });
    }

    private static string? FindFirstTextBlockText(DependencyObject root)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.TextBlock textBlock)
            {
                return textBlock.Text;
            }

            var nested = FindFirstTextBlockText(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void EnsureApplicationResources()
    {
        var appResources = System.Windows.Application.Current?.Resources;
        if (appResources == null)
        {
            return;
        }

        if (!appResources.Contains("BoolToVis"))
        {
            appResources["BoolToVis"] = new BoolToVisConverter();
        }

        if (!appResources.Contains("IndexConverter"))
        {
            appResources["IndexConverter"] = new IndexConverter();
        }
    }

    private static void AttachRequiredResources(ResourceDictionary target)
    {
        target.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/DarkTheme.xaml")
        });
        target.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/ScrollBarResources.xaml")
        });
        target["BoolToVis"] = new BoolToVisConverter();
        target["IndexConverter"] = new IndexConverter();
        var appStyle = LoadNeonComboBoxStyle();
        target["NeonComboBoxStyle"] = appStyle ?? new Style(typeof(System.Windows.Controls.ComboBox));
    }

    private static Style? LoadNeonComboBoxStyle()
    {
        var current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var appXamlPath = System.IO.Path.Combine(current, "AudioEffector", "App.xaml");
            if (System.IO.File.Exists(appXamlPath))
            {
                var doc = System.Xml.Linq.XDocument.Load(appXamlPath);
                var styleElement = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "Style" &&
                        e.Attributes().Any(a => a.Name.LocalName == "Key" && a.Value == "NeonComboBoxStyle"));
                if (styleElement != null)
                {
                    return (Style)System.Windows.Markup.XamlReader.Parse(styleElement.ToString());
                }

                break;
            }

            current = System.IO.Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    /// <summary>
    /// SelectArtistByName が検索フィルタ解除・大文字小文字無視での選択・未一致および Dispose 後の false 返却を行うことを検証します。
    /// </summary>
    [Fact]
    public void SelectArtistByName_検索絞り込み中や大文字小文字違いおよび未一致とDispose後_期待通り選択とfalse返却を行うこと()
    {
        // Arrange
        using var audio = new StubAudioService();
        var libService = new LibraryApplicationService(
            new InMemoryTrackRepository(),
            new InMemoryFavoriteRepository(),
            new AudioEffector.Infrastructure.Library.TagLibMetadataExtractor(),
            new InMemoryEventBus());
        using var libraryVm = new LibraryViewModel(libService, audio, new StubSettingsService());
        libraryVm.Tracks.Add(new Track { FilePath = @"C:\m\1.mp3", Title = "S1", Artist = "Aimer", Album = "Al1" });
        libraryVm.Tracks.Add(new Track { FilePath = @"C:\m\2.mp3", Title = "S2", Artist = "M83", Album = "Al2" });

        var sut = new ArtistsViewModel(libraryVm, audio, TimeSpan.Zero);
        sut.SearchKeyword = "M83";

        // Act
        bool matchedLower = sut.SelectArtistByName("aimer");
        string? selectedName = sut.SelectedArtist?.Name;
        string searchAfterSelect = sut.SearchKeyword;
        bool missing = sut.SelectArtistByName("NonExistent");
        sut.Dispose();
        bool afterDispose = sut.SelectArtistByName("Aimer");

        // Assert
        Assert.True(matchedLower);
        Assert.Equal("Aimer", selectedName);
        Assert.Equal(string.Empty, searchAfterSelect);
        Assert.False(missing);
        Assert.False(afterDispose);
    }

    private static void RunInStaThread(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    _ = new System.Windows.Application();
                }

                action();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            throw threadEx;
        }
    }

    private sealed class InMemoryTrackRepository : ITrackRepository
    {
        private readonly List<Track> _tracks = new();
        public Task<IReadOnlyList<Track>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Track>>(_tracks);
        public Task<Track?> GetByIdAsync(TrackId id, CancellationToken cancellationToken = default) => Task.FromResult(_tracks.FirstOrDefault(t => t.Id.Equals(id)));
        public Task<Track?> GetByPathAsync(AudioPath path, CancellationToken cancellationToken = default) => Task.FromResult(_tracks.FirstOrDefault(t => string.Equals(t.FilePath, path.Value, StringComparison.OrdinalIgnoreCase)));
        public Task SaveAsync(Track track, CancellationToken cancellationToken = default) { _tracks.Add(track); return Task.CompletedTask; }
        public Task SaveRangeAsync(IEnumerable<Track> tracks, CancellationToken cancellationToken = default) { _tracks.AddRange(tracks); return Task.CompletedTask; }
        public Task ReplaceAllAsync(IEnumerable<Track> tracks, CancellationToken cancellationToken = default) { _tracks.Clear(); _tracks.AddRange(tracks); return Task.CompletedTask; }
        public Task DeleteAsync(TrackId id, CancellationToken cancellationToken = default) { _tracks.RemoveAll(t => t.Id.Equals(id)); return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken = default) { _tracks.Clear(); return Task.CompletedTask; }
        public Task<IReadOnlyList<Track>> SearchAsync(string keyword, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Track>>(_tracks);
    }

    private sealed class InMemoryFavoriteRepository : IFavoriteRepository
    {
        private readonly List<FavoriteTrackEntry> _entries = new();
        public Task<IReadOnlyList<FavoriteTrackEntry>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FavoriteTrackEntry>>(_entries.ToList());
        public Task AddOrUpdateAsync(FavoriteTrackEntry entry, CancellationToken cancellationToken = default) { _entries.Add(entry); return Task.CompletedTask; }
        public Task RemoveAsync(Track track, CancellationToken cancellationToken = default) { _entries.RemoveAll(e => AudioEffector.Domain.Services.FavoriteTrackMatcher.IsMatchForRemoval(e, track)); return Task.CompletedTask; }
        public Task UpdateMetadataAsync(IReadOnlyList<FavoriteMetadataChange> changes, CancellationToken cancellationToken = default)
        {
            FavoriteMetadataChange.ApplyTo(_entries, changes);
            return Task.CompletedTask;
        }
        public Task SaveAllAsync(IReadOnlyList<FavoriteTrackEntry> entries, CancellationToken cancellationToken = default) { _entries.Clear(); _entries.AddRange(entries); return Task.CompletedTask; }
        public Task<bool> ContainsAsync(Track track, CancellationToken cancellationToken = default) => Task.FromResult(AudioEffector.Domain.Services.FavoriteTrackMatcher.FindBestMatchingEntry(track, _entries) != null);
    }

    private sealed class StubSettingsService : ISettingsService
    {
        private AppSettings _settings = new();
        public AppSettings LoadSettings() => _settings;
        public void UpdateSettings(System.Action<AppSettings> update) => update(_settings);
        public Task UpdateSettingsAsync(System.Action<AppSettings> update, CancellationToken cancellationToken = default)
        {
            update(_settings);
            return Task.CompletedTask;
        }
        public void SaveSettings(AppSettings settings) => _settings = settings;
        public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) { _settings = settings; return Task.CompletedTask; }
        public List<EqualizerPreset> LoadPresets() => new();
        public void SavePresets(List<EqualizerPreset> presets) { }
    }

    private sealed class StubAudioService : IAudioService
    {
        public List<Track> LastPlaylist { get; private set; } = new();
        public Track? LastStartTrack { get; private set; }
        public int SetPlaylistCallCount { get; private set; }
        public bool PlayCalled { get; private set; }
        public List<(IReadOnlyList<Track> Tracks, bool PlayNext)> EnqueuedCalls { get; } = new();

        public event Action<Track?>? TrackChanged { add { } remove { } }
        public event Action<bool>? PlaybackStateChanged { add { } remove { } }
        public event Action? PlaybackStopped { add { } remove { } }
        public event EventHandler? PlaylistEnded { add { } remove { } }
        public event Action<Track>? TrackPlaybackEnded { add { } remove { } }
        public event EventHandler<FftEventArgs>? FftCalculated { add { } remove { } }
        public event Action<List<Track>>? PlaylistChanged { add { } remove { } }
        public event Action<float>? VolumeChanged { add { } remove { } }
        public event Action<bool>? ShuffleChanged { add { } remove { } }
        public event Action<bool>? RepeatChanged { add { } remove { } }
        public event Action<string>? QueueSourceNameChanged { add { } remove { } }

        public IReadOnlyList<Track> UserQueue => Array.Empty<Track>();
        public IReadOnlyList<Track> AlbumQueue => LastPlaylist;
        public Track? CurrentTrack => LastStartTrack;
        public bool IsPlaying => PlayCalled;
        public bool IsPaused => false;
        public bool IsShuffleEnabled { get; set; }
        public bool IsRepeatEnabled { get; set; }
        public TimeSpan CurrentTime => TimeSpan.Zero;
        public TimeSpan TotalTime => TimeSpan.Zero;
        public float Volume { get; set; } = 0.5f;
        public float[] Frequencies { get; } = new float[10];

        public void ClearUserQueue() { }
        public void MoveUserQueueItem(int fromIndex, int toIndex) { }
        public void MoveAlbumQueueItem(int fromIndex, int toIndex) { }
        public void MoveAlbumQueueItemToUserQueue(int fromAlbumIndex, int toUserIndex) { }
        public void SetPlaylist(List<Track> tracks, Track? startTrack = null)
        {
            SetPlaylistCallCount++;
            LastPlaylist = tracks.ToList();
            LastStartTrack = startTrack ?? LastPlaylist.FirstOrDefault();
        }
        public void EnqueueTracks(IReadOnlyList<Track> tracks, bool playNext)
        {
            EnqueuedCalls.Add((tracks.ToList(), playNext));
        }
        public void RemoveTrack(Track track) { }
        public void PlayTrack(Track track) { LastStartTrack = track; PlayCalled = true; }
        public void PlayTrack(Track track, bool insertAtBeginning) { LastStartTrack = track; PlayCalled = true; }
        public Task<bool> PlayTrackAsync(Track track, CancellationToken cancellationToken = default) { PlayTrack(track); return Task.FromResult(true); }
        public Task<Track?> PlayPlaylistAsync(IReadOnlyList<Track> tracks, Track? startTrack = null, bool? shuffle = null, bool? repeat = null, string? sourceName = null, int? startTrackIndex = null, CancellationToken cancellationToken = default)
        {
            if (shuffle.HasValue) IsShuffleEnabled = shuffle.Value;
            if (repeat.HasValue) IsRepeatEnabled = repeat.Value;
            SetPlaylist(tracks.ToList(), startTrack);
            Track? started = startTrack ?? (tracks.Count > 0 ? tracks[0] : null);
            if (started != null) PlayTrack(started);
            return Task.FromResult(started);
        }
        public void TogglePlayPause() => PlayCalled = !PlayCalled;
        public bool Pause() { PlayCalled = false; return true; }
        public Task<bool> ResumeAsync(CancellationToken cancellationToken = default) { PlayCalled = true; return Task.FromResult(true); }
        public void Next() { }
        public Task<NextTrackResult> NextAsync(CancellationToken cancellationToken = default) => Task.FromResult(NextTrackResult.StoppedAtEnd);
        public void Previous() { }
        public Task<bool> PreviousAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public void Stop(bool internalStop = false) => PlayCalled = false;
        public void SeekTo(double percentage) { }
        public void PauseForSeek() { }
        public void ResumeAfterSeek() { }
        public void SetGain(int bandIndex, float gain) { }
        public void UpdateAudioProperties(int sampleRate, int bufferSizeMs) { }
        public void Dispose() { }
    }
}
