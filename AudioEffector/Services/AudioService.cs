using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using AudioEffector.Models;

namespace AudioEffector.Services
{
    public class AudioService : IDisposable
    {
        private IWavePlayer _outputDevice;
        private AudioFileReader _audioFile;
        private Equalizer _equalizer;
        private List<Track> _playlist = new List<Track>();
        private List<Track> _originalPlaylist = new List<Track>();
        private int _currentIndex = -1;
        private bool _isShuffleEnabled;
        private bool _wasPlayingBeforeSeek = false;

        public event Action<Track> TrackChanged;
        public event Action<bool> PlaybackStateChanged;
        public event Action PlaybackStopped;

        // 10-band EQ frequencies
        public readonly float[] Frequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

        public bool IsPlaying => _outputDevice?.PlaybackState == PlaybackState.Playing;

        public bool IsShuffleEnabled
        {
            get => _isShuffleEnabled;
            set
            {
                if (_isShuffleEnabled != value)
                {
                    _isShuffleEnabled = value;
                    if (_isShuffleEnabled)
                    {
                        ShufflePlaylist();
                    }
                    else
                    {
                        RestorePlaylist();
                    }
                }
            }
        }

        public void SetPlaylist(List<Track> tracks)
        {
            _originalPlaylist = new List<Track>(tracks);
            if (_isShuffleEnabled)
            {
                ShufflePlaylist();
            }
            else
            {
                _playlist = new List<Track>(tracks);
            }
            _currentIndex = -1;
        }

        private void ShufflePlaylist()
        {
            if (_originalPlaylist == null || !_originalPlaylist.Any()) return;
            
            var currentTrack = _currentIndex >= 0 && _currentIndex < _playlist.Count ? _playlist[_currentIndex] : null;
            
            var rng = new Random();
            _playlist = _originalPlaylist.OrderBy(x => rng.Next()).ToList();

            if (currentTrack != null)
            {
                _currentIndex = _playlist.IndexOf(currentTrack);
            }
            else
            {
                _currentIndex = -1;
            }
        }

        private void RestorePlaylist()
        {
            if (_originalPlaylist == null || !_originalPlaylist.Any()) return;

            var currentTrack = _currentIndex >= 0 && _currentIndex < _playlist.Count ? _playlist[_currentIndex] : null;

            _playlist = new List<Track>(_originalPlaylist);

            if (currentTrack != null)
            {
                _currentIndex = _playlist.IndexOf(currentTrack);
            }
            else
            {
                _currentIndex = -1;
            }
        }

        public void PlayTrack(Track track)
        {
            int index = _playlist.IndexOf(track);
            if (index >= 0)
            {
                _currentIndex = index;
                PlayCurrent();
            }
        }

        private bool _stopRequested = false;

        private void PlayCurrent()
        {
            if (_currentIndex < 0 || _currentIndex >= _playlist.Count) return;

            // Stop explicitly without triggering Next
            Stop(true);

            var track = _playlist[_currentIndex];
            try
            {
                _audioFile = new AudioFileReader(track.FilePath);
                
                // Setup EQ
                _equalizer = new Equalizer(_audioFile, Frequencies);

                _outputDevice = new WaveOutEvent();
                _outputDevice.Init(_equalizer);
                _outputDevice.PlaybackStopped += OnPlaybackStopped;
                
                TrackChanged?.Invoke(track);
                _outputDevice.Play();
                PlaybackStateChanged?.Invoke(true);
            }
            catch (Exception ex)
            {
                // Handle error (e.g. file not found)
                System.Diagnostics.Debug.WriteLine($"Error playing file: {ex.Message}");
            }
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (_stopRequested)
            {
                _stopRequested = false;
                PlaybackStopped?.Invoke();
                PlaybackStateChanged?.Invoke(false);
                return;
            }

            if (e.Exception == null)
            {
                // Natural end of track, play next
                Next();
            }
            else
            {
                // Error
                PlaybackStopped?.Invoke();
                PlaybackStateChanged?.Invoke(false);
            }
        }

        public void TogglePlayPause()
        {
            if (_outputDevice == null) 
            {
                if (_playlist.Any() && _currentIndex == -1)
                {
                    _currentIndex = 0;
                    PlayCurrent();
                }
                return;
            }

            if (_outputDevice.PlaybackState == PlaybackState.Playing)
            {
                _outputDevice.Pause();
                PlaybackStateChanged?.Invoke(false);
            }
            else
            {
                _outputDevice.Play();
                PlaybackStateChanged?.Invoke(true);
            }
        }

       public async void Next()
        {
            if (_playlist.Count == 0) return;
            _currentIndex++;
            if (_currentIndex >= _playlist.Count) _currentIndex = 0; // Loop
            PlayCurrent();
            // Wait for PlaybackState to update
            await Task.Delay(100);
            PlaybackStateChanged?.Invoke(IsPlaying);
        }

        public async void Previous()
        {
            if (_playlist.Count == 0) return;
            _currentIndex--;
            if (_currentIndex < 0) _currentIndex = _playlist.Count - 1; // Loop
            PlayCurrent();
            // Wait for PlaybackState to update
            await Task.Delay(100);
            PlaybackStateChanged?.Invoke(IsPlaying);
        }

        public void Stop(bool internalStop = false)
        {
            if (internalStop) _stopRequested = true;

            if (_outputDevice != null)
            {
                _outputDevice.Stop();
                _outputDevice.Dispose();
                _outputDevice = null;
            }
            if (_audioFile != null)
            {
                _audioFile.Dispose();
                _audioFile = null;
            }
            
            if (!internalStop)
            {
                PlaybackStateChanged?.Invoke(false);
            }
        }

        public void SeekTo(double percentage)
        {
            if (_audioFile != null)
            {
                long position = (long)(_audioFile.Length * (percentage / 100));
                _audioFile.Position = position;
            }
        }

        public void SetGain(int bandIndex, float gain)
        {
            _equalizer?.UpdateGain(bandIndex, gain);
        }

        public TimeSpan CurrentTime => _audioFile?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan TotalTime => _audioFile?.TotalTime ?? TimeSpan.Zero;

        public void PauseForSeek()
        {
            _wasPlayingBeforeSeek = IsPlaying;
            if (_wasPlayingBeforeSeek)
            {
                _outputDevice?.Pause();
            }
        }

        public void ResumeAfterSeek()
        {
            if (_wasPlayingBeforeSeek && _outputDevice != null)
            {
                _outputDevice.Play();
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
