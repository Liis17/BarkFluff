using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BarkFluff.Client.WPF.UserControls
{
    public partial class ProfileShare : UserControl
    {
        private string _barkflufffPrefix = "https://barkfluff.com/";
        public ProfileShare(string username)
        {
            InitializeComponent();
        }

        #region Dependency Properties

        // 1. Свойство для имени пользователя (напр. @barkfluff)
        public static readonly DependencyProperty UsernameProperty =
            DependencyProperty.Register("Username", typeof(string), typeof(ProfileShare), new PropertyMetadata(string.Empty));

        public string Username
        {
            get { return (string)GetValue(UsernameProperty); }
            set { SetValue(UsernameProperty, value); }
        }

        // 3. Свойство для картинки Аватара
        public static readonly DependencyProperty AvatarSourceProperty =
            DependencyProperty.Register("AvatarSource", typeof(ImageSource), typeof(ProfileShare), new PropertyMetadata(null));

        public ImageSource AvatarSource
        {
            get { return (ImageSource)GetValue(AvatarSourceProperty); }
            set { SetValue(AvatarSourceProperty, value); }
        }

        // 4. Свойство для картинки QR кода
        // (Обычно QR генерируется в ViewModel и передается сюда как Bitmap/ImageSource)
        public static readonly DependencyProperty QrCodeSourceProperty =
            DependencyProperty.Register("QrCodeSource", typeof(ImageSource), typeof(ProfileShare), new PropertyMetadata(null));

        public ImageSource QrCodeSource
        {
            get { return (ImageSource)GetValue(QrCodeSourceProperty); }
            set { SetValue(QrCodeSourceProperty, value); }
        }

        #endregion
    }
}