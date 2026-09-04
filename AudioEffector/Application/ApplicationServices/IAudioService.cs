using System;
using System.Collections.Generic;
using AudioEffector.Application.Common;
using AudioEffector.Domain.Entities;
using AudioEffector.Infrastructure.Logging;

namespace AudioEffector.Application.ApplicationServices;

/// <summary>
/// オーディオ再生、キュー制御、イコライザー処理を統括するオーディオサービスインターフェース
/// </summary>
public interface IAudioService : IDisposable
{
    /// <summary>
    /// 再生トラックが変更された際に発生するイベント
    /// </summary>
    event Action<Track> TrackChanged;

    /// <summary>
    /// 再生状態（再生中/一時停止）が変更された際に発生するイベント
    /// </summary>
    event Action<bool> PlaybackStateChanged;

    /// <summary>
    /// 再生が停止した際に発生するイベント
    /// </summary>
    event Action PlaybackStopped;

    /// <summary>
    /// プレイリスト末尾まで再生が終了した際に発生するイベント
    /// </summary>
    event EventHandler PlaylistEnded;

    /// <summary>
    /// FFT計算が完了した際に発生するイベント
    /// </summary>
    event EventHandler<FftEventArgs>? FftCalculated;

    /// <summary>
    /// プレイリスト内容が変更された際に発生するイベント
    /// </summary>
    event Action<List<Track>> PlaylistChanged;

    /// <summary>
    /// 音量が変更された際に発生するイベント
    /// </summary>
    event Action<float> VolumeChanged;

    /// <summary>
    /// 現在再生中かどうか
    /// </summary>
    bool IsPlaying { get; }

    /// <summary>
    /// シャッフル再生が有効かどうか
    /// </summary>
    bool IsShuffleEnabled { get; set; }

    /// <summary>
    /// リピート再生が有効かどうか
    /// </summary>
    bool IsRepeatEnabled { get; set; }

    /// <summary>
    /// 現在の再生時間位置
    /// </summary>
    TimeSpan CurrentTime { get; }

    /// <summary>
    /// 現在ロード中の楽曲の総再生時間
    /// </summary>
    TimeSpan TotalTime { get; }

    /// <summary>
    /// 音量値（0.0〜1.0）
    /// </summary>
    float Volume { get; set; }

    /// <summary>
    /// イコライザーの中心周波数一覧
    /// </summary>
    float[] Frequencies { get; }

    /// <summary>
    /// プレイリストを設定します
    /// </summary>
    /// <param name="tracks">設定するトラックのリスト</param>
    [LogDescription("プレイリストを設定します")]
    void SetPlaylist(List<Track> tracks);

    /// <summary>
    /// 指定された楽曲を再生します
    /// </summary>
    /// <param name="track">再生対象のトラック</param>
    [LogDescription("指定された楽曲を再生します")]
    void PlayTrack(Track track);

    /// <summary>
    /// 再生と一時停止を切り替えます
    /// </summary>
    [LogDescription("再生/一時停止を切り替えます")]
    void TogglePlayPause();

    /// <summary>
    /// 次の楽曲へ進みます
    /// </summary>
    [LogDescription("次の曲へ進みます")]
    void Next();

    /// <summary>
    /// 前の楽曲に戻ります
    /// </summary>
    [LogDescription("前の曲に戻ります")]
    void Previous();

    /// <summary>
    /// 再生を停止します
    /// </summary>
    /// <param name="internalStop">内部要因による停止かどうか</param>
    [LogDescription("再生を停止します")]
    void Stop(bool internalStop = false);

    /// <summary>
    /// 指定位置（パーセンテージ）へシークします
    /// </summary>
    /// <param name="percentage">シーク位置（0.0〜1.0）</param>
    [LogDescription("指定位置へシークします")]
    void SeekTo(double percentage);

    /// <summary>
    /// シーク操作のために再生を一時停止します
    /// </summary>
    [LogDescription("シークのために一時停止します")]
    void PauseForSeek();

    /// <summary>
    /// シーク操作完了後に再生を再開します
    /// </summary>
    [LogDescription("シーク後の再生を再開します")]
    void ResumeAfterSeek();

    /// <summary>
    /// イコライザー特定バンドのゲインを設定します
    /// </summary>
    /// <param name="bandIndex">バンドインデックス（0〜9）</param>
    /// <param name="gain">ゲイン値（dB）</param>
    [LogDescription("イコライザーのゲインを設定します")]
    void SetGain(int bandIndex, float gain);

    /// <summary>
    /// サンプリングレートおよびバッファサイズを更新します
    /// </summary>
    /// <param name="sampleRate">サンプリングレート（Hz）</param>
    /// <param name="bufferSizeMs">バッファサイズ（ミリ秒）</param>
    [LogDescription("オーディオプロパティを更新します")]
    void UpdateAudioProperties(int sampleRate, int bufferSizeMs);
}
