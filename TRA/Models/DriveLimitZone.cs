using System.ComponentModel;
using System.Windows.Media;

namespace TRA.Models
{
    public class DriveLimitZone : INotifyPropertyChanged
    {
        private int _id;
        private double _azimuthMin;
        private double _azimuthMax;
        private double _elevationMin;
        private double _elevationMax;

        public int Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        public double AzimuthMin
        {
            get => _azimuthMin;
            set { _azimuthMin = value; OnPropertyChanged(nameof(AzimuthMin)); }
        }

        public double AzimuthMax
        {
            get => _azimuthMax;
            set { _azimuthMax = value; OnPropertyChanged(nameof(AzimuthMax)); }
        }

        public double ElevationMin
        {
            get => _elevationMin;
            set { _elevationMin = value; OnPropertyChanged(nameof(ElevationMin)); }
        }

        public double ElevationMax
        {
            get => _elevationMax;
            set { _elevationMax = value; OnPropertyChanged(nameof(ElevationMax)); }
        }

        public Color Color { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
