using AudioEffector.Models;
using System;
using System.Collections.Generic;

namespace AudioEffector.Services
{
    public interface IAudioService : IDisposable
    {
        event Action<Track> TrackChanged;
        event Action<bool> PlaybackStateChanged;
        event Action PlaybackStopped;
        event EventHandler PlaylistEnded;
        event EventHandler<FftEventArgs>? FftCalculated;

        bool IsPlaying { get; }
        bool IsShuffleEnabled { get; set; }
        bool IsRepeatEnabled { get; set; }
        TimeSpan CurrentTime { get; }
        TimeSpan TotalTime { get; }
        float Volume { get; set; }

        float[] Frequencies { get; }

        void SetPlaylist(List<Track> tracks);
        void PlayTrack(Track track);
        void TogglePlayPause();
        void Next();
        void Previous();
        void Stop(bool internalStop = false);
        void SeekTo(double percentage);
        void PauseForSeek();
        void ResumeAfterSeek();
        void SetGain(int bandIndex, float gain);
    }
}
