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
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AudioEffector.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly AudioService _audioService;
        private readonly PresetService _presetService;
        private readonly FavoriteService _favoriteService;
        private Preset _selectedPreset;
        private Track _currentTrack;
        private bool _isPlaying;
        private string _currentTimeDisplay = "00:00";
        private string _totalTimeDisplay = "00:00";
        private double _progress;
        private DispatcherTimer _timer;
        private bool _isNowPlayingVisible = true;
        private bool _isEqualizerVisible = true;
        private bool _isLoading;
        private bool _isGridView = true;
        private string _selectedSortOption = "Artist";
        private List<string> _favoritePaths;



        public ObservableCollection<BandViewModel> Bands { get; set; }
        public ObservableCollection<Preset> Presets { get; set; }
        public ObservableCollection<Album> Albums { get; set; } = new ObservableCollection<Album>();

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
            set { _isEqualizerVisible = value; OnPropertyChanged(); }
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
        public ICommand ResetCommand { get; }
        public ICommand PlayTrackCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand ToggleViewCommand { get; }

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

        public ICommand ToggleSortDirectionCommand { get; }

        public MainViewModel()
        {
            _audioService = new AudioService();
            _presetService = new PresetService();
            _favoriteService = new FavoriteService();
            _favoritePaths = _favoriteService.LoadFavorites();
            
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
            ResetCommand = new RelayCommand(Reset);
            
            PlayTrackCommand = new RelayCommand(o => 
            {
                if (o is AudioEffector.Models.Track t) 
                {
                    var album = Albums.FirstOrDefault(a => a.Tracks.Contains(t));
                    if (album != null)
                    {
                        _audioService.SetPlaylist(album.Tracks);
                    }
                    _audioService.PlayTrack(t);
                }
            });
            
            ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
            ToggleViewCommand = new RelayCommand(o => IsGridView = !IsGridView);
            ToggleSortDirectionCommand = new RelayCommand(o => IsAscending = !IsAscending);
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
            IsPlaying = true;
            _timer.Start();
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
                LoadLibrary(dialog.FolderName);
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

        private async void LoadLibrary(string rootFolder)
        {
            IsLoading = true;
            Albums.Clear();
            
            await Task.Run(() =>
            {
                var extensions = new[] { ".mp3", ".wav", ".aiff", ".wma", ".m4a", ".flac" };
                var files = Directory.GetFiles(rootFolder, "*.*", SearchOption.AllDirectories)
                                     .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                                     .ToList();

                var tracks = new List<Track>();
                foreach (var file in files)
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
                            
                            track.Bitrate = tfile.Properties.AudioBitrate;
                            track.SampleRate = tfile.Properties.AudioSampleRate;
                            track.BitsPerSample = tfile.Properties.BitsPerSample;
                            string ext = Path.GetExtension(file).ToLower();
                            track.Format = ext.TrimStart('.').ToUpper();
                            
                            track.IsLossless = new[] { ".flac", ".wav", ".aiff", ".alac" }.Contains(ext);
                            track.IsHiRes = track.SampleRate > 48000 || track.BitsPerSample > 16;
                            
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
                                        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                                        image.CacheOption = BitmapCacheOption.OnLoad;
                                        image.UriSource = null;
                                        image.StreamSource = mem;
                                        image.EndInit();
                                    }
                                    image.Freeze();
                                    track.CoverImage = image;
                                });
                            }
                        }
                    }
                    catch { }

                    if (_favoritePaths.Contains(track.FilePath))
                    {
                        track.IsFavorite = true;
                    }

                    tracks.Add(track);
                }

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
                            CoverImage = g.First().CoverImage,
                            Tracks = g.ToList(),
                            Year = albumYear
                        });
                    }
                    SortLibrary();
                    _audioService.SetPlaylist(tracks);
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
                    if (!_favoritePaths.Contains(CurrentTrack.FilePath)) _favoritePaths.Add(CurrentTrack.FilePath);
                }
                else
                {
                    _favoritePaths.Remove(CurrentTrack.FilePath);
                }
                _favoriteService.SaveFavorites(_favoritePaths);
            }
        }

        private void SavePreset(object obj)
        {
            try
            {
                var inputBox = new InputBox("Enter Preset Name:", $"User Preset {DateTime.Now:MM-dd HH:mm}");
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
                if (MessageBox.Show($"Are you sure you want to delete '{SelectedPreset.Name}'?", "Delete Preset", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    Presets.Remove(SelectedPreset);
                    _presetService.SavePresets(Presets.ToList());
                    SelectedPreset = Presets.FirstOrDefault();
                }
            }
        }





        private void Reset(object obj)
        {
            foreach (var band in Bands)
            {
                band.Gain = 0;
            }
        }

        public void Cleanup()
        {
            _timer.Stop();
            _audioService.Dispose();
        }
    }
}
