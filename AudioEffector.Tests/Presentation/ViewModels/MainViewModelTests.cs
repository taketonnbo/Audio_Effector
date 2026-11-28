using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Events;
using AudioEffector.Domain.Repositories;
using AudioEffector.Domain.Services;
using AudioEffector.Domain.ValueObjects;
using AudioEffector.Infrastructure.Library;
using AudioEffector.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="MainViewModel"/> における右側タブの歌詞表示トグル・最大化モード連動・右側タブ開閉連動を検証するテストクラス
/// </summary>
public sealed class MainViewModelTests
{
    private sealed class ImmediateDispatcherStub : IUiDispatcher
    {
        public void Invoke(Action action) => action();

        public void BeginInvoke(Action action) => action();
    }

    private sealed class EmptyLyricsProviderStub : ILyricsProvider
    {
        public System.Threading.Tasks.Task<LyricsDocument?> LoadLyricsAsync(
            Track track,
            System.Threading.CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult<LyricsDocument?>(null);
        }
    }

    private sealed class InMemoryListeningStatsRepositoryStub : IListeningStatsRepository
    {
        private ListeningStatsData _data = new();

        public ListeningStatsData Load() => _data.Clone();

        public void Save(ListeningStatsData data) => _data = data.Clone();

        public ListeningStatsData Update(Action<ListeningStatsData> update)
        {
            ListeningStatsData working = _data.Clone();
            update(working);
            _data = working.Clone();
            return _data.Clone();
        }
    }

    private sealed class ServiceProviderScope : IDisposable
    {
        private readonly IServiceProvider? _previous;
        private readonly ServiceProvider _provider;

        public ServiceProviderScope(
            Func<LyricsApplicationService, IAudioService, IUiDispatcher, LrcGeneratorViewModel>? lrcGeneratorFactory = null)
        {
            _previous = App.ServiceProvider;

            var services = new ServiceCollection();
            var eventBus = new InMemoryEventBus();
            var dispatcher = new ImmediateDispatcherStub();

            var settingsMock = new Mock<ISettingsService>();
            settingsMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());

            var audioServiceMock = new Mock<IAudioService>();
            audioServiceMock.Setup(a => a.UserQueue).Returns(Array.Empty<Track>());
            audioServiceMock.Setup(a => a.AlbumQueue).Returns(Array.Empty<Track>());

            var audioEngineMock = new Mock<IAudioEngine>();
            var trackRepoMock = new Mock<ITrackRepository>();
            trackRepoMock.Setup(r => r.GetAllAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(Array.Empty<Track>());
            var favoriteRepoMock = new Mock<IFavoriteRepository>();
            favoriteRepoMock.Setup(r => r.GetAllAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());
            var statsRepo = new InMemoryListeningStatsRepositoryStub();
            var recentRepoMock = new Mock<IRecentTrackRepository>();
            recentRepoMock.Setup(r => r.Load()).Returns(Array.Empty<RecentTrackEntry>());
            var transcodeMock = new Mock<IAudioTranscodeService>();

            var libraryService = new LibraryApplicationService(
                trackRepoMock.Object,
                favoriteRepoMock.Object,
                new TagLibMetadataExtractor(),
                eventBus);
            var lyricsService = new LyricsApplicationService(new EmptyLyricsProviderStub(), eventBus);

            services.AddSingleton<IEventBus>(eventBus);
            services.AddSingleton<IUiDispatcher>(dispatcher);
            services.AddSingleton(settingsMock.Object);
            services.AddSingleton(audioServiceMock.Object);
            services.AddSingleton(audioEngineMock.Object);
            services.AddSingleton<IListeningStatsRepository>(statsRepo);
            services.AddSingleton(recentRepoMock.Object);
            services.AddSingleton(transcodeMock.Object);
            services.AddSingleton(libraryService);
            services.AddSingleton(lyricsService);
            services.AddSingleton<ListeningStatsApplicationService>();

            services.AddSingleton<PlayerControlViewModel>();
            services.AddSingleton<LibraryViewModel>();
            services.AddSingleton<AlbumDetailViewModel>();
            services.AddSingleton<AllSongsViewModel>();
            services.AddSingleton<ArtistsViewModel>();
            services.AddSingleton<RecentViewModel>();
            services.AddSingleton<LyricsViewModel>();
            services.AddSingleton<ListeningStatsViewModel>();

            if (lrcGeneratorFactory != null)
            {
                services.AddSingleton(lrcGeneratorFactory(lyricsService, audioServiceMock.Object, dispatcher));
            }

            _provider = services.BuildServiceProvider();
            App.ServiceProvider = _provider;
        }

        public void Dispose()
        {
            App.ServiceProvider = _previous;
            _provider.Dispose();
        }
    }

    /// <summary>
    /// 楽曲未再生時は右上の拡大／縮小コマンドおよび3タブ切替がガードされ、右側拡張領域を開けないことを検証します。
    /// </summary>
    [Fact]
    public void ToggleAlbumViewSizeModeCommand_楽曲未再生時の場合_コンパクト拡張領域を開けないこと()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        sut.IsRightPanelOpen = true;

        // Act
        Assert.False(sut.HasCurrentTrack);
        Assert.False(sut.CanToggleAlbumViewSizeMode);
        Assert.False(sut.ToggleAlbumViewSizeModeCommand.CanExecute(null));
        sut.ToggleAlbumViewSizeModeCommand.Execute(null);
        sut.SelectRightExtensionTabCommand.Execute(RightExtensionPanelType.Dashboard);

        // Assert
        Assert.True(sut.IsAlbumViewMaximized);
        Assert.False(sut.IsRightExtensionAreaVisible);
        Assert.Equal(RightExtensionPanelType.None, sut.ActiveRightExtensionPanel);
    }

    /// <summary>
    /// 楽曲再生中に右上の拡大／縮小ボタンでコンパクト表示へ切り替えると既定で歌詞タブが開き、
    /// 3タブ（歌詞・ダッシュボード・タグ）を排他的に切り替えられることを検証します。
    /// </summary>
    [Fact]
    public void SelectRightExtensionTabCommand_楽曲再生中にコンパクト表示へ切替時_3タブが排他的に切り替わりIsActiveViewが連動すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        sut.IsRightPanelOpen = true;
        sut.CurrentTrack = new Track { FilePath = @"C:\Music\Playing.flac", Title = "Playing" };

        // Act 1: 縮小ボタンでコンパクト表示へ切替 → 既定の歌詞タブ表示
        Assert.True(sut.ToggleAlbumViewSizeModeCommand.CanExecute(null));
        sut.ToggleAlbumViewSizeModeCommand.Execute(null);

        // Assert 1
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.True(sut.IsRightExtensionAreaVisible);
        Assert.Equal(RightExtensionPanelType.Lyrics, sut.ActiveRightExtensionPanel);
        Assert.True(sut.IsLyricsTabSelected);
        Assert.True(sut.IsLyricsViewVisible);
        Assert.True(sut.Lyrics!.IsActiveView);
        Assert.False(sut.IsDashboardViewVisible);
        Assert.False(sut.ListeningStats!.IsActiveView);

        // Act 2: ダッシュボードタブへ切替
        sut.SelectRightExtensionTabCommand.Execute(RightExtensionPanelType.Dashboard);

        // Assert 2
        Assert.Equal(RightExtensionPanelType.Dashboard, sut.ActiveRightExtensionPanel);
        Assert.True(sut.IsDashboardTabSelected);
        Assert.True(sut.IsDashboardViewVisible);
        Assert.True(sut.ListeningStats.IsActiveView);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.Lyrics.IsActiveView);

        // Act 3: タグタブへ切替
        sut.SelectRightExtensionTabCommand.Execute(RightExtensionPanelType.Tags);

        // Assert 3
        Assert.Equal(RightExtensionPanelType.Tags, sut.ActiveRightExtensionPanel);
        Assert.True(sut.IsTagsTabSelected);
        Assert.True(sut.IsTagsViewVisible);
        Assert.False(sut.IsDashboardViewVisible);
        Assert.False(sut.ListeningStats.IsActiveView);

        // Act 4: 拡大ボタンで最大化へ戻す
        sut.ToggleAlbumViewSizeModeCommand.Execute(null);

        // Assert 4
        Assert.True(sut.IsAlbumViewMaximized);
        Assert.False(sut.IsRightExtensionAreaVisible);
        Assert.False(sut.IsTagsViewVisible);
    }

    /// <summary>
    /// 右側プレイリストの右クリックメニューから、再生中以外の曲を対象にダッシュボード・タグ・LRCエディタへ直接遷移できることを検証します。
    /// </summary>
    [Fact]
    public void プレイリスト右クリック導線_再生中以外の曲を指定した場合_ダッシュボードおよびタグとLRCエディタへ対象曲付きで遷移できること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        var playingTrack = new Track { FilePath = @"C:\Music\Playing.flac", Title = "Playing Song", Artist = "Artist 1" };
        var selectedTrack = new Track { FilePath = @"C:\Music\Other.flac", Title = "Other Song", Artist = "Artist 2" };
        sut.CurrentTrack = playingTrack;
        sut.IsRightPanelOpen = false;
        Assert.True(sut.IsAlbumViewMaximized);

        // Act 1: ダッシュボードを表示
        sut.OpenTrackDashboardCommand.Execute(selectedTrack);

        // Assert 1
        Assert.True(sut.IsRightPanelOpen);
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.Equal(RightExtensionPanelType.Dashboard, sut.ActiveRightExtensionPanel);
        Assert.True(sut.IsDashboardViewVisible);
        Assert.Same(selectedTrack, sut.ListeningStats!.TargetTrack);
        Assert.True(sut.ListeningStats.IsShowingNonPlayingTrack);

        // Act 2: タグを表示
        sut.OpenTrackTagsCommand.Execute(selectedTrack);

        // Assert 2
        Assert.Equal(RightExtensionPanelType.Tags, sut.ActiveRightExtensionPanel);
        Assert.True(sut.IsTagsViewVisible);
        Assert.Same(selectedTrack, sut.TagsTargetTrack);
        Assert.True(sut.IsTagsShowingNonPlayingTrack);
        Assert.True(sut.CanReturnTagsToPlayingTrack);

        // Act 2b: タグタブで再生中の曲へ戻す
        sut.ReturnTagsToPlayingTrackCommand.Execute(null);
        Assert.Same(playingTrack, sut.TagsTargetTrack);
        Assert.False(sut.IsTagsShowingNonPlayingTrack);

        // Act 3: 歌詞を編集（LRCエディタ）
        sut.OpenLrcGeneratorCommand.Execute(selectedTrack);

        // Assert 3
        Assert.Equal(RightExtensionPanelType.LrcGenerator, sut.ActiveRightExtensionPanel);
        Assert.True(sut.IsLrcGeneratorViewVisible);
        Assert.True(sut.IsLyricsTabSelected);
        Assert.Same(selectedTrack, sut.LrcGenerator!.TargetTrack);
    }

    /// <summary>
    /// タグタブでピン留め中の楽曲自身が再生開始された場合、ピン留めが自動解除され後続の別楽曲遷移へ自然に追従することを検証します。
    /// </summary>
    [Fact]
    public void OpenTrackTags_ピン留め中の楽曲が再生開始され次曲へ遷移した場合_ピン留めが自動解除され次曲へ追従すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        var trackA = new Track { FilePath = @"C:\Music\TrackA.flac", Title = "Track A", Artist = "Artist A" };
        var trackB = new Track { FilePath = @"C:\Music\TrackB.flac", Title = "Track B", Artist = "Artist B" };
        var trackC = new Track { FilePath = @"C:\Music\TrackC.flac", Title = "Track C", Artist = "Artist C" };
        sut.CurrentTrack = trackA;

        // Act 1: 曲 A 再生中に曲 B をタグタブへピン留め
        sut.OpenTrackTagsCommand.Execute(trackB);
        Assert.Same(trackB, sut.TagsTargetTrack);
        Assert.True(sut.IsTagsShowingNonPlayingTrack);

        // Act 2: ピン留めしていた曲 B 自身の再生を開始 → ピン留め自動解除
        sut.CurrentTrack = trackB;
        Assert.Same(trackB, sut.TagsTargetTrack);
        Assert.False(sut.IsTagsShowingNonPlayingTrack);

        // Act 3: 続いて曲 C へ遷移 → 曲 B へ戻らず曲 C へ追従すること
        sut.CurrentTrack = trackC;

        // Assert
        Assert.Same(trackC, sut.TagsTargetTrack);
        Assert.False(sut.IsTagsShowingNonPlayingTrack);
    }

    /// <summary>
    /// 右側タブ閉鎖時は選択タブを保持したまま Lyrics.IsActiveView / ListeningStats.IsActiveView が false になり、再展開時に復帰することを検証します。
    /// </summary>
    [Fact]
    public void IsRightPanelOpen_拡張パネル表示中に右側タブを閉鎖および再展開した場合_選択タブ維持のままIsActiveViewが連動すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        sut.IsRightPanelOpen = true;
        sut.CurrentTrack = new Track { FilePath = @"C:\Music\PanelToggle.flac", Title = "PanelToggle" };
        sut.ToggleAlbumViewSizeModeCommand.Execute(null);
        Assert.True(sut.IsLyricsViewVisible);
        Assert.True(sut.Lyrics!.IsActiveView);

        // Act 1: 右側タブ閉鎖
        sut.IsRightPanelOpen = false;

        // Assert 1
        Assert.Equal(RightExtensionPanelType.Lyrics, sut.ActiveRightExtensionPanel);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.Lyrics.IsActiveView);

        // Act 2: 右側タブ再展開
        sut.IsRightPanelOpen = true;

        // Assert 2
        Assert.True(sut.IsLyricsViewVisible);
        Assert.True(sut.Lyrics.IsActiveView);
    }

    /// <summary>
    /// 最大化モードかつ右側タブ閉鎖状態から OpenLrcGenerator を呼ぶと右側タブ展開・LRC 生成表示され、
    /// 戻る操作で歌詞タブ、閉じる操作で最大化表示へ戻ることを検証します。
    /// </summary>
    [Fact]
    public void OpenLrcGenerator_最大化状態から展開し歌詞へ戻るおよび閉鎖_期待通り排他切替と最大化復帰すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        var track = new Track { FilePath = @"C:\Music\GeneratorTarget.flac", Title = "GeneratorTarget" };
        sut.IsRightPanelOpen = false;
        Assert.True(sut.IsAlbumViewMaximized);

        // Act 1: LRC 生成パネル展開
        sut.OpenLrcGenerator(track);

        // Assert 1
        Assert.True(sut.IsRightPanelOpen);
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.Equal(RightExtensionPanelType.LrcGenerator, sut.ActiveRightExtensionPanel);
        Assert.True(sut.IsLrcGeneratorViewVisible);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.Lyrics!.IsActiveView);
        Assert.Same(track, sut.LrcGenerator!.TargetTrack);

        // Act 2: [← 歌詞へ戻る] で歌詞タブへ切替
        sut.LrcGenerator.BackToLyricsCommand.Execute(null);
        Assert.True(sut.IsLyricsViewVisible);
        Assert.True(sut.Lyrics.IsActiveView);

        // Act 3: 歌詞パネルを閉じる → 最大化へ復帰
        sut.Lyrics.ClosePanelCommand.Execute(null);

        // Assert 3
        Assert.Equal(RightExtensionPanelType.None, sut.ActiveRightExtensionPanel);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.True(sut.IsAlbumViewMaximized);
    }

    private sealed class StubAlignmentEngineForMain : IAudioAlignmentEngine
    {
        public string EngineName => "StubMainEngine";
        public LrcGenerationMode? LastMode { get; private set; }

        public Task<AlignmentResult> GenerateAsync(
            AlignmentRequest request,
            IProgress<AlignmentProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastMode = request.Mode;
            var doc = new LyricsDocument(
                [new LyricLine(0, TimeSpan.FromSeconds(4), "再作成テスト歌詞")],
                isSynced: true,
                title: request.Title);
            return Task.FromResult(new AlignmentResult(
                doc,
                "[00:04.00]再作成テスト歌詞",
                Array.Empty<AlignmentSegment>(),
                request.Mode,
                EngineName));
        }
    }

    private sealed class StubTextEncodingServiceForMain : ITextEncodingService
    {
        public Task<TextEncodingInspectionResult> InspectAsync(string textFilePath, CancellationToken cancellationToken = default)
            => Task.FromResult(new TextEncodingInspectionResult(textFilePath, true, false, "UTF-8", 2, 2));

        public Task<string> ReadAsUtf8TextAsync(string textFilePath, bool allowEncodingConversion, CancellationToken cancellationToken = default)
            => Task.FromResult("歌詞1\n歌詞2");

        public Task SaveLrcFileAsync(string destinationPath, string lrcContent, bool overwrite, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubModelManagerForMain : ILocalSpeechModelManager
    {
        public LocalSpeechModelInfo GetActiveModelInfo()
            => new("base", "Whisper (base)", "ggml-base.bin", @"C:\models\ggml-base.bin", true, 140_000_000);

        public IReadOnlyList<LocalSpeechModelInfo> GetSupportedModels() => [GetActiveModelInfo()];

        public void SelectModel(string modelId) { }

        public void SetCustomModelPath(string? modelFilePath) { }

        public Task<LocalSpeechModelInfo> DownloadModelAsync(
            string? modelId = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(GetActiveModelInfo());
    }

    private sealed class StubLrcParserForMain : ILrcParser
    {
        public Func<string, LyricsDocument>? ParseHandler { get; set; }

        public LyricsDocument Parse(string content) => ParseHandler is not null ? ParseHandler(content) : LyricsDocument.Empty;

        public string Format(LyricsDocument document) => "[00:04.00]再作成テスト歌詞";

        public LrcOriginType ClassifyOrigin(string lrcContent) => LrcOriginType.ManualOrExternal;
    }

    /// <summary>
    /// 同一楽曲で自動文字起こし→保存→歌詞表示へ戻る→Lyrics.RequestGenerateLrcCommand で再起動した際に、
    /// LrcGenerator.IsInputState が true となりテキスト生成と自動文字起こしの双方を再開できることを検証します。
    /// </summary>
    [Fact]
    public async Task OpenLrcGenerator_自動文字起こし保存後に歌詞パネルのLRC生成コマンドで再起動_入力状態へ復帰しテキスト生成と自動文字起こしを再開できること()
    {
        // Arrange
        var engine = new StubAlignmentEngineForMain();
        var track = new Track { FilePath = @"C:\Music\MainRecreate.flac", Title = "MainRecreate", Artist = "Artist" };
        using var scope = new ServiceProviderScope((lyricsSvc, audioSvc, dispatcher) =>
        {
            var genSvc = new LrcGenerationApplicationService(
                engine,
                new StubTextEncodingServiceForMain(),
                new StubModelManagerForMain(),
                new StubLrcParserForMain(),
                lyricsSvc);
            var settingsMock = new Mock<ISettingsService>();
            settingsMock.Setup(s => s.LoadSettings())
                .Returns(new AppSettings { HasAcceptedLrcCopyrightGuideline = true });
            return new LrcGeneratorViewModel(
                genSvc,
                audioSvc,
                dispatcher,
                selectTextFileFunc: null,
                selectModelFileFunc: null,
                openFolderAction: _ => { },
                fileExistsFunc: _ => true,
                settingsService: settingsMock.Object);
        });
        using var sut = new MainViewModel();
        sut.IsRightPanelOpen = true;
        await sut.Lyrics!.HandleAsync(new TrackChangedEvent(track, TimeSpan.Zero));

        // 初回: LRC 自動生成を起動 → 自動文字起こし実行 → 保存 → 歌詞表示へ戻る
        sut.OpenLrcGenerator(track);
        Assert.True(sut.IsLrcGeneratorViewVisible);
        await sut.LrcGenerator!.TranscribeFromAudioAsync();
        Assert.True(sut.LrcGenerator.IsPreviewState);
        await sut.LrcGenerator.ExecuteSaveAsync(overwrite: true);
        Assert.True(sut.LrcGenerator.HasSavedFile);

        sut.LrcGenerator.BackToLyricsCommand.Execute(null);
        Assert.True(sut.IsLyricsViewVisible);

        // Act 1: 歌詞パネルヘッダーの「LRC を自動生成・再生成」（RequestGenerateLrcCommand → OpenLrcGenerator）で再起動
        Assert.True(sut.Lyrics.RequestGenerateLrcCommand.CanExecute(null));
        sut.Lyrics.RequestGenerateLrcCommand.Execute(null);

        // Assert 1: 入力・待機状態へ復帰し、自動文字起こしが即座に実行可能
        Assert.True(sut.IsLrcGeneratorViewVisible);
        Assert.True(sut.LrcGenerator.IsInputState);
        Assert.False(sut.LrcGenerator.HasSavedFile);
        Assert.True(sut.LrcGenerator.TranscribeAudioCommand.CanExecute(null));

        // Act 2: テキストファイルを選択してテキストからの LRC 生成を実行
        await sut.LrcGenerator.InspectAndSelectTextFileAsync(@"C:\Lyrics\MainRecreate.txt");
        Assert.True(sut.LrcGenerator.GenerateLrcCommand.CanExecute(null));
        await sut.LrcGenerator.GenerateFromTextAsync();

        // Assert 2: テキスト生成が TextAlignment モードで完了
        Assert.True(sut.LrcGenerator.IsPreviewState);
        Assert.Equal(LrcGenerationMode.TextAlignment, engine.LastMode);
    }

    /// <summary>
    /// 既存 .lrc ファイルが存在する楽曲に対して歌詞パネルの RequestGenerateLrcCommand から開いた場合、
    /// LRC 編集画面（IsPreviewState == true）がメイン表示され、「↺ 再生成」操作時のみ入力画面へ遷移してテキスト生成・自動文字起こしを再開できることを検証します。
    /// </summary>
    [Fact]
    public async Task OpenLrcGenerator_既存Lrcが存在する楽曲を歌詞パネルから再起動_編集画面をメイン表示し再生成押下時のみ入力画面へ遷移すること()
    {
        // Arrange
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ae_main_lrc_edit_{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempDir);
        string audioPath = System.IO.Path.Combine(tempDir, "ExistingMain.flac");
        string lrcPath = System.IO.Path.Combine(tempDir, "ExistingMain.lrc");
        System.IO.File.WriteAllText(audioPath, "dummy");
        System.IO.File.WriteAllText(lrcPath, "[00:08.00]既存保存済み歌詞");

        try
        {
            var engine = new StubAlignmentEngineForMain();
            var parser = new StubLrcParserForMain
            {
                ParseHandler = _ => new LyricsDocument(
                    [new LyricLine(0, TimeSpan.FromSeconds(8), "既存保存済み歌詞")],
                    isSynced: true,
                    title: "ExistingMain"),
            };
            var track = new Track { FilePath = audioPath, Title = "ExistingMain", Artist = "Artist" };

            using var scope = new ServiceProviderScope((lyricsSvc, audioSvc, dispatcher) =>
            {
                var genSvc = new LrcGenerationApplicationService(
                    engine,
                    new StubTextEncodingServiceForMain(),
                    new StubModelManagerForMain(),
                    parser,
                    lyricsSvc);
                var settingsMock = new Mock<ISettingsService>();
                settingsMock.Setup(s => s.LoadSettings())
                    .Returns(new AppSettings { HasAcceptedLrcCopyrightGuideline = true });
                return new LrcGeneratorViewModel(
                    genSvc,
                    audioSvc,
                    dispatcher,
                    selectTextFileFunc: null,
                    selectModelFileFunc: null,
                    openFolderAction: _ => { },
                    fileExistsFunc: System.IO.File.Exists,
                    settingsService: settingsMock.Object);
            });
            using var sut = new MainViewModel();
            sut.IsRightPanelOpen = true;
            await sut.Lyrics!.HandleAsync(new TrackChangedEvent(track, TimeSpan.Zero));

            // Act 1: 歌詞パネルヘッダーの「LRC 自動生成・再生成」から既存 .lrc 付き楽曲を開く（OpenLrcGenerator 単体経路を検証）
            sut.Lyrics.RequestGenerateLrcCommand.Execute(null);

            // Assert 1: 既存 .lrc の手動編集画面がメイン表示される
            Assert.True(sut.IsLrcGeneratorViewVisible);
            Assert.True(sut.LrcGenerator!.IsPreviewState);
            Assert.Single(sut.LrcGenerator.PreviewLines);
            Assert.Equal("既存保存済み歌詞", sut.LrcGenerator.PreviewLines[0].Text);

            // Act 2: 「↺ 再生成」クリック時のみ入力・待機画面へ遷移し、テキスト生成・自動文字起こしを再開可能
            sut.LrcGenerator.RegenerateCommand.Execute(null);
            Assert.True(sut.LrcGenerator.IsInputState);
            Assert.True(sut.LrcGenerator.TranscribeAudioCommand.CanExecute(null));

            await sut.LrcGenerator.InspectAndSelectTextFileAsync(@"C:\Lyrics\ExistingMain.txt");
            Assert.True(sut.LrcGenerator.GenerateLrcCommand.CanExecute(null));
            await sut.LrcGenerator.GenerateFromTextAsync();
            Assert.True(sut.LrcGenerator.IsPreviewState);
            Assert.Equal(LrcGenerationMode.TextAlignment, engine.LastMode);
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// プレイリスト詳細表示中にデータ保存先変更で ReloadDataAfterDirectoryChange が実行された際、
    /// 新ディレクトリに存在しない旧プレイリストがクリアされプレイリスト一覧画面へ安全遷移することを検証します。
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task ReloadDataAfterDirectoryChange_プレイリスト詳細表示中に新フォルダへ切替時_不在プレイリストをクリアして一覧へ戻ること()
    {
        // Arrange
        var previousProvider = App.ServiceProvider;
        var services = new ServiceCollection();
        var eventBus = new InMemoryEventBus();
        var dispatcher = new ImmediateDispatcherStub();
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.LoadSettings()).Returns(new AppSettings());
        var audioServiceMock = new Mock<IAudioService>();
        audioServiceMock.Setup(a => a.UserQueue).Returns(Array.Empty<Track>());
        audioServiceMock.Setup(a => a.AlbumQueue).Returns(Array.Empty<Track>());

        var trackRepoMock = new Mock<ITrackRepository>();
        trackRepoMock.Setup(r => r.GetAllAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(Array.Empty<Track>());
        var favoriteRepoMock = new Mock<IFavoriteRepository>();
        favoriteRepoMock.Setup(r => r.GetAllAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(Array.Empty<FavoriteTrackEntry>());
        var recentRepoMock = new Mock<IRecentTrackRepository>();
        recentRepoMock.Setup(r => r.Load()).Returns(Array.Empty<RecentTrackEntry>());

        var oldPlaylist = new UserPlaylist(AudioEffector.Domain.ValueObjects.PlaylistId.From(Guid.NewGuid()), "OldList");
        IReadOnlyList<UserPlaylist> currentPlaylists = [oldPlaylist];
        var playlistRepoMock = new Mock<IPlaylistRepository>();
        playlistRepoMock.Setup(r => r.GetAllAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(() => currentPlaylists);

        var libraryService = new LibraryApplicationService(
            trackRepoMock.Object,
            favoriteRepoMock.Object,
            new TagLibMetadataExtractor(),
            eventBus);
        var playlistService = new PlaylistApplicationService(
            playlistRepoMock.Object,
            trackRepoMock.Object,
            eventBus,
            favoriteRepoMock.Object);
        var lyricsService = new LyricsApplicationService(new EmptyLyricsProviderStub(), eventBus);

        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<IUiDispatcher>(dispatcher);
        services.AddSingleton(settingsMock.Object);
        services.AddSingleton(audioServiceMock.Object);
        services.AddSingleton(Mock.Of<IAudioEngine>());
        services.AddSingleton(recentRepoMock.Object);
        services.AddSingleton(Mock.Of<IAudioTranscodeService>());
        services.AddSingleton(libraryService);
        services.AddSingleton(playlistService);
        services.AddSingleton(lyricsService);
        services.AddSingleton<PlayerControlViewModel>();
        services.AddSingleton<PlaylistViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<AlbumDetailViewModel>();
        services.AddSingleton<AllSongsViewModel>();
        services.AddSingleton<ArtistsViewModel>();
        services.AddSingleton<RecentViewModel>();
        services.AddSingleton<LyricsViewModel>();

        using var provider = services.BuildServiceProvider();
        App.ServiceProvider = provider;
        try
        {
            using var sut = new MainViewModel();
            Assert.NotNull(sut.Playlist);
            Assert.Single(sut.Playlist!.UserPlaylists);

            sut.CurrentViewType = ViewType.PlaylistTracks;
            await sut.Playlist.SelectPlaylistAsync(oldPlaylist);
            Assert.Equal("OldList", sut.Playlist.CurrentPlaylistName);

            // Act: 新ディレクトリは空のプレイリスト一覧へ切替
            currentPlaylists = [];
            sut.ReloadDataAfterDirectoryChange();

            // Assert
            Assert.Empty(sut.Playlist.UserPlaylists);
            Assert.Null(sut.Playlist.SelectedPlaylist);
            Assert.Equal(string.Empty, sut.Playlist.CurrentPlaylistName);
            Assert.Equal(ViewType.Playlists, sut.CurrentViewType);
        }
        finally
        {
            App.ServiceProvider = previousProvider;
        }
    }
}
