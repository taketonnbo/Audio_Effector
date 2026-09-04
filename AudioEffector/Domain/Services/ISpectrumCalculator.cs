using System;

namespace AudioEffector.Domain.Services;

/// <summary>
/// FFT複素数/振幅データからスペクトラムバー表示用振幅値を計算するドメインサービスインターフェース
/// </summary>
public interface ISpectrumCalculator
{
    /// <summary>
    /// FFT振幅配列から指定本数のスペクトラムバー振幅値を計算します
    /// </summary>
    /// <param name="fftMagnitudes">FFTの各ビンの振幅（大きさ）配列</param>
    /// <param name="sampleRate">サンプリングレート（Hz）</param>
    /// <param name="barCount">出力スペクトラムバー本数（デフォルト: 64）</param>
    /// <param name="sensitivity">感度係数</param>
    /// <param name="bassScale">低音域スケーリング係数</param>
    /// <param name="midScale">中音域スケーリング係数</param>
    /// <param name="trebleScale">高音域スケーリング係数</param>
    /// <param name="trebleTiltDb">高音域チルト補正（dB/octave）</param>
    /// <returns>計算されたバー振幅値配列（0.0以上）</returns>
    double[] CalculateBars(
        ReadOnlySpan<double> fftMagnitudes,
        int sampleRate,
        int barCount = 64,
        double sensitivity = 1.0,
        double bassScale = 0.55,
        double midScale = 1.0,
        double trebleScale = 2.4,
        double trebleTiltDb = 8.5);
}
