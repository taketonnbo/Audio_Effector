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
        private MainViewModel.DeviceViewModel _currentDevice;
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

        public DeviceManagerViewModel(MainViewModel.DeviceViewModel device, List<Album> pcAlbums)
        {
            _currentDevice = device;
            _pcAlbums = pcAlbums;
            
            DeleteTrackCommand = new RelayCommand(ExecuteDeleteTrack);
            DeleteAlbumCommand = new RelayCommand(ExecuteDeleteAlbum);

            LoadAlbumsAsync();
        }

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
                    if (_currentDevice.Type == MainViewModel.DeviceType.FileSystem && _currentDevice.Drive != null)
                    {
                        string rootPath = _currentDevice.Drive.RootDirectory.FullName;
                        if (Directory.Exists(rootPath))
                        {
                            var artists = Directory.GetDirectories(rootPath);
                            foreach (var artistDir in artists)
                            {
                                string artistName = Path.GetFileName(artistDir);
                                var albums = Directory.GetDirectories(artistDir);
                                foreach (var albumDir in albums)
                                {
                                    string albumName = Path.GetFileName(albumDir);
                                    var files = Directory.GetFiles(albumDir);
                                    
                                    if (files.Length > 0)
                                    {
                                        var deviceAlbum = new DeviceAlbum
                                        {
                                            Artist = artistName,
                                            Title = albumName,
                                            Path = albumDir,
                                            CoverImage = FindCoverImage(albumName)
                                        };
                                        foreach (var file in files)
                                        {
                                            deviceAlbum.Tracks.Add(new DeviceTrack { Title = Path.GetFileName(file), Path = file });
                                        }
                                        loadedAlbums.Add(deviceAlbum);
                                    }
                                }
                            }
                        }
                    }
                    else if (_currentDevice.Type == MainViewModel.DeviceType.MTP && _currentDevice.MtpDevice != null && _currentDevice.MtpDevice.IsConnected)
                    {
                        var artists = _currentDevice.MtpDevice.GetDirectories(@"\");
                        foreach (var artistDir in artists)
                        {
                            string artistName = Path.GetFileName(artistDir);
                            var albums = _currentDevice.MtpDevice.GetDirectories(artistDir);
                            foreach (var albumDir in albums)
                            {
                                string albumName = Path.GetFileName(albumDir);
                                var files = _currentDevice.MtpDevice.GetFiles(albumDir);

                                if (files.Any())
                                {
                                    var deviceAlbum = new DeviceAlbum
                                    {
                                        Artist = artistName,
                                        Title = albumName,
                                        Path = albumDir,
                                        CoverImage = FindCoverImage(albumName)
                                    };
                                    foreach (var file in files)
                                    {
                                        deviceAlbum.Tracks.Add(new DeviceTrack { Title = Path.GetFileName(file), Path = file });
                                    }
                                    loadedAlbums.Add(deviceAlbum);
                                }
                            }
                        }
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

            foreach (var album in loadedAlbums)
            {
                DeviceAlbums.Add(album);
            }
            IsLoading = false;
        }

        private System.Windows.Media.Imaging.BitmapImage FindCoverImage(string albumTitle)
        {
            var match = _pcAlbums?.FirstOrDefault(a => a.Title.Equals(albumTitle, StringComparison.OrdinalIgnoreCase));
            return match?.CoverImage;
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
