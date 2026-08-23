using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Diagnostics;

namespace AudioEffector.Services
{
    /// <summary>
    /// 音声サンプルを集計し、FFT（高速フーリエ変換）を実行するためのクラス。
    /// スペクトラムアナライザーのデータソースとして機能します。
    /// </summary>
    public class SampleAggregator : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _fftLength;
        private readonly Complex[] _fftBuffer;
        private readonly float[] _sampleBuffer;
        private int _sampleBufferIndex;

        /// <summary>
        /// FFT計算完了時に発生するイベント。
        /// </summary>
        public event EventHandler<FftEventArgs>? FftCalculated;

        public SampleAggregator(ISampleProvider source, int fftLength = 1024)
        {
            _source = source;
            _fftLength = fftLength;
            _fftBuffer = new Complex[fftLength];
            _sampleBuffer = new float[fftLength];
            _sampleBufferIndex = 0;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);

            for (int i = 0; i < samplesRead; i++)
            {
                Add(buffer[offset + i]);
            }

            return samplesRead;
        }

        private void Add(float value)
        {
            if (_sampleBufferIndex >= _fftLength)
            {
                // Buffer full, perform FFT
                // バッファがいっぱいになったらFFTを実行
                PerformFft();
                _sampleBufferIndex = 0;
            }

            _sampleBuffer[_sampleBufferIndex++] = value;
        }

        private void PerformFft()
        {
            // Copy to complex buffer and apply Hann window
            // 複素数バッファにコピーし、ハン窓関数を適用
            for (int i = 0; i < _fftLength; i++)
            {
                float window = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (_fftLength - 1))));
                _fftBuffer[i].X = _sampleBuffer[i] * window;
                _fftBuffer[i].Y = 0;
            }

            // Perform FFT
            // FFTを実行
            FastFourierTransform.FFT(true, (int)Math.Log(_fftLength, 2.0), _fftBuffer);

            // Calculate magnitudes
            // We only need the first half (Nyquist frequency)
            // But for visualization, we might want to group them later.
            // Here we pass the raw complex data or calculated magnitudes.
            // Let's pass magnitudes.

            // Note: FastFourierTransform.FFT in NAudio might not be available in standard NAudio package without NAudio.Dsp?
            // Actually NAudio.Dsp is usually included or I might need to implement it.
            // Let's assume NAudio.Dsp is available as it is a common dependency. 
            // If not, I will get a build error and fix it.

            FftCalculated?.Invoke(this, new FftEventArgs(_fftBuffer));
        }
    }

    public class FftEventArgs : EventArgs
    {
        public Complex[] Result { get; }

        public FftEventArgs(Complex[] result)
        {
            Result = result;
        }
    }
}
