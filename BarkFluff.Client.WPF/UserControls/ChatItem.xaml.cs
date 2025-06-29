using BarkFluff.Client.WPF.Services;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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
        public ChatItem(string imageUrl, string chatName, string lastMessageText, string time, ReadingStatus reading)
        {
            InitializeComponent();
            Title.Text = chatName;
            LastMessage.Text = lastMessageText;
            TimeMessage.Text = FormatDateTime(time.Length >=2 ? time.Substring(1, time.Length - 2) : time);

            Loaded += ChatItem_Loaded;
            _url = imageUrl;
        }

        private async void ChatItem_Loaded(object sender, RoutedEventArgs e)
        {
            // Загружаем изображение в UI (можно сразу)
            ImageBrush imageBrush = new ImageBrush
            {
                ImageSource = new BitmapImage(new Uri(_url)),
                Stretch = Stretch.UniformToFill
            };
            border.Background = imageBrush;

            // Асинхронно получаем средний цвет
            Color averageColor = await Task.Run(() =>
            {
                return App.ColorAnalyzer.GetAverageColorFromUrl(_url);
            });

            // Применяем DropShadowEffect в UI-потоке
            DropShadowEffect shadowEffect = new DropShadowEffect
            {
                BlurRadius = 12,
                Opacity = 0.3,
                ShadowDepth = 0,
                Color = averageColor
            };
            border.Effect = shadowEffect;
        }

        private string FormatDateTime(string input)
        {
            // Парсим строку как UTC
            if (!DateTime.TryParseExact(input, "yyyy-MM-ddTHH:mm:ss.ffffffZ",
                CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime dateTimeUtc))
            {
                return "Неверный формат даты";
            }

            // Переводим в локальное время
            DateTime localDateTime = dateTimeUtc.ToLocalTime();
            DateTime now = DateTime.Now;

            if (localDateTime.Date == now.Date)
            {
                // Если сегодня — возвращаем только время (часы и минуты)
                return localDateTime.ToString("HH:mm");
            }
            else if (localDateTime.Date <= now.Date.AddDays(-1))
            {
                // Если вчера или ранее — возвращаем сокращённое название дня недели (Пн, Вт и т.д.)
                return localDateTime.ToString("ddd", new CultureInfo("ru-RU"));
            }

            // По условию других случаев быть не должно
            return "";
        }
    }
}
