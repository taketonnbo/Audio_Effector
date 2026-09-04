using System;
using System.Threading;
using System.Threading.Tasks;
using AudioEffector.Domain.Entities;
using AudioEffector.Domain.ValueObjects;

namespace AudioEffector.Application.Common;

/// <summary>
/// FFT計算完了時のイベント引数
/// </summary>
public class FftCalculatedEventArgs : EventArgs
{
    /// <summary>
    /// FFT複素数/振幅配列
    /// </summary>
    public double[] Magnitudes { get; }

    /// <summary>
    /// サンプリング周波数（Hz）
    /// </summary>
    public int SampleRate { get; }

    /// <summary>
    /// FftCalculatedEventArgsを初期化します
    /// </summary>
    /// <param name="magnitudes">振幅配列</param>
    /// <param name="sampleRate">サンプリングレート</param>
    public FftCalculatedEventArgs(double[] magnitudes, int sampleRate)
    {
        Magnitudes = magnitudes ?? [];
        SampleRate = sampleRate;
    }
}

/// <summary>
/// 音声再生・DSPイコライザー・FFT解析を担当するオーディオエンジンの抽象インターフェース
/// </summary>
public interface IAudioEngine : IDisposable
{
    /// <summary>
    /// 現在の再生状態（Stopped, Playing, Paused）
    /// </summary>
    PlaybackState CurrentState { get; }

    /// <summary>
    /// 現在の再生時間位置
    /// </summary>
    TimeSpan CurrentPosition { get; }

    /// <summary>
    /// ロードされている楽曲の総再生時間
    /// </summary>
    TimeSpan TotalDuration { get; }

    /// <summary>
    /// 現在の音量設定値（0.0〜1.0）
    /// </summary>
    float Volume { get; }

    /// <summary>
    /// FFT計算完了時に発生するイベント
    /// </summary>
    event EventHandler<FftCalculatedEventArgs>? FftCalculated;

    /// <summary>
    /// 楽曲の末尾まで再生が終了した際に発生するイベント
    /// </summary>
    event EventHandler? PlaybackEnded;

    /// <summary>
    /// 指定されたトラックをロードして再生準備を行います
    /// </summary>
    /// <param name="track">再生対象のトラック</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task LoadTrackAsync(Track track, CancellationToken cancellationToken = default);

    /// <summary>
    /// 音声の再生を開始します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task PlayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 音声の再生を一時停止します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 音声の再生を停止し、位置を先頭に戻します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定された時間位置へシークします
    /// </summary>
    /// <param name="position">シーク先時間位置</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);

    /// <summary>
    /// 音量（0.0〜1.0）を設定します
    /// </summary>
    /// <param name="volume">音量値（0.0〜1.0）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task SetVolumeAsync(float volume, CancellationToken cancellationToken = default);

    /// <summary>
    /// イコライザーの特定バンドのゲインを設定します
    /// </summary>
    /// <param name="bandIndex">バンドインデックス（0〜9）</param>
    /// <param name="gainDb">ゲイン値（dB）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task SetEqualizerBandGainAsync(int bandIndex, float gainDb, CancellationToken cancellationToken = default);

    /// <summary>
    /// 10バンドすべてのイコライザーゲインを一括設定します
    /// </summary>
    /// <param name="gainsDb">10要素のゲイン配列（dB）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>非同期タスク</returns>
    Task SetEqualizerAllGainsAsync(float[] gainsDb, CancellationToken cancellationToken = default);
}
