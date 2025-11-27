using BarkFluff.Client.WPF.Pages.SetupPages;
using BarkFluff.Client.WPF.Pages.SetupPages.Registration;
using BarkFluff.Client.WPF.Services.App.Converter;
using BarkFluff.Client.WPF.Services.Notification;
using BarkFluff.Client.WPF.UserControls;

using System.Windows;

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
            toastNotificationService.ShowToast("Apogee", "пошли в фортнайт пёс", "https://image.barkfluff.com/apogeeavatar.png");
        }

        private void AdvancedNotification(object sender, RoutedEventArgs e)
        {

        }

        public void ImageNotification(object sender, RoutedEventArgs e)
        {
            toastNotificationService.ShowToastWithImage(
            "Apogee",
            "Отправил(a) изображение",
            "https://image.barkfluff.com/apogeeavatar.png",
            "https://image.barkfluff.com/photo_52063@10-08-2024_19-42-02.jpg"
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

        private void VoiceRecord(object sender, RoutedEventArgs e)
        {
            var child = new RecordButton();
            HolaBolaGrid.Children.Clear();
            HolaBolaGrid.Children.Add(child);
        }

        private void viewVoice(object sender, RoutedEventArgs e)
        {
            var a = AudioAnalyzer.AnalyzeLoudness(@"C:\Users\daske\Desktop\record_20250720_002906.ogg");
            var child = new VoiceMessage(a);
            HolaBolaGrid.Children.Clear();
            HolaBolaGrid.Children.Add(child);
        }

        private void OpenVideoPlayer(object sender, RoutedEventArgs e)
        {
            var child = new VideoPlayer();
            HolaBolaGrid.Children.Clear();
            HolaBolaGrid.Children.Add(child);
        }
    }
}
