using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioEffector.Models
{
    public class DeviceTrack : INotifyPropertyChanged
    {
        public string Title { get; set; }
        public string Path { get; set; } // File path on the device
        
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
