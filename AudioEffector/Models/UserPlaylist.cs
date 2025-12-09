using System.Collections.Generic;

namespace AudioEffector.Models
{
    public class UserPlaylist
    {
        public string Name { get; set; } = string.Empty;
        public List<string> TrackPaths { get; set; } = new List<string>();

        [System.Text.Json.Serialization.JsonIgnore]
        public System.Collections.ObjectModel.ObservableCollection<string> ThumbnailTrackPaths { get; set; } = new System.Collections.ObjectModel.ObservableCollection<string>();
    }
}
