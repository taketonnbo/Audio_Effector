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
        event Action<List<Track>> PlaylistChanged;

        bool IsPlaying { get; }
        bool IsShuffleEnabled { get; set; }
        bool IsRepeatEnabled { get; set; }
        TimeSpan CurrentTime { get; }
        TimeSpan TotalTime { get; }
        float Volume { get; set; }

        float[] Frequencies { get; }

        [LogDescription("プレイリストを設定します")]
        void SetPlaylist(List<Track> tracks);
        [LogDescription("指定された楽曲を再生します")]
        void PlayTrack(Track track);
        [LogDescription("再生/一時停止を切り替えます")]
        void TogglePlayPause();
        [LogDescription("次の曲へ進みます")]
        void Next();
        [LogDescription("前の曲に戻ります")]
        void Previous();
        [LogDescription("再生を停止します")]
        void Stop(bool internalStop = false);
        [LogDescription("指定位置へシークします")]
        void SeekTo(double percentage);
        [LogDescription("シークのために一時停止します")]
        void PauseForSeek();
        [LogDescription("シーク後の再生を再開します")]
        void ResumeAfterSeek();
        [LogDescription("イコライザーのゲインを設定します")]
        void SetGain(int bandIndex, float gain);
        [LogDescription("オーディオプロパティを更新します")]
        void UpdateAudioProperties(int sampleRate, int bufferSizeMs);
    }
}
