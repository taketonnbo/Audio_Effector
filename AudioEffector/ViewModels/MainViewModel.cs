using AudioEffector.Models;
using AudioEffector.Services;
using AudioEffector.Views;
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

namespace AudioEffector.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly AudioService _audioService;
        private readonly PresetService _presetService;
        private readonly FavoriteService _favoriteService;
        private readonly PlaylistService _playlistService;
        private readonly SettingsService _settingsService;
        private readonly DeviceSyncService _deviceSyncService;

        public AudioService AudioService => _audioService; // Public accessor for code-behind

        private Preset? _selectedPreset;
        private Track? _currentTrack;
        private bool _isPlaying;
        private string _currentTimeDisplay = "00:00";
        private BitmapImage? _nowPlayingImage;
        private string _totalTimeDisplay = "00:00";
        private double _progress;
        private DispatcherTimer _timer;
        private bool _isNowPlayingVisible = true;
        private bool _isEqualizerVisible = false;
        private bool _isDeviceSyncVisible = false;
        private bool _isLoading;
        private bool _isGridView = true;
        private string _selectedSortOption = "Artist";
        private List<string> _favoritePaths;
        private ObservableCollection<UserPlaylist> _userPlaylists = new ObservableCollection<UserPlaylist>();
        private ObservableCollection<Track> _playlistTracks = new ObservableCollection<Track>();
        private const int SpectrumBarCount = 32;
        private int _spectrumGeneration = 0;
        private bool _isLibraryVisible = true;
        private bool _isPlaylistSelectorVisible = false;
        private bool _isPlaylistTracksVisible = false;
        private Dictionary<string, BitmapImage> _albumArtCache = new Dictionary<string, BitmapImage>();
        private UserPlaylist? _currentViewingPlaylist;
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
        public string PlaybackListName
        {
            get => _playbackListName;
            set { _playbackListName = value; OnPropertyChanged(); }
        }

        private string _playbackListSubtitle = "";
        public string PlaybackListSubtitle
        {
            get => _playbackListSubtitle;
            set { _playbackListSubtitle = value; OnPropertyChanged(); }
        }

        private ObservableCollection<Track> _playbackListTracks = new ObservableCollection<Track>();
        public ObservableCollection<Track> PlaybackListTracks
        {
            get => _playbackListTracks;
            set { _playbackListTracks = value; OnPropertyChanged(); }
        }

        private BitmapImage? _defaultSpectrumImage;
        private BitmapImage? _favoritesImage;
        private BitmapImage? _defaultNowPlayingImage;

        private ImageSource? _playlistBackgroundImage;
        public ImageSource? PlaylistBackgroundImage
        {
            get => _playlistBackgroundImage;
            set { _playlistBackgroundImage = value; OnPropertyChanged(); }
        }

        private ImageSource? _spectrumBackgroundImage;
        public ImageSource? SpectrumBackgroundImage
        {
            get => _spectrumBackgroundImage;
            set { _spectrumBackgroundImage = value; OnPropertyChanged(); }
        }

        // ... (existing code) ...

        public MainViewModel()
        {
            _audioService = new AudioService();
            _presetService = new PresetService();
            _favoriteService = new FavoriteService();
            _playlistService = new PlaylistService();
            _settingsService = new SettingsService();
            _deviceSyncService = new DeviceSyncService();
            _favoritePaths = _favoriteService.LoadFavorites();

            // Load playlists
            var loadedPlaylists = _playlistService.LoadPlaylists();
            UserPlaylists = new ObservableCollection<UserPlaylist>(loadedPlaylists);

            // Generate thumbnails for loaded playlists
            foreach (var playlist in UserPlaylists)
            {
                UpdatePlaylistThumbnails(playlist);
            }

            _audioService.TrackChanged += OnTrackChanged;
            _audioService.PlaybackStateChanged += OnPlaybackStateChanged;

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
            for (int i = 0; i < SpectrumBarCount; i++)
            {
                SpectrumValues.Add(new SpectrumBarItem { Value = 0 });
            }

            Presets = new ObservableCollection<Preset>(_presetService.LoadPresets());
            SelectedPreset = Presets.FirstOrDefault();

            OpenFolderCommand = new RelayCommand(OpenFolder);
            TogglePlayPauseCommand = new RelayCommand(o => _audioService.TogglePlayPause());
            NextCommand = new RelayCommand(o => _audioService.Next());
            PreviousCommand = new RelayCommand(o => _audioService.Previous());
            SavePresetCommand = new RelayCommand(SavePreset);
            DeletePresetCommand = new RelayCommand(DeletePreset);
            ResetPresetCommand = new RelayCommand(Reset);

            PlayTrackCommand = new RelayCommand(o =>
            {
                if (o is AudioEffector.Models.Track t)
                {
                    // Check if playing from playlist/favorites view
                    if (IsPlaylistTracksVisible && PlaylistTracks.Any() && PlaylistTracks.Contains(t))
                    {
                        _audioService.SetPlaylist(PlaylistTracks.ToList());
                        PlaybackListName = CurrentPlaylistName;
                        PlaybackListSubtitle = IsFavoritesView ? "Selected You" : "Playlist"; // Phase 8: Selected You
                        PlaybackListTracks = new ObservableCollection<Track>(PlaylistTracks);
                    }
                    else
                    {
                        var album = Albums.FirstOrDefault(a => a.Tracks.Contains(t));
                        if (album != null)
                        {
                            _audioService.SetPlaylist(album.Tracks);
                            PlaybackListName = album.Title;
                            PlaybackListSubtitle = album.Artist;
                            PlaybackListTracks = new ObservableCollection<Track>(album.Tracks);
                        }
                    }
                    _audioService.PlayTrack(t);
                }
            });

            ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
            ToggleViewCommand = new RelayCommand(o => IsGridView = !IsGridView);
            ToggleSortDirectionCommand = new RelayCommand(o => IsAscending = !IsAscending);

            // Playlist commands
            CreatePlaylistCommand = new RelayCommand(CreatePlaylist);
            AddToPlaylistCommand = new RelayCommand(AddToPlaylist);
            ShowPlaylistCommand = new RelayCommand(ShowPlaylist);
            ShowFavoritesCommand = new RelayCommand(o => ShowFavorites());
            ShowLibraryCommand = new RelayCommand(o => ShowLibrary());
            ShowPlaylistSelectorCommand = new RelayCommand(o => ShowPlaylistSelector());
            ShowAddToPlaylistDialogCommand = new RelayCommand(ShowAddToPlaylistDialog);
            DeletePlaylistCommand = new RelayCommand(DeletePlaylist);
            RemoveFromPlaylistCommand = new RelayCommand(RemoveFromPlaylist);

            ToggleSelectionModeCommand = new RelayCommand(o => IsSelectionMode = !IsSelectionMode);
            ToggleRepeatCommand = new RelayCommand(ToggleRepeat);
            AddSelectedToPlaylistCommand = new RelayCommand(AddSelectedToPlaylist);
            PlayAlbumCommand = new RelayCommand(PlayAlbum);

            IncreaseVolumeCommand = new RelayCommand(o => Volume = Math.Min(1.0f, Volume + 0.05f));
            DecreaseVolumeCommand = new RelayCommand(o => Volume = Math.Max(0.0f, Volume - 0.05f));

            // Device Sync Command Initialization
            SwitchToDeviceSyncCommand = new RelayCommand(o => IsDeviceSyncVisible = true);
            SwitchToEqualizerCommand = new RelayCommand(o => IsEqualizerVisible = true);
            SwitchToSpectrumCommand = new RelayCommand(o => IsSpectrumVisible = true);

            RefreshDrivesCommand = new RelayCommand(o => RefreshDrives());
            TransferSelectedCommand = new RelayCommand(o => TransferSelected());
            NavigateDirectoryCommand = new RelayCommand(o => NavigateDirectory(o as DirectoryItem));
            NavigateUpCommand = new RelayCommand(o => NavigateUp());
            RefreshDirectoryCommand = new RelayCommand(o => LoadDeviceDirectories(CurrentDevicePath));

            _audioService.PlaylistEnded += OnPlaylistEnded;
            _audioService.FftCalculated += OnFftCalculated;

            PlaylistTracks.CollectionChanged += OnPlaylistTracksChanged;

            var settings = _settingsService.LoadSettings();
            if (settings.LeftColumnWidth > 0)
            {
                LeftColumnWidth = new GridLength(settings.LeftColumnWidth);
            }
            // Load Volume
            Volume = settings.Volume;

            LoadLibrary();
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


        public enum DeviceType { FileSystem, MTP }

        public class DeviceViewModel
        {
            public string Name { get; set; } = string.Empty;
            public DeviceType Type { get; set; }
            public DriveInfo? Drive { get; set; }
            public MediaDevice? MtpDevice { get; set; }
            public string RootPath { get; set; } = string.Empty; // For MTP, this might be device ID or root
        }

        public ObservableCollection<DeviceViewModel> RemovableDrives { get; set; } = new ObservableCollection<DeviceViewModel>();

        private DeviceViewModel? _selectedDevice;
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
        public double TransferProgress
        {
            get => _transferProgress;
            set { _transferProgress = value; OnPropertyChanged(); }
        }

        private bool _isTransferring;
        public bool IsTransferring
        {
            get => _isTransferring;
            set { _isTransferring = value; OnPropertyChanged(); }
        }

        public bool IsDeviceSyncVisible
        {
            get => _isDeviceSyncVisible;
            set
            {
                if (_isDeviceSyncVisible != value)
                {
                    _isDeviceSyncVisible = value;
                    OnPropertyChanged();
                    if (value)
                    {
                        IsEqualizerVisible = false;
                        IsSpectrumVisible = false;
                        RefreshDrives();
                    }
                }
            }
        }


        public BitmapImage? NowPlayingImage
        {
            get => _nowPlayingImage;
            set { _nowPlayingImage = value; OnPropertyChanged(); }
        }

        public ObservableCollection<BandViewModel> Bands { get; set; }
        public ObservableCollection<Preset> Presets { get; set; }
        public ObservableCollection<Album> Albums { get; set; } = new ObservableCollection<Album>();

        public ObservableCollection<UserPlaylist> UserPlaylists
        {
            get => _userPlaylists;
            set { _userPlaylists = value; OnPropertyChanged(); }
        }

        public ObservableCollection<Track> PlaylistTracks
        {
            get => _playlistTracks;
            set { _playlistTracks = value; OnPropertyChanged(); }
        }

        public bool IsLibraryVisible
        {
            get => _isLibraryVisible;
            set { _isLibraryVisible = value; OnPropertyChanged(); }
        }

        public bool IsPlaylistSelectorVisible
        {
            get => _isPlaylistSelectorVisible;
            set { _isPlaylistSelectorVisible = value; OnPropertyChanged(); }
        }

        public bool IsPlaylistTracksVisible
        {
            get => _isPlaylistTracksVisible;
            set { _isPlaylistTracksVisible = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

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

        public bool IsListView
        {
            get => !_isGridView;
            set { IsGridView = !value; OnPropertyChanged(nameof(IsGridView)); }
        }

        public List<string> SortOptions { get; } = new List<string> { "Artist", "Album" };

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

        public bool IsShuffleEnabled
        {
            get => _audioService.IsShuffleEnabled;
            set { _audioService.IsShuffleEnabled = value; OnPropertyChanged(); }
        }

        private Album? _currentAlbum;
        public Album? CurrentAlbum
        {
            get => _currentAlbum;
            set { _currentAlbum = value; OnPropertyChanged(); }
        }

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

        public bool IsPlaying
        {
            get => _isPlaying;
            set { _isPlaying = value; OnPropertyChanged(); }
        }

        public string CurrentTimeDisplay
        {
            get => _currentTimeDisplay;
            set { _currentTimeDisplay = value; OnPropertyChanged(); }
        }

        public string TotalTimeDisplay
        {
            get => _totalTimeDisplay;
            set { _totalTimeDisplay = value; OnPropertyChanged(); }
        }

        private bool _isDraggingProgress;
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

        public bool IsDraggingProgress
        {
            get => _isDraggingProgress;
            set => _isDraggingProgress = value;
        }

        public bool IsNowPlayingVisible
        {
            get => _isNowPlayingVisible;
            set { _isNowPlayingVisible = value; OnPropertyChanged(); }
        }

        public bool IsEqualizerVisible
        {
            get => _isEqualizerVisible;
            set
            {
                if (_isEqualizerVisible != value)
                {
                    _isEqualizerVisible = value;
                    OnPropertyChanged();
                    if (value)
                    {
                        IsDeviceSyncVisible = false;
                        IsSpectrumVisible = false;
                    }
                }
            }
        }

        public Preset? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (_selectedPreset != value)
                {
                    _selectedPreset = value;
                    OnPropertyChanged();
                    if (_selectedPreset != null) ApplyPreset(_selectedPreset);
                }
            }
        }

        public ICommand OpenFolderCommand { get; }
        public ICommand TogglePlayPauseCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand SavePresetCommand { get; }
        public ICommand DeletePresetCommand { get; }
        public ICommand ResetPresetCommand { get; }
        public ICommand PlayTrackCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand ToggleViewCommand { get; }
        public ICommand CreatePlaylistCommand { get; }
        public ICommand AddToPlaylistCommand { get; }
        public ICommand ShowPlaylistCommand { get; }
        public ICommand ShowFavoritesCommand { get; }
        public ICommand ShowLibraryCommand { get; }
        public ICommand ShowPlaylistSelectorCommand { get; }
        public ICommand ShowAddToPlaylistDialogCommand { get; }
        public ICommand DeletePlaylistCommand { get; }
        public ICommand RemoveFromPlaylistCommand { get; }
        public ICommand PlayAlbumCommand { get; }

        // Device Sync Commands
        public ICommand SwitchToDeviceSyncCommand { get; }
        public ICommand SwitchToEqualizerCommand { get; }
        public ICommand TransferSelectedCommand { get; }
        public ICommand RefreshDrivesCommand { get; }
        public ICommand NavigateDirectoryCommand { get; }
        public ICommand NavigateUpCommand { get; }

        private bool _isAscending = true;

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

        public ICommand ToggleSortDirectionCommand { get; }
        public ICommand ToggleSelectionModeCommand { get; }
        public ICommand ToggleRepeatCommand { get; }
        public ICommand AddSelectedToPlaylistCommand { get; }

        public ICommand IncreaseVolumeCommand { get; }
        public ICommand DecreaseVolumeCommand { get; }

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
                    var settings = _settingsService.LoadSettings();
                    settings.Volume = value;
                    _settingsService.SaveSettings(settings);
                }
            }
        }

        public string VolumePercent => $"{(int)(Volume * 100)}%";



        public class DirectoryItem
        {
            public string Name { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;
            public bool IsFolder { get; set; }
        }

        public ObservableCollection<DirectoryItem> DeviceDirectories { get; set; } = new ObservableCollection<DirectoryItem>();

        private GridLength _leftColumnWidth = new GridLength(300);
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

        public ICommand RefreshDirectoryCommand { get; private set; }

        private string _currentDevicePath = string.Empty;
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
        public DirectoryItem? SelectedDeviceDirectory
        {
            get => _selectedDeviceDirectory;
            set
            {
                _selectedDeviceDirectory = value;
                OnPropertyChanged();
            }
        }

        private void RefreshDrives()
        {
            RemovableDrives.Clear();

            // Add File System Drives
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

        private async void CheckDeviceAlbums()
        {
            if (SelectedDevice == null || string.IsNullOrEmpty(CurrentDevicePath)) return;

            await Task.Run(() =>
            {
                foreach (var album in Albums)
                {
                    string artist = SanitizeFileName(album.Artist);
                    string albumName = SanitizeFileName(album.Title);
                    bool allTracksExist = true;

                    if (album.Tracks != null)
                    {
                        foreach (var track in album.Tracks)
                        {
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

                    Application.Current.Dispatcher.Invoke(() =>
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

            if (!tracksToTransfer.Any())
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
                                if (!SelectedDevice.MtpDevice.DirectoryExists(artistDir))
                                {
                                    SelectedDevice.MtpDevice.CreateDirectory(artistDir);
                                }
                                if (!SelectedDevice.MtpDevice.DirectoryExists(targetDir))
                                {
                                    SelectedDevice.MtpDevice.CreateDirectory(targetDir);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error creating MTP directory: {ex.Message}");
                            }

                            string destPath = System.IO.Path.Combine(targetDir, fileName);

                            // Skip if already exists
                            bool fileExists = false;
                            try { fileExists = SelectedDevice.MtpDevice.FileExists(destPath); } catch { }

                            if (fileExists)
                            {
                                current++;
                                ((IProgress<double>)progress).Report((double)current / total * 100);
                                continue;
                            }

                            SelectedDevice.MtpDevice.UploadFile(track.FilePath, destPath);

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

        private void OnPlaylistTracksChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (IsPlaylistTracksVisible)
            {
                _audioService.SetPlaylist(PlaylistTracks.ToList());
            }
        }

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
        public ImageSource? SpectrumBackgroundImageGray
        {
            get => _spectrumBackgroundImageGray;
            set { _spectrumBackgroundImageGray = value; OnPropertyChanged(); }
        }

        private bool _isDefaultSpectrumImage;
        public bool IsDefaultSpectrumImage
        {
            get => _isDefaultSpectrumImage;
            set { _isDefaultSpectrumImage = value; OnPropertyChanged(); }
        }

        private Brush _spectrumBarBrush;
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
            set { _spectrumBarBrush = value; OnPropertyChanged(); }
        }

        private Brush _spectrumBorderBrush = new SolidColorBrush(Color.FromArgb(230, 0, 229, 255)); // Default Neon Cyan Border (90%)
        public Brush SpectrumBorderBrush
        {
            get => _spectrumBorderBrush;
            set { _spectrumBorderBrush = value; OnPropertyChanged(); }
        }

        private Color _spectrumShadowColor = Color.FromRgb(0, 229, 255); // Default Neon Cyan
        public Color SpectrumShadowColor
        {
            get => _spectrumShadowColor;
            set { _spectrumShadowColor = value; OnPropertyChanged(); }
        }

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

                // Boost Saturation
                // Saturation Gradient: Right (Low) -> Left (High)
                // Right (Start): Low Saturation
                double satRight = 0.3;
                // Left (End): High Saturation
                double satLeft = 1.0;

                // Value/Brightness: Max
                double val = 1.0;

                // Color 1 (Right/High Freq)
                Color colorRight = HsvToColor(fh, satRight, val);
                colorRight.A = 242; // ~95% Opacity (Bright)

                // Color 2 (Left/Low Freq)
                Color colorLeft = HsvToColor(fh, satLeft, val);
                colorLeft.A = 153; // ~60% Opacity

                // Border Color: Brighter (Lower Saturation), 100% Opacity
                // Use 20% saturation to make it whiter/brighter
                Color borderColor = HsvToColor(fh, 0.2, 1.0);
                borderColor.A = 255; // 100%
                SpectrumBorderBrush = new SolidColorBrush(borderColor);

                // Shadow Color
                SpectrumShadowColor = HsvToColor(fh, 1.0, 1.0);

                var brush = new LinearGradientBrush();
                brush.StartPoint = new Point(1, 0.5); // Right
                brush.EndPoint = new Point(0, 0.5);   // Left
                brush.GradientStops.Add(new GradientStop(colorRight, 0.0));
                brush.GradientStops.Add(new GradientStop(colorLeft, 1.0));

                SpectrumBarBrush = brush;
            }
            catch
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Default Gradient
                    // Default Gradient: Right (Low Sat) -> Left (High Sat)
                    var brush = new LinearGradientBrush();
                    brush.StartPoint = new Point(1, 0.5);
                    brush.EndPoint = new Point(0, 0.5);

                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(242, 200, 250, 255), 0.0));
                    brush.GradientStops.Add(new GradientStop(Color.FromArgb(153, 0, 229, 255), 1.0));

                    SpectrumBarBrush = brush;
                    SpectrumShadowColor = Color.FromRgb(0, 229, 255);

                    // Border: Brighter (Lower Saturation), 100% Opacity
                    var borderColor = Color.FromRgb(204, 249, 255);
                    borderColor.A = 255;
                    SpectrumBorderBrush = new SolidColorBrush(borderColor);
                });
            }
        }

        private void OnTrackChanged(Track track)
        {
            // Increment generation to invalidate pending FFT updates
            System.Threading.Interlocked.Increment(ref _spectrumGeneration);

            // Reset Spectrum immediately to prevent glitches
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var item in SpectrumValues)
                {
                    item.Value = 0;
                }
            });

            CurrentTrack = track;
            Progress = 0;

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
                                Application.Current.Dispatcher.Invoke(() =>
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
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    NowPlayingImage = _defaultNowPlayingImage;
                                    SpectrumBackgroundImage = _defaultSpectrumImage;
                                    SpectrumBackgroundImageGray = null;

                                    // Border: Brighter (Lower Saturation), 100% Opacity
                                    var borderColor = Color.FromRgb(204, 249, 255);
                                    borderColor.A = 255;
                                    SpectrumBorderBrush = new SolidColorBrush(borderColor);
                                });
                            }
                        }
                    }
                    catch
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            // Default Gradient
                            // Default Gradient: Right to Left
                            var brush = new LinearGradientBrush();
                            brush.StartPoint = new Point(1, 0.5);
                            brush.EndPoint = new Point(0, 0.5);
                            brush.GradientStops.Add(new GradientStop(Color.FromArgb(242, 200, 250, 255), 0.0));
                            brush.GradientStops.Add(new GradientStop(Color.FromArgb(153, 0, 229, 255), 1.0));
                            SpectrumBarBrush = brush;
                            SpectrumShadowColor = Color.FromRgb(0, 229, 255);

                            // Border: Brighter (Lower Saturation), 100% Opacity
                            // Reduced Saturation: S=0.2 (Very White)
                            var borderColor = Color.FromRgb(204, 249, 255); // Approx for H186 S0.2 V1.0
                            borderColor.A = 255;
                            SpectrumBorderBrush = new SolidColorBrush(borderColor);
                        });
                    }
                });
            }

            else
            {
                IsDefaultSpectrumImage = true;
                // Default Gradient
                // Default Gradient: Right to Left
                var brush = new LinearGradientBrush();
                brush.StartPoint = new Point(1, 0.5);
                brush.EndPoint = new Point(0, 0.5);
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(242, 200, 250, 255), 0.0));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(153, 0, 229, 255), 1.0));
                SpectrumBarBrush = brush;

                // Border: Brighter (Lower Saturation), 100% Opacity
                var borderColor = Color.FromRgb(204, 249, 255);
                borderColor.A = 255;
                SpectrumBorderBrush = new SolidColorBrush(borderColor);
            }
        }

        // HSV Helpers
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
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (_audioService != null)
            {
                var current = _audioService.CurrentTime;
                var total = _audioService.TotalTime;

                CurrentTimeDisplay = current.ToString(@"mm\:ss");
                TotalTimeDisplay = total.ToString(@"mm\:ss");

                if (total.TotalSeconds > 0 && !_isDraggingProgress)
                {
                    Progress = (current.TotalSeconds / total.TotalSeconds) * 100;
                }
            }
        }

        private void OpenFolder(object obj)
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

        private void ApplyPreset(Preset preset)
        {
            if (preset == null || preset.Gains == null) return;

            for (int i = 0; i < Bands.Count && i < preset.Gains.Count; i++)
            {
                Bands[i].Gain = preset.Gains[i];
            }
        }

        private async void LoadLibrary(string rootFolder = null)
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
                var extensions = new[] { ".mp3", ".wav", ".aiff", ".wma", ".m4a", ".flac" };
                var files = Directory.GetFiles(rootFolder, "*.*", SearchOption.AllDirectories)
                                     .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                                     .ToList();

                // _albumArtCache is no longer used here for bulk loading
                var tracks = new System.Collections.Concurrent.ConcurrentBag<Track>();

                Parallel.ForEach(files, file =>
                {
                    var track = new Track { FilePath = file, Title = Path.GetFileNameWithoutExtension(file) };
                    uint year = 0;

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
                            string ext = Path.GetExtension(file).ToLower();
                            track.Format = ext.TrimStart('.').ToUpper();

                            track.IsLossless = new[] { ".flac", ".wav", ".aiff", ".alac" }.Contains(ext);
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

                Application.Current.Dispatcher.Invoke(() =>
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

        private void ToggleFavorite(object obj)
        {
            if (CurrentTrack != null)
            {
                CurrentTrack.IsFavorite = !CurrentTrack.IsFavorite;
                OnPropertyChanged(nameof(CurrentTrack)); // Refresh UI

                if (CurrentTrack.IsFavorite)
                {
                    if (!_favoritePaths.Contains(CurrentTrack.FilePath))
                    {
                        _favoritePaths.Add(CurrentTrack.FilePath);
                        // Immediate update if viewing favorites
                        if (IsFavoritesView)
                        {
                            PlaylistTracks.Add(CurrentTrack);
                        }
                    }
                }
                else
                {
                    _favoritePaths.Remove(CurrentTrack.FilePath);
                    // Immediate update if viewing favorites
                    if (IsFavoritesView)
                    {
                        var trackToRemove = PlaylistTracks.FirstOrDefault(t => t.FilePath == CurrentTrack.FilePath);
                        if (trackToRemove != null)
                        {
                            PlaylistTracks.Remove(trackToRemove);
                        }
                    }
                }
                _favoriteService.SaveFavorites(_favoritePaths);
            }
        }

        private void ToggleRepeat(object obj)
        {
            IsAlbumRepeat = !IsAlbumRepeat;
        }

        private void OnPlaylistEnded(object sender, EventArgs e)
        {
            // If repeat is OFF, try to play next album
            if (!IsAlbumRepeat && CurrentTrack != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var currentAlbum = Albums.FirstOrDefault(a => a.Tracks.Any(t => t.FilePath == CurrentTrack.FilePath));
                    if (currentAlbum != null)
                    {
                        int index = Albums.IndexOf(currentAlbum);
                        if (index >= 0 && index < Albums.Count - 1)
                        {
                            var nextAlbum = Albums[index + 1];
                            if (nextAlbum.Tracks.Any())
                            {
                                _audioService.SetPlaylist(nextAlbum.Tracks);
                                _audioService.PlayTrack(nextAlbum.Tracks.First());
                            }
                        }
                    }
                });
            }
        }

        private void ShowAddToPlaylistDialog(object parameter)
        {
            if (parameter is Track track)
            {
                if (!UserPlaylists.Any())
                {
                    MessageBox.Show("Please create a playlist first", "No Playlists");
                    return;
                }

                ShowPlaylistSelectionDialog(selectedPlaylist =>
                {
                    if (!selectedPlaylist.TrackPaths.Contains(track.FilePath))
                    {
                        selectedPlaylist.TrackPaths.Add(track.FilePath);
                        UpdatePlaylistThumbnails(selectedPlaylist);
                        _playlistService.SavePlaylists(UserPlaylists.ToList());

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

        private void AddSelectedToPlaylist(object parameter)
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
                            PlaylistTracks.Add(track);
                        }

                        addedCount++;
                    }
                }

                if (addedCount > 0)
                {
                    UpdatePlaylistThumbnails(selectedPlaylist);
                    _playlistService.SavePlaylists(UserPlaylists.ToList());
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
                Height = 450,
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
                Margin = new Thickness(0, 0, 0, 20),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stackPanel.Children.Add(title);

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

        private void SavePreset(object obj)
        {
            try
            {
                var inputBox = new Views.InputBox("Enter Preset Name:", $"User Preset {DateTime.Now:MM-dd HH:mm}");
                if (inputBox.ShowDialog() == true)
                {
                    string name = inputBox.InputText;
                    if (string.IsNullOrWhiteSpace(name)) name = "Untitled Preset";

                    var newPreset = new Preset
                    {
                        Name = name,
                        Gains = Bands.Select(b => b.Gain).ToList()
                    };
                    Presets.Add(newPreset);
                    _presetService.SavePresets(Presets.ToList());
                    SelectedPreset = newPreset;
                    MessageBox.Show("プリセットを保存しました。\nPreset Saved.", "保存完了");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving preset: {ex.Message}", "Error");
            }
        }

        private void DeletePreset(object obj)
        {
            if (SelectedPreset != null && Presets.Contains(SelectedPreset))
            {
                // Prevent deletion of default presets
                var defaultPresets = new[] { "繝輔Λ繝・ヨ (Flat)", "繝ｭ繝・け (Rock)", "繝昴ャ繝・(Pop)" };
                if (defaultPresets.Contains(SelectedPreset.Name))
                {
                    MessageBox.Show($"'{SelectedPreset.Name}' is a default preset and cannot be deleted.", "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (MessageBox.Show($"Are you sure you want to delete '{SelectedPreset.Name}'?", "Delete Preset", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    try
                    {
                        Presets.Remove(SelectedPreset);
                        _presetService.SavePresets(Presets.ToList());
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





        private void Reset(object obj)
        {
            foreach (var band in Bands)
            {
                band.Gain = 0;
            }
            // Set preset display to "Flat" (match default preset name)
            SelectedPreset = Presets.FirstOrDefault(p => p.Name.Contains("Flat"));
        }

        public void Cleanup()
        {
            _timer.Stop();
            _audioService.Dispose();
        }

        // Playlist management methods
        private void CreatePlaylist(object obj)
        {
            var dialog = new Views.InputBox("New Playlist", "Enter playlist name:");
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            {
                var newPlaylist = new UserPlaylist { Name = dialog.InputText };
                UserPlaylists.Add(newPlaylist);
                _playlistService.SavePlaylists(UserPlaylists.ToList());
            }
        }

        private void AddToPlaylist(object obj)
        {
            if (obj is UserPlaylist playlist && CurrentTrack != null)
            {
                if (!playlist.TrackPaths.Contains(CurrentTrack.FilePath))
                {
                    playlist.TrackPaths.Add(CurrentTrack.FilePath);
                    _playlistService.SavePlaylists(UserPlaylists.ToList());

                    // Immediate update if viewing this playlist
                    if (CurrentPlaylistName == playlist.Name && IsPlaylistTracksVisible)
                    {
                        PlaylistTracks.Add(CurrentTrack);
                    }

                    MessageBox.Show($"Added '{CurrentTrack.Title}' to '{playlist.Name}'", "Track Added");
                }
            }
        }




        public bool IsPlaylistSectionActive => IsPlaylistSelectorVisible || (IsPlaylistTracksVisible && !IsFavoritesView);

        private bool _isFavoritesView;
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

        private string _currentPlaylistName;
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

        private void ShowPlaylist(object obj)
        {
            if (obj is UserPlaylist playlist)
            {
                System.Diagnostics.Debug.WriteLine($"ShowPlaylist: {playlist.Name}, Tracks: {playlist.TrackPaths.Count}");

                IsLibraryVisible = false;
                IsPlaylistSelectorVisible = false;
                IsPlaylistTracksVisible = true;
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
            IsLibraryVisible = false;
            IsPlaylistSelectorVisible = false;
            IsPlaylistTracksVisible = true;
            IsFavoritesView = true;
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
            IsLibraryVisible = true;
            IsPlaylistSelectorVisible = false;
            IsPlaylistTracksVisible = false;
            CurrentViewingPlaylist = null;
            IsFavoritesView = false;
            OnPropertyChanged(nameof(IsPlaylistSectionActive));
        }

        private void ShowPlaylistSelector()
        {
            IsLibraryVisible = false;
            IsPlaylistSelectorVisible = true;
            IsPlaylistTracksVisible = false;
            IsFavoritesView = false;
            OnPropertyChanged(nameof(IsPlaylistSectionActive));
        }

        private Track? LoadTrack(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                var tagFile = TagLib.File.Create(filePath);
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

                string ext = Path.GetExtension(filePath).ToLower();
                track.Format = ext.TrimStart('.').ToUpper();
                track.IsLossless = new[] { ".flac", ".wav", ".aiff", ".alac" }.Contains(ext);
                track.IsHiRes = track.SampleRate > 48000 || track.BitsPerSample > 16;

                return track;
            }
            catch
            {
                return null;
            }
        }
        private void DeletePlaylist(object parameter)
        {
            if (parameter is UserPlaylist playlist)
            {
                if (MessageBox.Show($"Are you sure you want to delete playlist '{playlist.Name}'?", "Delete Playlist", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    UserPlaylists.Remove(playlist);
                    _playlistService.SavePlaylists(UserPlaylists.ToList());
                }
            }
        }

        private void RemoveFromPlaylist(object parameter)
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

                        _favoriteService.SaveFavorites(_favoritePaths);
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

                        _playlistService.SavePlaylists(UserPlaylists.ToList());
                    }
                }
            }
        }

        private void PlayAlbum(object parameter)
        {
            if (parameter is Album album && album.Tracks.Any())
            {
                _audioService.SetPlaylist(album.Tracks);

                PlaybackListName = album.Title;
                PlaybackListSubtitle = album.Artist;
                PlaybackListTracks = new ObservableCollection<Track>(album.Tracks);

                _audioService.PlayTrack(album.Tracks.First());
            }
        }

        // Spectrum Analyzer Logic
        private bool _isSpectrumVisible = true; // Default to true as requested
        public bool IsSpectrumVisible
        {
            get => _isSpectrumVisible;
            set
            {
                if (_isSpectrumVisible != value)
                {
                    _isSpectrumVisible = value;
                    OnPropertyChanged();
                    if (value)
                    {
                        IsEqualizerVisible = false;
                        IsDeviceSyncVisible = false;
                    }
                }
            }
        }



        public ICommand SwitchToSpectrumCommand { get; }

        public ObservableCollection<SpectrumBarItem> SpectrumValues { get; } = new ObservableCollection<SpectrumBarItem>();

        private void OnFftCalculated(object? sender, FftEventArgs e)
        {
            if (!IsSpectrumVisible) return;

            // Capture current generation
            int currentGen = _spectrumGeneration;

            int barCount = SpectrumBarCount;
            var newValues = new double[barCount];

            // FFT parameters
            // Assuming 44.1kHz sample rate and 1024 FFT size (approx)
            // Bin resolution ~43Hz.
            // We want 40Hz to 20kHz to use low end better.
            double minFreq = 40;
            double maxFreq = 20000;
            double logMin = Math.Log10(minFreq);
            double logMax = Math.Log10(maxFreq);
            double logStep = (logMax - logMin) / barCount;

            for (int i = 0; i < barCount; i++)
            {
                // Calculate frequency range for this bar (Log scale)
                double fStart = Math.Pow(10, logMin + i * logStep);
                double fEnd = Math.Pow(10, logMin + (i + 1) * logStep);

                // Convert to bin indices (approximate)
                // Bin 0 = 0Hz, Bin 512 = 22050Hz (Nyquist)
                // Index = Freq * 512 / 22050
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
                double db = 20 * Math.Log10(avg);

                // Map dB to height
                // Reduced scaling factor from 3 to 1.5 to halve the height as requested
                double val = Math.Max(0, db + 60) * 1.5;

                // Apply Mid-range Boost
                // Simple quadratic curve: y = -a(x-h)^2 + k
                // Center h=15.5. Max boost k=1.5. Ends=1.0.
                double normalizedPos = (i - 15.5) / 15.5; // -1 to 1
                double boost = 1.5 - 0.5 * (normalizedPos * normalizedPos); // 1.5 at center, 1.0 at ends

                // Apply High Frequency Boost (Compensate for Pink Noise roll-off)
                // Linear increase from 0.3 to 3.0 across the spectrum
                double highBoost = 0.3 + (i / (double)barCount) * 2.7;
                val *= boost * highBoost;

                // User Requested Scaling: 0.5x (Low) -> 2.0x (High)
                double userScale = 0.5 + (i / (double)(barCount - 1)) * 1.5;
                val *= userScale;

                // Final Global Scaling (Reduced to 0.75x)
                val *= 0.75;

                // Glitch Prevention: Handle NaN/Infinity
                if (double.IsNaN(val) || double.IsInfinity(val)) val = 0;

                newValues[i] = val;
            }

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Check if generation has changed (track changed)
                if (currentGen != _spectrumGeneration) return;

                // Enforce Bar Count (Fix for fluctuating bars)
                int targetCount = SpectrumBarCount;
                int currentCount = SpectrumValues.Count;

                if (currentCount != targetCount)
                {
                    System.Diagnostics.Debug.WriteLine($"[Spectrum] Count Mismatch! Current: {currentCount}, Target: {targetCount}");
                }

                if (currentCount < targetCount)
                {
                    System.Diagnostics.Debug.WriteLine($"[Spectrum] Adding {targetCount - currentCount} bars.");
                    for (int i = currentCount; i < targetCount; i++)
                    {
                        SpectrumValues.Add(new SpectrumBarItem { Value = 0 });
                    }
                }
                else if (currentCount > targetCount)
                {
                    System.Diagnostics.Debug.WriteLine($"[Spectrum] Removing {currentCount - targetCount} bars.");
                    for (int i = currentCount; i > targetCount; i--)
                    {
                        SpectrumValues.RemoveAt(SpectrumValues.Count - 1);
                    }
                }

                // Update with smoothing
                for (int i = 0; i < targetCount; i++)
                {
                    var item = SpectrumValues[i];
                    double current = item.Value;
                    double target = newValues[i];

                    // Rise fast, fall slow
                    if (target > current)
                    {
                        item.Value = current + (target - current) * 0.2;
                    }
                    else
                    {
                        item.Value = current - (current - target) * 0.05;
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

            Application.Current.Dispatcher.Invoke(() =>
            {
                playlist.ThumbnailTrackPaths.Clear();
                foreach (var p in distinctAlbumPaths)
                {
                    playlist.ThumbnailTrackPaths.Add(p);
                }
            });
        }
    }
}
