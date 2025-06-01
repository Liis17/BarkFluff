using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace BarkFluff.Client.WPF.Services.App
{
    public class AvatarImageHolder
    {
        private BitmapSource _image;
        public BitmapSource Image
        {
            get => _image;
            set
            {
                if (_image != value)
                {
                    _image = value;
                    OnPropertyChanged(nameof(Image));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
