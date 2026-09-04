using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace AudioEffector.Infrastructure.Audio;

/// <summary>
/// NAudioのISampleProviderを実装し、10バンドBiQuadフィルターによるイコライザー処理を行うDSPクラス
/// </summary>
public class EqualizerDsp : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly BiQuadFilter[,] _filters;
    private readonly float[] _frequencies;
    private readonly float[] _gains;
    private readonly int _channels;
    private readonly int _bandCount;
    private bool _updated = true;

    /// <summary>
    /// 出力音声フォーマット
    /// </summary>
    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>
    /// イコライザーDSPを初期化します
    /// </summary>
    /// <param name="source">入力オーディオソース</param>
    /// <param name="frequencies">調整対象の周波数配列（Hz）</param>
    public EqualizerDsp(ISampleProvider source, float[] frequencies)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(frequencies);

        _source = source;
        _channels = source.WaveFormat.Channels;
        _bandCount = frequencies.Length;
        _frequencies = (float[])frequencies.Clone();
        _gains = new float[_bandCount];
        _filters = new BiQuadFilter[_channels, _bandCount];
        CreateFilters();
    }

    /// <summary>
    /// 指定されたバンドのゲイン（dB）を更新します
    /// </summary>
    /// <param name="bandIndex">バンドインデックス</param>
    /// <param name="gainDb">ゲイン値（dB）</param>
    public void SetBandGain(int bandIndex, float gainDb)
    {
        if (bandIndex >= 0 && bandIndex < _bandCount)
        {
            _gains[bandIndex] = gainDb;
            _updated = true;
        }
    }

    /// <summary>
    /// 指定されたバンドのゲイン（dB）を更新します（SetBandGainの互換エイリアス）
    /// </summary>
    public void UpdateGain(int bandIndex, float gain) => SetBandGain(bandIndex, gain);

    /// <summary>
    /// 全バンドのゲイン（dB）を一括更新します
    /// </summary>
    /// <param name="gainsDb">ゲイン配列</param>
    public void SetAllGains(float[] gainsDb)
    {
        ArgumentNullException.ThrowIfNull(gainsDb);
        int count = Math.Min(_bandCount, gainsDb.Length);
        for (int i = 0; i < count; i++)
        {
            _gains[i] = gainsDb[i];
        }

        _updated = true;
    }

    private void CreateFilters()
    {
        int sampleRate = _source.WaveFormat.SampleRate;
        for (int ch = 0; ch < _channels; ch++)
        {
            for (int band = 0; band < _bandCount; band++)
            {
                _filters[ch, band] = BiQuadFilter.PeakingEQ(sampleRate, _frequencies[band], 0.8f, _gains[band]);
            }
        }
    }

    private void UpdateFilters()
    {
        int sampleRate = _source.WaveFormat.SampleRate;
        for (int ch = 0; ch < _channels; ch++)
        {
            for (int band = 0; band < _bandCount; band++)
            {
                _filters[ch, band].SetPeakingEq(sampleRate, _frequencies[band], 0.8f, _gains[band]);
            }
        }

        _updated = false;
    }

    /// <summary>
    /// サンプルデータを読み込み、イコライザーフィルターを適用します
    /// </summary>
    /// <param name="buffer">出力バッファ</param>
    /// <param name="offset">オフセット</param>
    /// <param name="count">読み込みサンプル数</param>
    /// <returns>読み込まれたサンプル数</returns>
    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);

        if (_updated)
        {
            UpdateFilters();
        }

        for (int n = 0; n < samplesRead; n += _channels)
        {
            for (int ch = 0; ch < _channels; ch++)
            {
                float sample = buffer[offset + n + ch];
                for (int band = 0; band < _bandCount; band++)
                {
                    sample = _filters[ch, band].Transform(sample);
                }

                buffer[offset + n + ch] = sample;
            }
        }

        return samplesRead;
    }
}
