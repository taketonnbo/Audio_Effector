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
            services.AddSingleton(recentRepoMock.Object);
            services.AddSingleton(transcodeMock.Object);
            services.AddSingleton(libraryService);
            services.AddSingleton(lyricsService);

            services.AddSingleton<PlayerControlViewModel>();
            services.AddSingleton<LibraryViewModel>();
            services.AddSingleton<AlbumDetailViewModel>();
            services.AddSingleton<AllSongsViewModel>();
            services.AddSingleton<ArtistsViewModel>();
            services.AddSingleton<RecentViewModel>();
            services.AddSingleton<LyricsViewModel>();

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
    /// 最大化モードで歌詞表示ボタンを ON にすると自動コンパクト切替し、OFF にすると元の最大化モードへ自動復帰することを検証します。
    /// </summary>
    [Fact]
    public void ToggleLyricsPanelCommand_最大化モードからONおよびOFF操作_自動コンパクト切替後に元の最大化モードへ復帰すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        sut.IsRightPanelOpen = true;
        Assert.True(sut.IsAlbumViewMaximized);
        Assert.False(sut.IsLyricsPanelVisible);
        Assert.NotNull(sut.Lyrics);
        Assert.False(sut.Lyrics!.IsActiveView);

        // Act 1: 最大化モードから歌詞 ON → 自動コンパクト＋歌詞表示
        sut.ToggleLyricsPanelCommand.Execute(null);

        // Assert 1
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.True(sut.IsLyricsPanelVisible);
        Assert.True(sut.IsLyricsViewVisible);
        Assert.True(sut.Lyrics.IsActiveView);

        // Act 2: 歌詞 OFF → 元の最大化モードへ自動復帰
        sut.ToggleLyricsPanelCommand.Execute(null);

        // Assert 2
        Assert.True(sut.IsAlbumViewMaximized);
        Assert.False(sut.IsLyricsPanelVisible);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.Lyrics.IsActiveView);
    }

    /// <summary>
    /// もともとコンパクトモードの状態で歌詞表示を ON → OFF にした場合、コンパクトモードを維持することを検証します。
    /// </summary>
    [Fact]
    public void ToggleLyricsPanelCommand_コンパクトモードからONおよびOFF操作_コンパクトモードを維持すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        sut.IsRightPanelOpen = true;
        sut.IsAlbumViewMaximized = false;

        // Act 1: 歌詞 ON
        sut.ToggleLyricsPanelCommand.Execute(null);
        Assert.True(sut.IsLyricsPanelVisible);
        Assert.False(sut.IsAlbumViewMaximized);

        // Act 2: 歌詞ヘッダーの閉じる操作（ClosePanelCommand）経由で OFF
        sut.Lyrics!.ClosePanelCommand.Execute(null);

        // Assert
        Assert.False(sut.IsLyricsPanelVisible);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.False(sut.Lyrics.IsActiveView);
    }

    /// <summary>
    /// 歌詞パネル表示中に手動で最大化モードへ切り替えた場合、歌詞表示が自動的に OFF となることを検証します。
    /// </summary>
    [Fact]
    public void ToggleAlbumViewSizeModeCommand_歌詞表示中に手動で最大化した場合_歌詞表示が自動OFFになること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        sut.IsRightPanelOpen = true;
        sut.ToggleLyricsPanelCommand.Execute(null);
        Assert.True(sut.IsLyricsPanelVisible);
        Assert.False(sut.IsAlbumViewMaximized);

        // Act: 手動で最大化モードへ切替
        sut.ToggleAlbumViewSizeModeCommand.Execute(null);

        // Assert
        Assert.True(sut.IsAlbumViewMaximized);
        Assert.False(sut.IsLyricsPanelVisible);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.Lyrics!.IsActiveView);
    }

    /// <summary>
    /// 右側タブ閉鎖時は IsLyricsPanelVisible を維持したまま Lyrics.IsActiveView が false になり、再展開時に true へ復帰することを検証します。
    /// </summary>
    [Fact]
    public void IsRightPanelOpen_歌詞表示中に右側タブを閉鎖および再展開した場合_表示状態維持のままIsActiveViewが連動すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        sut.IsRightPanelOpen = true;
        sut.ToggleLyricsPanelCommand.Execute(null);
        Assert.True(sut.IsLyricsPanelVisible);
        Assert.True(sut.Lyrics!.IsActiveView);

        // Act 1: 右側タブ閉鎖
        sut.IsRightPanelOpen = false;

        // Assert 1
        Assert.True(sut.IsLyricsPanelVisible);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.Lyrics.IsActiveView);

        // Act 2: 右側タブ再展開
        sut.IsRightPanelOpen = true;

        // Assert 2
        Assert.True(sut.IsLyricsPanelVisible);
        Assert.True(sut.IsLyricsViewVisible);
        Assert.True(sut.Lyrics.IsActiveView);
    }

    /// <summary>
    /// 最大化モードかつ右側タブ閉鎖状態から OpenLrcGenerator を呼ぶと右側タブ自動展開・自動コンパクト切替され、
    /// LRC 生成パネル閉鎖時に元の最大化モードへ自動復帰することを検証します。
    /// </summary>
    [Fact]
    public void OpenLrcGenerator_最大化かつ右側タブ閉鎖状態から展開し閉鎖_自動コンパクト後に元の最大化モードへ復帰すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        var track = new Track { FilePath = @"C:\Music\GeneratorTarget.flac", Title = "GeneratorTarget" };
        sut.IsRightPanelOpen = false;
        Assert.True(sut.IsAlbumViewMaximized);

        // Act 1: LRC 生成パネル展開
        sut.OpenLrcGenerator(track);

        // Assert 1: 右側タブ自動展開・自動コンパクト切替・排他表示・Lyrics.IsActiveView は false
        Assert.True(sut.IsRightPanelOpen);
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.Equal(RightExtensionPanelType.LrcGenerator, sut.ActiveRightExtensionPanel);
        Assert.True(sut.IsLrcGeneratorPanelVisible);
        Assert.True(sut.IsLrcGeneratorViewVisible);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.Lyrics!.IsActiveView);
        Assert.Same(track, sut.LrcGenerator!.TargetTrack);

        // Act 2: LRC 生成パネルを閉じる
        sut.LrcGenerator.CloseGeneratorCommand.Execute(null);

        // Assert 2: 元の最大化モードへ自動復帰
        Assert.Equal(RightExtensionPanelType.None, sut.ActiveRightExtensionPanel);
        Assert.False(sut.IsLrcGeneratorViewVisible);
        Assert.True(sut.IsAlbumViewMaximized);
    }

    /// <summary>
    /// 最大化モードから OpenLrcGenerator で自動コンパクト切替した後、右上 [♪] ボタンで歌詞表示へ切り替えてから閉鎖しても
    /// 最大化復帰フラグが維持され、元の最大化モードへ自動復帰することを検証します。
    /// </summary>
    [Fact]
    public void OpenLrcGenerator_最大化から展開後に右上歌詞トグル経由で切替および閉鎖_元の最大化モードへ自動復帰すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        var track = new Track { FilePath = @"C:\Music\ToggleKeepMax.flac", Title = "ToggleKeepMax" };
        sut.IsRightPanelOpen = true;
        Assert.True(sut.IsAlbumViewMaximized);

        // Act 1: 最大化モードから LRC 生成パネル展開
        sut.OpenLrcGenerator(track);
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.True(sut.IsLrcGeneratorViewVisible);
        Assert.False(sut.Lyrics!.IsActiveView);

        // Act 2: 右上 [♪] ボタンで歌詞表示へ切替
        sut.ToggleLyricsPanelCommand.Execute(null);
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.True(sut.IsLyricsViewVisible);
        Assert.False(sut.IsLrcGeneratorViewVisible);
        Assert.True(sut.Lyrics.IsActiveView);

        // Act 3: 右上 [♪] ボタンで歌詞表示を閉鎖
        sut.ToggleLyricsPanelCommand.Execute(null);

        // Assert 3: 元の最大化モードへ自動復帰すること
        Assert.Equal(RightExtensionPanelType.None, sut.ActiveRightExtensionPanel);
        Assert.False(sut.IsLyricsViewVisible);
        Assert.False(sut.Lyrics.IsActiveView);
        Assert.True(sut.IsAlbumViewMaximized);
    }

    /// <summary>
    /// もともとコンパクトモードから OpenLrcGenerator → [← 歌詞へ戻る] → 閉鎖とした場合、コンパクトモードを維持することを検証します。
    /// </summary>
    [Fact]
    public void OpenLrcGenerator_コンパクトモードから展開し歌詞へ戻ってから閉鎖_コンパクトモードを維持すること()
    {
        // Arrange
        using var scope = new ServiceProviderScope();
        using var sut = new MainViewModel();
        sut.IsRightPanelOpen = true;
        sut.IsAlbumViewMaximized = false;

        // Act 1: コンパクトモードから LRC 生成展開
        sut.OpenLrcGenerator(new Track { FilePath = @"C:\Music\Compact.flac", Title = "Compact" });
        Assert.True(sut.IsLrcGeneratorViewVisible);

        // Act 2: [← 歌詞へ戻る] で歌詞表示へ切替
        sut.LrcGenerator!.BackToLyricsCommand.Execute(null);
        Assert.True(sut.IsLyricsViewVisible);
        Assert.True(sut.Lyrics!.IsActiveView);

        // Act 3: 歌詞パネル閉鎖
        sut.Lyrics.ClosePanelCommand.Execute(null);

        // Assert 3: もともとコンパクトだったのでコンパクト維持
        Assert.Equal(RightExtensionPanelType.None, sut.ActiveRightExtensionPanel);
        Assert.False(sut.IsAlbumViewMaximized);
        Assert.False(sut.Lyrics.IsActiveView);
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
