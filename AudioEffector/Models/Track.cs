using System.Windows.Media.Imaging;
using System;

namespace AudioEffector.Models
{
    public class Track
    {
        public string FilePath { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public BitmapImage CoverImage { get; set; }
        public TimeSpan Duration { get; set; }
        public bool IsFavorite { get; set; }
        public uint Year { get; set; }
        public int Bitrate { get; set; }
        public int SampleRate { get; set; }
        public int BitsPerSample { get; set; }
        public string Format { get; set; }
        public bool IsLossless { get; set; }
        public bool IsHiRes { get; set; }
        public bool IsSelected { get; set; }

        public string QualityInfo => $"{BitsPerSample}bit/{SampleRate / 1000.0:F1}kHz {Format}";
        public string QualityLabel => IsHiRes ? "Hi-Res" : (IsLossless ? "Lossless" : "");
    }
}
