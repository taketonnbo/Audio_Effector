using System;
using NAudio.Dsp;

namespace AudioEffector.Application.Common;

/// <summary>
/// FFT計算完了時のイベント引数（Complex配列通知）
/// </summary>
public class FftEventArgs : EventArgs
{
    /// <summary>
    /// FFT計算結果の複素数配列
    /// </summary>
    public Complex[] Result { get; }

    /// <summary>
    /// FftEventArgsを初期化します
    /// </summary>
    /// <param name="result">FFT複素数配列</param>
    public FftEventArgs(Complex[] result)
    {
        Result = result ?? Array.Empty<Complex>();
    }
}
