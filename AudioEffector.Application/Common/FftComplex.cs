namespace AudioEffector.Application.Common;

/// <summary>
/// FFT計算結果の複素数値を表す軽量構造体（外部DSPライブラリ非依存）
/// </summary>
/// <param name="X">実部</param>
/// <param name="Y">虚部</param>
public readonly record struct FftComplex(float X, float Y);
