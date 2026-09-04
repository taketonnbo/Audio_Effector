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
using NAudio.Dsp;
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
        private readonly EqualizerApplicationService? _equalizerApplicationService;
        private readonly LibraryApplicationService? _libraryService;
        private readonly PlaylistApplicationService? _playlistService;
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
        /// コードビハインドからAudioServiceへアクセスするためのプロパティ
        /// </summary>
        public IAudioService AudioService => _audioService; // Public accessor for code-behind

        /// <summary>
        /// コードビハインドからSettingsServiceへアクセスするためのプロパティ
        /// </summary>
        public ISettingsService SettingsService => _settingsService;

        private EqualizerPreset? _selectedPreset;
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
        private ObservableCollection<UserPlaylist> _userPlaylists = new ObservableCollection<UserPlaylist>();
        private ObservableCollection<Track> _playlistTracks = new ObservableCollection<Track>();
        private const int SpectrumBarCount = 64;
        private int _spectrumGeneration;

        #region Spectrum Analyzer Tuning Coefficients
        /// <summary>スペクトラムアナライザ: 低音域（〜250Hz）のスケーリング係数（低音の過度な頭打ちを抑制）</summary>
        public const double SpectrumBassScale = 0.55;

        /// <summary>スペクトラムアナライザ: 中音域（250Hz〜2.5kHz）のスケーリング係数</summary>
        public const double SpectrumMidScale = 0.90;

        /// <summary>スペクトラムアナライザ: 高音域（2.5kHz〜18kHz）のスケーリング係数（高音の躍動感を大幅強化）</summary>
        public const double SpectrumTrebleScale = 2.90;

        /// <summary>スペクトラムアナライザ: 高音域のオクターブあたりdB補正（チルト）係数</summary>
        public const double SpectrumTrebleTiltDb = 8.5;

        /// <summary>スペクトラムアナライザ: 全体感度（ゲイン）係数</summary>
        public const double SpectrumSensitivity = 1.65;
        #endregion

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

                    if (_currentViewType == ViewType.DeviceSync)
                    {
                        IsSpectrumVisible = false;
                        RefreshDrives();
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
        private bool _isPlaylistSelectorVisible;
        private Dictionary<string, BitmapImage> _albumArtCache = new Dictionary<string, BitmapImage>();
        private UserPlaylist? _currentViewingPlaylist;

        /// <summary>
        /// 現在表示中のプレイリスト。
        /// </summary>
        public UserPlaylist? CurrentViewingPlaylist
        {
            get => _currentViewingPlaylist;
            set
            {
                if (_currentViewingPlaylist != value)
                {
                    _currentViewingPlaylist = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _playbackListName = "No Album Selected";

        /// <summary>
        /// 再生リストの名称（アルバム名やプレイリスト名）。
        /// </summary>
        public string PlaybackListName
        {
            get => _playbackListName;
            set
            {
                _playbackListName = value;
                OnPropertyChanged();
            }
        }

        private string _playbackListSubtitle = "";

        /// <summary>
        /// 再生リストのサブタイトル（アーティスト名など）。
        /// </summary>
        public string PlaybackListSubtitle
        {
            get => _playbackListSubtitle;
            set
            {
                _playbackListSubtitle = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<Track> _playbackListTracks = new ObservableCollection<Track>();

        /// <summary>
        /// 現在画面の右ペインに表示されているトラックのコレクション（アルバム・プレイリスト）。
        /// </summary>
        public ObservableCollection<Track> PlaybackListTracks
        {
            get => _playbackListTracks;
            set
            {
                _playbackListTracks = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlaybackListTracksCountText));
            }
        }

        /// <summary>
        /// トラックリストの総曲数表示文字列（例: "Tracklist (9 tracks)"）。
        /// </summary>
        public string PlaybackListTracksCountText
        {
            get
            {
                int count = PlaybackListTracks?.Count ?? 0;
                return count == 1 ? "Tracklist (1 track)" : $"Tracklist ({count} tracks)";
            }
        }

        private ObservableCollection<Track> _playQueue = new ObservableCollection<Track>();

        /// <summary>
        /// 実際の再生予定キュー。
        /// </summary>
        public ObservableCollection<Track> PlayQueue
        {
            get => _playQueue;
            set
            {
                _playQueue = value;
                OnPropertyChanged();
            }
        }

        private BitmapImage? _defaultSpectrumImage;
        private BitmapImage? _favoritesImage;
        private BitmapImage? _defaultNowPlayingImage;

        private ImageSource? _playlistBackgroundImage;

        /// <summary>
        /// プレイリストビューの背景画像。
        /// </summary>
        public ImageSource? PlaylistBackgroundImage
        {
            get => _playlistBackgroundImage;
            set
            {
                _playlistBackgroundImage = value;
                OnPropertyChanged();
            }
        }

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
            _equalizerApplicationService = App.ServiceProvider != null ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<EqualizerApplicationService>(App.ServiceProvider) : null;
            _libraryService = App.ServiceProvider != null ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<LibraryApplicationService>(App.ServiceProvider) : null;
            _playlistService = App.ServiceProvider != null ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<PlaylistApplicationService>(App.ServiceProvider) : null;
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

            var appSettings = _settingsService.LoadSettings();
            _audioService.UpdateAudioProperties(appSettings.SampleRate, appSettings.AudioBufferSizeMs);

            // Load playlists
            // プレイリストの読み込み
            var loadedPlaylists = _playlistService?.LoadPlaylists() ?? new List<UserPlaylist>();
            UserPlaylists = new ObservableCollection<UserPlaylist>(loadedPlaylists);

            // Generate thumbnails for loaded playlists
            // プレイリストのサムネイル生成
            foreach (var playlist in UserPlaylists)
            {
                UpdatePlaylistThumbnails(playlist);
            }

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

            Bands = new ObservableCollection<BandViewModel>();
            for (int i = 0; i < _audioService.Frequencies.Length; i++)
            {
                Bands.Add(new BandViewModel
                {
                    Index = i,
                    Frequency = _audioService.Frequencies[i],
                    OnGainChanged = (idx, gain) => _audioService.SetGain(idx, gain)
                });
            }

            // Pre-populate SpectrumValues to avoid layout glitches
            // レイアウト崩れを防ぐためにスペクトラム値を初期化
            for (int i = 0; i < SpectrumBarCount; i++)
            {
                SpectrumValues.Add(new SpectrumBarItem { Value = 0 });
            }

            Presets = new ObservableCollection<EqualizerPreset>(_equalizerApplicationService?.LoadPresets() ?? new List<EqualizerPreset>());
            if (!string.IsNullOrEmpty(appSettings.LastUsedEffectPreset))
            {
                SelectedPreset = Presets.FirstOrDefault(p => p.Name == appSettings.LastUsedEffectPreset) ?? Presets.FirstOrDefault();
            }
            else
            {
                SelectedPreset = Presets.FirstOrDefault();
            }

            OpenFolderCommand = new RelayCommand(OpenFolder);
            TogglePlayPauseCommand = new RelayCommand(o => _audioService.TogglePlayPause());
            StopCommand = new RelayCommand(o => _audioService.Stop(false));
            NextCommand = new RelayCommand(o => _audioService.Next());
            PreviousCommand = new RelayCommand(o => _audioService.Previous());
            SavePresetCommand = new RelayCommand(SavePreset);
            DeletePresetCommand = new RelayCommand(DeletePreset);
            ResetPresetCommand = new RelayCommand(Reset);

            PlayTrackCommand = new RelayCommand(o =>
            {
                if (o is Track t)
                {
                    // If clicking the currently playing track, toggle play/pause instead of restarting
                    if (CurrentTrack != null && CurrentTrack.Equals(t))
                    {
                        _audioService.TogglePlayPause();
                        return;
                    }

                    // Check if playing from playlist/favorites view
                    // プレイリストまたはお気に入りビューからの再生かどうかを確認
                    if (IsPlaylistTracksVisible && PlaylistTracks.Any() && PlaylistTracks.Contains(t))
                    {
                        PlayQueue = new ObservableCollection<Track>(PlaylistTracks);
                        _audioService.SetPlaylist(PlayQueue.ToList());
                        PlaybackListName = CurrentPlaylistName;
                        PlaybackListSubtitle = IsFavoritesView ? "Selected You" : "Playlist"; // Phase 8: Selected You
                        PlaybackListTracks = new ObservableCollection<Track>(PlaylistTracks);
                    }
                    else
                    {
                        var album = Albums.FirstOrDefault(a => a.Tracks.Contains(t));
                        if (album != null)
                        {
                            PlayQueue = new ObservableCollection<Track>(album.Tracks);
                            _audioService.SetPlaylist(PlayQueue.ToList());
                            PlaybackListName = album.Title;
                            PlaybackListSubtitle = album.Artist;
                            PlaybackListTracks = new ObservableCollection<Track>(album.Tracks);
                        }
                    }
                    _audioService.PlayTrack(t);
                }
            });

            ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
            PlayNextCommand = new RelayCommand(PlayNext);
            EnqueueTrackCommand = new RelayCommand(EnqueueTrack);
            ShowTrackPropertiesCommand = new RelayCommand(ShowTrackProperties);
            OpenFileLocationCommand = new RelayCommand(OpenFileLocation);
            DeleteTrackCommand = new RelayCommand(DeleteTrack);
            ShowQueueDialogCommand = new RelayCommand(o => ShowQueueDialog());
            PlayFromQueueCommand = new RelayCommand(o =>
            {
                if (o is Track t)
                {
                    if (t == CurrentTrack)
                    {
                        _audioService.TogglePlayPause();
                    }
                    else
                    {
                        _audioService.PlayTrack(t);
                    }
                }
            });
            ToggleViewCommand = new RelayCommand(o => IsGridView = !IsGridView);
            ToggleSortDirectionCommand = new RelayCommand(o => IsAscending = !IsAscending);

            // Playlist commands
            CreatePlaylistCommand = new RelayCommand(CreatePlaylist);
            AddToPlaylistCommand = new RelayCommand(AddToPlaylist);
            ShowPlaylistCommand = new RelayCommand(ShowPlaylist);
            ShowFavoritesCommand = new RelayCommand(o => ShowFavorites());

            SwitchViewCommand = new RelayCommand(param =>
            {
                if (param is ViewType viewType)
                {
                    CurrentViewType = viewType;
                    // Handle special cases
                    if (viewType == ViewType.Favorites)
                    {
                        ShowFavorites();
                    }
                }
            });

            ShowLibraryCommand = new RelayCommand(o => ShowLibrary());
            ShowFolderCommand = new RelayCommand(o => ShowFolder());
            ShowPlaylistSelectorCommand = new RelayCommand(o => ShowPlaylistSelector());
            ShowAddToPlaylistDialogCommand = new RelayCommand(ShowAddToPlaylistDialog);
            DeletePlaylistCommand = new RelayCommand(DeletePlaylist);
            PlayPlaylistCommand = new RelayCommand(PlayPlaylist);
            ShufflePlayPlaylistCommand = new RelayCommand(ShufflePlayPlaylist);
            RenamePlaylistCommand = new RelayCommand(RenamePlaylist);
            RemoveFromPlaylistCommand = new RelayCommand(RemoveFromPlaylist);

            ToggleSelectionModeCommand = new RelayCommand(o => IsSelectionMode = !IsSelectionMode);
            ToggleRepeatCommand = new RelayCommand(ToggleRepeat);
            AddSelectedToPlaylistCommand = new RelayCommand(AddSelectedToPlaylist);
            PlayAlbumCommand = new RelayCommand(PlayAlbum);
            PlayNextAlbumCommand = new RelayCommand(PlayNextAlbum);
            EnqueueAlbumCommand = new RelayCommand(EnqueueAlbum);
            ShowAddAlbumToPlaylistDialogCommand = new RelayCommand(ShowAddAlbumToPlaylistDialog);
            DeleteAlbumCommand = new RelayCommand(DeleteAlbum);

            IncreaseVolumeCommand = new RelayCommand(o => Volume = Math.Min(1.0f, Volume + 0.05f));
            DecreaseVolumeCommand = new RelayCommand(o => Volume = Math.Max(0.0f, Volume - 0.05f));
            ToggleMuteCommand = new RelayCommand(o => ToggleMute());

            // Device Sync Command Initialization
            SwitchToDeviceSyncCommand = new RelayCommand(o => CurrentViewType = ViewType.DeviceSync);
            SwitchToSpectrumCommand = new RelayCommand(o => IsSpectrumVisible = true);
            ToggleSpectrumCommand = new RelayCommand(o => IsSpectrumVisible = !IsSpectrumVisible);

            RefreshDrivesCommand = new RelayCommand(o => RefreshDrives());
            TransferSelectedCommand = new RelayCommand(o => TransferSelected());
            NavigateDirectoryCommand = new RelayCommand(o => NavigateDirectory(o as DirectoryItem));
            NavigateUpCommand = new RelayCommand(o => NavigateUp());
            RefreshDirectoryCommand = new RelayCommand(o => LoadDeviceDirectories(CurrentDevicePath));
            ShowDeviceManagerCommand = new RelayCommand(o => ShowDeviceManager());
            ShowSettingsCommand = new RelayCommand(o => ShowSettings());
            ToggleRightPanelCommand = new RelayCommand(o => IsRightPanelOpen = !IsRightPanelOpen);
            ToggleAlbumViewSizeModeCommand = new RelayCommand(o => IsAlbumViewMaximized = !IsAlbumViewMaximized);

            _audioService.PlaylistEnded += OnPlaylistEnded;
            _audioService.FftCalculated += OnFftCalculated;

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
        /// 接続されている外部デバイス一覧を取得または設定します
        /// </summary>
        public ObservableCollection<DeviceViewModel> RemovableDrives { get; set; } = new ObservableCollection<DeviceViewModel>();

        private DeviceViewModel? _selectedDevice;

        /// <summary>
        /// 現在選択されている同期対象デバイス。
        /// 変更時にデバイスへの接続や初期ディレクトリ読み込みを行います。
        /// </summary>
        public DeviceViewModel? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (_selectedDevice != value)
                {
                    // Disconnect previous MTP device if applicable
                    if (_selectedDevice?.Type == DeviceType.MTP && _selectedDevice.MtpDevice != null && _selectedDevice.MtpDevice.IsConnected)
                    {
                        try { _selectedDevice.MtpDevice.Disconnect(); } catch { }
                    }

                    _selectedDevice = value;
                    OnPropertyChanged();

                    if (_selectedDevice != null)
                    {
                        if (_selectedDevice.Type == DeviceType.MTP && _selectedDevice.MtpDevice != null)
                        {
                            try
                            {
                                _selectedDevice.MtpDevice.Connect();
                                LoadDeviceDirectories(@"\"); // Root for MTP
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Failed to connect to device: {ex.Message}");
                            }
                        }
                        else if (_selectedDevice.Type == DeviceType.FileSystem && _selectedDevice.Drive != null)
                        {
                            LoadDeviceDirectories(_selectedDevice.Drive.RootDirectory.FullName);
                        }
                    }
                }
            }
        }

        private double _transferProgress;
        /// <summary>
        /// ファイル転送の進捗状況（0-100）。
        /// </summary>
        public double TransferProgress
        {
            get => _transferProgress;
            set
            {
                _transferProgress = value;
                OnPropertyChanged();
            }
        }

        private bool _isTransferring;
        /// <summary>
        /// ファイル転送中かどうか。
        /// </summary>
        public bool IsTransferring
        {
            get => _isTransferring;
            set
            {
                _isTransferring = value;
                OnPropertyChanged();
            }
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

        /// <summary>
        /// イコライザー周波数バンドのViewModelコレクションを取得または設定します
        /// </summary>
        public ObservableCollection<BandViewModel> Bands { get; set; }

        /// <summary>
        /// イコライザープリセットのコレクションを取得または設定します
        /// </summary>
        public ObservableCollection<EqualizerPreset> Presets { get; set; }

        /// <summary>
        /// ライブラリ内のアルバムコレクションを取得または設定します
        /// </summary>
        public ObservableCollection<Album> Albums { get; set; } = new ObservableCollection<Album>();

        /// <summary>
        /// ユーザーが作成したプレイリストのコレクションを取得または設定します
        /// </summary>
        public ObservableCollection<UserPlaylist> UserPlaylists
        {
            get => _userPlaylists;
            set
            {
                _userPlaylists = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 選択中のプレイリストに含まれる楽曲コレクションを取得または設定します
        /// </summary>
        public ObservableCollection<Track> PlaylistTracks
        {
            get => _playlistTracks;
            set
            {
                _playlistTracks = value;
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
        /// プレイリスト選択画面が表示されているかどうかを示す値を取得または設定します
        /// </summary>
        public bool IsPlaylistSelectorVisible
        {
            get => _isPlaylistSelectorVisible;
            set
            {
                _isPlaylistSelectorVisible = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// プレイリスト楽曲一覧が表示されているかどうかを示す値を取得します
        /// </summary>
        public bool IsPlaylistTracksVisible => CurrentViewType == ViewType.PlaylistTracks || CurrentViewType == ViewType.Favorites;

        /// <summary>
        /// データ読み込み中かどうかを示す値を取得または設定します
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
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
            get => _audioService.IsShuffleEnabled;
            set
            {
                _audioService.IsShuffleEnabled = value;
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
            get => _currentTrack;
            set
            {
                _currentTrack = value;
                OnPropertyChanged();

                if (_currentTrack != null)
                {
                    CurrentAlbum = Albums.FirstOrDefault(a => a.Tracks.Contains(_currentTrack));
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
            get => _isPlaying;
            set
            {
                _isPlaying = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 現在の再生時間表示文字列を取得または設定します
        /// </summary>
        public string CurrentTimeDisplay
        {
            get => _currentTimeDisplay;
            set
            {
                _currentTimeDisplay = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 総再生時間表示文字列を取得または設定します
        /// </summary>
        public string TotalTimeDisplay
        {
            get => _totalTimeDisplay;
            set
            {
                _totalTimeDisplay = value;
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
            get => _progress;
            set
            {
                _progress = value;
                OnPropertyChanged();
                if (_isDraggingProgress)
                {
                    _audioService.SeekTo(value);
                }
            }
        }

        /// <summary>
        /// シークバーがドラッグ操作中かどうか
        /// </summary>
        public bool IsDraggingProgress
        {
            get => _isDraggingProgress;
            set => _isDraggingProgress = value;
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

        /// <summary>
        /// 選択されているイコライザープリセットを取得または設定します
        /// </summary>
        public EqualizerPreset? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (_selectedPreset != value)
                {
                    _selectedPreset = value;
                    OnPropertyChanged();
                    if (_selectedPreset != null)
                    {
                        ApplyPreset(_selectedPreset);
                        var settings = _settingsService.LoadSettings();
                        settings.LastUsedEffectPreset = _selectedPreset.Name;
                        _settingsService.SaveSettings(settings);
                    }
                }
            }
        }

        // コマンド定義

        /// <summary>
        /// 音楽フォルダーを開くコマンドを取得します
        /// </summary>
        public ICommand OpenFolderCommand { get; }

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
                        RefreshDrives();
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
        /// イコライザープリセットを保存するコマンドを取得します
        /// </summary>
        public ICommand SavePresetCommand { get; }

        /// <summary>
        /// イコライザープリセットを削除するコマンドを取得します
        /// </summary>
        public ICommand DeletePresetCommand { get; }

        /// <summary>
        /// イコライザー設定をリセットするコマンドを取得します
        /// </summary>
        public ICommand ResetPresetCommand { get; }

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
        /// 新しいプレイリストを作成するコマンドを取得します
        /// </summary>
        public ICommand CreatePlaylistCommand { get; }

        /// <summary>
        /// 楽曲をプレイリストに追加するコマンドを取得します
        /// </summary>
        public ICommand AddToPlaylistCommand { get; }

        /// <summary>
        /// 指定したプレイリストを表示するコマンドを取得します
        /// </summary>
        public ICommand ShowPlaylistCommand { get; }

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
        /// プレイリスト選択画面を表示するコマンドを取得します
        /// </summary>
        public ICommand ShowPlaylistSelectorCommand { get; }

        /// <summary>
        /// プレイリスト追加ダイアログを表示するコマンドを取得します
        /// </summary>
        public ICommand ShowAddToPlaylistDialogCommand { get; }

        /// <summary>
        /// プレイリストを削除するコマンドを取得します
        /// </summary>
        public ICommand DeletePlaylistCommand { get; }

        /// <summary>
        /// プレイリストを再生するコマンドを取得します
        /// </summary>
        public ICommand PlayPlaylistCommand { get; }

        /// <summary>
        /// プレイリストをシャッフル再生するコマンドを取得します
        /// </summary>
        public ICommand ShufflePlayPlaylistCommand { get; }

        /// <summary>
        /// プレイリスト名を変更するコマンドを取得します
        /// </summary>
        public ICommand RenamePlaylistCommand { get; }

        /// <summary>
        /// プレイリストから楽曲を削除するコマンドを取得します
        /// </summary>
        public ICommand RemoveFromPlaylistCommand { get; }

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
        /// アルバムをプレイリストに追加するダイアログを表示するコマンドを取得します
        /// </summary>
        public ICommand ShowAddAlbumToPlaylistDialogCommand { get; }

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

        /// <summary>
        /// 選択された楽曲を外部デバイスへ転送するコマンドを取得します
        /// </summary>
        public ICommand TransferSelectedCommand { get; }

        /// <summary>
        /// 接続中の外部ドライブ一覧を再検出するコマンドを取得します
        /// </summary>
        public ICommand RefreshDrivesCommand { get; }

        /// <summary>
        /// デバイス内の指定ディレクトリへ移動するコマンドを取得します
        /// </summary>
        public ICommand NavigateDirectoryCommand { get; }

        /// <summary>
        /// デバイス内の親ディレクトリへ移動するコマンドを取得します
        /// </summary>
        public ICommand NavigateUpCommand { get; }

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
            get => _isAlbumRepeat;
            set
            {
                _isAlbumRepeat = value;
                _audioService.IsRepeatEnabled = value;
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
        /// リピート再生の有効/無効を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleRepeatCommand { get; }

        /// <summary>
        /// 選択されたトラックをプレイリストに追加するコマンドを取得します
        /// </summary>
        public ICommand AddSelectedToPlaylistCommand { get; }

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
        private float _preMuteVolume = 1.0f;

        private void ToggleMute()
        {
            if (_isMuted)
            {
                Volume = _preMuteVolume;
                _isMuted = false;
            }
            else
            {
                _preMuteVolume = Volume > 0 ? Volume : 1.0f; // Save reasonable previous volume
                Volume = 0;
                _isMuted = true;
            }
        }

        /// <summary>
        /// 音量（0.0 - 1.0）
        /// 変更時に設定ファイルへ保存されます
        /// </summary>
        public float Volume
        {
            get => _audioService.Volume;
            set
            {
                if (_audioService.Volume != value)
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
            }
        }

        /// <summary>
        /// 音量のパーセント表示文字列を取得します
        /// </summary>
        public string VolumePercent => $"{(int)(Volume * 100)}%";

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

        /// <summary>
        /// デバイス内のディレクトリアイテム一覧を取得または設定します
        /// </summary>
        public ObservableCollection<DirectoryItem> DeviceDirectories { get; set; } = new ObservableCollection<DirectoryItem>();

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
        /// ディレクトリ一覧を再読み込みするコマンドを取得します
        /// </summary>
        public ICommand RefreshDirectoryCommand { get; }

        /// <summary>
        /// 外部デバイス管理ダイアログを表示するコマンドを取得します
        /// </summary>
        public ICommand ShowDeviceManagerCommand { get; }

        /// <summary>
        /// 設定ダイアログを表示するコマンドを取得します
        /// </summary>
        public ICommand ShowSettingsCommand { get; }

        private string _currentDevicePath = string.Empty;
        /// <summary>
        /// 現在デバイス上で表示しているパス
        /// </summary>
        public string CurrentDevicePath
        {
            get => _currentDevicePath;
            set
            {
                _currentDevicePath = value;
                OnPropertyChanged();
            }
        }

        private DirectoryItem? _selectedDeviceDirectory;

        /// <summary>
        /// 選択されているディレクトリアイテムを取得または設定します
        /// </summary>
        public DirectoryItem? SelectedDeviceDirectory
        {
            get => _selectedDeviceDirectory;
            set
            {
                _selectedDeviceDirectory = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 接続されているリムーバブルドライブとMTPデバイスを検出し、リストを更新します。
        /// </summary>
        private void RefreshDrives()
        {
            if (IsTransferring) return; // 転送中の切断を防止

            RemovableDrives.Clear();

            // Add File System Drives
            // ファイルシステムドライブの追加
            var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable).ToList();
            foreach (var drive in drives)
            {
                RemovableDrives.Add(new DeviceViewModel
                {
                    Name = $"{drive.VolumeLabel} ({drive.Name})",
                    Type = DeviceType.FileSystem,
                    Drive = drive,
                    RootPath = drive.RootDirectory.FullName
                });
            }

            // Add MTP Devices
            // MTPデバイスの追加
            try
            {
                var devices = MediaDevice.GetDevices();
                foreach (var device in devices)
                {
                    RemovableDrives.Add(new DeviceViewModel
                    {
                        Name = device.FriendlyName,
                        Type = DeviceType.MTP,
                        MtpDevice = device,
                        RootPath = @"\" // MTP root
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error listing MTP devices: {ex.Message}");
            }

            SelectedDevice = RemovableDrives.FirstOrDefault();
        }

        /// <summary>
        /// 指定されたパスのディレクトリとファイルをロードし、デバイスディレクトリリストを更新します。
        /// </summary>
        /// <param name="path">ロード対象のディレクトリパス。</param>
        private void LoadDeviceDirectories(string path)
        {
            try
            {
                DeviceDirectories.Clear();
                CurrentDevicePath = path;

                if (SelectedDevice == null) return;

                if (SelectedDevice.Type == DeviceType.FileSystem)
                {
                    if (Directory.Exists(path))
                    {
                        // Add Directories
                        // ディレクトリの追加
                        var dirs = Directory.GetDirectories(path);
                        foreach (var dir in dirs)
                        {
                            DeviceDirectories.Add(new DirectoryItem
                            {
                                Name = Path.GetFileName(dir),
                                FullPath = dir,
                                IsFolder = true
                            });
                        }

                        // Add Files
                        // ファイルの追加
                        var files = Directory.GetFiles(path);
                        foreach (var file in files)
                        {
                            DeviceDirectories.Add(new DirectoryItem
                            {
                                Name = Path.GetFileName(file),
                                FullPath = file,
                                IsFolder = false
                            });
                        }
                    }
                }
                else if (SelectedDevice.Type == DeviceType.MTP && SelectedDevice.MtpDevice != null)
                {
                    if (SelectedDevice.MtpDevice.IsConnected)
                    {
                        // Add Directories
                        var dirs = SelectedDevice.MtpDevice.GetDirectories(path);
                        foreach (var dir in dirs)
                        {
                            DeviceDirectories.Add(new DirectoryItem
                            {
                                Name = Path.GetFileName(dir),
                                FullPath = dir,
                                IsFolder = true
                            });
                        }

                        // Add Files
                        // ファイルの追加
                        var files = SelectedDevice.MtpDevice.GetFiles(path);
                        foreach (var file in files)
                        {
                            DeviceDirectories.Add(new DirectoryItem
                            {
                                Name = Path.GetFileName(file),
                                FullPath = file,
                                IsFolder = false
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading directories: {ex.Message}");
                MessageBox.Show($"Error loading directory: {ex.Message}");
            }

            CheckDeviceAlbums();
        }

        /// <summary>
        /// ライブラリ内のアルバムがデバイス上に存在するかどうかをチェックし、ステータスを更新します。
        /// </summary>
        private async void CheckDeviceAlbums()
        {
            if (SelectedDevice == null || string.IsNullOrEmpty(CurrentDevicePath)) return;

            await Task.Run(() =>
            {
                foreach (var album in Albums)
                {
                    bool allTracksExist = true;

                    if (album.Tracks != null && album.Tracks.Count > 0)
                    {
                        foreach (var track in album.Tracks)
                        {
                            string artist = SanitizeFileName(track.Artist);
                            string albumName = SanitizeFileName(track.Album);
                            string fileName = System.IO.Path.GetFileName(track.FilePath);
                            bool trackExists = false;

                            try
                            {
                                if (SelectedDevice.Type == DeviceType.FileSystem)
                                {
                                    string path = System.IO.Path.Combine(CurrentDevicePath, artist, albumName, fileName);
                                    trackExists = System.IO.File.Exists(path);
                                }
                                else if (SelectedDevice.Type == DeviceType.MTP && SelectedDevice.MtpDevice != null && SelectedDevice.MtpDevice.IsConnected)
                                {
                                    string path = System.IO.Path.Combine(CurrentDevicePath, artist, albumName, fileName);
                                    trackExists = SelectedDevice.MtpDevice.FileExists(path);
                                }
                            }
                            catch { }

                            if (!trackExists)
                            {
                                allTracksExist = false;
                                break;
                            }
                        }
                    }
                    else
                    {
                        allTracksExist = false;
                    }

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        album.IsOnDevice = allTracksExist;
                        if (allTracksExist) album.IsSelected = false;
                    });
                }
            });
        }

        private void NavigateDirectory(DirectoryItem? dir)
        {
            if (dir == null || !dir.IsFolder) return;
            LoadDeviceDirectories(dir.FullPath);
        }

        private void NavigateUp()
        {
            try
            {
                if (string.IsNullOrEmpty(CurrentDevicePath) || SelectedDevice == null) return;

                if (SelectedDevice.Type == DeviceType.FileSystem)
                {
                    var parent = Directory.GetParent(CurrentDevicePath);
                    if (parent != null)
                    {
                        LoadDeviceDirectories(parent.FullName);
                    }
                }
                else if (SelectedDevice.Type == DeviceType.MTP)
                {
                    if (CurrentDevicePath == @"\" || CurrentDevicePath == "/") return;

                    var parentPath = Path.GetDirectoryName(CurrentDevicePath);
                    if (string.IsNullOrEmpty(parentPath)) parentPath = @"\";
                    LoadDeviceDirectories(parentPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating up: {ex.Message}");
            }
        }

        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown";
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            return new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        }

        /// <summary>
        /// 選択されたアルバムまたはトラックを、現在選択されているデバイスへ転送します。
        /// </summary>
        private async void TransferSelected()
        {
            if (SelectedDevice == null)
            {
                MessageBox.Show("Please select a device first.", "No Device Selected");
                return;
            }

            string destinationFolder = !string.IsNullOrEmpty(CurrentDevicePath) ? CurrentDevicePath : SelectedDevice.RootPath;

            if (SelectedDevice.Type == DeviceType.FileSystem && !destinationFolder.StartsWith(SelectedDevice.RootPath, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Current folder is not on the selected drive.", "Error");
                return;
            }

            var tracksToTransfer = new List<Track>();

            foreach (var album in Albums.Where(a => a.IsSelected))
            {
                tracksToTransfer.AddRange(album.Tracks);
            }

            foreach (var album in Albums)
            {
                foreach (var track in album.Tracks.Where(t => t.IsSelected))
                {
                    if (!tracksToTransfer.Contains(track))
                    {
                        tracksToTransfer.Add(track);
                    }
                }
            }

            if (tracksToTransfer.Count == 0)
            {
                MessageBox.Show("Please select at least one album or track to transfer.", "No Items Selected");
                return;
            }

            IsTransferring = true;
            TransferProgress = 0;

            try
            {
                var progress = new Progress<double>(p => TransferProgress = p);

                if (SelectedDevice.Type == DeviceType.FileSystem)
                {
                    await Task.Run(() =>
                    {
                        int total = tracksToTransfer.Count;
                        int current = 0;
                        foreach (var track in tracksToTransfer)
                        {
                            if (!System.IO.File.Exists(track.FilePath)) continue;

                            string artist = SanitizeFileName(track.Artist);
                            string album = SanitizeFileName(track.Album);
                            string fileName = System.IO.Path.GetFileName(track.FilePath);

                            string targetDir = System.IO.Path.Combine(destinationFolder, artist, album);
                            string destPath = System.IO.Path.Combine(targetDir, fileName);

                            // Skip if already exists
                            if (System.IO.File.Exists(destPath))
                            {
                                current++;
                                ((IProgress<double>)progress).Report((double)current / total * 100);
                                continue;
                            }

                            if (!System.IO.Directory.Exists(targetDir))
                            {
                                System.IO.Directory.CreateDirectory(targetDir);
                            }

                            System.IO.File.Copy(track.FilePath, destPath, true);

                            current++;
                            ((IProgress<double>)progress).Report((double)current / total * 100);
                        }
                    });
                }
                else if (SelectedDevice.Type == DeviceType.MTP && SelectedDevice.MtpDevice != null)
                {
                    var mtpDevice = SelectedDevice.MtpDevice;
                    await Task.Run(() =>
                    {
                        int total = tracksToTransfer.Count;
                        int current = 0;
                        foreach (var track in tracksToTransfer)
                        {
                            if (!System.IO.File.Exists(track.FilePath)) continue;

                            string artist = SanitizeFileName(track.Artist);
                            string album = SanitizeFileName(track.Album);
                            string fileName = System.IO.Path.GetFileName(track.FilePath);

                            string targetDir = System.IO.Path.Combine(destinationFolder, artist, album);

                            try
                            {
                                string artistDir = System.IO.Path.Combine(destinationFolder, artist);
                                if (!mtpDevice.DirectoryExists(artistDir))
                                {
                                    mtpDevice.CreateDirectory(artistDir);
                                }
                                if (!mtpDevice.DirectoryExists(targetDir))
                                {
                                    mtpDevice.CreateDirectory(targetDir);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error creating MTP directory: {ex.Message}");
                            }

                            string destPath = System.IO.Path.Combine(targetDir, fileName);

                            // Skip if already exists
                            bool fileExists = false;
                            try { fileExists = mtpDevice.FileExists(destPath); } catch { }

                            if (fileExists)
                            {
                                current++;
                                ((IProgress<double>)progress).Report((double)current / total * 100);
                                continue;
                            }

                            mtpDevice.UploadFile(track.FilePath, destPath);

                            current++;
                            ((IProgress<double>)progress).Report((double)current / total * 100);
                        }
                    });
                }

                LoadDeviceDirectories(destinationFolder);
                CheckDeviceAlbums(); // Refresh status

                MessageBox.Show("Transfer completed successfully!", "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Transfer failed: {ex.Message}", "Error");
            }
            finally
            {
                IsTransferring = false;
                TransferProgress = 0;
            }
        }

        /// <summary>
        /// ライブラリをソート条件（アーティスト/アルバム）と昇順/降順に従って並び替えます。
        /// </summary>
        private void SortLibrary()
        {
            if (!Albums.Any()) return;

            var sorted = Albums.ToList();
            switch (SelectedSortOption)
            {
                case "Artist":
                    sorted = IsAscending ? sorted.OrderBy(a => a.Artist).ToList() : sorted.OrderByDescending(a => a.Artist).ToList();
                    break;
                case "Album":
                    sorted = IsAscending ? sorted.OrderBy(a => a.Title).ToList() : sorted.OrderByDescending(a => a.Title).ToList();
                    break;
            }

            Albums.Clear();
            foreach (var album in sorted) Albums.Add(album);
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
        private void OnTrackChanged(Track track)
        {
            // Increment generation to invalidate pending FFT updates
            System.Threading.Interlocked.Increment(ref _spectrumGeneration);

            // Reset Spectrum immediately to prevent glitches
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var item in SpectrumValues)
                {
                    item.Value = 0;
                }
            });

            if (CurrentTrack != null)
            {
                CurrentTrack.IsPlaying = false;
            }

            CurrentTrack = track;
            Progress = 0;

            if (track != null)
            {
                track.IsPlaying = true;
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!IsPlaylistTracksVisible && PlaybackListTracks != null && !PlaybackListTracks.Contains(track))
                    {
                        var album = Albums.FirstOrDefault(a => a.Tracks.Contains(track));
                        if (album != null)
                        {
                            PlaybackListName = album.Title;
                            PlaybackListSubtitle = album.Artist;
                            PlaybackListTracks = new System.Collections.ObjectModel.ObservableCollection<Track>(album.Tracks);
                        }
                    }
                });
            }

            // Phase 9: Logic Update
            // If viewing Favorites, FORCE Background to Galaxy.
            if (IsFavoritesView && _favoritesImage != null)
            {
                PlaylistBackgroundImage = _favoritesImage;
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
                                        PlaylistBackgroundImage = image;
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
                                });
                            }
                            else
                            {
                                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
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

            if (_audioService != null)
            {
                var current = _audioService.CurrentTime;
                var total = _audioService.TotalTime;

                CurrentTimeDisplay = current.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
                TotalTimeDisplay = total.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);

                if (total.TotalSeconds > 0 && !_isDraggingProgress)
                {
                    Progress = (current.TotalSeconds / total.TotalSeconds) * 100;
                }
            }
        }

        /// <summary>
        /// フォルダー選択ダイアログを開き、新しいライブラリパスを設定します。
        /// </summary>
        private void OpenFolder(object? obj)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                string selectedPath = dialog.FolderName;

                var settings = _settingsService.LoadSettings();
                settings.LastLibraryPath = selectedPath;
                _settingsService.SaveSettings(settings);

                LoadLibrary(selectedPath);
            }
        }

        /// <summary>
        /// 指定されたイコライザープリセットを適用します。
        /// </summary>
        private void ApplyPreset(EqualizerPreset preset)
        {
            if (preset == null || preset.Gains == null) return;

            for (int i = 0; i < Bands.Count && i < preset.Gains.Count; i++)
            {
                Bands[i].Gain = preset.Gains[i];
            }
        }

        /// <summary>
        /// 指定されたフォルダー（または設定された最後のパス）からライブラリを非同期でロードします。
        /// サポートされている音声ファイルを検索し、メタデータを読み取ってアルバムごとにグループ化します。
        /// </summary>
        private async void LoadLibrary(string? rootFolder = null)
        {
            if (string.IsNullOrEmpty(rootFolder))
            {
                var settings = _settingsService.LoadSettings();
                rootFolder = settings.LastLibraryPath;
            }

            if (string.IsNullOrEmpty(rootFolder) || !Directory.Exists(rootFolder)) return;

            IsLoading = true;
            Albums.Clear();

            await Task.Run(() =>
            {
                var files = Directory.GetFiles(rootFolder, "*.*", SearchOption.AllDirectories)
                                     .Where(f => SupportedAudioExtensions.Contains(Path.GetExtension(f).ToLower(System.Globalization.CultureInfo.InvariantCulture)))
                                     .ToList();

                // _albumArtCache is no longer used here for bulk loading
                var tracks = new System.Collections.Concurrent.ConcurrentBag<Track>();

                Parallel.ForEach(files, file =>
                {
                    var track = new Track
                    {
                        FilePath = file,
                        Title = Path.GetFileNameWithoutExtension(file),
                        Artist = "Unknown Artist",
                        Album = "Unknown Album"
                    };

                    try
                    {
                        using (var tfile = TagLib.File.Create(file))
                        {
                            track.Title = tfile.Tag.Title ?? track.Title;
                            track.Artist = tfile.Tag.FirstPerformer ?? "Unknown Artist";
                            track.Album = tfile.Tag.Album ?? "Unknown Album";
                            track.Duration = tfile.Properties.Duration;
                            track.Year = tfile.Tag.Year;
                            track.TrackNumber = tfile.Tag.Track;

                            track.Bitrate = tfile.Properties.AudioBitrate;
                            track.SampleRate = tfile.Properties.AudioSampleRate;
                            track.BitsPerSample = tfile.Properties.BitsPerSample;
                            string ext = Path.GetExtension(file).ToLower(System.Globalization.CultureInfo.InvariantCulture);
                            track.Format = ext.TrimStart('.').ToUpper(System.Globalization.CultureInfo.InvariantCulture);

                            track.IsLossless = LosslessAudioExtensions.Contains(ext);
                            track.IsHiRes = track.SampleRate > 48000 || track.BitsPerSample > 16;

                            // Image loading removed to save memory. 
                            // Images will be loaded on-demand via AlbumArtLoader in the UI.
                        }
                    }
                    catch { }

                    if (_favoritePaths.Contains(track.FilePath))
                    {
                        track.IsFavorite = true;
                    }

                    tracks.Add(track);
                });

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var grouped = tracks.GroupBy(t => t.Album);
                    foreach (var g in grouped)
                    {
                        // Find most common year in album or take first
                        uint albumYear = g.Select(t => t.Year).Where(y => y > 0).GroupBy(y => y).OrderByDescending(z => z.Count()).FirstOrDefault()?.Key ?? 0;

                        Albums.Add(new Album
                        {
                            Title = g.Key,
                            Artist = g.First().Artist,
                            CoverImage = null, // Will be loaded by UI
                            Tracks = g.OrderBy(t => t.TrackNumber).ThenBy(t => t.Title).ToList(),
                            Year = albumYear
                        });
                    }
                    SortLibrary();
                    _audioService.SetPlaylist(tracks.ToList());
                });
            });

            IsLoading = false;
        }

        private void ToggleFavorite(object? obj)
        {
            var targetTrack = obj as Track ?? CurrentTrack;
            if (targetTrack != null)
            {
                targetTrack.IsFavorite = !targetTrack.IsFavorite;
                if (targetTrack == CurrentTrack)
                {
                    OnPropertyChanged(nameof(CurrentTrack)); // Refresh UI
                }

                if (targetTrack.IsFavorite)
                {
                    if (!_favoritePaths.Contains(targetTrack.FilePath))
                    {
                        _favoritePaths.Add(targetTrack.FilePath);
                        // Immediate update if viewing favorites
                        if (IsFavoritesView)
                        {
                            PlaylistTracks.Add(targetTrack);
                        }
                    }
                }
                else
                {
                    _favoritePaths.Remove(targetTrack.FilePath);
                    // Immediate update if viewing favorites
                    if (IsFavoritesView)
                    {
                        var trackToRemove = PlaylistTracks.FirstOrDefault(t => t.FilePath == targetTrack.FilePath);
                        if (trackToRemove != null)
                        {
                            PlaylistTracks.Remove(trackToRemove);
                        }
                    }
                }
                _libraryService?.SaveFavorites(_favoritePaths);
            }
        }

        private void PlayNext(object? obj)
        {
            if (obj is Track track)
            {
                if (CurrentTrack != null && PlayQueue.Contains(CurrentTrack))
                {
                    int currentIndex = PlayQueue.IndexOf(CurrentTrack);
                    PlayQueue.Insert(currentIndex + 1, track);
                }
                else
                {
                    PlayQueue.Insert(0, track);
                }
                _audioService.SetPlaylist(PlayQueue.ToList());
            }
        }

        private void EnqueueTrack(object? obj)
        {
            if (obj is Track track)
            {
                PlayQueue.Add(track);
                _audioService.SetPlaylist(PlayQueue.ToList());
            }
        }

        private void ShowTrackProperties(object? obj)
        {
            if (obj is Track track)
            {
                var info = $"Title: {track.Title}\n" +
                           $"Artist: {track.Artist}\n" +
                           $"Album: {track.Album}\n" +
                           $"Duration: {track.Duration}\n" +
                           $"File Size: {new System.IO.FileInfo(track.FilePath).Length / 1024 / 1024.0:F2} MB\n\n" +
                           $"File Path:\n{track.FilePath}";
                System.Windows.MessageBox.Show(info, "Track Properties", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }

        private void ShowQueueDialog()
        {
            var dialog = new PlayQueueDialog();
            dialog.Owner = App.Current.MainWindow;
            dialog.DataContext = this;
            dialog.Show();
        }

        private void OpenFileLocation(object? obj)
        {
            if (obj is Track track && System.IO.File.Exists(track.FilePath))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{track.FilePath}\"");
            }
        }

        private void DeleteTrack(object? obj)
        {
            if (obj is Track track)
            {
                var result = System.Windows.MessageBox.Show($"Remove '{track.Title}' from Library?\n(File will NOT be deleted from disk)", "Confirm", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    // Remove from Albums
                    var album = Albums.FirstOrDefault(a => a.Tracks.Contains(track));
                    if (album != null)
                    {
                        album.Tracks.Remove(track);
                        if (album.Tracks.Count == 0)
                        {
                            Albums.Remove(album);
                        }
                    }

                    // Remove from Playlists and views
                    PlaylistTracks.Remove(track);
                    PlaybackListTracks.Remove(track);
                }
            }
        }

        private void ToggleRepeat(object? obj)
        {
            IsAlbumRepeat = !IsAlbumRepeat;
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
        /// トラックをプレイリストに追加するためのダイアログを表示します。
        /// </summary>
        private void ShowAddToPlaylistDialog(object? parameter)
        {
            if (parameter is Track track)
            {
                ShowPlaylistSelectionDialog(selectedPlaylist =>
                {
                    if (!selectedPlaylist.TrackPaths.Contains(track.FilePath))
                    {
                        selectedPlaylist.TrackPaths.Add(track.FilePath);
                        UpdatePlaylistThumbnails(selectedPlaylist);
                        _playlistService?.SavePlaylists(UserPlaylists.ToList());

                        // Immediate update if viewing this playlist
                        if (CurrentPlaylistName == selectedPlaylist.Name && IsPlaylistTracksVisible)
                        {
                            PlaylistTracks.Add(track);
                        }

                        MessageBox.Show($"Added '{track.Title}' to '{selectedPlaylist.Name}'", "Track Added");
                    }
                    else
                    {
                        MessageBox.Show($"'{track.Title}' is already in '{selectedPlaylist.Name}'", "Already Added");
                    }
                });
            }
        }

        /// <summary>
        /// 選択されている複数のトラックを、選択したプレイリストに追加します。
        /// </summary>
        private void AddSelectedToPlaylist(object? parameter)
        {
            var selectedTracks = new List<Track>();

            // Collect from Album tracks
            if (Albums != null)
            {
                foreach (var album in Albums)
                {
                    if (album.Tracks != null)
                    {
                        foreach (var track in album.Tracks)
                        {
                            if (track.IsSelected) selectedTracks.Add(track);
                        }
                    }
                }
            }

            // Collect from Playlist tracks
            if (PlaylistTracks != null)
            {
                foreach (var track in PlaylistTracks)
                {
                    if (track.IsSelected) selectedTracks.Add(track);
                }
            }

            // Also include the parameter if it's a track and not already selected
            if (parameter is Track paramTrack && !selectedTracks.Contains(paramTrack))
            {
                selectedTracks.Add(paramTrack);
            }

            if (selectedTracks.Count == 0)
            {
                MessageBox.Show("No tracks selected.", "Add to Playlist");
                return;
            }

            ShowPlaylistSelectionDialog(selectedPlaylist =>
            {
                int addedCount = 0;
                foreach (var track in selectedTracks)
                {
                    if (!selectedPlaylist.TrackPaths.Contains(track.FilePath))
                    {
                        selectedPlaylist.TrackPaths.Add(track.FilePath);

                        // Immediate update if viewing this playlist
                        if (CurrentPlaylistName == selectedPlaylist.Name && IsPlaylistTracksVisible)
                        {
                            PlaylistTracks?.Add(track);
                        }

                        addedCount++;
                    }
                }

                if (addedCount > 0)
                {
                    UpdatePlaylistThumbnails(selectedPlaylist);
                    _playlistService?.SavePlaylists(UserPlaylists.ToList());
                    MessageBox.Show($"Added {addedCount} tracks to '{selectedPlaylist.Name}'", "Tracks Added");

                    // Clear selection
                    foreach (var track in selectedTracks)
                    {
                        track.IsSelected = false;
                    }
                }
                else
                {
                    MessageBox.Show("All selected tracks are already in the playlist.", "No Tracks Added");
                }
            });
        }

        private void ShowPlaylistSelectionDialog(Action<UserPlaylist> onPlaylistSelected)
        {
            var dialog = new Window
            {
                Title = "Add to Playlist",
                Width = 300,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(20, 20, 20))
            };
            dialog.MouseLeftButtonDown += (s, e) => dialog.DragMove();

            var stackPanel = new StackPanel { Margin = new Thickness(20) };

            var title = new TextBlock
            {
                Text = "Select Playlist",
                Foreground = Brushes.White,
                FontSize = 18,
                Margin = new Thickness(0, 0, 0, 15),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stackPanel.Children.Add(title);

            var newPlaylistButton = new Button
            {
                Content = "+ NEW PLAYLIST",
                Width = 150,
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            newPlaylistButton.Click += (s, e) =>
            {
                int previousCount = UserPlaylists.Count;
                CreatePlaylist(null);
                if (UserPlaylists.Count > previousCount)
                {
                    onPlaylistSelected(UserPlaylists.Last());
                    dialog.Close();
                }
            };
            stackPanel.Children.Add(newPlaylistButton);

            var listBox = new ListBox
            {
                ItemsSource = UserPlaylists,
                DisplayMemberPath = "Name",
                Height = 250,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 20)
            };
            stackPanel.Children.Add(listBox);

            var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

            var addButton = new Button
            {
                Content = "ADD",
                Width = 80,
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(0, 255, 255)),
                Foreground = Brushes.Black,
                Margin = new Thickness(0, 0, 10, 0),
                BorderThickness = new Thickness(0)
            };
            addButton.Click += (s, e) =>
            {
                if (listBox.SelectedItem is UserPlaylist selectedPlaylist)
                {
                    onPlaylistSelected(selectedPlaylist);
                    dialog.Close();
                }
            };
            buttonsPanel.Children.Add(addButton);

            var cancelButton = new Button
            {
                Content = "CANCEL",
                Width = 80,
                Height = 30,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                BorderThickness = new Thickness(1)
            };
            cancelButton.Click += (s, e) => dialog.Close();
            buttonsPanel.Children.Add(cancelButton);

            stackPanel.Children.Add(buttonsPanel);
            dialog.Content = stackPanel;
            dialog.ShowDialog();
        }

        private void SavePreset(object? obj)
        {
            try
            {
                var inputBox = new Views.InputBox("Enter Preset Name:", $"User Preset {DateTime.Now:MM-dd HH:mm}");
                if (inputBox.ShowDialog() == true)
                {
                    string name = inputBox.InputText;
                    if (string.IsNullOrWhiteSpace(name)) name = "Untitled Preset";

                    var newPreset = new EqualizerPreset
                    {
                        Name = name,
                        Gains = Bands.Select(b => b.Gain).ToList()
                    };
                    Presets.Add(newPreset);
                    _equalizerApplicationService?.SavePresets(Presets.ToList());
                    SelectedPreset = newPreset;
                    MessageBox.Show("プリセットを保存しました。\nPreset Saved.", "保存完了");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving preset: {ex.Message}", "Error");
            }
        }

        private void DeletePreset(object? obj)
        {
            if (SelectedPreset != null && Presets.Contains(SelectedPreset))
            {
                // Prevent deletion of default presets
                var defaultPresets = new[] { "フラット (Flat)", "ロック (Rock)", "ポップ (Pop)" }; // Corrected literals to match potential JP names if localized, but keeping safe
                if (defaultPresets.Contains(SelectedPreset.Name) || SelectedPreset.Name.Contains("Flat") || SelectedPreset.Name.Contains("Rock") || SelectedPreset.Name.Contains("Pop"))
                {
                    MessageBox.Show($"'{SelectedPreset.Name}' is a default preset and cannot be deleted.", "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (MessageBox.Show($"Are you sure you want to delete '{SelectedPreset.Name}'?", "Delete Preset", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    try
                    {
                        Presets.Remove(SelectedPreset);
                        _equalizerApplicationService?.SavePresets(Presets.ToList());
                        SelectedPreset = Presets.FirstOrDefault();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error saving presets: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        // Optionally reload presets to ensure UI is in sync with file
                        // Presets = new ObservableCollection<Preset>(_presetService.LoadPresets());
                    }
                }
            }
        }





        private void Reset(object? obj)
        {
            foreach (var band in Bands)
            {
                band.Gain = 0;
            }
            // Set preset display to "Flat" (match default preset name)
            SelectedPreset = Presets.FirstOrDefault(p => p.Name.Contains("Flat"));
        }

        /// <summary>
        /// リソースのクリーンアップを行います
        /// </summary>
        public void Cleanup()
        {
            _timer.Stop();
            _audioService.Dispose();
        }

        // Playlist management methods
        private void CreatePlaylist(object? obj)
        {
            var dialog = new Views.InputBox("New Playlist", "Enter playlist name:");
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            {
                var newPlaylist = new UserPlaylist { Name = dialog.InputText };
                UserPlaylists.Add(newPlaylist);
                _playlistService?.SavePlaylists(UserPlaylists.ToList());
            }
        }

        private void AddToPlaylist(object? obj)
        {
            if (obj is UserPlaylist playlist && CurrentTrack != null)
            {
                if (!playlist.TrackPaths.Contains(CurrentTrack.FilePath))
                {
                    playlist.TrackPaths.Add(CurrentTrack.FilePath);
                    _playlistService?.SavePlaylists(UserPlaylists.ToList());

                    // Immediate update if viewing this playlist
                    if (CurrentPlaylistName == playlist.Name && IsPlaylistTracksVisible)
                    {
                        PlaylistTracks.Add(CurrentTrack);
                    }

                    MessageBox.Show($"Added '{CurrentTrack.Title}' to '{playlist.Name}'", "Track Added");
                }
            }
        }




        /// <summary>
        /// プレイリストセクションがアクティブかどうかを示す値を取得します
        /// </summary>
        public bool IsPlaylistSectionActive => IsPlaylistSelectorVisible || (IsPlaylistTracksVisible && !IsFavoritesView);

        private bool _isFavoritesView;

        /// <summary>
        /// 現在お気に入り画面を表示しているかどうかを示す値を取得または設定します
        /// </summary>
        public bool IsFavoritesView
        {
            get => _isFavoritesView;
            set
            {
                if (_isFavoritesView != value)
                {
                    _isFavoritesView = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsPlaylistSectionActive));
                }
            }
        }

        private string _currentPlaylistName = string.Empty;

        /// <summary>
        /// 現在選択・表示されているプレイリスト名を取得または設定します
        /// </summary>
        public string CurrentPlaylistName
        {
            get => _currentPlaylistName;
            set
            {
                if (_currentPlaylistName != value)
                {
                    _currentPlaylistName = value;
                    OnPropertyChanged();
                }
            }
        }

        private void ShowPlaylist(object? obj)
        {
            if (obj is UserPlaylist playlist)
            {
                System.Diagnostics.Debug.WriteLine($"ShowPlaylist: {playlist.Name}, Tracks: {playlist.TrackPaths.Count}");

                CurrentViewType = ViewType.PlaylistTracks;




                IsFavoritesView = false;
                CurrentPlaylistName = playlist.Name;
                CurrentPlaylistName = playlist.Name;
                CurrentViewingPlaylist = playlist; // Update Public Property

                // Phase 9: When opening playlist, background should default to Now Playing (or default)
                PlaylistBackgroundImage = NowPlayingImage ?? _defaultNowPlayingImage;

                PlaylistTracks.Clear();
                foreach (var path in playlist.TrackPaths)
                {
                    System.Diagnostics.Debug.WriteLine($"Loading track: {path}");
                    var track = LoadTrack(path);
                    if (track != null)
                    {
                        PlaylistTracks.Add(track);
                        System.Diagnostics.Debug.WriteLine($"Added track: {track.Title}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to load track: {path}");
                    }
                }
                System.Diagnostics.Debug.WriteLine($"Total tracks loaded: {PlaylistTracks.Count}");
                OnPropertyChanged(nameof(IsPlaylistSectionActive));
            }
        }

        private void ShowFavorites()
        {




            IsFavoritesView = true;
            CurrentViewType = ViewType.Favorites;
            CurrentPlaylistName = "Favorites";
            CurrentPlaylistName = "Favorites";
            CurrentViewingPlaylist = null; // Important: Favorites has no UserPlaylist object

            // Phase 9: When opening favorites, background is Galaxy
            if (_favoritesImage != null)
                PlaylistBackgroundImage = _favoritesImage;

            PlaylistTracks.Clear();
            foreach (var path in _favoritePaths)
            {
                var track = LoadTrack(path);
                if (track != null)
                    PlaylistTracks.Add(track);
            }
            OnPropertyChanged(nameof(IsPlaylistSectionActive));
        }

        private void ShowLibrary()
        {
            CurrentViewType = ViewType.Albums;




            CurrentViewingPlaylist = null;
            IsFavoritesView = false;
            OnPropertyChanged(nameof(IsPlaylistSectionActive));
        }

        private void ShowFolder()
        {
            CurrentViewType = ViewType.Folders;




            CurrentViewingPlaylist = null;
            IsFavoritesView = false;
            OnPropertyChanged(nameof(IsPlaylistSectionActive));
        }

        private void ShowPlaylistSelector()
        {
            CurrentViewType = ViewType.Playlists;




            IsFavoritesView = false;
            OnPropertyChanged(nameof(IsPlaylistSectionActive));
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

        /// <summary>
        /// プレイリストを再生キューにセットして再生を開始します。
        /// </summary>
        private void PlayPlaylist(object? parameter)
        {
            if (parameter is UserPlaylist playlist && playlist.TrackPaths != null && playlist.TrackPaths.Count > 0)
            {
                var tracks = new List<Track>();
                foreach (var path in playlist.TrackPaths)
                {
                    var track = LoadTrack(path);
                    if (track != null)
                    {
                        tracks.Add(track);
                    }
                }

                if (tracks.Count > 0)
                {
                    PlayQueue = new System.Collections.ObjectModel.ObservableCollection<Track>(tracks);
                    _audioService.SetPlaylist(PlayQueue.ToList());
                    PlaybackListName = playlist.Name;
                    PlaybackListSubtitle = "Playlist";
                    PlaybackListTracks = new System.Collections.ObjectModel.ObservableCollection<Track>(tracks);
                    _audioService.PlayTrack(tracks.First());
                }
            }
        }

        /// <summary>
        /// プレイリストをシャッフルして再生キューにセットし、再生を開始します。
        /// </summary>
        private void ShufflePlayPlaylist(object? parameter)
        {
            if (parameter is UserPlaylist playlist && playlist.TrackPaths != null && playlist.TrackPaths.Count > 0)
            {
                var tracks = new List<Track>();
                foreach (var path in playlist.TrackPaths)
                {
                    var track = LoadTrack(path);
                    if (track != null)
                    {
                        tracks.Add(track);
                    }
                }

                if (tracks.Count > 0)
                {
                    var shuffled = tracks.OrderBy(x => Guid.NewGuid()).ToList();
                    PlayQueue = new System.Collections.ObjectModel.ObservableCollection<Track>(shuffled);
                    _audioService.SetPlaylist(PlayQueue.ToList());
                    PlaybackListName = playlist.Name;
                    PlaybackListSubtitle = "Playlist (Shuffled)";
                    PlaybackListTracks = new System.Collections.ObjectModel.ObservableCollection<Track>(shuffled);
                    _audioService.PlayTrack(shuffled.First());
                }
            }
        }

        /// <summary>
        /// プレイリストの名前を変更します。
        /// </summary>
        private void RenamePlaylist(object? parameter)
        {
            if (parameter is UserPlaylist playlist)
            {
                var inputBox = new Views.InputBox("新しい名前を入力してください:", playlist.Name);
                if (inputBox.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputBox.InputText))
                {
                    playlist.Name = inputBox.InputText.Trim();
                    _playlistService?.SavePlaylists(UserPlaylists.ToList());
                    OnPropertyChanged(nameof(UserPlaylists));
                }
            }
        }

        /// <summary>
        /// プレイリストを削除します。
        /// </summary>
        private void DeletePlaylist(object? parameter)
        {
            if (parameter is UserPlaylist playlist)
            {
                if (MessageBox.Show($"Are you sure you want to delete playlist '{playlist.Name}'?", "Delete Playlist", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    UserPlaylists.Remove(playlist);
                    _playlistService?.SavePlaylists(UserPlaylists.ToList());
                }
            }
        }

        /// <summary>
        /// プレイリストからトラックを削除します。
        /// </summary>
        private void RemoveFromPlaylist(object? parameter)
        {
            if (parameter is Track track)
            {
                // Special handling for Favorites View
                if (IsFavoritesView)
                {
                    // ToggleFavorite handles removal from _favoritePaths and _playlistTracks
                    ToggleFavorite(null); // Wait, ToggleFavorite assumes CurrentTrack. We need to handle the *passed* track.
                                          // Refactoring logic to allow passing track to ToggleFavorite would be ideal, but for now let's reproduce logic safely.

                    if (track.IsFavorite)
                    {
                        track.IsFavorite = false;
                        _favoritePaths.Remove(track.FilePath);

                        // Immediate update
                        PlaylistTracks.Remove(track);

                        _libraryService?.SaveFavorites(_favoritePaths);
                    }
                    return;
                }

                // Normal Playlist Logic
                if (CurrentViewingPlaylist != null)
                {
                    if (MessageBox.Show($"Remove '{track.Title}' from playlist?", "Remove Song", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        PlaylistTracks.Remove(track);
                        CurrentViewingPlaylist.TrackPaths = PlaylistTracks.Select(t => t.FilePath).ToList();
                        UpdatePlaylistThumbnails(CurrentViewingPlaylist);

                        _playlistService?.SavePlaylists(UserPlaylists.ToList());
                    }
                }
            }
        }

        /// <summary>
        /// 指定されたアルバム全体を再生キューに設定し、再生を開始します。
        /// </summary>
        private void PlayAlbum(object? parameter)
        {
            if (parameter is Album album && album.Tracks.Count > 0)
            {
                PlayQueue = new ObservableCollection<Track>(album.Tracks);
                _audioService.SetPlaylist(PlayQueue.ToList());

                PlaybackListName = album.Title;
                PlaybackListSubtitle = album.Artist;
                PlaybackListTracks = new ObservableCollection<Track>(album.Tracks);

                _audioService.PlayTrack(album.Tracks.First());
            }
        }

        private void PlayNextAlbum(object? parameter)
        {
            if (parameter is Album album && album.Tracks.Count > 0)
            {
                if (CurrentTrack != null && PlayQueue.Contains(CurrentTrack))
                {
                    int currentIndex = PlayQueue.IndexOf(CurrentTrack);
                    for (int i = 0; i < album.Tracks.Count; i++)
                    {
                        PlayQueue.Insert(currentIndex + 1 + i, album.Tracks[i]);
                    }
                }
                else
                {
                    for (int i = 0; i < album.Tracks.Count; i++)
                    {
                        PlayQueue.Insert(i, album.Tracks[i]);
                    }
                }
                _audioService.SetPlaylist(PlayQueue.ToList());
            }
        }

        private void EnqueueAlbum(object? parameter)
        {
            if (parameter is Album album && album.Tracks.Count > 0)
            {
                foreach (var track in album.Tracks)
                {
                    PlayQueue.Add(track);
                }
                _audioService.SetPlaylist(PlayQueue.ToList());
            }
        }

        private void ShowAddAlbumToPlaylistDialog(object? parameter)
        {
            if (parameter is Album album && album.Tracks.Count > 0)
            {
                ShowPlaylistSelectionDialog(selectedPlaylist =>
                {
                    bool added = false;
                    foreach (var track in album.Tracks)
                    {
                        if (!selectedPlaylist.TrackPaths.Contains(track.FilePath))
                        {
                            selectedPlaylist.TrackPaths.Add(track.FilePath);
                            added = true;
                        }
                    }
                    if (added)
                    {
                        UpdatePlaylistThumbnails(selectedPlaylist);
                        _playlistService?.SavePlaylists(UserPlaylists.ToList());
                    }
                });
            }
        }

        private void DeleteAlbum(object? parameter)
        {
            if (parameter is Album album)
            {
                var result = System.Windows.MessageBox.Show($"Remove album '{album.Title}' from Library?\n(Files will NOT be deleted from disk)", "Confirm", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    var tracksToRemove = album.Tracks.ToList();

                    Albums.Remove(album);

                    foreach (var track in tracksToRemove)
                    {
                        PlaylistTracks.Remove(track);
                        PlaybackListTracks.Remove(track);

                        if (_favoritePaths.Contains(track.FilePath))
                        {
                            _favoritePaths.Remove(track.FilePath);
                        }
                    }

                    _libraryService?.SaveFavorites(_favoritePaths);
                }
            }
        }

        // Spectrum Analyzer Logic
        private bool _isSpectrumVisible = true; // Default to true as requested

        /// <summary>
        /// スペクトラムアナライザーが表示されているかどうかを示す値を取得または設定します
        /// </summary>
        public bool IsSpectrumVisible
        {
            get => _isSpectrumVisible;
            set
            {
                if (_isSpectrumVisible != value)
                {
                    _isSpectrumVisible = value;
                    OnPropertyChanged();
                    if (value && CurrentViewType == ViewType.DeviceSync)
                    {
                        CurrentViewType = ViewType.Albums;
                    }
                }
            }
        }

        /// <summary>
        /// スペクトラムアナライザー表示へ切り替えるコマンドを取得します
        /// </summary>
        public ICommand SwitchToSpectrumCommand { get; }

        /// <summary>
        /// スペクトラムアナライザーの表示/非表示を切り替えるコマンドを取得します
        /// </summary>
        public ICommand ToggleSpectrumCommand { get; }

        /// <summary>
        /// スペクトラムアナライザーの各周波数バーのViewModelコレクションを取得します
        /// </summary>
        public ObservableCollection<SpectrumBarItem> SpectrumValues { get; } = new ObservableCollection<SpectrumBarItem>();

        private readonly TimeSpan _spectrumUpdateInterval = TimeSpan.FromMilliseconds(1000.0 / 30.0); // 約33ms (30fps)
        private DateTime _lastSpectrumUpdateTime = DateTime.MinValue;

        /// <summary>
        /// FFT（高速フーリエ変換）の計算結果を受け取り、スペクトラムアナライザーのバーの高さを更新します
        /// </summary>
        private void OnFftCalculated(object? sender, FftEventArgs e)
        {
            if (!IsSpectrumVisible) return;

            // スロットリング: 一定間隔未満の更新はスキップ
            if (DateTime.Now - _lastSpectrumUpdateTime < _spectrumUpdateInterval) return;
            _lastSpectrumUpdateTime = DateTime.Now;

            // Capture current generation
            int currentGen = _spectrumGeneration;

            int barCount = SpectrumBarCount;
            var newValues = new double[barCount];

            // FFT parameters (40Hz to 18kHz for rich musical responsiveness)
            double minFreq = 30;
            double maxFreq = 18000;
            double logMin = Math.Log10(minFreq);
            double logMax = Math.Log10(maxFreq);
            double logStep = (logMax - logMin) / barCount;

            for (int i = 0; i < barCount; i++)
            {
                // Calculate frequency range for this bar (Log scale)
                double fStart = Math.Pow(10, logMin + i * logStep);
                double fEnd = Math.Pow(10, logMin + (i + 1) * logStep);

                int iStart = (int)(fStart * 512 / 22050);
                int iEnd = (int)(fEnd * 512 / 22050);

                if (iStart < 0) iStart = 0;
                if (iEnd >= 512) iEnd = 511;
                if (iEnd < iStart) iEnd = iStart;

                double sum = 0;
                int count = 0;

                for (int index = iStart; index <= iEnd; index++)
                {
                    // Skip DC offset (index 0)
                    if (index < 1) continue;

                    if (index < e.Result.Length)
                    {
                        var c = e.Result[index];
                        double mag = Math.Sqrt(c.X * c.X + c.Y * c.Y);
                        sum += mag;
                        count++;
                    }
                }

                double avg = count > 0 ? sum / count : 0;
                double db = (avg > 1e-6) ? 20 * Math.Log10(avg) : -120;

                // Center frequency of this bar (Hz)
                double centerFreq = Math.Sqrt(fStart * fEnd);

                // High frequency tilt: compensate physical sound rolloff (+8.5dB per octave above 250Hz)
                // 高音域（250Hz以上）の周波数減衰をオクターブ単位で大幅補正
                double trebleTilt = (centerFreq > 250) ? Math.Log2(centerFreq / 250.0) * SpectrumTrebleTiltDb : 0.0;

                // Dynamic range mapping (-65dB floor threshold)
                double adjustedDb = db + 65 + trebleTilt;
                double val = Math.Max(0, adjustedDb) * SpectrumSensitivity;

                // Apply frequency band scaling coefficients (Bass / Mid / Treble)
                if (centerFreq < 250)
                {
                    // Smooth transition from sub-bass to upper bass
                    double bassRatio = Math.Min(1.0, centerFreq / 250.0);
                    double bassMultiplier = 0.45 + (SpectrumBassScale - 0.45) * bassRatio;
                    val *= bassMultiplier;
                }
                else if (centerFreq < 2500)
                {
                    val *= SpectrumMidScale;
                }
                else
                {
                    // Smooth progressive treble boost for >2.5kHz up to 18kHz
                    double trebleRatio = Math.Min(1.0, (centerFreq - 2500.0) / 12000.0);
                    double trebleMultiplier = SpectrumMidScale + (SpectrumTrebleScale - SpectrumMidScale) * Math.Pow(trebleRatio, 0.85);
                    val *= trebleMultiplier;
                }

                // Glitch Prevention: Handle NaN/Infinity
                if (double.IsNaN(val) || double.IsInfinity(val)) val = 0;

                newValues[i] = val;
            }

            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Check if generation has changed (track changed)
                if (currentGen != _spectrumGeneration) return;

                // Enforce Bar Count
                int targetCount = SpectrumBarCount;
                int currentCount = SpectrumValues.Count;

                if (currentCount < targetCount)
                {
                    for (int i = currentCount; i < targetCount; i++)
                    {
                        SpectrumValues.Add(new SpectrumBarItem { Value = 0 });
                    }
                }
                else if (currentCount > targetCount)
                {
                    for (int i = currentCount; i > targetCount; i--)
                    {
                        SpectrumValues.RemoveAt(SpectrumValues.Count - 1);
                    }
                }

                // Update with high-speed attack & liquid decay smoothing + floating neon peak dots
                for (int i = 0; i < targetCount; i++)
                {
                    var item = SpectrumValues[i];
                    double current = item.Value;
                    double target = Math.Min(78, newValues[i]);

                    // Fast attack (0.45) and smooth decay (0.075)
                    if (target > current)
                    {
                        item.Value = current + (target - current) * 0.45;
                    }
                    else
                    {
                        item.Value = current - (current - target) * 0.075;
                    }

                    // Floating Peak Hold Logic
                    if (item.Value >= item.PeakValue)
                    {
                        item.PeakValue = item.Value;
                        item.PeakHoldCount = 14; // Hold at top for ~14 frames
                    }
                    else
                    {
                        if (item.PeakHoldCount > 0)
                        {
                            item.PeakHoldCount--;
                        }
                        else
                        {
                            // Gravity fall
                            item.PeakValue = Math.Max(item.Value, item.PeakValue - 1.3);
                        }
                    }
                }
            });
        }

        private void UpdatePlaylistThumbnails(UserPlaylist playlist)
        {
            if (playlist == null) return;

            var distinctAlbumPaths = new List<string>();
            var processedAlbums = new HashSet<string>();

            // Only take up to 4 distinct albums
            foreach (var path in playlist.TrackPaths)
            {
                if (distinctAlbumPaths.Count >= 4) break;

                try
                {
                    var directory = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory) && !processedAlbums.Contains(directory))
                    {
                        processedAlbums.Add(directory);
                        distinctAlbumPaths.Add(path);
                    }
                }
                catch { }
            }

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                playlist.ThumbnailTrackPaths.Clear();
                foreach (var p in distinctAlbumPaths)
                {
                    playlist.ThumbnailTrackPaths.Add(p);
                }
            });
        }

        private void ShowDeviceManager()
        {
            if (SelectedDevice == null) return;

            string basePath = !string.IsNullOrEmpty(CurrentDevicePath) ? CurrentDevicePath : SelectedDevice.RootPath;
            var dialogViewModel = new DeviceManagerViewModel(SelectedDevice, basePath, Albums.ToList());
            var dialog = new DeviceManagerDialog(dialogViewModel);
            dialog.ShowDialog();

            // デバイス管理ダイアログを閉じた後、デバイス上のアルバム状況が変更されている可能性があるためチェックを再実行
            CheckDeviceAlbums();

            // 現在のディレクトリ表示も更新する
            if (!string.IsNullOrEmpty(CurrentDevicePath))
            {
                LoadDeviceDirectories(CurrentDevicePath);
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
