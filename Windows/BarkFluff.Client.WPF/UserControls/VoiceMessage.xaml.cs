using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для VoiceMessage.xaml
    /// </summary>
    public partial class VoiceMessage : UserControl
    {
        private const double BarWidth = 10;
        private const double MinHeight = 10;
        private const double MaxHeight = 100;
        public VoiceMessage(List<int> values)
        {
            InitializeComponent();

            if (values == null || values.Count != 40)
                throw new ArgumentException("Должно быть ровно 40 значений от 0 до 100");

            foreach (int value in values)
            {
                double normalized = Math.Max(0, Math.Min(100, value));
                double barHeight = MinHeight + (normalized / 100.0) * (MaxHeight - MinHeight);

                var bar = new Border
                {
                    Width = BarWidth,
                    Height = barHeight,
                    CornerRadius = new CornerRadius(4),
                    Background = Brushes.Black,
                    Margin = new Thickness(2, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Bottom
                };

                BarsPanel.Children.Add(bar);
            }
        }
    }
}
