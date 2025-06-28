using BarkFluff.Client.WPF.Pages.SetupPages;
using BarkFluff.Client.WPF.Pages.SetupPages.Registration;
using BarkFluff.Client.WPF.Services.App.Converter;
using BarkFluff.Client.WPF.Services.Notification;
using BarkFluff.Client.WPF.UserControls;

using System.Threading.Tasks;
using System.Windows;

using Windows.ApplicationModel.Appointments;

using Wpf.Ui.Controls;

using MessageBox = System.Windows.MessageBox;


namespace BarkFluff.Client.WPF.Debugs
{
    /// <summary>
    /// Логика взаимодействия для DebugWindow.xaml
    /// </summary>
    public partial class DebugWindow : FluentWindow
    {
        private ToastNotificationService toastNotificationService;
        public DebugWindow()
        {
            InitializeComponent();
            toastNotificationService = new ToastNotificationService();
        }

        private async void GetQrCodeToOtp(object sender, RoutedEventArgs e)
        {
            await App.ServerCommunication.OtpReceipt(App.GParam);
        }

        private void SimpleNotification(object sender, RoutedEventArgs e)
        {
            toastNotificationService.ShowToast("Apogee","пошли в фортнайт пёс", "https://charlie.liis17.ru/apogeemini.png");
        }

        private void AdvancedNotification(object sender, RoutedEventArgs e)
        {
           
        }

        public void ImageNotification(object sender, RoutedEventArgs e) 
        {
            toastNotificationService.ShowToastWithImage(
            "Apogee",
            "Отправил(a) изображение",
            "https://charlie.liis17.ru/apogeemini.png", 
            "https://charlie.liis17.ru/FortniteClient-Win64-Shipping_lOV3mo24Xo.jpg"
            );
        }

        private void OpenCropper(object sender, RoutedEventArgs e)
        {
            var child = new CropImage();
            HolaBolaGrid.Children.Clear();
            HolaBolaGrid.Children.Add(child);
        }

        private void Openotp(object sender, RoutedEventArgs e)
        {
            var child = new TwoFA();
            HolaBolaGrid.Children.Clear();
            HolaBolaGrid.Children.Add(child);
        }

        private async void CompressVideo(object sender, RoutedEventArgs e)
        {
            await VideoCompressor.CompressAsync(@"C:\Users\daske\Desktop\test1\original.mp4", @"C:\Users\daske\Desktop\test1\NEoriginal.mp4");
            MessageBox.Show("Обработка завершена");
        }

        private void ShowCompletionRegistrationWindow(object sender, RoutedEventArgs e)
        {
            var child = new CompletionRegistration();
            HolaBolaGrid.Children.Clear();
            HolaBolaGrid.Children.Add(child);
        }

        private void OpenSendMessage(object sender, RoutedEventArgs e)
        {
            var child = new DebugSendMessageUI();
            HolaBolaGrid.Children.Clear();
            HolaBolaGrid.Children.Add(child);
        }

        private async void GetChats(object sender, RoutedEventArgs e)
        {
            var response = await App.ServerCommunication.GetChats(App.GParam);
            var a = response;
        }
    }
}
