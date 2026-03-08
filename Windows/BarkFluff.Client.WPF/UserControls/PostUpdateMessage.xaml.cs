using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для PostUpdateMessage.xaml
    /// </summary>
    public partial class PostUpdateMessage : UserControl
    {
        public PostUpdateMessage()
        {
            InitializeComponent();
            TitleText.Text = $"{AppVersion.AppName} обновлен";
            VersionText.Text = $"Текущая версия {AppVersion.VersionName}{AppVersion.Version}";
        }
    }
}
