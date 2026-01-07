using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using BarkFluff.Client.WPF.Pages;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    public partial class VideoMessageContent : UserControl
    {
        private AttachmentsModel? _attachment;

        public VideoMessageContent()
        {
            InitializeComponent();
            SizeChanged += VideoMessageContent_SizeChanged;
        }

        public VideoMessageContent(string fileId, string? previewUrl) : this()
        {
            // Оставлено для обратной совместимости - показываем placeholder
            // Не загружаем превью, так как сервер не возвращает его
        }

        public VideoMessageContent(AttachmentsModel attachment) : this()
        {
            _attachment = attachment;

            // Пытаемся загрузить превью если есть PreviewUrl
            if (!string.IsNullOrEmpty(attachment.PreviewUrl))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(attachment.PreviewUrl, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    PreviewImage.Source = bitmap;
                }
                catch
                {
                    // Если не удалось загрузить превью - остается placeholder из XAML
                }
            }
            // Иначе остается placeholder из XAML
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_attachment != null)
            {
                OpenVideoPlayer(new List<AttachmentsModel> { _attachment }, 0);
            }
            e.Handled = true;
        }

        private void OpenVideoPlayer(List<AttachmentsModel> attachments, int currentIndex)
        {
            var messengerPage = FindParent<MessengerPage>(this);
            messengerPage?.OpenVideoPlayer(attachments, currentIndex);
        }

        private T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            return parent is T ? (T)parent : FindParent<T>(parent);
        }

        private void VideoMessageContent_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ClipGeometry.Rect = new Rect(0, 0, VideoBorder.ActualWidth, VideoBorder.ActualHeight);
        }
    }
}
