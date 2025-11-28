namespace AudioEffector.Models
{
    public class EqualizerBand
    {
        public float Frequency { get; set; }
        public float Gain { get; set; }
        public float Bandwidth { get; set; } = 0.8f;
    }
}
