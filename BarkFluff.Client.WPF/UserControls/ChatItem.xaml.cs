using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

using Color = System.Windows.Media.Color;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для ChatItem.xaml
    /// </summary>
    public partial class ChatItem : UserControl
    {
        public enum ReadingStatus
        {
            None,
            OnlySent,
            SentAndRead
        }
        private string _url;
        public ChatItem(string imageUrl, string chatName, string lastMessageText, string time, ReadingStatus reading, List<long> readBy)
        {
            InitializeComponent();
            Title.Text = chatName;
            LastMessage.Text = lastMessageText;
            TimeMessage.Text = FormatDateTime(time.Length >= 2 ? time.Substring(1, time.Length - 2) : time);

            Loaded += ChatItem_Loaded;
            _url = imageUrl;
        }

        private async void ChatItem_Loaded(object sender, RoutedEventArgs e)
        {
            ImageBrush imageBrush = new ImageBrush
            {
                ImageSource = new BitmapImage(new Uri(_url)),
                Stretch = Stretch.UniformToFill
            };
            border.Background = imageBrush;

            Color averageColor = await Task.Run(() =>
            {
                return App.ColorAnalyzer.GetAverageColorFromUrl(_url);
            });

            DropShadowEffect shadowEffect = new DropShadowEffect
            {
                BlurRadius = 12,
                Opacity = 0.7,
                ShadowDepth = 0,
                Color = averageColor
            };
            border.Effect = shadowEffect;
        }

        private string FormatDateTime(string input)
        {
            if (!DateTime.TryParseExact(input, "yyyy-MM-ddTHH:mm:ss.ffffffZ",
                CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime dateTimeUtc))
            {
                return "Неверный формат даты";
            }

            DateTime localDateTime = dateTimeUtc.ToLocalTime();
            DateTime now = DateTime.Now;

            CultureInfo ruCulture = new CultureInfo("ru-RU");

            if (localDateTime.Date == now.Date)
            {
                return localDateTime.ToString("HH:mm");
            }

            System.Globalization.Calendar calendar = ruCulture.Calendar;
            CalendarWeekRule rule = ruCulture.DateTimeFormat.CalendarWeekRule;
            DayOfWeek firstDayOfWeek = ruCulture.DateTimeFormat.FirstDayOfWeek;

            int weekNow = calendar.GetWeekOfYear(now, rule, firstDayOfWeek);
            int weekThen = calendar.GetWeekOfYear(localDateTime, rule, firstDayOfWeek);

            if (localDateTime.Year == now.Year && weekThen == weekNow)
            {
                return localDateTime.ToString("ddd", ruCulture); 
            }
            else if (localDateTime.Year == now.Year)
            {
                
                return localDateTime.ToString("dd MMM", ruCulture); 
            }
            else
            {
                return localDateTime.ToString("dd MMM yyyy", ruCulture); 
            }
        }
    }
}
