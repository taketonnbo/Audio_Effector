using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AudioEffector.Application.ApplicationServices;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.Repositories;
using AudioEffector.Infrastructure.Library;
using AudioEffector.Presentation.ViewModels;
using AudioEffector.Presentation.Views;
using AudioEffector.Tests.Presentation.Diagnostics;
using Moq;
using Xunit;

namespace AudioEffector.Tests.Presentation.ViewModels;

/// <summary>
/// <see cref="FolderViewModel"/> のフォルダ選択、新規フォルダ指定スキャン、指定済みフォルダ再スキャン、
/// およびスキャン進捗・ステータス同期を検証するテストクラス
/// </summary>
public sealed class FolderViewModelTests : IDisposable
{
    private readonly Mock<ISettingsService> _settingsServiceMock = new();
    private readonly Mock<ITrackRepository> _trackRepoMock = new();
    private readonly Mock<IFavoriteRepository> _favoriteRepoMock = new();
    private readonly Mock<IAudioService> _audioServiceMock = new();
    private readonly InMemoryEventBus _eventBus = new();
    private readonly LibraryApplicationService _libraryAppService;
    private readonly AppSettings _settings;
    private readonly string _tempDir;

    public FolderViewModelTests()
    {
        _settings = new AppSettings();
        _settingsServiceMock.Setup(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()))
            .Callback<Action<AppSettings>>(update => update(_settings));
        _settingsServiceMock.Setup(s => s.LoadSettings()).Returns(_settings);

        _libraryAppService = new LibraryApplicationService(
            _trackRepoMock.Object,
            _favoriteRepoMock.Object,
            new TagLibMetadataExtractor(),
            _eventBus);

        _tempDir = Path.Combine(Path.GetTempPath(), "AudioEffectorTests_FolderVM", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // クリーンアップ失敗は無視
        }
    }

    /// <summary>
    /// コンストラクタ初期化時に、設定サービスの LastLibraryPath が CurrentFolderPath および DisplayFolderPath に反映されることを検証します
    /// </summary>
    [Fact]
    public void コンストラクタ_LastLibraryPathが設定済みの場合_CurrentFolderPathとDisplayFolderPathに反映されること()
    {
        // Arrange
        _settings.LastLibraryPath = _tempDir;

        // Act
        using var sut = new FolderViewModel(_settingsServiceMock.Object, _ => { });

        // Assert
        Assert.Equal(_tempDir, sut.CurrentFolderPath);
        Assert.Equal(_tempDir, sut.DisplayFolderPath);
        Assert.True(sut.HasCurrentFolderPath);
    }

    /// <summary>
    /// LastLibraryPath が未設定の初期状態では HasCurrentFolderPath が false となり、DisplayFolderPath に未設定案内文言が表示されることを検証します
    /// </summary>
    [Fact]
    public void コンストラクタ_LastLibraryPathが未設定の場合_HasCurrentFolderPathがfalseとなり案内文言が表示されること()
    {
        // Arrange
        _settings.LastLibraryPath = null;

        // Act
        using var sut = new FolderViewModel(_settingsServiceMock.Object, _ => { });

        // Assert
        Assert.Equal(string.Empty, sut.CurrentFolderPath);
        Assert.False(sut.HasCurrentFolderPath);
        Assert.Contains("未設定", sut.DisplayFolderPath);
        Assert.False(sut.RescanCurrentFolderCommand.CanExecute(null));
    }

    /// <summary>
    /// SelectFolderCommand（SELECT FOLDER）実行時、選択されたパスが設定へ保存され CurrentFolderPath が更新されるが、スキャン自体は実行されないことを検証します
    /// </summary>
    [Fact]
    public void SelectFolderCommand_フォルダが選択された場合_設定保存とCurrentFolderPath更新のみ行われスキャンは実行されないこと()
    {
        // Arrange
        bool scanCalled = false;
        using var sut = new FolderViewModel(
            _settingsServiceMock.Object,
            _ => scanCalled = true,
            _ => _tempDir);

        // Act
        sut.SelectFolderCommand.Execute(null);

        // Assert
        Assert.False(scanCalled);
        Assert.Equal(_tempDir, sut.CurrentFolderPath);
        Assert.Equal(_tempDir, _settings.LastLibraryPath);
        _settingsServiceMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Once);
        Assert.False(string.IsNullOrWhiteSpace(sut.ScanStatusText));
    }

    /// <summary>
    /// フォルダ選択ダイアログがキャンセルされた場合、設定保存もスキャンも行われないことを検証します
    /// </summary>
    [Fact]
    public void SelectFolderCommandおよびScanNewFolderCommand_ダイアログがキャンセルされた場合_設定変更もスキャンも行われないこと()
    {
        // Arrange
        _settings.LastLibraryPath = @"C:\Initial\Music";
        bool scanCalled = false;
        using var sut = new FolderViewModel(
            _settingsServiceMock.Object,
            _ => scanCalled = true,
            _ => null);

        // Act
        sut.SelectFolderCommand.Execute(null);
        sut.ScanNewFolderCommand.Execute(null);

        // Assert
        Assert.False(scanCalled);
        Assert.Equal(@"C:\Initial\Music", sut.CurrentFolderPath);
        _settingsServiceMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Never);
    }

    /// <summary>
    /// ScanNewFolderCommand（「フォルダを指定してスキャン」）実行時、選択されたパスが永続化され即時スキャンが実行されることを検証します
    /// </summary>
    [Fact]
    public void ScanNewFolderCommand_新しいフォルダが選択された場合_設定保存とCurrentFolderPath更新およびスキャン実行が行われること()
    {
        // Arrange
        string? scannedPath = null;
        using var sut = new FolderViewModel(
            _settingsServiceMock.Object,
            path => scannedPath = path,
            _ => _tempDir);

        // Act
        sut.ScanNewFolderCommand.Execute(null);

        // Assert
        Assert.Equal(_tempDir, sut.CurrentFolderPath);
        Assert.Equal(_tempDir, _settings.LastLibraryPath);
        Assert.Equal(_tempDir, scannedPath);
        _settingsServiceMock.Verify(s => s.UpdateSettings(It.IsAny<Action<AppSettings>>()), Times.Once);
    }

    /// <summary>
    /// RescanCurrentFolderCommand（「指定済みのフォルダをスキャン」）実行時、現在設定されているフォルダパスで再スキャンが実行されることを検証します
    /// </summary>
    [Fact]
    public void RescanCurrentFolderCommand_有効な指定済みフォルダが存在する場合_現在のパスで再スキャンが実行されること()
    {
        // Arrange
        _settings.LastLibraryPath = _tempDir;
        string? scannedPath = null;
        using var sut = new FolderViewModel(
            _settingsServiceMock.Object,
            path => scannedPath = path);

        // Act
        bool canExecute = sut.RescanCurrentFolderCommand.CanExecute(null);
        sut.RescanCurrentFolderCommand.Execute(null);

        // Assert
        Assert.True(canExecute);
        Assert.Equal(_tempDir, scannedPath);
    }

    /// <summary>
    /// 指定済みフォルダがディスク上に存在しない場合、スキャンを実行せず警告ステータスメッセージを設定して安全に中断することを検証します
    /// </summary>
    [Fact]
    public void RescanCurrentFolderCommand_指定済みフォルダがディスク上に存在しない場合_スキャンを実行せず警告メッセージを表示すること()
    {
        // Arrange
        string nonExistentPath = Path.Combine(_tempDir, "DeletedSubFolder");
        _settings.LastLibraryPath = nonExistentPath;
        bool scanCalled = false;
        using var sut = new FolderViewModel(
            _settingsServiceMock.Object,
            _ => scanCalled = true);

        // Act
        sut.RescanCurrentFolderCommand.Execute(null);

        // Assert
        Assert.False(scanCalled);
        Assert.Contains("見つかりません", sut.ScanStatusText);
    }

    /// <summary>
    /// LibraryViewModel と連携した際、スキャン実行で進捗と完了ステータスが FolderViewModel に同期されることを検証します
    /// </summary>
    [Fact]
    public async Task LibraryViewModel連携_スキャン完了時_進捗率と同期完了ステータステキストが更新されること()
    {
        // Arrange
        string file1 = Path.Combine(_tempDir, "song1.mp3");
        File.WriteAllText(file1, "dummy");
        _settings.LastLibraryPath = _tempDir;

        using var libraryVm = new LibraryViewModel(
            _libraryAppService,
            _audioServiceMock.Object,
            _settingsServiceMock.Object,
            playlistViewModel: null,
            trackMetadataLoader: path => new Track
            {
                FilePath = path,
                Title = "Song 1",
                Artist = "Artist 1",
                Album = "Album 1"
            });

        using var sut = new FolderViewModel(_settingsServiceMock.Object, libraryVm);

        // Act
        await libraryVm.LoadLibraryAsync(_tempDir);

        // Assert
        Assert.False(sut.IsScanning);
        Assert.Equal(1.0, sut.ScanProgress);
        Assert.Equal(100, sut.ScanProgressPercent);
        Assert.Contains("同期完了", sut.ScanStatusText);
        Assert.Contains("+1", sut.ScanStatusText);
    }

    /// <summary>
    /// FolderView に FolderViewModel をバインドして描画した際、3つの操作ボタンとパス表示が正しく構成され、
    /// XAML データバインディングエラーが1件も発生しないことを検証します
    /// </summary>
    [Fact]
    public void FolderView描画_FolderViewModelバインド時_3ボタンとパス表示が構成されバインディングエラーが発生しないこと()
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

                // Arrange
                using var listener = new BindingErrorTraceListener();
                _settings.LastLibraryPath = _tempDir;
                using var sut = new FolderViewModel(_settingsServiceMock.Object, _ => { });

                var view = new FolderView
                {
                    DataContext = sut,
                    Width = 800,
                    Height = 450
                };

                // Act
                view.Measure(new Size(800, 450));
                view.Arrange(new Rect(0, 0, 800, 450));
                view.UpdateLayout();

                var selectButton = Assert.IsType<Button>(view.FindName("SelectFolderButton"));
                var scanNewButton = Assert.IsType<Button>(view.FindName("ScanNewFolderButton"));
                var rescanButton = Assert.IsType<Button>(view.FindName("RescanCurrentFolderButton"));
                var pathTextBlock = Assert.IsType<TextBlock>(view.FindName("CurrentFolderPathTextBlock"));

                // Assert
                Assert.Equal("SELECT FOLDER", selectButton.Content);
                Assert.Equal("フォルダを指定してスキャン", scanNewButton.Content);
                Assert.Equal("指定済みのフォルダをスキャン", rescanButton.Content);
                Assert.Equal(_tempDir, pathTextBlock.Text);
                Assert.True(rescanButton.IsEnabled);
                Assert.False(listener.HasErrors, $"バインディングエラーが発生しました: {string.Join("; ", listener.Errors)}");
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
}
