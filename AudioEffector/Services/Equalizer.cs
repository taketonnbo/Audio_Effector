using NAudio.Wave;
using NAudio.Dsp;
using System;

namespace AudioEffector.Services
{
    public class Equalizer : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly BiQuadFilter[,] _filters;
        private readonly float[] _frequencies;
        private readonly float[] _gains;
        private readonly int _channels;
        private readonly int _bandCount;
        private bool _updated = true;

        public Equalizer(ISampleProvider source, float[] frequencies)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            _bandCount = frequencies.Length;
            _frequencies = frequencies;
            _gains = new float[_bandCount];
            _filters = new BiQuadFilter[_channels, _bandCount];
            CreateFilters();
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public void UpdateGain(int bandIndex, float gain)
        {
            if (bandIndex >= 0 && bandIndex < _bandCount)
            {
                _gains[bandIndex] = gain;
                _updated = true;
            }
        }

        private void CreateFilters()
        {
            for (int ch = 0; ch < _channels; ch++)
            {
                for (int band = 0; band < _bandCount; band++)
                {
                    _filters[ch, band] = BiQuadFilter.PeakingEQ(_source.WaveFormat.SampleRate, _frequencies[band], 0.8f, _gains[band]);
                }
            }
        }

        private void UpdateFilters()
        {
             for (int ch = 0; ch < _channels; ch++)
            {
                for (int band = 0; band < _bandCount; band++)
                {
                    _filters[ch, band].SetPeakingEq(_source.WaveFormat.SampleRate, _frequencies[band], 0.8f, _gains[band]);
                }
            }
            _updated = false;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);

            if (_updated) UpdateFilters();

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
}
