using AudioEffector.Models;
using AudioEffector.Services;
using AudioEffector.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaDevices;

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

        private Preset _selectedPreset;
        private Track _currentTrack;
        private bool _isPlaying;
        private string _currentTimeDisplay = "00:00";
        private string _totalTimeDisplay = "00:00";
        private double _progress;
        private DispatcherTimer _timer;
        private bool _isNowPlayingVisible = true;
        private bool _isEqualizerVisible = true;
        private bool _isDeviceSyncVisible = false;
        private bool _isLoading;
        private bool _isGridView = true;
        private string _selectedSortOption = "Artist";
        private List<string> _favoritePaths;
        private ObservableCollection<UserPlaylist> _userPlaylists = new ObservableCollection<UserPlaylist>();
        private ObservableCollection<Track> _playlistTracks = new ObservableCollection<Track>();
        private bool _isLibraryVisible = true;
        private bool _isPlaylistSelectorVisible = false;
        private bool _isPlaylistTracksVisible = false;
        private Dictionary<string, BitmapImage> _albumArtCache = new Dictionary<string, BitmapImage>();

        // Device Sync Properties
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
                        RefreshDrives();
                    }
                }
            }
        }

        private BitmapImage _nowPlayingImage;
        public BitmapImage NowPlayingImage
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

        public Track CurrentTrack
        {
            get => _currentTrack;
            set { _currentTrack = value; OnPropertyChanged(); }
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
                    if (value) IsDeviceSyncVisible = false;
                }
            }
        }

        public Preset SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (_selectedPreset != value)
                {
                    _selectedPreset = value;
                    OnPropertyChanged();
                    ApplyPreset(_selectedPreset);
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

            _audioService.TrackChanged += OnTrackChanged;
            _audioService.PlaybackStateChanged += OnPlaybackStateChanged;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += OnTimerTick;
            _timer.Start();

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
                    if (IsPlaylistTracksVisible && PlaylistTracks.Any())
                    {
                        _audioService.SetPlaylist(PlaylistTracks.ToList());
                    }
                    else
                    {
                        var album = Albums.FirstOrDefault(a => a.Tracks.Contains(t));
                        if (album != null)
                        {
                            _audioService.SetPlaylist(album.Tracks);
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

            ToggleSelectionModeCommand = new RelayCommand(o => IsSelectionMode = !IsSelectionMode);
            ToggleRepeatCommand = new RelayCommand(ToggleRepeat);
            AddSelectedToPlaylistCommand = new RelayCommand(AddSelectedToPlaylist);

            // Device Sync Command Initialization
            SwitchToDeviceSyncCommand = new RelayCommand(o => IsDeviceSyncVisible = true);
            SwitchToEqualizerCommand = new RelayCommand(o => IsEqualizerVisible = true);
            RefreshDrivesCommand = new RelayCommand(o => RefreshDrives());
            TransferSelectedCommand = new RelayCommand(o => TransferSelected());
            NavigateDirectoryCommand = new RelayCommand(o => NavigateDirectory(o as DirectoryItem));
            NavigateUpCommand = new RelayCommand(o => NavigateUp());
            RefreshDirectoryCommand = new RelayCommand(o => LoadDeviceDirectories(CurrentDevicePath));

            _audioService.PlaylistEnded += OnPlaylistEnded;

            PlaylistTracks.CollectionChanged += OnPlaylistTracksChanged;

            LoadLibrary();
        }

        public class DirectoryItem
        {
            public string Name { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;
            public bool IsFolder { get; set; }
        }

        public ObservableCollection<DirectoryItem> DeviceDirectories { get; set; } = new ObservableCollection<DirectoryItem>();

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
                // Navigation is now handled by double-click command, not selection change
                // to allow selecting items without navigating immediately
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
                            // MTP paths might need adjustment depending on library behavior
                            // MediaDevices library usually returns full path or name
                            DeviceDirectories.Add(new DirectoryItem
                            {
                                Name = Path.GetFileName(dir), // or dir itself if it's just name
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
        }

        private void NavigateDirectory(DirectoryItem? dir)
        {
            if (dir == null || !dir.IsFolder) return;
            LoadDeviceDirectories(dir.FullPath);
        }

        private void NavigateUp()
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
                // MTP path handling (simple string manipulation for now)
                // Assuming paths are like \Folder\Subfolder
                if (CurrentDevicePath == @"\" || CurrentDevicePath == "/") return;

                var parentPath = Path.GetDirectoryName(CurrentDevicePath);
                if (string.IsNullOrEmpty(parentPath)) parentPath = @"\";
                LoadDeviceDirectories(parentPath);
            }
        }


        private async void TransferSelected()
        {
            if (SelectedDevice == null)
            {
                MessageBox.Show("Please select a device first.", "No Device Selected");
                return;
            }

            // Use current path or root
            string destinationFolder = !string.IsNullOrEmpty(CurrentDevicePath) ? CurrentDevicePath : SelectedDevice.RootPath;

            // Verify destination is on the selected drive (FileSystem only check)
            if (SelectedDevice.Type == DeviceType.FileSystem && !destinationFolder.StartsWith(SelectedDevice.RootPath, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Current folder is not on the selected drive.", "Error");
                return;
            }

            var selectedAlbums = Albums.Where(a => a.IsSelected).ToList();
            var filesToTransfer = new List<string>();

            // Add tracks from selected albums
            foreach (var album in selectedAlbums)
            {
                filesToTransfer.AddRange(album.Tracks.Select(t => t.FilePath));
            }

            // Add individually selected tracks (avoiding duplicates)
            foreach (var album in Albums)
            {
                foreach (var track in album.Tracks.Where(t => t.IsSelected))
                {
                    if (!filesToTransfer.Contains(track.FilePath))
                    {
                        filesToTransfer.Add(track.FilePath);
                    }
                }
            }

            if (!filesToTransfer.Any())
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
                    await _deviceSyncService.TransferFilesAsync(filesToTransfer, destinationFolder, progress);
                }
                else if (SelectedDevice.Type == DeviceType.MTP && SelectedDevice.MtpDevice != null)
                {
                    await Task.Run(() =>
                    {
                        int total = filesToTransfer.Count;
                        int current = 0;
                        foreach (var file in filesToTransfer)
                        {
                            if (!File.Exists(file)) continue;

                            string fileName = Path.GetFileName(file);
                            // MTP path separator is usually backslash, but library might handle it.
                            // Construct destination path.
                            string destPath = Path.Combine(destinationFolder, fileName);

                            // Upload
                            // Note: MediaDevices UploadFile takes (localPath, remotePath)
                            // We might need to ensure destinationFolder is correct for MTP
                            SelectedDevice.MtpDevice.UploadFile(file, destPath);

                            current++;
                            ((IProgress<double>)progress).Report((double)current / total * 100);
                        }
                    });
                }

                // Refresh to show new files
                LoadDeviceDirectories(destinationFolder);

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
            // If we are currently playing/viewing this playlist, sync with AudioService
            if (IsPlaylistTracksVisible)
            {
                // Use a slight delay or debounce if frequent updates occur, but for now direct update is fine
                // as SetPlaylist is relatively cheap (just list replacement)
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

        private void OnTrackChanged(Track track)
        {
            CurrentTrack = track;
            Progress = 0; // Reset progress when track changes

            // Load high-res image for Now Playing
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
                                        image.DecodePixelWidth = 500; // Higher quality for Now Playing
                                        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                                        image.CacheOption = BitmapCacheOption.OnLoad;
                                        image.UriSource = null;
                                        image.StreamSource = mem;
                                        image.EndInit();
                                    }
                                    image.Freeze();
                                    NowPlayingImage = image;
                                });
                            }
                            else
                            {
                                Application.Current.Dispatcher.Invoke(() => NowPlayingImage = null);
                            }
                        }
                    }
                    catch
                    {
                        Application.Current.Dispatcher.Invoke(() => NowPlayingImage = null);
                    }
                });
            }
            else
            {
                NowPlayingImage = null;
            }
        }

        private void OnPlaybackStateChanged(bool isPlaying)
        {
            IsPlaying = isPlaying;
            if (isPlaying) _timer.Start();
            else _timer.Stop();
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

                // Save selected path to settings
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
                var defaultPresets = new[] { "フラット (Flat)", "ロック (Rock)", "ポップ (Pop)" };
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
            }
        }

        private void ShowFavorites()
        {
            IsLibraryVisible = false;
            IsPlaylistSelectorVisible = false;
            IsPlaylistTracksVisible = true;
            IsFavoritesView = true;
            CurrentPlaylistName = "Favorites";

            PlaylistTracks.Clear();
            foreach (var path in _favoritePaths)
            {
                var track = LoadTrack(path);
                if (track != null)
                    PlaylistTracks.Add(track);
            }
        }

        private void ShowLibrary()
        {
            IsLibraryVisible = true;
            IsPlaylistSelectorVisible = false;
            IsPlaylistTracksVisible = false;
            IsFavoritesView = false;
        }

        private void ShowPlaylistSelector()
        {
            IsLibraryVisible = false;
            IsPlaylistSelectorVisible = true;
            IsPlaylistTracksVisible = false;
            IsFavoritesView = false;
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
    }
}
