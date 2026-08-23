using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AudioEffector.Models;

namespace AudioEffector.ViewModels
{
    public class DeviceManagerViewModel : ViewModelBase
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private MainViewModel.DeviceViewModel _currentDevice;
        private string _basePath;
        private List<Album> _pcAlbums;

        public ObservableCollection<DeviceAlbum> DeviceAlbums { get; set; } = new ObservableCollection<DeviceAlbum>();

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        public ICommand DeleteTrackCommand { get; }
        public ICommand DeleteAlbumCommand { get; }

        public DeviceManagerViewModel(MainViewModel.DeviceViewModel device, string basePath, List<Album> pcAlbums)
        {
            _currentDevice = device;
            _basePath = basePath;
            _pcAlbums = pcAlbums;
            
            DeleteTrackCommand = new RelayCommand(ExecuteDeleteTrack);
            DeleteAlbumCommand = new RelayCommand(ExecuteDeleteAlbum);

            LoadAlbumsAsync();
        }

        private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".aiff", ".wma", ".m4a", ".mp4", ".flac", ".aac", ".ogg", ".opus", ".alac" };

        private async void LoadAlbumsAsync()
        {
            if (_currentDevice == null) return;
            
            IsLoading = true;
            DeviceAlbums.Clear();

            var loadedAlbums = new List<DeviceAlbum>();

            await Task.Run(() =>
            {
                try
                {
                    Logger.Info($"LoadAlbumsAsync started. DeviceType: {_currentDevice.Type}, BasePath: {_basePath}");
                    if (_currentDevice.Type == MainViewModel.DeviceType.FileSystem && _currentDevice.Drive != null)
                    {
                        string targetPath = string.IsNullOrEmpty(_basePath) ? _currentDevice.Drive.RootDirectory.FullName : _basePath;
                        ScanFileSystemDirectory(targetPath, loadedAlbums);
                    }
                    else if (_currentDevice.Type == MainViewModel.DeviceType.MTP && _currentDevice.MtpDevice != null && _currentDevice.MtpDevice.IsConnected)
                    {
                        string targetPath = string.IsNullOrEmpty(_basePath) ? @"\" : _basePath;
                        Logger.Info($"Scanning MTP targetPath: {targetPath}");
                        ScanMtpDirectory(targetPath, loadedAlbums);
                    }
                    else
                    {
                        Logger.Warn($"Device not fully connected or recognized.");
                    }
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Error loading device albums: {ex.Message}");
                    });
                }
            });

            // ソートして追加
            foreach (var album in loadedAlbums.OrderBy(a => a.Artist).ThenBy(a => a.Title))
            {
                DeviceAlbums.Add(album);
            }
            IsLoading = false;
        }

        private void ScanFileSystemDirectory(string path, List<DeviceAlbum> loadedAlbums)
        {
            try
            {
                var files = Directory.GetFiles(path);
                var audioFiles = files.Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLower())).ToList();

                if (audioFiles.Any())
                {
                    string albumName = Path.GetFileName(path);
                    string artistName = Path.GetFileName(Path.GetDirectoryName(path));

                    var deviceAlbum = new DeviceAlbum
                    {
                        Artist = artistName,
                        Title = albumName,
                        Path = path
                    };
                    foreach (var file in audioFiles)
                    {
                        deviceAlbum.Tracks.Add(new DeviceTrack { Title = Path.GetFileName(file), Path = file });
                    }
                    loadedAlbums.Add(deviceAlbum);
                }
            }
            catch (Exception ex) { Logger.Error(ex, "Error scanning directory"); }

            try
            {
                var dirs = Directory.GetDirectories(path);
                foreach (var dir in dirs)
                {
                    string name = Path.GetFileName(dir);
                    if (name.StartsWith(".") || name == "System Volume Information" || name == "$RECYCLE.BIN") continue;
                    ScanFileSystemDirectory(dir, loadedAlbums);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error scanning FS dirs in {path}: {ex.Message}"); }
        }

        private void ScanMtpDirectory(string path, List<DeviceAlbum> loadedAlbums)
        {
            try
            {
                var files = _currentDevice.MtpDevice.GetFiles(path);
                var audioFiles = files.Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLower())).ToList();

                if (audioFiles.Any())
                {
                    string albumName = Path.GetFileName(path);
                    string artistName = Path.GetFileName(GetParentDirectory(path));

                    var deviceAlbum = new DeviceAlbum
                    {
                        Artist = artistName ?? "Unknown Artist",
                        Title = albumName ?? "Unknown Album",
                        Path = path
                    };
                    foreach (var file in audioFiles)
                    {
                        deviceAlbum.Tracks.Add(new DeviceTrack { Title = Path.GetFileName(file), Path = file });
                    }
                    loadedAlbums.Add(deviceAlbum);
                }
            }
            catch (Exception ex) { Logger.Error(ex, "Error scanning directory"); }

            try
            {
                var dirs = _currentDevice.MtpDevice.GetDirectories(path);
                foreach (var dir in dirs)
                {
                    string name = Path.GetFileName(dir);
                    if (name.StartsWith(".") || name == "Android" || name == "LOST.DIR" || name == "System Volume Information" || name == "Alarms" || name == "Notifications" || name == "Ringtones" || name == "Podcasts") continue;
                    ScanMtpDirectory(dir, loadedAlbums);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error scanning MTP dirs in {path}: {ex.Message}"); }
        }

        private void ExecuteDeleteTrack(object parameter)
        {
            if (parameter is DeviceTrack track)
            {
                var result = MessageBox.Show($"Are you sure you want to delete '{track.Title}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        if (_currentDevice.Type == MainViewModel.DeviceType.FileSystem)
                        {
                            if (File.Exists(track.Path))
                                File.Delete(track.Path);
                        }
                        else if (_currentDevice.Type == MainViewModel.DeviceType.MTP && _currentDevice.MtpDevice != null)
                        {
                            _currentDevice.MtpDevice.DeleteFile(track.Path);
                        }

                        // Remove from UI
                        foreach (var album in DeviceAlbums)
                        {
                            if (album.Tracks.Contains(track))
                            {
                                album.Tracks.Remove(track);
                                if (album.Tracks.Count == 0)
                                {
                                    ExecuteDeleteAlbum(album, skipConfirm: true);
                                }
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error deleting file: {ex.Message}");
                    }
                }
            }
        }

        private void ExecuteDeleteAlbum(object parameter)
        {
            ExecuteDeleteAlbum(parameter, false);
        }

        private void ExecuteDeleteAlbum(object parameter, bool skipConfirm)
        {
            if (parameter is DeviceAlbum album)
            {
                if (!skipConfirm)
                {
                    var result = MessageBox.Show($"Are you sure you want to delete the entire album '{album.Title}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes) return;
                }

                try
                {
                    if (_currentDevice.Type == MainViewModel.DeviceType.FileSystem)
                    {
                        if (Directory.Exists(album.Path))
                            Directory.Delete(album.Path, true);
                        
                        // Check if artist folder is empty
                        string artistPath = Path.GetDirectoryName(album.Path);
                        if (Directory.Exists(artistPath) && !Directory.EnumerateFileSystemEntries(artistPath).Any())
                        {
                            Directory.Delete(artistPath);
                        }
                    }
                    else if (_currentDevice.Type == MainViewModel.DeviceType.MTP && _currentDevice.MtpDevice != null)
                    {
                        _currentDevice.MtpDevice.DeleteDirectory(album.Path, true);
                        
                        // Check if artist folder is empty (MTP doesn't have Path.GetDirectoryName easily, but we can do a hack)
                        string artistPath = GetParentDirectory(album.Path);
                        if (!string.IsNullOrEmpty(artistPath))
                        {
                            var remainingItems = _currentDevice.MtpDevice.GetDirectories(artistPath);
                            var remainingFiles = _currentDevice.MtpDevice.GetFiles(artistPath);
                            if (!remainingItems.Any() && !remainingFiles.Any())
                            {
                                _currentDevice.MtpDevice.DeleteDirectory(artistPath, true);
                            }
                        }
                    }

                    // Remove from UI
                    DeviceAlbums.Remove(album);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting album: {ex.Message}");
                }
            }
        }

        private string GetParentDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            path = path.TrimEnd('\\');
            int index = path.LastIndexOf('\\');
            if (index > 0)
                return path.Substring(0, index);
            if (index == 0)
                return @"\";
            return string.Empty;
        }
    }
}
