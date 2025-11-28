using System.Collections.Generic;

namespace AudioEffector.Models
{
    public class Preset
    {
        public string Name { get; set; }
        public List<float> Gains { get; set; } = new List<float>();
    }
}
