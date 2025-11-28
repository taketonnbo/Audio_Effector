using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace AudioEffector.Models
{
    public class Album
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public BitmapImage CoverImage { get; set; }
        public List<Track> Tracks { get; set; } = new List<Track>();
        public uint Year { get; set; }
    }
}
