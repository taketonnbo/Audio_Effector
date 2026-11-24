namespace AudioEffector.Application.Common;

/// <summary>
/// FFT計算結果の複素数値を表す軽量構造体（外部DSPライブラリ非依存）
/// </summary>
public readonly struct FftComplex
{
    /// <summary>
    /// 実部
    /// </summary>
    public float X { get; }

    /// <summary>
    /// 虚部
    /// </summary>
    public float Y { get; }

    /// <summary>
    /// FftComplexを初期化します
    /// </summary>
    /// <param name="x">実部</param>
    /// <param name="y">虚部</param>
    public FftComplex(float x, float y)
    {
        X = x;
        Y = y;
    }
}
