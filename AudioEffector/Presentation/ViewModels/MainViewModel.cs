using AudioEffector.Application.ApplicationServices;
using AudioEffector.Domain.Entities;
using AudioEffector.Infrastructure.Logging;
using AudioEffector.Presentation.Views;
using AudioEffector.Application.Common;
using AudioEffector.Infrastructure.Audio;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaDevices;
using System.Threading.Tasks;
using NLog;

namespace AudioEffector.Presentation.ViewModels
{
    /// <summary>
    /// アプリケーションのメインViewModel。
    /// UIのロジック、データバインディング、およびサービス間の連携を担当します。
    /// </summary>
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly string[] SupportedAudioExtensions = [".mp3", ".wav", ".aiff", ".wma", ".m4a", ".mp4", ".flac", ".aac", ".ogg", ".opus", ".alac"];
        private static readonly string[] LosslessAudioExtensions = [".flac", ".wav", ".aiff", ".alac"];
        private readonly IAudioService _audioService;
        private readonly AudioService? _fallbackAudioService;
        private readonly LibraryApplicationService? _libraryService;
        private readonly ISettingsService _settingsService;
        private readonly SettingsApplicationService? _fallbackSettings;
        private bool _disposed;

        /// <summary>
        /// 設定が更新された際に発生するイベント
        /// </summary>
        public event Action? SettingsUpdated;

        /// <summary>
        /// 再生コントロール専用ViewModel
        /// </summary>
        public Presentation.ViewModels.PlayerControlViewModel? PlayerControl { get; }

        /// <summary>
        /// ライブラリ専用ViewModel
        /// </summary>
        public Presentation.ViewModels.LibraryViewModel? Library { get; }

        /// <summary>
        /// プレイリスト専用ViewModel
        /// </summary>
        public Presentation.ViewModels.PlaylistViewModel? Playlist { get; }

        /// <summary>
        /// イコライザー専用ViewModel
        /// </summary>
        public Presentation.ViewModels.EqualizerViewModel? Equalizer { get; }

        /// <summary>
        /// デバイス同期専用ViewModel
        /// </summary>
        public Presentation.ViewModels.DeviceSyncViewModel? DeviceSync { get; }

        /// <summary>
        /// 再生中情報専用ViewModel
        /// </summary>
        public Presentation.ViewModels.NowPlayingViewModel? NowPlaying { get; }

        /// <summary>
        /// フォルダーブラウズ専用ViewModel
        /// </summary>
        public FolderViewModel Folder { get; }

        /// <summary>
        /// デバイスブラウズ・転送専用ViewModel
        /// </summary>
        public DeviceBrowserViewModel DeviceBrowser { get; }

        /// <summary>
        /// コードビハインドからAudioServiceへアクセスするためのプロパティ
        /// </summary>
        public IAudioService AudioService => _audioService; // Public accessor for code-behind

        /// <summary>
        /// コードビハインドからSettingsServiceへアクセスするためのプロパティ
        /// </summary>
        public ISettingsService SettingsService => _settingsService;

        private Track? _currentTrack;
        private bool _isPlaying;
        private string _currentTimeDisplay = "00:00";
        private BitmapImage? _nowPlayingImage;
        private string _totalTimeDisplay = "00:00";
        private double _progress;
        private DispatcherTimer _timer;
        private bool _isNowPlayingVisible = true;
        private bool _isLoading;
        private bool _isGridView = true;
        private string _selectedSortOption = "Artist";
        private List<string> _favoritePaths;
        private ViewType _currentViewType = ViewType.Albums;

        /// <summary>
        /// 現在表示されているメインコンテンツのビュー種別を取得または設定します
        /// </summary>
        public ViewType CurrentViewType
        {
            get => _currentViewType;
            set
            {
                if (_currentViewType != value)
                {
                    _currentViewType = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsLibraryVisible));
                    OnPropertyChanged(nameof(IsFolderViewVisible));
                    OnPropertyChanged(nameof(IsPlaylistSelectorVisible));
                    OnPropertyChanged(nameof(IsPlaylistTracksVisible));
                    OnPropertyChanged(nameof(IsPlaylistSectionActive));
                    OnPropertyChanged(nameof(IsDeviceSyncVisible));
                    OnPropertyChanged(nameof(IsFavoritesView));

                    if (_currentViewType == ViewType.DeviceSync)
                    {
                        if (PlayerControl != null)
                        {
                            PlayerControl.IsSpectrumVisible = false;
                        }
                        DeviceBrowser.RefreshDrives();
                    }
                }
            }
        }

        /// <summary>
        /// 表示ビューを切り替えるコマンドを取得します
        /// </summary>
        public ICommand SwitchViewCommand { get; }

        private bool _isRightPanelOpen;
        /// <summary>
        /// 右側タブパネルが開いているかどうかを示すプロパティ
        /// Falseの場合は画面上部にコンパクトプレイヤーが表示されます
        /// </summary>
        public bool IsRightPanelOpen
        {
            get => _isRightPanelOpen;
            set
            {
                _isRightPanelOpen = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 右側タブパネルの開閉状態を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleRightPanelCommand { get; }

        private bool _isPlayQueuePanelOpen;
        /// <summary>
        /// 再生キュースライドパネルが開いているかどうかを示すプロパティ
        /// </summary>
        public bool IsPlayQueuePanelOpen
        {
            get => _isPlayQueuePanelOpen;
            set
            {
                _isPlayQueuePanelOpen = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 再生キュースライドパネルの開閉状態を切り替えるコマンドを取得します
        /// </summary>
        public ICommand TogglePlayQueuePanelCommand { get; }

        /// <summary>
        /// 再生キュースライドパネルを閉じるコマンドを取得します
        /// </summary>
        public ICommand ClosePlayQueuePanelCommand { get; }

        /// <summary>
        /// 再生キューからトラックを削除するコマンドを取得します
        /// </summary>
        public ICommand RemoveFromQueueCommand { get; }

        /// <summary>
        /// 再生キューを全クリアするコマンドを取得します
        /// </summary>
        public ICommand ClearQueueCommand { get; }


        private bool _isAlbumViewMaximized = true;
        /// <summary>
        /// 右側パネルのアルバム情報表示が最大化（全画面）モードかどうかを示すプロパティ
        /// Trueの場合は特大アートとフルハイトトラックリスト、Falseの場合は上部にコンパクトプレイヤーと下部に拡張領域を表示します
        /// </summary>
        public bool IsAlbumViewMaximized
        {
            get => _isAlbumViewMaximized;
            set
            {
                if (_isAlbumViewMaximized != value)
                {
                    _isAlbumViewMaximized = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 右側アルバム表示のサイズモード（最大化 / コンパクト）を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleAlbumViewSizeModeCommand { get; }

        private bool _isLibraryVisible = true;
        private bool _isFolderViewVisible;
        private Dictionary<string, BitmapImage> _albumArtCache = new Dictionary<string, BitmapImage>();

        private string _playbackListName = "No Album Selected";

        /// <summary>
        /// 再生リストの名称（アルバム名やプレイリスト名）。
        /// </summary>
        public string PlaybackListName
        {
            get => PlayerControl?.PlaybackListName ?? _playbackListName;
            set
            {
                _playbackListName = value;
                if (PlayerControl != null)
                {
                    PlayerControl.PlaybackListName = value;
                }
                OnPropertyChanged();
            }
        }

        private string _playbackListSubtitle = "";

        /// <summary>
        /// 再生リストのサブタイトル（アーティスト名など）。
        /// </summary>
        public string PlaybackListSubtitle
        {
            get => PlayerControl?.PlaybackListSubtitle ?? _playbackListSubtitle;
            set
            {
                _playbackListSubtitle = value;
                if (PlayerControl != null)
                {
                    PlayerControl.PlaybackListSubtitle = value;
                }
                OnPropertyChanged();
            }
        }

        private ObservableCollection<Track> _playbackListTracks = new ObservableCollection<Track>();

        /// <summary>
        /// 現在画面の右ペインに表示されているトラックのコレクション（アルバム・プレイリスト）。
        /// </summary>
        public ObservableCollection<Track> PlaybackListTracks
        {
            get => PlayerControl?.PlaybackListTracks ?? _playbackListTracks;
            set
            {
                _playbackListTracks = value;
                if (PlayerControl != null)
                {
                    PlayerControl.PlaybackListTracks = value;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlaybackListTracksCountText));
            }
        }

        /// <summary>
        /// トラックリストの総曲数表示文字列（例: "Tracklist (9 tracks)"）。
        /// </summary>
        public string PlaybackListTracksCountText => PlayerControl?.PlaybackListTracksCountText ?? ((PlaybackListTracks?.Count ?? 0) == 1 ? "Tracklist (1 track)" : $"Tracklist ({PlaybackListTracks?.Count ?? 0} tracks)");

        private ObservableCollection<Track> _playQueue = new ObservableCollection<Track>();

        /// <summary>
        /// 実際の再生予定キュー。
        /// </summary>
        public ObservableCollection<Track> PlayQueue
        {
            get => PlayerControl?.PlayQueue ?? _playQueue;
            set
            {
                _playQueue = value;
                if (PlayerControl != null)
                {
                    PlayerControl.PlayQueue = value;
                }
                OnPropertyChanged();
            }
        }

        private BitmapImage? _defaultSpectrumImage;
        private BitmapImage? _favoritesImage;
        private BitmapImage? _defaultNowPlayingImage;

        private ImageSource? _spectrumBackgroundImage;

        /// <summary>
        /// スペクトラムアナライザーの背景画像。
        /// </summary>
        public ImageSource? SpectrumBackgroundImage
        {
            get => _spectrumBackgroundImage;
            set
            {
                _spectrumBackgroundImage = value;
                OnPropertyChanged();
            }
        }

        // ... (existing code) ...

        /// <summary>
        /// ViewModelの初期化を行い、各サービスのインスタンス生成、データの読み込み、コマンドの初期化を行います。
        /// </summary>
        public MainViewModel()
        {
            if (App.ServiceProvider != null)
            {
                PlayerControl = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.PlayerControlViewModel>(App.ServiceProvider);
                Library = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.LibraryViewModel>(App.ServiceProvider);
                Playlist = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.PlaylistViewModel>(App.ServiceProvider);
                Equalizer = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.EqualizerViewModel>(App.ServiceProvider);
                DeviceSync = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.DeviceSyncViewModel>(App.ServiceProvider);
                NowPlaying = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Presentation.ViewModels.NowPlayingViewModel>(App.ServiceProvider);
            }

            if (App.ServiceProvider != null)
            {
                var resolved = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IAudioService>(App.ServiceProvider);
                if (resolved != null)
                {
                    _audioService = resolved;
                }
                else
                {
                    _fallbackAudioService = new AudioService();
                    _audioService = LoggingProxy<IAudioService>.Create(_fallbackAudioService);
                }
            }
            else
            {
                _fallbackAudioService = new AudioService();
                _audioService = LoggingProxy<IAudioService>.Create(_fallbackAudioService);
            }
            _libraryService = App.ServiceProvider != null ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<LibraryApplicationService>(App.ServiceProvider) : null;
            if (Playlist != null)
            {
                Playlist.ViewRequested += viewType =>
                {
                    CurrentViewType = viewType;
                    if (viewType == ViewType.PlaylistTracks)
                    {
                        Playlist.SetBackgroundImage(NowPlayingImage ?? _defaultNowPlayingImage);
                    }
                };
                Playlist.PlaybackRequested += (tracks, name, subtitle) =>
                {
                    PlayQueue = new ObservableCollection<Track>(tracks);
                    PlaybackListName = name;
                    PlaybackListSubtitle = subtitle;
                    PlaybackListTracks = new ObservableCollection<Track>(tracks);
                    PlayerControl?.SetPlaybackList(tracks, name, subtitle);
                };
                Playlist.FavoriteRemovalRequested += track =>
                {
                    if (track.IsFavorite)
                    {
                        ToggleFavorite(track);
                    }
                };
                Playlist.LoadPlaylists();
            }
            if (App.ServiceProvider != null)
            {
                var resolvedSettings = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<ISettingsService>(App.ServiceProvider);
                if (resolvedSettings != null)
                {
                    _settingsService = resolvedSettings;
                }
                else
                {
                    _fallbackSettings = new SettingsApplicationService();
                    _settingsService = LoggingProxy<ISettingsService>.Create(_fallbackSettings);
                }
            }
            else
            {
                _fallbackSettings = new SettingsApplicationService();
                _settingsService = LoggingProxy<ISettingsService>.Create(_fallbackSettings);
            }
            _favoritePaths = _libraryService?.LoadFavorites() ?? new List<string>();

            if (PlayerControl == null)
            {
#pragma warning disable CA2000 // オブジェクトの破棄
                PlayerControl = new Presentation.ViewModels.PlayerControlViewModel(
                    _audioService,
                    new Infrastructure.Audio.NAudioPlaybackEngine(),
                    new Application.Common.InMemoryEventBus(),
                    _settingsService);
#pragma warning restore CA2000 // オブジェクトの破棄
            }

            if (PlayerControl != null)
            {
                PlayerControl.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != null)
                    {
                        OnPropertyChanged(e.PropertyName);
                    }
                };
                PlayerControl.TrackPlayHandler = track =>
                {
                    if (IsPlaylistTracksVisible && Playlist is { PlaylistTracks.Count: > 0 } playlistViewModel && playlistViewModel.PlaylistTracks.Contains(track))
                    {
                        PlayerControl.SetPlaybackList(playlistViewModel.PlaylistTracks, playlistViewModel.CurrentPlaylistName, playlistViewModel.IsFavoritesView ? "Selected You" : "Playlist");
                        return false;
                    }
                    var album = Albums.FirstOrDefault(a => a.Tracks.Contains(track));
                    if (album != null)
                    {
                        PlayerControl.SetPlaybackList(album.Tracks, album.Title, album.Artist);
                        return false;
                    }
                    return false;
                };
                PlayerControl.FavoriteToggleRequested = track => ToggleFavorite(track);
                PlayerControl.TrackChanged += track =>
                {
                    if (track != null)
                    {
                        CurrentAlbum = Albums.FirstOrDefault(a => a.Tracks.Contains(track));
                    }
                    else
                    {
                        CurrentAlbum = null;
                    }
                };
            }

            if (Library == null)
            {
#pragma warning disable CA2000 // オブジェクトの破棄
                Library = new Presentation.ViewModels.LibraryViewModel(
                    _libraryService ?? new LibraryApplicationService(
                        new Infrastructure.Repository.JsonTrackRepository(),
                        new Infrastructure.Repository.JsonFavoriteRepository(),
                        new Infrastructure.Library.TagLibMetadataExtractor(),
                        new InMemoryEventBus()),
                    _audioService,
                    _settingsService,
                    Playlist);
#pragma warning restore CA2000 // オブジェクトの破棄
            }

            if (Library != null)
            {
                Library.PlaybackRequested += (tracks, startTrack, name, subtitle) =>
                {
                    PlayQueue = new ObservableCollection<Track>(tracks);
                    PlaybackListName = name;
                    PlaybackListSubtitle = subtitle;
                    PlaybackListTracks = new ObservableCollection<Track>(tracks);
                    PlayerControl?.SetPlaybackList(tracks, name, subtitle);
                };
                Library.EnqueueRequested += (tracks, playNext) =>
                {
                    _audioService.EnqueueTracks(tracks, playNext);
                };
                Library.FavoriteToggled += track =>
                {
                    if (track == CurrentTrack)
                    {
                        OnPropertyChanged(nameof(CurrentTrack));
                    }
                    if (track.IsFavorite)
                    {
                        if (!_favoritePaths.Contains(track.FilePath))
                        {
                            _favoritePaths.Add(track.FilePath);
                            if (IsFavoritesView)
                            {
                                Playlist?.AddFavoriteTrack(track);
                            }
                        }
                    }
                    else
                    {
                        if (_favoritePaths.Contains(track.FilePath))
                        {
                            _favoritePaths.Remove(track.FilePath);
                            if (IsFavoritesView)
                            {
                                Playlist?.RemoveDisplayedTrack(track);
                            }
                        }
                    }
                };
                Library.TrackRemoved += track =>
                {
                    Playlist?.RemoveDisplayedTrack(track);
                    PlaybackListTracks.Remove(track);
                };
                Library.AlbumRemoved += album =>
                {
                    foreach (var track in album.Tracks)
                    {
                        Playlist?.RemoveDisplayedTrack(track);
                        PlaybackListTracks.Remove(track);
                    }
                };
                Library.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(Library.IsLoading))
                    {
                        OnPropertyChanged(nameof(IsLoading));
                    }
                };
            }

            var appSettings = _settingsService.LoadSettings();
            _audioService.UpdateAudioProperties(appSettings.SampleRate, appSettings.AudioBufferSizeMs);

            _audioService.TrackChanged += OnTrackChanged;
            _audioService.PlaybackStateChanged += OnPlaybackStateChanged;
            _audioService.PlaylistChanged += OnPlaylistChanged;
            _audioService.VolumeChanged += OnVolumeChanged;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += OnTimerTick;
            _timer.Start();

            LoadDefaultImages();
            NowPlayingImage = _defaultNowPlayingImage;
            SpectrumBackgroundImage = _defaultSpectrumImage;

            Folder = new FolderViewModel(_settingsService, path => LoadLibrary(path));
            // 再生制御系コマンド（PlayerControlViewModelへ完全委譲）
            TogglePlayPauseCommand = PlayerControl!.TogglePlayPauseCommand;
            StopCommand = PlayerControl!.StopCommand;
            NextCommand = PlayerControl!.NextCommand;
            PreviousCommand = PlayerControl!.PreviousCommand;
            PlayTrackCommand = PlayerControl!.PlayTrackCommand;
            PlayNextCommand = PlayerControl!.PlayNextCommand;
            EnqueueTrackCommand = PlayerControl!.EnqueueTrackCommand;
            PlayFromQueueCommand = PlayerControl!.PlayFromQueueCommand;
            ShowQueueDialogCommand = PlayerControl!.ShowQueueDialogCommand;
            RemoveFromQueueCommand = PlayerControl!.RemoveFromQueueCommand;
            ClearQueueCommand = PlayerControl!.ClearQueueCommand;
            ToggleShuffleCommand = PlayerControl!.ToggleShuffleCommand;
            ToggleRepeatCommand = PlayerControl!.ToggleRepeatCommand;
            IncreaseVolumeCommand = PlayerControl!.IncreaseVolumeCommand;
            DecreaseVolumeCommand = PlayerControl!.DecreaseVolumeCommand;
            ToggleMuteCommand = PlayerControl!.ToggleMuteCommand;

            // ライブラリ・アルバム系コマンド（LibraryViewModelへ完全委譲）
            PlayAlbumCommand = Library!.PlayAlbumCommand;
            PlayNextAlbumCommand = Library!.PlayNextAlbumCommand;
            EnqueueAlbumCommand = Library!.EnqueueAlbumCommand;
            DeleteAlbumCommand = Library!.DeleteAlbumCommand;
            DeleteTrackCommand = Library!.DeleteTrackCommand;
            ShowTrackPropertiesCommand = Library!.ShowTrackPropertiesCommand;
            OpenFileLocationCommand = Library!.OpenFileLocationCommand;
            ToggleViewCommand = Library!.ToggleViewCommand;
            ToggleSortDirectionCommand = Library!.ToggleSortDirectionCommand;
            ToggleFavoriteCommand = Library!.ToggleFavoriteCommand;

            // ナビゲーション・表示切り替えコマンド
            ShowFavoritesCommand = new RelayCommand(o => ShowFavorites());
            SwitchViewCommand = new RelayCommand(param =>
            {
                if (param is ViewType viewType)
                {
                    CurrentViewType = viewType;
                    if (viewType == ViewType.Favorites)
                    {
                        ShowFavorites();
                    }
                }
            });
            ShowLibraryCommand = new RelayCommand(o => ShowLibrary());
            ShowFolderCommand = new RelayCommand(o => ShowFolder());
            ToggleSelectionModeCommand = new RelayCommand(o => IsSelectionMode = !IsSelectionMode);

            // Device Sync Command Initialization
            SwitchToDeviceSyncCommand = new RelayCommand(o => CurrentViewType = ViewType.DeviceSync);

            DeviceBrowser = new DeviceBrowserViewModel(
                Albums,
                () => Albums.ToList().AsReadOnly(),
                () => { });
            ShowSettingsCommand = new RelayCommand(o => ShowSettings());
            ToggleRightPanelCommand = new RelayCommand(o => IsRightPanelOpen = !IsRightPanelOpen);
            TogglePlayQueuePanelCommand = new RelayCommand(o => IsPlayQueuePanelOpen = !IsPlayQueuePanelOpen);
            ClosePlayQueuePanelCommand = new RelayCommand(o => IsPlayQueuePanelOpen = false);
            ToggleAlbumViewSizeModeCommand = new RelayCommand(o => IsAlbumViewMaximized = !IsAlbumViewMaximized);

            _audioService.PlaylistEnded += OnPlaylistEnded;

            var settings = _settingsService.LoadSettings();
            if (settings.LeftColumnWidth > 0)
            {
                LeftColumnWidth = new GridLength(settings.LeftColumnWidth);
            }
            // Load Volume
            Volume = settings.Volume;

            LoadLibrary();
        }

        private void OnVolumeChanged(float newVolume)
        {
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(VolumePercent));
        }

        private void LoadDefaultImages()
        {
            try
            {
                // Load Spectrum Default
                var uriSpectrum = new Uri("pack://application:,,,/Assets/Images/default_spectrum_bg.png");
                var bitmapSpectrum = new BitmapImage();
                bitmapSpectrum.BeginInit();
                bitmapSpectrum.UriSource = uriSpectrum;
                bitmapSpectrum.CacheOption = BitmapCacheOption.OnLoad;
                bitmapSpectrum.EndInit();
                bitmapSpectrum.Freeze();
                _defaultSpectrumImage = bitmapSpectrum;

                // Load Now Playing Default
                var uriNowPlaying = new Uri("pack://application:,,,/Assets/Images/default_now_playing_bg.png");
                var bitmapNowPlaying = new BitmapImage();
                bitmapNowPlaying.BeginInit();
                bitmapNowPlaying.UriSource = uriNowPlaying;
                bitmapNowPlaying.CacheOption = BitmapCacheOption.OnLoad;
                bitmapNowPlaying.EndInit();
                bitmapNowPlaying.Freeze();
                _defaultNowPlayingImage = bitmapNowPlaying;

                // Load Favorites Image (Phase 8)
                var uriFav = new Uri("pack://application:,,,/Assets/Images/favorites_bg.png");
                var bitmapFav = new BitmapImage();
                bitmapFav.BeginInit();
                bitmapFav.UriSource = uriFav;
                bitmapFav.CacheOption = BitmapCacheOption.OnLoad;
                bitmapFav.EndInit();
                bitmapFav.Freeze();
                _favoritesImage = bitmapFav;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load default images: {ex.Message}");
            }
        }

        // ...


        /// <summary>
        /// デバイスの種類（ファイルシステム または MTP）
        /// </summary>
        public enum DeviceType
        {
            /// <summary>
            /// USBマスストレージなどのファイルシステムデバイス
            /// </summary>
            FileSystem,

            /// <summary>
            /// ポータブルデバイスなどのMTPデバイス
            /// </summary>
            MTP
        }

        /// <summary>
        /// 接続されたデバイスを表すViewModel
        /// </summary>
        public class DeviceViewModel
        {
            /// <summary>
            /// デバイス名を取得または設定します
            /// </summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>
            /// デバイス種別を取得または設定します
            /// </summary>
            public DeviceType Type { get; set; }

            /// <summary>
            /// ファイルシステムデバイスの場合のドライブ情報を取得または設定します
            /// </summary>
            public DriveInfo? Drive { get; set; }

            /// <summary>
            /// MTPデバイスの場合のデバイスインスタンスを取得または設定します
            /// </summary>
            public MediaDevice? MtpDevice { get; set; }

            /// <summary>
            /// デバイスのルートパスを取得または設定します
            /// </summary>
            public string RootPath { get; set; } = string.Empty; // For MTP, this might be device ID or root
        }

        /// <summary>
        /// デバイス同期画面の表示状態。
        /// </summary>
        public bool IsDeviceSyncVisible => CurrentViewType == ViewType.DeviceSync;


        /// <summary>
        /// 現在再生中の曲のアルバムアート画像を取得または設定します
        /// </summary>
        public BitmapImage? NowPlayingImage
        {
            get => _nowPlayingImage;
            set
            {
                _nowPlayingImage = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<Album> _albums = new ObservableCollection<Album>();

        /// <summary>
        /// ライブラリ内のアルバムコレクションを取得または設定します
        /// </summary>
        public ObservableCollection<Album> Albums
        {
            get => Library?.Albums ?? _albums;
            set
            {
                if (Library != null)
                {
                    Library.Albums.Clear();
                    foreach (var a in value) Library.Albums.Add(a);
                }
                _albums = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// ライブラリビューが表示されているかどうかを示す値を取得または設定します
        /// </summary>
        public bool IsLibraryVisible
        {
            get => _isLibraryVisible;
            set
            {
                _isLibraryVisible = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// フォルダービューが表示されているかどうかを示す値を取得または設定します
        /// </summary>
        public bool IsFolderViewVisible
        {
            get => _isFolderViewVisible;
            set
            {
                _isFolderViewVisible = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// プレイリスト選択画面が表示されているかどうかを示す値を取得します
        /// </summary>
        public bool IsPlaylistSelectorVisible => CurrentViewType == ViewType.Playlists;

        /// <summary>
        /// プレイリスト楽曲一覧が表示されているかどうかを示す値を取得します
        /// </summary>
        public bool IsPlaylistTracksVisible => CurrentViewType == ViewType.PlaylistTracks || CurrentViewType == ViewType.Favorites;

        /// <summary>
        /// データ読み込み中かどうかを示す値を取得または設定します
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading || (Library?.IsLoading ?? false);
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// グリッド表示モードかどうか。
        /// </summary>
        public bool IsGridView
        {
            get => _isGridView;
            set
            {
                _isGridView = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsListView));
            }
        }

        /// <summary>
        /// リスト表示モードかどうか。
        /// </summary>
        public bool IsListView
        {
            get => !_isGridView;
            set
            {
                IsGridView = !value;
                OnPropertyChanged(nameof(IsGridView));
            }
        }

        /// <summary>
        /// ライブラリのソート順選択肢一覧を取得します
        /// </summary>
        public List<string> SortOptions { get; } = new List<string> { "Artist", "Album" };

        /// <summary>
        /// 選択されているライブラリのソート順を取得または設定します
        /// </summary>
        public string SelectedSortOption
        {
            get => _selectedSortOption;
            set
            {
                _selectedSortOption = value;
                OnPropertyChanged();
                SortLibrary();
            }
        }

        /// <summary>
        /// シャッフル再生が有効かどうかを取得または設定します
        /// </summary>
        public bool IsShuffleEnabled
        {
            get => PlayerControl?.IsShuffleEnabled ?? _audioService.IsShuffleEnabled;
            set
            {
                if (PlayerControl != null)
                {
                    PlayerControl.IsShuffleEnabled = value;
                }
                else
                {
                    _audioService.IsShuffleEnabled = value;
                }
                OnPropertyChanged();
            }
        }

        private Album? _currentAlbum;

        /// <summary>
        /// 現在再生中のアルバムを取得または設定します
        /// </summary>
        public Album? CurrentAlbum
        {
            get => _currentAlbum;
            set
            {
                _currentAlbum = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 現在再生中のトラックを取得または設定します
        /// </summary>
        public Track? CurrentTrack
        {
            get => PlayerControl?.CurrentTrack ?? _currentTrack;
            set
            {
                _currentTrack = value;
                if (PlayerControl != null)
                {
                    PlayerControl.CurrentTrack = value;
                }
                OnPropertyChanged();

                if (value != null)
                {
                    CurrentAlbum = Albums.FirstOrDefault(a => a.Tracks.Contains(value));
                }
                else
                {
                    CurrentAlbum = null;
                }
            }
        }

        /// <summary>
        /// 現在再生中かどうかを示す値を取得または設定します
        /// </summary>
        public bool IsPlaying
        {
            get => PlayerControl?.IsPlaying ?? _isPlaying;
            set
            {
                _isPlaying = value;
                if (PlayerControl != null)
                {
                    PlayerControl.IsPlaying = value;
                }
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 現在の再生時間表示文字列を取得または設定します
        /// </summary>
        public string CurrentTimeDisplay
        {
            get => PlayerControl?.CurrentTimeDisplay ?? _currentTimeDisplay;
            set
            {
                _currentTimeDisplay = value;
                if (PlayerControl != null)
                {
                    PlayerControl.CurrentTimeDisplay = value;
                }
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 総再生時間表示文字列を取得または設定します
        /// </summary>
        public string TotalTimeDisplay
        {
            get => PlayerControl?.TotalTimeDisplay ?? _totalTimeDisplay;
            set
            {
                _totalTimeDisplay = value;
                if (PlayerControl != null)
                {
                    PlayerControl.TotalTimeDisplay = value;
                }
                OnPropertyChanged();
            }
        }

        private bool _isDraggingProgress;
        /// <summary>
        /// 現在の再生位置（進捗、0-100）
        /// ドラッグ操作中は再生位置を更新しません
        /// </summary>
        public double Progress
        {
            get => PlayerControl?.Progress ?? _progress;
            set
            {
                _progress = value;
                if (PlayerControl != null)
                {
                    PlayerControl.Progress = value;
                }
                else if (_isDraggingProgress)
                {
                    _audioService.SeekTo(value);
                }
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// シークバーがドラッグ操作中かどうか
        /// </summary>
        public bool IsDraggingProgress
        {
            get => PlayerControl?.IsDraggingProgress ?? _isDraggingProgress;
            set
            {
                _isDraggingProgress = value;
                if (PlayerControl != null)
                {
                    PlayerControl.IsDraggingProgress = value;
                }
            }
        }

        /// <summary>
        /// 指定パーセント位置へシークします
        /// </summary>
        /// <param name="percentage">シーク位置（0-100）</param>
        public void Seek(double percentage)
        {
            if (PlayerControl != null)
            {
                PlayerControl.Seek(percentage);
            }
            else
            {
                _progress = percentage;
                OnPropertyChanged(nameof(Progress));
                _audioService.SeekTo(percentage);
            }
        }

        /// <summary>
        /// "Now Playing"セクションが表示されているかどうか
        /// </summary>
        public bool IsNowPlayingVisible
        {
            get => _isNowPlayingVisible;
            set
            {
                _isNowPlayingVisible = value;
                OnPropertyChanged();
            }
        }

        // コマンド定義

        private bool _isDeviceConnected;

        /// <summary>
        /// 外部デバイスが接続されているかどうかを示す値を取得または設定します
        /// </summary>
        public bool IsDeviceConnected
        {
            get => _isDeviceConnected;
            set
            {
                if (_isDeviceConnected != value)
                {
                    _isDeviceConnected = value;
                    OnPropertyChanged();
                    if (_isDeviceConnected)
                    {
                        DeviceBrowser.RefreshDrives();
                    }
                    else if (CurrentViewType == ViewType.DeviceSync)
                    {
                        CurrentViewType = ViewType.Albums;
                    }
                }
            }
        }

        /// <summary>
        /// 再生/一時停止を切り替えるコマンドを取得します
        /// </summary>
        public ICommand TogglePlayPauseCommand { get; }

        /// <summary>
        /// 次のトラックへ進むコマンドを取得します
        /// </summary>
        public ICommand NextCommand { get; }

        /// <summary>
        /// 前のトラックへ戻るコマンドを取得します
        /// </summary>
        public ICommand PreviousCommand { get; }

        /// <summary>
        /// 指定したトラックを再生するコマンドを取得します
        /// </summary>
        public ICommand PlayTrackCommand { get; }

        /// <summary>
        /// お気に入り状態を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleFavoriteCommand { get; }

        /// <summary>
        /// 表示ビューの切り替えコマンドを取得します
        /// </summary>
        public ICommand ToggleViewCommand { get; }

        /// <summary>
        /// お気に入り楽曲一覧を表示するコマンドを取得します
        /// </summary>
        public ICommand ShowFavoritesCommand { get; }

        /// <summary>
        /// ライブラリ一覧を表示するコマンドを取得します
        /// </summary>
        public ICommand ShowLibraryCommand { get; }

        /// <summary>
        /// フォルダーツリービューを表示するコマンドを取得します
        /// </summary>
        public ICommand ShowFolderCommand { get; }

        /// <summary>
        /// アルバム全体を再生するコマンドを取得します
        /// </summary>
        public ICommand PlayAlbumCommand { get; }

        /// <summary>
        /// 次に再生するアルバムとしてキューに追加するコマンドを取得します
        /// </summary>
        public ICommand PlayNextAlbumCommand { get; }

        /// <summary>
        /// アルバムをキューの末尾に追加するコマンドを取得します
        /// </summary>
        public ICommand EnqueueAlbumCommand { get; }

        /// <summary>
        /// アルバムを削除するコマンドを取得します
        /// </summary>
        public ICommand DeleteAlbumCommand { get; }

        /// <summary>
        /// 指定トラックを次に再生するようキューに追加するコマンドを取得します
        /// </summary>
        public ICommand PlayNextCommand { get; }

        /// <summary>
        /// 指定トラックをキューの末尾に追加するコマンドを取得します
        /// </summary>
        public ICommand EnqueueTrackCommand { get; }

        /// <summary>
        /// トラックプロパティを表示するコマンドを取得します
        /// </summary>
        public ICommand ShowTrackPropertiesCommand { get; }

        /// <summary>
        /// ファイルの保存場所をエクスプローラーで開くコマンドを取得します
        /// </summary>
        public ICommand OpenFileLocationCommand { get; }

        /// <summary>
        /// トラックを削除するコマンドを取得します
        /// </summary>
        public ICommand DeleteTrackCommand { get; }

        /// <summary>
        /// 再生キューダイアログを表示するコマンドを取得します
        /// </summary>
        public ICommand ShowQueueDialogCommand { get; }

        /// <summary>
        /// 再生キュー内の指定曲を再生するコマンドを取得します
        /// </summary>
        public ICommand PlayFromQueueCommand { get; }

        // Device Sync Commands

        /// <summary>
        /// デバイス同期画面へ切り替えるコマンドを取得します
        /// </summary>
        public ICommand SwitchToDeviceSyncCommand { get; }

        private bool _isAscending = true;

        /// <summary>
        /// ソート順が昇順かどうかを取得または設定します
        /// </summary>
        public bool IsAscending
        {
            get => _isAscending;
            set
            {
                _isAscending = value;
                OnPropertyChanged();
                SortLibrary();
            }
        }

        private bool _isSelectionMode;
        /// <summary>
        /// トラック選択モードが有効かどうか
        /// </summary>
        public bool IsSelectionMode
        {
            get => _isSelectionMode;
            set
            {
                _isSelectionMode = value;
                OnPropertyChanged();
            }
        }

        private bool _isAlbumRepeat;
        /// <summary>
        /// アルバムリピートモードが有効かどうか
        /// </summary>
        public bool IsAlbumRepeat
        {
            get => PlayerControl?.IsAlbumRepeat ?? _isAlbumRepeat;
            set
            {
                if (PlayerControl != null)
                {
                    PlayerControl.IsAlbumRepeat = value;
                }
                else
                {
                    _isAlbumRepeat = value;
                    _audioService.IsRepeatEnabled = value;
                }
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// ソート順（昇順/降順）を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleSortDirectionCommand { get; }

        /// <summary>
        /// トラック選択モードの有効/無効を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleSelectionModeCommand { get; }

        /// <summary>
        /// シャッフル再生の有効/無効を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleShuffleCommand { get; }

        /// <summary>
        /// リピート再生の有効/無効を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleRepeatCommand { get; }

        /// <summary>
        /// 音量を上げるコマンドを取得します
        /// </summary>
        public ICommand IncreaseVolumeCommand { get; }

        /// <summary>
        /// 音量を下げるコマンドを取得します
        /// </summary>
        public ICommand DecreaseVolumeCommand { get; }

        /// <summary>
        /// 再生を停止するコマンドを取得します
        /// </summary>
        public ICommand StopCommand { get; }

        /// <summary>
        /// ミュート状態を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleMuteCommand { get; }

        private bool _isMuted;

        /// <summary>
        /// ミュート状態
        /// </summary>
        public bool IsMuted
        {
            get => PlayerControl?.IsMuted ?? _isMuted;
            set
            {
                if (PlayerControl != null)
                {
                    PlayerControl.IsMuted = value;
                }
                else
                {
                    _isMuted = value;
                }
                OnPropertyChanged();
            }
        }

        private void ToggleMute()
        {
            PlayerControl?.ToggleMute();
        }

        /// <summary>
        /// 音量（0.0 - 1.0）
        /// 変更時に設定ファイルへ保存されます
        /// </summary>
        public float Volume
        {
            get => PlayerControl?.Volume ?? _audioService.Volume;
            set
            {
                if (PlayerControl != null)
                {
                    PlayerControl.Volume = value;
                }
                else if (_audioService.Volume != value)
                {
                    _audioService.Volume = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(VolumePercent));

                    // Save settings
                    // 設定を保存
                    var settings = _settingsService.LoadSettings();
                    settings.Volume = value;
                    _settingsService.SaveSettings(settings);
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumePercent));
            }
        }

        /// <summary>
        /// 音量のパーセント表示文字列を取得します
        /// </summary>
        public string VolumePercent => PlayerControl?.VolumePercent ?? $"{(int)(Volume * 100)}%";

        /// <summary>
        /// 外部デバイス上のディレクトリまたはファイルアイテムを表すクラス
        /// </summary>
        public class DirectoryItem
        {
            /// <summary>
            /// アイテム名を取得または設定します
            /// </summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>
            /// アイテムのフルパスを取得または設定します
            /// </summary>
            public string FullPath { get; set; } = string.Empty;

            /// <summary>
            /// フォルダーかどうかを示す値を取得または設定します
            /// </summary>
            public bool IsFolder { get; set; }
        }

        private GridLength _leftColumnWidth = new GridLength(300);
        /// <summary>
        /// 左カラム（サイドバー）の幅
        /// </summary>
        public GridLength LeftColumnWidth
        {
            get => _leftColumnWidth;
            set
            {
                _leftColumnWidth = value;
                OnPropertyChanged();
                var settings = _settingsService.LoadSettings();
                settings.LeftColumnWidth = value.Value;
                _settingsService.SaveSettings(settings);
            }
        }

        /// <summary>
        /// 設定ダイアログを表示するコマンドを取得します
        /// </summary>
        public ICommand ShowSettingsCommand { get; }



        /// <summary>
        /// ライブラリをソート条件（アーティスト/アルバム）と昇順/降順に従って並び替えます。
        /// </summary>
        private void SortLibrary()
        {
            Library?.SortLibrary();
        }




        private ImageSource? _spectrumBackgroundImageGray;

        /// <summary>
        /// スペクトラムアナライザーのグレースケール背景画像を取得または設定します
        /// </summary>
        public ImageSource? SpectrumBackgroundImageGray
        {
            get => _spectrumBackgroundImageGray;
            set
            {
                _spectrumBackgroundImageGray = value;
                OnPropertyChanged();
            }
        }

        private bool _isDefaultSpectrumImage;

        /// <summary>
        /// スペクトラムアナライザーの背景画像がデフォルト画像かどうかを示す値を取得または設定します
        /// </summary>
        public bool IsDefaultSpectrumImage
        {
            get => _isDefaultSpectrumImage;
            set
            {
                _isDefaultSpectrumImage = value;
                OnPropertyChanged();
            }
        }

        private Brush? _spectrumBarBrush;
        /// <summary>
        /// スペクトラムアナライザーのバーのブラシ
        /// アルバムアートの主要な色に基づいて動的に更新されます
        /// </summary>
        public Brush SpectrumBarBrush
        {
            get
            {
                if (_spectrumBarBrush == null)
                {
                    // Default Gradient: Horizontal, Right (Low Sat) to Left (High Sat)
                    var brush = new LinearGradientBrush();
                    brush.StartPoint = new Point(1, 0.5); // Right
                    brush.EndPoint = new Point(0, 0.5);   // Left

                    // Right (High Freq): Low Saturation (pale), High Opacity (Bright)
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(242, 200, 250, 255), 0.0));
                    // Left (Low Freq): High Saturation (vibrant)
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(153, 0, 229, 255), 1.0));

                    _spectrumBarBrush = brush;
                }
                return _spectrumBarBrush;
            }

            set
            {
                _spectrumBarBrush = value;
                OnPropertyChanged();
            }
        }

        private Brush _spectrumBorderBrush = new SolidColorBrush(Color.FromArgb(230, 0, 229, 255)); // Default Neon Cyan Border (90%)

        /// <summary>
        /// スペクトラムアナライザーのボーダーブラシを取得または設定します
        /// </summary>
        public Brush SpectrumBorderBrush
        {
            get => _spectrumBorderBrush;
            set
            {
                _spectrumBorderBrush = value;
                OnPropertyChanged();
            }
        }

        private Color _spectrumShadowColor = Color.FromRgb(0, 229, 255); // Default Neon Cyan

        /// <summary>
        /// スペクトラムアナライザーのシャドウ色を取得または設定します
        /// </summary>
        public Color SpectrumShadowColor
        {
            get => _spectrumShadowColor;
            set
            {
                _spectrumShadowColor = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// アルバムアートを解析し、スペクトラムアナライザーの色（バー、ボーダー、シャドウ）を更新します。
        /// </summary>
        /// <param name="bitmap">解析対象の画像。</param>
        private void UpdateSpectrumBrush(BitmapSource bitmap)
        {
            try
            {
                // 1. Force Convert to Bgra32 to ensure byte order is B-G-R-A
                var converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = bitmap;
                converted.DestinationFormat = PixelFormats.Bgra32;
                converted.EndInit();
                converted.Freeze();

                // 2. Resize to small size for performance
                var resized = new TransformedBitmap(converted, new ScaleTransform(100.0 / converted.PixelWidth, 100.0 / converted.PixelHeight));
                resized.Freeze();
                int width = resized.PixelWidth;
                int height = resized.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[height * stride];
                resized.CopyPixels(pixels, stride, 0);

                // 3. Histogram / Bucketing Approach
                // Buckets for Hue (0-360), e.g., 36 buckets of 10 degrees
                long[] bucketR = new long[36];
                long[] bucketG = new long[36];
                long[] bucketB = new long[36];
                int[] bucketCount = new int[36];

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    // alpha is pixels[i+3], ignore

                    Color c = Color.FromRgb(r, g, b);
                    ColorToHsv(c, out double h, out double s, out double v);

                    // Skip grays, blacks, whites
                    if (s < 0.2 || v < 0.2) continue;

                    int bucketIndex = (int)(h / 10.0);
                    if (bucketIndex >= 36) bucketIndex = 35;

                    bucketR[bucketIndex] += r;
                    bucketG[bucketIndex] += g;
                    bucketB[bucketIndex] += b;
                    bucketCount[bucketIndex]++;
                }

                // Find winning bucket
                int maxCount = 0;
                int winningBucket = -1;
                for (int i = 0; i < 36; i++)
                {
                    if (bucketCount[i] > maxCount)
                    {
                        maxCount = bucketCount[i];
                        winningBucket = i;
                    }
                }

                Color finalColor;
                if (winningBucket != -1)
                {
                    byte avgR = (byte)(bucketR[winningBucket] / bucketCount[winningBucket]);
                    byte avgG = (byte)(bucketG[winningBucket] / bucketCount[winningBucket]);
                    byte avgB = (byte)(bucketB[winningBucket] / bucketCount[winningBucket]);
                    finalColor = Color.FromRgb(avgR, avgG, avgB);
                }
                else
                {
                    // Fallback to simple average if no vibrant pixels found
                    long sumR = 0, sumG = 0, sumB = 0;
                    int total = 0;
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        sumB += pixels[i];
                        sumG += pixels[i + 1];
                        sumR += pixels[i + 2];
                        total++;
                    }
                    if (total > 0)
                    {
                        finalColor = Color.FromRgb((byte)(sumR / total), (byte)(sumG / total), (byte)(sumB / total));
                    }
                    else
                    {
                        finalColor = Color.FromRgb(64, 235, 255);
                    }
                }

                // 4. Boost Saturation/Value
                ColorToHsv(finalColor, out double fh, out double fs, out double fv);

                // Vertical Neon Gradient (Bottom -> Top)
                var brush = new LinearGradientBrush();
                brush.StartPoint = new Point(0.5, 1.0); // Bottom
                brush.EndPoint = new Point(0.5, 0.0);   // Top

                // Bottom: Deep Vibrant Purple/Magenta (Offset 0.0)
                Color colorBottom = HsvToColor((fh + 40) % 360, 0.85, 0.85);
                colorBottom.A = 220;

                // Middle: Bright Vibrant Neon Accent (Offset 0.5)
                Color colorMid = HsvToColor(fh, 0.95, 1.0);
                colorMid.A = 255;

                // Top: Luminous White-tinted Neon (Offset 1.0)
                Color colorTop = HsvToColor(fh, 0.15, 1.0);
                colorTop.A = 255;

                brush.GradientStops.Add(new GradientStop(colorBottom, 0.0));
                brush.GradientStops.Add(new GradientStop(colorMid, 0.5));
                brush.GradientStops.Add(new GradientStop(colorTop, 1.0));
                brush.Freeze();

                SpectrumBarBrush = brush;
                SpectrumShadowColor = HsvToColor(fh, 1.0, 1.0);

                Color borderColor = HsvToColor(fh, 0.2, 1.0);
                borderColor.A = 240;
                var solidBorderBrush = new SolidColorBrush(borderColor);
                solidBorderBrush.Freeze();
                SpectrumBorderBrush = solidBorderBrush;
            }
            catch
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var brush = new LinearGradientBrush();
                    brush.StartPoint = new Point(0.5, 1.0);
                    brush.EndPoint = new Point(0.5, 0.0);

                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(220, 139, 92, 246), 0.0)); // Deep Purple
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 229, 255), 0.5));   // Electric Cyan
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 1.0)); // White Neon Tip
                    brush.Freeze();

                    SpectrumBarBrush = brush;
                    SpectrumShadowColor = Color.FromRgb(0, 229, 255);

                    var borderColor = Color.FromArgb(240, 204, 249, 255);
                    var solidBorderBrush = new SolidColorBrush(borderColor);
                    solidBorderBrush.Freeze();
                    SpectrumBorderBrush = solidBorderBrush;
                });
            }
        }

        private void OnPlaylistChanged(List<Track> playlist)
        {
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PlayQueue = new System.Collections.ObjectModel.ObservableCollection<Track>(playlist);
            });
        }

        /// <summary>
        /// 再生中のトラックが変更されたときに呼び出されます。
        /// スペクトラムの更新、アルバムアートの読み込み、背景画像の更新を行います。
        /// </summary>
        /// <param name="track">新しいトラック。</param>
        private void OnTrackChanged(Track? track)
        {
            if (CurrentTrack != null)
            {
                CurrentTrack.IsPlaying = false;
            }

            CurrentTrack = track;
            Progress = 0;

            if (track == null)
            {
                CurrentAlbum = null;
                PlaybackListTracks = new ObservableCollection<Track>();
                PlaybackListName = "No Album Selected";
                PlaybackListSubtitle = string.Empty;
                PlayerControl?.SyncTrackPlayingStates(null);
                RunOnUiThread(() =>
                {
                    NowPlayingImage = _defaultNowPlayingImage;
                    SpectrumBackgroundImage = _defaultSpectrumImage;
                    SpectrumBackgroundImageGray = null;
                    IsDefaultSpectrumImage = true;
                });
                return;
            }

            track.IsPlaying = true;

            PlayerControl?.SyncTrackPlayingStates(track);

            if (track != null)
            {
                RunOnUiThread(() =>
                {
                    if (!IsPlaylistTracksVisible && PlaybackListTracks != null && !PlaybackListTracks.Any(t => PlayerControlViewModel.IsSameTrack(t, track)))
                    {
                        var album = Albums.FirstOrDefault(a => a.Tracks.Any(t => PlayerControlViewModel.IsSameTrack(t, track)));
                        if (album != null)
                        {
                            PlaybackListName = album.Title;
                            PlaybackListSubtitle = album.Artist;
                            PlaybackListTracks = new System.Collections.ObjectModel.ObservableCollection<Track>(album.Tracks);
                        }
                    }
                    PlayerControl?.SyncTrackPlayingStates(track);
                });
            }

            // Phase 9: Logic Update
            // If viewing Favorites, FORCE Background to Galaxy.
            if (IsFavoritesView && _favoritesImage != null)
            {
                Playlist?.SetFavoritesBackgroundImage(_favoritesImage);
            }
            // If viewing anything else (Playlist), use Track Art (NowPlayingImage).
            // But NowPlayingImage is not yet updated here (async).
            // So we wait for async block below.

            if (track != null && File.Exists(track.FilePath))
            {
                Task.Run(() =>
                {
                    try
                    {
                        using (var tfile = TagLib.File.Create(track.FilePath))
                        {
                            if (tfile.Tag.Pictures.Length > 0)
                            {
                                var bin = tfile.Tag.Pictures[0].Data.Data;
                                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    try
                                    {
                                        var image = new BitmapImage();
                                        using (var mem = new MemoryStream(bin))
                                        {
                                            mem.Position = 0;
                                            image.BeginInit();
                                            image.DecodePixelWidth = 500;
                                            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                                            image.CacheOption = BitmapCacheOption.OnLoad;
                                            image.UriSource = null;
                                            image.StreamSource = mem;
                                            image.EndInit();
                                        }
                                        image.Freeze();
                                        NowPlayingImage = image;
                                        SpectrumBackgroundImage = image; // Keep Color

                                        // Phase 9: Update PlaylistBackgroundImage if NOT favorites view
                                        if (!IsFavoritesView)
                                        {
                                            Playlist?.SetBackgroundImage(image);
                                        }

                                        // Create Grayscale version for Spectrum Background Overlay
                                        var grayImage = new FormatConvertedBitmap();
                                        grayImage.BeginInit();
                                        grayImage.Source = image;
                                        grayImage.DestinationFormat = PixelFormats.Gray8;
                                        grayImage.EndInit();
                                        grayImage.Freeze();

                                        SpectrumBackgroundImageGray = grayImage;
                                        IsDefaultSpectrumImage = false;

                                        // Update Spectrum Bar Color
                                        UpdateSpectrumBrush(image);
                                    }
                                    catch
                                    {
                                        // 画像デコード中断 (COMException E_ABORT) 等の例外を安全に無視・フォールバック
                                        NowPlayingImage = _defaultNowPlayingImage;
                                        SpectrumBackgroundImage = _defaultSpectrumImage;
                                        SpectrumBackgroundImageGray = null;
                                    }
                                });
                            }
                            else
                            {
                                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    try
                                    {
                                        NowPlayingImage = _defaultNowPlayingImage;
                                        SpectrumBackgroundImage = _defaultSpectrumImage;
                                        SpectrumBackgroundImageGray = null;

                                        // Border: Brighter (Lower Saturation), 100% Opacity
                                        var borderColor = Color.FromRgb(204, 249, 255);
                                        borderColor.A = 255;
                                        var solidBorderBrush = new SolidColorBrush(borderColor);
                                        solidBorderBrush.Freeze();
                                        SpectrumBorderBrush = solidBorderBrush;
                                    }
                                    catch
                                    {
                                        // 例外無視
                                    }
                                });
                            }
                        }
                    }
                    catch
                    {
                        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            // Default Gradient
                            // Default Gradient: Right to Left
                            var brush = new LinearGradientBrush();
                            brush.StartPoint = new Point(1, 0.5);
                            brush.EndPoint = new Point(0, 0.5);
                            brush.GradientStops.Add(new GradientStop(Color.FromArgb(242, 200, 250, 255), 0.0));
                            brush.GradientStops.Add(new GradientStop(Color.FromArgb(153, 0, 229, 255), 1.0));
                            brush.Freeze();
                            SpectrumBarBrush = brush;
                            SpectrumShadowColor = Color.FromRgb(0, 229, 255);

                            // Border: Brighter (Lower Saturation), 100% Opacity
                            // Reduced Saturation: S=0.2 (Very White)
                            var borderColor = Color.FromRgb(204, 249, 255); // Approx for H186 S0.2 V1.0
                            borderColor.A = 255;
                            var solidBorderBrush = new SolidColorBrush(borderColor);
                            solidBorderBrush.Freeze();
                            SpectrumBorderBrush = solidBorderBrush;
                        });
                    }
                });
            }
            else
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsDefaultSpectrumImage = true;
                    // Default Gradient
                    // Default Gradient: Right to Left
                    var brush = new LinearGradientBrush();
                    brush.StartPoint = new Point(1, 0.5);
                    brush.EndPoint = new Point(0, 0.5);
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(242, 200, 250, 255), 0.0));
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(153, 0, 229, 255), 1.0));
                    brush.Freeze();
                    SpectrumBarBrush = brush;

                    // Border: Brighter (Lower Saturation), 100% Opacity
                    var borderColor = Color.FromRgb(204, 249, 255);
                    borderColor.A = 255;
                    var solidBorderBrush = new SolidColorBrush(borderColor);
                    solidBorderBrush.Freeze();
                    SpectrumBorderBrush = solidBorderBrush;
                });
            }
        }

        // HSV Helpers
        /// <summary>
        /// RGBカラーをHSV色空間に変換します。
        /// </summary>
        private void ColorToHsv(Color color, out double hue, out double saturation, out double value)
        {
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));

            // Calculate Hue
            if (max == min)
            {
                hue = 0;
            }
            else if (max == color.R)
            {
                hue = (60 * (color.G - color.B) / (double)(max - min) + 360) % 360;
            }
            else if (max == color.G)
            {
                hue = (60 * (color.B - color.R) / (double)(max - min) + 120);
            }
            else
            {
                hue = (60 * (color.R - color.G) / (double)(max - min) + 240);
            }

            saturation = (max == 0) ? 0 : 1d - (1d * min / max);
            value = max / 255d;
        }

        /// <summary>
        /// HSV色空間からRGBカラーを作成します。
        /// </summary>
        private Color HsvToColor(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            if (hi == 0)
                return Color.FromRgb((byte)v, (byte)t, (byte)p);
            else if (hi == 1)
                return Color.FromRgb((byte)q, (byte)v, (byte)p);
            else if (hi == 2)
                return Color.FromRgb((byte)p, (byte)v, (byte)t);
            else if (hi == 3)
                return Color.FromRgb((byte)p, (byte)q, (byte)v);
            else if (hi == 4)
                return Color.FromRgb((byte)t, (byte)p, (byte)v);
            else
                return Color.FromRgb((byte)v, (byte)p, (byte)q);
        }

        private void OnPlaybackStateChanged(bool isPlaying)
        {
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsPlaying = isPlaying;
                if (isPlaying)
                {
                    _timer.Start();
                }
                else
                {
                    _timer.Stop();
                    // Do not reset images here to persist album art on stop
                }
            });
        }

        private int _tickCount;
        private bool _isCheckingDevices;

        private void OnTimerTick(object? sender, EventArgs e)
        {
            // デバイス接続状態を2秒おき(4 ticks = 500ms * 4)に非同期でチェック
            _tickCount++;
            if (_tickCount >= 4 && !_isCheckingDevices)
            {
                _tickCount = 0;
                _isCheckingDevices = true;

                Task.Run(() =>
                {
                    bool hasRemovable = DriveInfo.GetDrives().Any(d => d.DriveType == DriveType.Removable && d.IsReady);
                    bool hasMtp = false;
                    try
                    {
                        hasMtp = MediaDevice.GetDevices().Any();
                    }
                    catch
                    {
                        // Ignore MTP exceptions
                    }

                    bool connected = hasRemovable || hasMtp;

                    App.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsDeviceConnected = connected;

                        // もしデバイスが取り外されて、かつデバイス同期ビューが開いていたら閉じる
                        if (!IsDeviceConnected && CurrentViewType == ViewType.DeviceSync)
                        {
                            CurrentViewType = ViewType.Albums;
                        }

                        _isCheckingDevices = false;
                    });
                });
            }
        }



        /// <summary>
        /// 指定されたフォルダー（または設定された最後のパス）からライブラリを非同期でロードします。
        /// </summary>
        private void LoadLibrary(string? rootFolder = null)
        {
            Library?.LoadLibrary(rootFolder);
        }

        private void ToggleFavorite(object? obj)
        {
            var targetTrack = obj as Track ?? CurrentTrack;
            if (targetTrack != null)
            {
                if (Library != null)
                {
                    Library.ToggleFavorite(targetTrack);
                }
                else
                {
                    targetTrack.IsFavorite = !targetTrack.IsFavorite;
                    if (targetTrack.IsFavorite)
                    {
                        if (!_favoritePaths.Contains(targetTrack.FilePath))
                        {
                            _favoritePaths.Add(targetTrack.FilePath);
                        }
                    }
                    else
                    {
                        _favoritePaths.Remove(targetTrack.FilePath);
                    }
                    _libraryService?.SaveFavorites(_favoritePaths);
                }
            }
        }

        private void OnPlaylistEnded(object? sender, EventArgs e)
        {
            // If repeat is OFF, try to play next album
            if (!IsAlbumRepeat && CurrentTrack != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var currentAlbum = Albums.FirstOrDefault(a => a.Tracks.Any(t => t.FilePath == CurrentTrack.FilePath));
                    if (currentAlbum != null)
                    {
                        int index = Albums.IndexOf(currentAlbum);
                        if (index >= 0 && index < Albums.Count - 1)
                        {
                            var nextAlbum = Albums[index + 1];
                            if (nextAlbum.Tracks.Count > 0)
                            {
                                _audioService.SetPlaylist(nextAlbum.Tracks);
                                _audioService.PlayTrack(nextAlbum.Tracks.First());
                            }
                        }
                    }
                });
            }
        }

        /// <summary>
        /// リソースのクリーンアップを行います
        /// </summary>
        public void Cleanup()
        {
            _timer.Stop();
            _audioService.Dispose();
        }

        /// <summary>
        /// プレイリストセクションがアクティブかどうかを示す値を取得します
        /// </summary>
        public bool IsPlaylistSectionActive => CurrentViewType is ViewType.Playlists or ViewType.PlaylistTracks;

        /// <summary>
        /// 現在お気に入り画面を表示しているかどうかを示す値を取得します
        /// </summary>
        public bool IsFavoritesView => CurrentViewType == ViewType.Favorites;

        private void ShowFavorites()
        {
            CurrentViewType = ViewType.Favorites;
            var favoriteTracks = _favoritePaths
                .Select(LoadTrack)
                .Where(track => track != null)
                .Cast<Track>()
                .ToList();
            Playlist?.ShowFavorites(favoriteTracks, _favoritesImage);
        }

        private void ShowLibrary()
        {
            CurrentViewType = ViewType.Albums;
        }

        private void ShowFolder()
        {
            CurrentViewType = ViewType.Folders;
        }

        /// <summary>
        /// 指定されたパスのトラック情報を読み込みます。
        /// メタデータとカバーアートを取得します。
        /// </summary>
        private Track? LoadTrack(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                var track = new Track
                {
                    Title = tagFile.Tag.Title ?? Path.GetFileNameWithoutExtension(filePath),
                    Artist = tagFile.Tag.FirstPerformer ?? "Unknown Artist",
                    Album = tagFile.Tag.Album ?? "Unknown Album",
                    FilePath = filePath,
                    Duration = tagFile.Properties.Duration,
                    IsFavorite = _favoritePaths.Contains(filePath)
                };

                // Load cover art
                if (tagFile.Tag.Pictures.Length > 0)
                {
                    string cacheKey = $"{track.Artist}|{track.Album}";
                    if (_albumArtCache.TryGetValue(cacheKey, out var cachedImage))
                    {
                        track.CoverImage = cachedImage;
                    }
                    else
                    {
                        var bin = tagFile.Tag.Pictures[0].Data.Data;
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = new MemoryStream(bin);
                        bitmap.DecodePixelWidth = 150; // Reduce memory usage
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        track.CoverImage = bitmap;

                        _albumArtCache[cacheKey] = bitmap;
                    }
                }

                // Set quality information
                track.Bitrate = tagFile.Properties.AudioBitrate;
                track.SampleRate = tagFile.Properties.AudioSampleRate;
                track.BitsPerSample = tagFile.Properties.BitsPerSample;

                string ext = Path.GetExtension(filePath).ToLower(System.Globalization.CultureInfo.InvariantCulture);
                track.Format = ext.TrimStart('.').ToUpper(System.Globalization.CultureInfo.InvariantCulture);
                track.IsLossless = LosslessAudioExtensions.Contains(ext);
                track.IsHiRes = track.SampleRate > 48000 || track.BitsPerSample > 16;

                return track;
            }
            catch
            {
                return null;
            }
        }

        private void ShowSettings()
        {
            var settingsViewModel = new SettingsViewModel(_settingsService, _audioService);
            var settingsDialog = new SettingsDialog(settingsViewModel);
            if (System.Windows.Application.Current.MainWindow != null)
            {
                settingsDialog.Owner = System.Windows.Application.Current.MainWindow;
            }
            settingsDialog.ShowDialog();
            SettingsUpdated?.Invoke();
        }

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.InvokeAsync(action);
            }
        }

        /// <summary>
        /// リソースを解放します
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// アンマネージドリソースおよびマネージドリソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースを破棄するかどうか</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _timer?.Stop();
                    _fallbackAudioService?.Dispose();
                    _fallbackSettings?.Dispose();
                    (_audioService as IDisposable)?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
