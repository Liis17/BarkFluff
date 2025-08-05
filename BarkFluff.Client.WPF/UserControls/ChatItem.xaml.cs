using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

using Color = System.Windows.Media.Color;
using Erida = BarkFluff.Client.WPF.Services.Erida.MessageType;
using MType = BarkFluff.Client.WPF.Services.Erida.MessageType.MessageTypeEnum;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для ChatItem.xaml
    /// </summary>
    public partial class ChatItem : UserControl
    {
        /// <summary>
        /// Статус прочтения сообщения
        /// </summary>
        public enum ReadingStatus
        {
            /// <summary>
            /// Сообщение отправлено и прочитано мной
            /// </summary>
            Me,

            /// <summary>
            /// Сообщение отправлено, но не прочитано собеседником
            /// </summary>
            OnlySent,

            /// <summary>
            /// Сообщение отправлено и прочитано собеседником
            /// </summary>
            SentAndRead,

            /// <summary>
            /// Сообщение отправлено мне, но не прочитано мной
            /// </summary>
            ForMe
        }
        private string _url;
        public ChatItem(string imageUrl, string chatName, string lastMessageText, string time, ReadingStatus reading, List<long> readBy, long unReaded)
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
                BlurRadius = 15,
                Opacity = 0.9,
                ShadowDepth = 2,
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
