using System;

namespace AudioEffector.Domain.Services;

/// <summary>
/// FFT解析データから対数周波数分割およびチルト補正を適用してスペクトラムバー振幅を計算するドメインサービス
/// </summary>
public class SpectrumCalculator : ISpectrumCalculator
{
    /// <summary>
    /// デフォルトバー本数
    /// </summary>
    public const int DEFAULT_BAR_COUNT = 64;

    /// <summary>
    /// 最低周波数（Hz）
    /// </summary>
    public const double MIN_FREQUENCY_HZ = 20.0;

    /// <summary>
    /// 最高周波数（Hz）
    /// </summary>
    public const double MAX_FREQUENCY_HZ = 20000.0;

    /// <summary>
    /// 低音域基準スケーリング係数
    /// </summary>
    public const double DEFAULT_BASS_SCALE = 0.55;

    /// <summary>
    /// 中音域基準スケーリング係数
    /// </summary>
    public const double DEFAULT_MID_SCALE = 1.0;

    /// <summary>
    /// 高音域基準スケーリング係数
    /// </summary>
    public const double DEFAULT_TREBLE_SCALE = 2.4;

    /// <summary>
    /// 高音域チルト補正値（dB）
    /// </summary>
    public const double DEFAULT_TREBLE_TILT_DB = 8.5;

    /// <summary>
    /// デシベル変換時のフロア閾値（dB）
    /// </summary>
    public const double DB_FLOOR_THRESHOLD = 65.0;

    /// <summary>
    /// FFT振幅配列から指定本数のスペクトラムバー振幅値を計算します
    /// </summary>
    /// <param name="fftMagnitudes">FFTの各ビンの振幅（大きさ）配列</param>
    /// <param name="sampleRate">サンプリングレート（Hz）</param>
    /// <param name="barCount">出力スペクトラムバー本数</param>
    /// <param name="sensitivity">感度係数</param>
    /// <param name="bassScale">低音域スケーリング係数</param>
    /// <param name="midScale">中音域スケーリング係数</param>
    /// <param name="trebleScale">高音域スケーリング係数</param>
    /// <param name="trebleTiltDb">高音域チルト補正（dB/octave）</param>
    /// <returns>計算されたバー振幅値配列（0.0以上）</returns>
    public double[] CalculateBars(
        ReadOnlySpan<double> fftMagnitudes,
        int sampleRate,
        int barCount = DEFAULT_BAR_COUNT,
        double sensitivity = 1.0,
        double bassScale = DEFAULT_BASS_SCALE,
        double midScale = DEFAULT_MID_SCALE,
        double trebleScale = DEFAULT_TREBLE_SCALE,
        double trebleTiltDb = DEFAULT_TREBLE_TILT_DB)
    {
        if (fftMagnitudes.Length == 0 || sampleRate <= 0 || barCount <= 0)
        {
            return new double[Math.Max(0, barCount)];
        }

        var result = new double[barCount];
        int fftLength = fftMagnitudes.Length * 2; // ナイキスト周波数までの長さの2倍
        double binWidth = (double)sampleRate / fftLength;

        double minFreq = MIN_FREQUENCY_HZ;
        double maxFreq = Math.Min(MAX_FREQUENCY_HZ, sampleRate / 2.0);

        for (int i = 0; i < barCount; i++)
        {
            // 対数スケールによる帯域境界周波数の算出
            double fStart = minFreq * Math.Pow(maxFreq / minFreq, (double)i / barCount);
            double fEnd = minFreq * Math.Pow(maxFreq / minFreq, (double)(i + 1) / barCount);

            int binStart = (int)Math.Floor(fStart / binWidth);
            int binEnd = (int)Math.Ceiling(fEnd / binWidth);

            binStart = Math.Clamp(binStart, 0, fftMagnitudes.Length - 1);
            binEnd = Math.Clamp(binEnd, binStart, fftMagnitudes.Length - 1);

            // 帯域内の最大振幅（ピーク値）を抽出
            double maxMag = 0.0;
            for (int b = binStart; b <= binEnd; b++)
            {
                if (fftMagnitudes[b] > maxMag)
                {
                    maxMag = fftMagnitudes[b];
                }
            }

            // dB変換 (20 * log10(magnitude))
            double db = maxMag > 1e-7 ? 20.0 * Math.Log10(maxMag) : -100.0;

            // 中心周波数の算出
            double centerFreq = Math.Sqrt(fStart * fEnd);

            // 高音域チルト補正（250Hz以上）
            double trebleTilt = centerFreq > 250.0 ? Math.Log2(centerFreq / 250.0) * trebleTiltDb : 0.0;

            // ダイナミックレンジマッピング
            double adjustedDb = db + DB_FLOOR_THRESHOLD + trebleTilt;
            double val = Math.Max(0.0, adjustedDb) * sensitivity;

            // 周波数帯域スケーリング（Bass / Mid / Treble）
            if (centerFreq < 250.0)
            {
                double bassRatio = Math.Min(1.0, centerFreq / 250.0);
                double bassMultiplier = 0.45 + (bassScale - 0.45) * bassRatio;
                val *= bassMultiplier;
            }
            else if (centerFreq < 2500.0)
            {
                val *= midScale;
            }
            else
            {
                double trebleRatio = Math.Min(1.0, (centerFreq - 2500.0) / 12000.0);
                double trebleMultiplier = midScale + (trebleScale - midScale) * Math.Pow(trebleRatio, 0.85);
                val *= trebleMultiplier;
            }

            // エッジケース防止（NaN / Infinity は 0 に丸める）
            if (double.IsNaN(val) || double.IsInfinity(val))
            {
                val = 0.0;
            }

            result[i] = val;
        }

        return result;
    }
}
