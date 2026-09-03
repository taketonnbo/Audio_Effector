using System;
using AudioEffector.Application.Common;
using NAudio.Dsp;
using NAudio.Wave;

namespace AudioEffector.Infrastructure.Audio;

/// <summary>
/// 音声サンプルを集計し、FFT（高速フーリエ変換）を実行して振幅データを通知するDSPクラス
/// </summary>
public class SampleAggregator : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _fftLength;
    private readonly Complex[] _fftBuffer;
    private readonly float[] _sampleBuffer;
    private int _sampleBufferIndex;

    /// <summary>
    /// FFT計算完了時に発生するイベント（振幅データ）
    /// </summary>
    public event EventHandler<FftCalculatedEventArgs>? FftCalculated;

    /// <summary>
    /// FFT計算完了時に発生するイベント（複素数データ・旧互換）
    /// </summary>
    public event EventHandler<FftEventArgs>? ComplexFftCalculated;

    /// <summary>
    /// 出力音声フォーマット
    /// </summary>
    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>
    /// SampleAggregatorを初期化します
    /// </summary>
    /// <param name="source">入力オーディオソース</param>
    /// <param name="fftLength">FFT長（2のべき乗、デフォルト: 1024）</param>
    public SampleAggregator(ISampleProvider source, int fftLength = 1024)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _fftLength = fftLength;
        _fftBuffer = new Complex[fftLength];
        _sampleBuffer = new float[fftLength];
        _sampleBufferIndex = 0;
    }

    /// <summary>
    /// 音声サンプルを読み込み、バッファ蓄積時にFFTを実行します
    /// </summary>
    /// <param name="buffer">出力バッファ</param>
    /// <param name="offset">オフセット</param>
    /// <param name="count">サンプル数</param>
    /// <returns>読み込まれたサンプル数</returns>
    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);
        int channels = _source.WaveFormat.Channels;

        for (int n = 0; n < samplesRead; n += channels)
        {
            AddSample(buffer[offset + n]);
        }

        return samplesRead;
    }

    private void AddSample(float sampleValue)
    {
        _sampleBuffer[_sampleBufferIndex] = sampleValue;
        _sampleBufferIndex++;

        if (_sampleBufferIndex >= _fftLength)
        {
            _sampleBufferIndex = 0;
            CalculateFft();
        }
    }

    private void CalculateFft()
    {
        // ハミング窓（Hamming Window）の適用
        for (int i = 0; i < _fftLength; i++)
        {
            double window = FastFourierTransform.HammingWindow(i, _fftLength);
            _fftBuffer[i].X = (float)(_sampleBuffer[i] * window);
            _fftBuffer[i].Y = 0;
        }

        int log2 = (int)Math.Log2(_fftLength);
        FastFourierTransform.FFT(true, log2, _fftBuffer);

        // ナイキスト周波数までの振幅（Magnitude）を算出
        int halfLength = _fftLength / 2;
        var magnitudes = new double[halfLength];
        for (int i = 0; i < halfLength; i++)
        {
            double real = _fftBuffer[i].X;
            double imag = _fftBuffer[i].Y;
            magnitudes[i] = Math.Sqrt(real * real + imag * imag);
        }

        FftCalculated?.Invoke(this, new FftCalculatedEventArgs(magnitudes, _source.WaveFormat.SampleRate));
        ComplexFftCalculated?.Invoke(this, new FftEventArgs(_fftBuffer));
    }
}
