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
/// AllSongsViewModelのフィルタリング・ソート・サマリー計算・再生コマンド・破棄およびViewバインディングを検証するテストクラス
/// </summary>
public sealed class AllSongsViewModelTests
{
    private static Track CreateTrack(
        string title,
        string artist = "Artist A",
        string album = "Album A",
        TimeSpan? duration = null,
        string genre = "Pop",
        string format = "FLAC",
        uint trackNumber = 0)
    {
        return new Track
        {
            FilePath = $@"C:\music\{artist}\{album}\{title}.flac",
            Title = title,
            Artist = artist,
            Album = album,
            Duration = duration ?? TimeSpan.FromMinutes(3),
            Genre = genre,
            Format = format,
            TrackNumber = trackNumber,
        };
    }

    private static (AllSongsViewModel Sut, LibraryViewModel Library, StubAudioService Audio) CreateSut(TimeSpan? searchDebounceDelay = null)
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
            ? new AllSongsViewModel(library, audio, searchDebounceDelay.Value)
            : new AllSongsViewModel(library, audio, TimeSpan.Zero);
        return (sut, library, audio);
    }

    /// <summary>
    /// ライブラリに楽曲が追加された際、FilteredTracksに自動反映されサマリーが更新されることを検証します。
    /// </summary>
    [Fact]
    public void RefreshTracks_ライブラリへ楽曲追加時_FilteredTracksとサマリーが更新される()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        var track1 = CreateTrack("Beta", duration: TimeSpan.FromMinutes(4));
        var track2 = CreateTrack("Alpha", duration: TimeSpan.FromMinutes(2));

        // Act
        library.Tracks.Add(track1);
        library.Tracks.Add(track2);

        // Assert
        Assert.Equal(2, sut.FilteredTracks.Count);
        Assert.Equal("Alpha", sut.FilteredTracks[0].Title);
        Assert.Equal("Beta", sut.FilteredTracks[1].Title);
        Assert.False(sut.IsEmpty);
        Assert.Contains("全 2 曲", sut.SummaryText);
    }

    /// <summary>
    /// 検索キーワードを設定した際、曲名・アーティスト・アルバム・ジャンルで絞り込まれることを検証します。
    /// </summary>
    [Fact]
    public void SearchKeyword_キーワード入力時_該当楽曲のみに絞り込まれる()
    {
        // Arrange
        var (sut, library, _) = CreateSut(TimeSpan.Zero);
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("Get Lucky", artist: "Daft Punk", album: "RAM", genre: "Disco"));
        library.Tracks.Add(CreateTrack("Midnight City", artist: "M83", album: "Hurry Up", genre: "Synthpop"));

        // Act
        sut.SearchKeyword = "daft";

        // Assert
        Assert.Single(sut.FilteredTracks);
        Assert.Equal("Get Lucky", sut.FilteredTracks[0].Title);
    }

    /// <summary>
    /// デバウンスが有効な状態で連続して検索キーワードを入力した際、待機後に最新キーワードの結果のみ反映されることを検証します。
    /// </summary>
    [Fact]
    public async Task SearchKeyword_デバウンス有効時の連続入力_待機後に最新キーワードの結果のみ反映される()
    {
        // Arrange
        var (sut, library, _) = CreateSut(TimeSpan.FromMilliseconds(80));
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("Get Lucky", artist: "Daft Punk", genre: "Disco"));
        library.Tracks.Add(CreateTrack("Midnight City", artist: "M83", genre: "Synthpop"));

        // Act
        sut.SearchKeyword = "d";
        sut.SearchKeyword = "di";
        sut.SearchKeyword = "disco";

        // デバウンス待機直後はまだ全件のまま
        Assert.Equal(2, sut.FilteredTracks.Count);

        await Task.Delay(180);

        // Assert
        Assert.Single(sut.FilteredTracks);
        Assert.Equal("Get Lucky", sut.FilteredTracks[0].Title);
    }

    /// <summary>
    /// 検索キーワードを空にクリアした際、デバウンス待機なしで即座に全件表示へ戻ることを検証します。
    /// </summary>
    [Fact]
    public async Task SearchKeyword_クリア時_デバウンス待機せず即座に全件表示へ戻る()
    {
        // Arrange
        var (sut, library, _) = CreateSut(TimeSpan.FromMilliseconds(60));
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("Get Lucky", artist: "Daft Punk"));
        library.Tracks.Add(CreateTrack("Midnight City", artist: "M83"));
        sut.SearchKeyword = "daft";
        await Task.Delay(150);
        Assert.Single(sut.FilteredTracks);

        // Act
        sut.ClearSearchCommand.Execute(null);

        // Assert
        Assert.Equal(2, sut.FilteredTracks.Count);
    }

    /// <summary>
    /// ソート選択肢に内部キーと日本語表示ラベルのペアが提供されることを検証します。
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
        Assert.Equal(5, options.Count);
        Assert.Contains(options, o => o.Key == "Title" && o.DisplayName == "タイトル");
        Assert.Contains(options, o => o.Key == "Artist" && o.DisplayName == "アーティスト");
        Assert.Contains(options, o => o.Key == "Album" && o.DisplayName == "アルバム");
        Assert.Contains(options, o => o.Key == "Duration" && o.DisplayName == "時間");
        Assert.Contains(options, o => o.Key == "Genre" && o.DisplayName == "ジャンル");
    }

    /// <summary>
    /// ソート基準と昇降順を変更した際、楽曲リストが正しく並び替えられることを検証します。
    /// </summary>
    [Fact]
    public void SelectedSortOption_再生時間降順を指定した場合_長い曲順に並ぶ()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("Short", duration: TimeSpan.FromMinutes(2)));
        library.Tracks.Add(CreateTrack("Long", duration: TimeSpan.FromMinutes(6)));
        library.Tracks.Add(CreateTrack("Medium", duration: TimeSpan.FromMinutes(4)));

        // Act
        sut.SelectedSortOption = "Duration";
        sut.IsAscending = false;

        // Assert
        Assert.Equal(new[] { "Long", "Medium", "Short" }, sut.FilteredTracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// アーティスト降順およびアルバム降順でトラック番号が同一の楽曲が存在する場合、タイトル昇順のタイブレーカーで安定ソートされることを検証します。
    /// </summary>
    [Theory]
    [InlineData("Artist")]
    [InlineData("Album")]
    public void SelectedSortOption_降順でトラック番号同一の場合_タイトル昇順のタイブレーカーが適用される(string sortKey)
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("Zulu", artist: "Same Artist", album: "Same Album", trackNumber: 0));
        library.Tracks.Add(CreateTrack("Alpha", artist: "Same Artist", album: "Same Album", trackNumber: 0));
        library.Tracks.Add(CreateTrack("Mike", artist: "Same Artist", album: "Same Album", trackNumber: 0));

        // Act
        sut.SelectedSortOption = sortKey;
        sut.IsAscending = false;

        // Assert
        Assert.Equal(new[] { "Alpha", "Mike", "Zulu" }, sut.FilteredTracks.Select(t => t.Title).ToArray());
    }

    /// <summary>
    /// PlayAllを実行した際、表示中の全楽曲がAudioServiceのプレイリストに1回だけセットされ再生開始されることを検証します。
    /// </summary>
    [Fact]
    public void PlayAll_楽曲が存在する場合_全曲をプレイリストにセットし再生開始する()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("Track 1"));
        library.Tracks.Add(CreateTrack("Track 2"));
        IReadOnlyList<Track>? requestedTracks = null;
        sut.PlaybackRequested += (tracks, _, _, _) => requestedTracks = tracks;

        // Act
        sut.PlayAll();

        // Assert
        Assert.NotNull(requestedTracks);
        Assert.Equal(2, requestedTracks!.Count);
        Assert.Equal(1, audio.SetPlaylistCallCount);
        Assert.Equal(2, audio.LastPlaylist.Count);
        Assert.True(audio.PlayCalled);
    }

    /// <summary>
    /// ShufflePlayを実行した際、表示中の全楽曲が漏れなく含まれてプレイリスト設定・再生開始されることを検証します。
    /// </summary>
    [Fact]
    public void ShufflePlay_楽曲が存在する場合_全曲を含めてプレイリスト設定し再生開始する()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        var t1 = CreateTrack("Track 1");
        var t2 = CreateTrack("Track 2");
        var t3 = CreateTrack("Track 3");
        library.Tracks.Add(t1);
        library.Tracks.Add(t2);
        library.Tracks.Add(t3);
        IReadOnlyList<Track>? requestedTracks = null;
        Track? requestedStart = null;
        sut.PlaybackRequested += (tracks, start, _, _) =>
        {
            requestedTracks = tracks;
            requestedStart = start;
        };

        // Act
        sut.ShufflePlay();

        // Assert
        Assert.NotNull(requestedTracks);
        Assert.Equal(3, requestedTracks!.Count);
        Assert.Equal(new[] { t1, t2, t3 }.OrderBy(t => t.Title), requestedTracks.OrderBy(t => t.Title));
        Assert.Equal(requestedStart, audio.LastStartTrack);
        Assert.Equal(1, audio.SetPlaylistCallCount);
        Assert.True(audio.PlayCalled);
    }

    /// <summary>
    /// 楽曲が空の状態でShufflePlayを実行した際、AudioServiceやPlaybackRequestedが呼ばれないことを検証します。
    /// </summary>
    [Fact]
    public void ShufflePlay_楽曲が空の場合_再生処理を実行しない()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        bool requested = false;
        sut.PlaybackRequested += (_, _, _, _) => requested = true;

        // Act
        sut.ShufflePlay();

        // Assert
        Assert.False(requested);
        Assert.Equal(0, audio.SetPlaylistCallCount);
        Assert.False(audio.PlayCalled);
    }

    /// <summary>
    /// PlayTrackを実行した際、選択された楽曲を開始トラックとして再生開始されることを検証します。
    /// </summary>
    [Fact]
    public void PlayTrack_特定トラックを指定した場合_対象トラックを開始曲として再生する()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        var track1 = CreateTrack("A Track");
        var track2 = CreateTrack("B Track");
        library.Tracks.Add(track1);
        library.Tracks.Add(track2);

        // Act
        sut.PlayTrack(track2);

        // Assert
        Assert.Equal(1, audio.SetPlaylistCallCount);
        Assert.Equal(track2, audio.LastStartTrack);
        Assert.True(audio.PlayCalled);
    }

    /// <summary>
    /// EnqueueRequestedが購読されている場合、PlayNextCommandおよびEnqueueTrackCommandがイベント経由で正しいplayNextフラグを通知することを検証します。
    /// </summary>
    [Fact]
    public void EnqueueCommands_EnqueueRequested購読ありの場合_イベント経由で通知される()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        var track = CreateTrack("Queued Track");
        var events = new List<(IReadOnlyList<Track> Tracks, bool PlayNext)>();
        sut.EnqueueRequested += (tracks, playNext) => events.Add((tracks, playNext));

        // Act
        sut.PlayNextCommand.Execute(track);
        sut.EnqueueTrackCommand.Execute(track);

        // Assert
        Assert.Equal(2, events.Count);
        Assert.True(events[0].PlayNext);
        Assert.Single(events[0].Tracks, track);
        Assert.False(events[1].PlayNext);
        Assert.Single(events[1].Tracks, track);
        Assert.Empty(audio.EnqueuedCalls);
    }

    /// <summary>
    /// EnqueueRequestedが未購読の場合、PlayNextCommandおよびEnqueueTrackCommandがAudioServiceへ直接委譲されることを検証します。
    /// </summary>
    [Fact]
    public void EnqueueCommands_EnqueueRequested購読なしの場合_AudioServiceへ直接委譲される()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        var track = CreateTrack("Direct Queue Track");

        // Act
        sut.PlayNextCommand.Execute(track);
        sut.EnqueueTrackCommand.Execute(track);

        // Assert
        Assert.Equal(2, audio.EnqueuedCalls.Count);
        Assert.True(audio.EnqueuedCalls[0].PlayNext);
        Assert.Single(audio.EnqueuedCalls[0].Tracks, track);
        Assert.False(audio.EnqueuedCalls[1].PlayNext);
        Assert.Single(audio.EnqueuedCalls[1].Tracks, track);
    }

    /// <summary>
    /// 楽曲数が0件の時はPlayAllCommandとShufflePlayCommandが実行不可となり、1件以上追加されると実行可能になることを検証します。
    /// </summary>
    [Fact]
    public void CanExecute_楽曲数0件と1件以上の場合_PlayAllとShufflePlayの実行可否が切り替わる()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;

        // Act & Assert (0件)
        Assert.False(sut.PlayAllCommand.CanExecute(null));
        Assert.False(sut.ShufflePlayCommand.CanExecute(null));

        // Act (1件追加)
        library.Tracks.Add(CreateTrack("Track 1"));

        // Assert (1件以上)
        Assert.True(sut.PlayAllCommand.CanExecute(null));
        Assert.True(sut.ShufflePlayCommand.CanExecute(null));
    }

    /// <summary>
    /// Dispose後にライブラリへ楽曲が追加されても、FilteredTracksが更新されないことを検証します。
    /// </summary>
    [Fact]
    public void Dispose_破棄後にライブラリへ楽曲追加した場合_FilteredTracksが更新されない()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        library.Tracks.Add(CreateTrack("Before Dispose"));
        Assert.Single(sut.FilteredTracks);

        // Act
        sut.Dispose();
        library.Tracks.Add(CreateTrack("After Dispose"));

        // Assert
        Assert.Single(sut.FilteredTracks);
        Assert.Equal("Before Dispose", sut.FilteredTracks[0].Title);
    }

    /// <summary>
    /// AudioServiceのシャッフルモードが有効な状態でShufflePlayを実行した際、UI通知リストの先頭が開始曲となり全曲が重複なく含まれることを検証します。
    /// </summary>
    [Fact]
    public void ShufflePlay_AudioServiceのシャッフルが有効な場合_開始曲とAlbumQueueが重複なく同期される()
    {
        // Arrange
        var (sut, library, audio) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        audio.IsShuffleEnabled = true;
        var t1 = CreateTrack("Track 1");
        var t2 = CreateTrack("Track 2");
        var t3 = CreateTrack("Track 3");
        library.Tracks.Add(t1);
        library.Tracks.Add(t2);
        library.Tracks.Add(t3);
        IReadOnlyList<Track>? requestedTracks = null;
        Track? requestedStart = null;
        sut.PlaybackRequested += (tracks, start, _, _) =>
        {
            requestedTracks = tracks;
            requestedStart = start;
        };

        // Act
        sut.ShufflePlay();

        // Assert
        Assert.NotNull(requestedTracks);
        Assert.NotNull(requestedStart);
        Assert.Equal(requestedStart, requestedTracks![0]);
        Assert.Equal(3, requestedTracks.Count);
        Assert.Equal(3, requestedTracks.Select(t => t.FilePath).Distinct().Count());
        Assert.Equal(1, audio.SetPlaylistCallCount);
    }

    /// <summary>
    /// PlayerControlViewModel.SetPlaybackListでupdateAudioServiceにfalseを指定した際、AudioService.SetPlaylistが再呼び出しされないことを検証します。
    /// </summary>
    [Fact]
    public void SetPlaybackList_updateAudioServiceがFalseの場合_AudioServiceのSetPlaylistを再呼び出ししない()
    {
        // Arrange
        using var audio = new StubAudioService();
        using var audioEngine = new StubAudioEngine();
        var eventBus = new InMemoryEventBus();
        var settings = new StubSettingsService();
        using var sut = new PlayerControlViewModel(audio, audioEngine, eventBus, settings);
        var tracks = new[] { CreateTrack("Track A"), CreateTrack("Track B") };

        // Act
        sut.SetPlaybackList(tracks, "すべての曲", "2 曲", updateAudioService: false);

        // Assert
        Assert.Equal(0, audio.SetPlaylistCallCount);
        Assert.Equal("すべての曲", sut.PlaybackListName);
        Assert.Equal("2 曲", sut.PlaybackListSubtitle);
        Assert.Equal(2, sut.PlaybackListTracks.Count);
    }

    /// <summary>
    /// ソート条件や昇降順を切り替えた際、DisplayTracksの連番（DisplayNumber）が常に1始まりの連続番号に再採番され、SelectedSortOptionItemの表示名も同期することを検証します。
    /// </summary>
    [Fact]
    public void DisplayTracks_ソート変更および昇降順切替時_連番が常に1始まりで整合しソート表示名も更新される()
    {
        // Arrange
        var (sut, library, _) = CreateSut();
        using var _lib = library;
        using var _sut = sut;
        library.Tracks.Add(CreateTrack("Track C", artist: "Artist B", album: "Album Z"));
        library.Tracks.Add(CreateTrack("Track A", artist: "Artist C", album: "Album X"));
        library.Tracks.Add(CreateTrack("Track B", artist: "Artist A", album: "Album Y"));

        // Act: アルバム順へ切替
        sut.SelectedSortOptionItem = sut.SortOptions.Single(o => o.Key == "Album");

        // Assert
        Assert.Equal("Album", sut.SelectedSortOption);
        Assert.Equal("アルバム", sut.SelectedSortOptionItem.DisplayName);
        Assert.Equal(new[] { 1, 2, 3 }, sut.DisplayTracks.Select(r => r.DisplayNumber).ToArray());
        Assert.Equal(new[] { "Album X", "Album Y", "Album Z" }, sut.DisplayTracks.Select(r => r.Track.Album).ToArray());

        // Act: 降順へ切替
        sut.IsAscending = false;

        // Assert
        Assert.Equal(new[] { 1, 2, 3 }, sut.DisplayTracks.Select(r => r.DisplayNumber).ToArray());
        Assert.Equal(new[] { "Album Z", "Album Y", "Album X" }, sut.DisplayTracks.Select(r => r.Track.Album).ToArray());
    }

    /// <summary>
    /// AllSongsViewの実体でソートComboBoxの選択を「アルバム」へ変更した際、SelectionBoxItemの表示ラベルが「アルバム」へ更新され、バインディングエラーも0件であることを検証します。
    /// </summary>
    [Fact]
    public void AllSongsView_ソート設定変更時_ComboBoxの表示名称が選択項目へ更新されること()
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
            var t1 = CreateTrack("Song B", album: "Album Z");
            var t2 = CreateTrack("Song A", album: "Album A");
            t1.FilePath = string.Empty;
            t2.FilePath = string.Empty;
            library.Tracks.Add(t1);
            library.Tracks.Add(t2);

            using var listener = new BindingErrorTraceListener();
            EnsureApplicationResources();
            var view = new AllSongsView();
            AttachRequiredResources(view.Resources);
            view.DataContext = viewModel;
            view.Measure(new Size(900, 600));
            view.Arrange(new Rect(0, 0, 900, 600));
            view.UpdateLayout();

            var comboBox = Assert.IsType<System.Windows.Controls.ComboBox>(view.FindName("SortComboBox"));
            var initialPresenter = Assert.IsType<System.Windows.Controls.ContentPresenter>(comboBox.Template.FindName("contentPresenter", comboBox));
            Assert.Equal("タイトル", FindFirstTextBlockText(initialPresenter));

            // Act
            comboBox.SelectedItem = viewModel.SortOptions.Single(o => o.Key == "Album");
            view.UpdateLayout();

            // Assert
            Assert.Equal("Album", viewModel.SelectedSortOption);
            var selectedBox = Assert.IsType<AllSongsSortOption>(comboBox.SelectionBoxItem);
            Assert.Equal("アルバム", selectedBox.DisplayName);
            Assert.NotNull(comboBox.SelectionBoxItemTemplate);
            Assert.Equal("アルバム", FindFirstTextBlockText(initialPresenter));
            Assert.Equal(new[] { 1, 2 }, viewModel.DisplayTracks.Select(r => r.DisplayNumber).ToArray());
            Assert.False(listener.HasErrors, $"AllSongsView でバインディングエラーが検出されました: {string.Join("; ", listener.Errors)}");
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

    /// <summary>
    /// AllSongsViewの実体へAllSongsViewModelと楽曲データをバインドしてレイアウト更新した際、WPFバインディングエラーが0件であることを検証します。
    /// </summary>
    [Fact]
    public void AllSongsView_実体へバインドしてレイアウト更新した場合_バインディングエラーが0件であること()
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
            var track = CreateTrack("Binding Check Song", artist: "Test Artist", album: "Test Album", genre: "Rock", format: "FLAC");
            track.FilePath = string.Empty;
            library.Tracks.Add(track);

            using var listener = new BindingErrorTraceListener();
            EnsureApplicationResources();
            var view = new AllSongsView();
            AttachRequiredResources(view.Resources);
            view.DataContext = viewModel;

            // Act
            view.Measure(new Size(900, 600));
            view.Arrange(new Rect(0, 0, 900, 600));
            view.UpdateLayout();

            // Assert
            Assert.False(listener.HasErrors, $"AllSongsView でバインディングエラーが検出されました: {string.Join("; ", listener.Errors)}");
            Assert.Equal(0, listener.ErrorCount);
        });
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

        public IReadOnlyList<Track> UserQueue => Array.Empty<Track>();
        public IReadOnlyList<Track> AlbumQueue => LastPlaylist;
        public bool IsPlaying => PlayCalled;
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
        public void TogglePlayPause() => PlayCalled = !PlayCalled;
        public void Next() { }
        public void Previous() { }
        public void Stop(bool internalStop = false) => PlayCalled = false;
        public void SeekTo(double percentage) { }
        public void PauseForSeek() { }
        public void ResumeAfterSeek() { }
        public void SetGain(int bandIndex, float gain) { }
        public void UpdateAudioProperties(int sampleRate, int bufferSizeMs) { }
        public void Dispose() { }
    }

    private sealed class StubAudioEngine : IAudioEngine
    {
        public PlaybackState CurrentState => PlaybackState.Stopped;
        public TimeSpan CurrentPosition => TimeSpan.Zero;
        public TimeSpan TotalDuration => TimeSpan.Zero;
        public float Volume => 0.5f;

        public event EventHandler<FftCalculatedEventArgs>? FftCalculated { add { } remove { } }
        public event EventHandler? PlaybackEnded { add { } remove { } }

        public Task LoadTrackAsync(Track track, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PlayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetEqualizerBandGainAsync(int bandIndex, float gainDb, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetEqualizerAllGainsAsync(float[] gainsDb, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
