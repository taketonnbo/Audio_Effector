using NAudio.Wave;
using NAudio.Dsp;
using System;
using System.Collections.Generic;
using System.Linq;
using AudioEffector.Models;
using System.Threading.Tasks;

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
        private Guid _currentPlaybackId;

        private bool _stopRequested;
        private readonly object _lock = new object();

        public event Action<Track> TrackChanged;
        public event Action<bool> PlaybackStateChanged;
        public event Action PlaybackStopped;
        public event EventHandler PlaylistEnded;
        public event EventHandler<FftEventArgs>? FftCalculated;

        // 10-band EQ frequencies
        public readonly float[] Frequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

        public bool IsPlaying => _outputDevice?.PlaybackState == PlaybackState.Playing;

        public bool IsShuffleEnabled
        {
            get => _isShuffleEnabled;
            set
            {
                lock (_lock)
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
        }

        public bool IsRepeatEnabled { get; set; }

        public void SetPlaylist(List<Track> tracks)
        {
            lock (_lock)
            {
                // Capture current track before updating
                var currentTrack = _currentIndex >= 0 && _currentIndex < _playlist.Count ? _playlist[_currentIndex] : null;

                _originalPlaylist = new List<Track>(tracks);
                if (_isShuffleEnabled)
                {
                    ShufflePlaylist();
                }
                else
                {
                    _playlist = new List<Track>(tracks);
                }

                // Restore index if current track still exists
                if (currentTrack != null)
                {
                    var newIndex = _playlist.FindIndex(t => t.FilePath == currentTrack.FilePath);
                    if (newIndex >= 0)
                    {
                        _currentIndex = newIndex;
                    }
                    else
                    {
                        _currentIndex = -1; // Track removed
                    }
                }
                else
                {
                    _currentIndex = -1;
                }
            }
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

        public async void PlayTrack(Track track)
        {
            int index = _playlist.IndexOf(track);
            if (index >= 0)
            {
                _currentIndex = index;
                PlayCurrent();
                // Wait for PlaybackState to update
                await Task.Delay(100);
                PlaybackStateChanged?.Invoke(IsPlaying);
            }
        }



        private void PlayCurrent()
        {
            lock (_lock)
            {
                if (_currentIndex < 0 || _currentIndex >= _playlist.Count) return;

                // Stop explicitly without triggering Next
                Stop(true);

                // Generate new session ID
                _currentPlaybackId = Guid.NewGuid();

                var track = _playlist[_currentIndex];
                try
                {
                    _audioFile = new AudioFileReader(track.FilePath);
                    _audioFile.Volume = _volume;

                    // Setup EQ
                    _equalizer = new Equalizer(_audioFile, Frequencies);

                    // Setup SampleAggregator for FFT
                    var aggregator = new SampleAggregator(_equalizer);
                    aggregator.FftCalculated += (s, e) => FftCalculated?.Invoke(this, e);

                    // Wrap with EndOfStreamProvider to detect end of playback reliably
                    var endOfStreamProvider = new EndOfStreamProvider(aggregator);
                    endOfStreamProvider.EndOfStream += OnEndOfStream;

                    _outputDevice = new WaveOutEvent();
                    _outputDevice.Init(endOfStreamProvider);
                    _outputDevice.PlaybackStopped += OnPlaybackStopped;

                    TrackChanged?.Invoke(track);
                    _outputDevice.Play();
                    PlaybackStateChanged?.Invoke(true);
                }
                catch (Exception ex)
                {
                    // Handle error (e.g. file not found)
                    System.Diagnostics.Debug.WriteLine($"Error playing file: {ex.Message}");
                    // Ensure cleanup if initialization fails
                    Stop(true);
                }
            }
        }

        private void OnEndOfStream()
        {
            // Capture the current ID
            var sessionId = _currentPlaybackId;

            // Trigger Next() when the stream ends (0 bytes read)
            // Run asynchronously to avoid blocking the audio thread
            Task.Run(() =>
            {
                // Add a small delay to allow the last buffer to play out
                System.Threading.Thread.Sleep(500);

                // Check if the session is still valid
                lock (_lock)
                {
                    if (_currentPlaybackId != sessionId) return;
                }

                Next();
            });
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
                // Natural end of track, play next asynchronously to avoid disposing active device in event handler
                // Note: If OnEndOfStream triggered Next(), this might be redundant or race.
                // But Next() handles re-entrancy by checking state or just starting next.
                // If OnEndOfStream already called Next(), _stopRequested might be true (from Stop(true) in PlayCurrent),
                // so the check at the top of this method would catch it.
                Task.Run(() =>
                {
                    Next();
                });
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
            lock (_lock)
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

                try
                {
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
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in TogglePlayPause: {ex.Message}");
                    // If device is in bad state, stop and cleanup
                    Stop(true);
                    PlaybackStateChanged?.Invoke(false);
                }
            }
        }

        public async void Next()
        {
            if (_playlist.Count == 0) return;

            if (_currentIndex < _playlist.Count - 1)
            {
                _currentIndex++;
                PlayCurrent();
            }
            else
            {
                // End of playlist
                if (IsRepeatEnabled)
                {
                    _currentIndex = 0;
                    PlayCurrent();
                }
                else
                {
                    Stop(true); // Stop and reset position
                    PlaylistEnded?.Invoke(this, EventArgs.Empty);
                }
            }

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
            lock (_lock)
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
        }

        public void SeekTo(double percentage)
        {
            lock (_lock)
            {
                if (_audioFile != null)
                {
                    long position = (long)(_audioFile.Length * (percentage / 100));
                    _audioFile.Position = position;
                }
            }
        }

        public void SetGain(int bandIndex, float gain)
        {
            _equalizer?.UpdateGain(bandIndex, gain);
        }

        public TimeSpan CurrentTime
        {
            get
            {
                lock (_lock)
                {
                    return _audioFile?.CurrentTime ?? TimeSpan.Zero;
                }
            }
        }

        public TimeSpan TotalTime
        {
            get
            {
                lock (_lock)
                {
                    return _audioFile?.TotalTime ?? TimeSpan.Zero;
                }
            }
        }

        public void PauseForSeek()
        {
            lock (_lock)
            {
                _wasPlayingBeforeSeek = IsPlaying;
                if (_wasPlayingBeforeSeek)
                {
                    _outputDevice?.Pause();
                }
            }
        }

        public void ResumeAfterSeek()
        {
            lock (_lock)
            {
                if (_wasPlayingBeforeSeek && _outputDevice != null)
                {
                    _outputDevice.Play();
                }
            }
        }

        private float _volume = 1.0f;
        public float Volume
        {
            get => _volume;
            set
            {
                lock (_lock)
                {
                    _volume = Math.Min(1.0f, Math.Max(0.0f, value));
                    if (_audioFile != null)
                    {
                        _audioFile.Volume = _volume;
                    }
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    // Helper class to detect end of stream
    public class EndOfStreamProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private bool _endReached;

        public event Action EndOfStream;

        public EndOfStreamProvider(ISampleProvider source)
        {
            _source = source;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            if (read == 0 && !_endReached)
            {
                _endReached = true;
                EndOfStream?.Invoke();
            }
            return read;
        }
    }
}
