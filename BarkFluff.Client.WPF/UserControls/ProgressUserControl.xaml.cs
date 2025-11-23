using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для ProgressUserControl.xaml
    /// </summary>
    public partial class ProgressUserControl : UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(ProgressUserControl),
                new PropertyMetadata(0.0, OnValueChanged));
        private DispatcherTimer timer;
        private double angle = 0;
        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public ProgressUserControl()
        {
            InitializeComponent();
            Loaded += (s, e) => {
                UpdateProgress();
                StartTestAnimation();
            };
            SizeChanged += (s, e) => UpdateProgress();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((ProgressUserControl)d).UpdateProgress();
        }

        private void UpdateProgress()
        {
            double clampedValue = Math.Clamp(Value, 0, 100);
            double percentage = clampedValue / 100.0;
            double totalWidth = ActualWidth;
            ProgressBorder.Width = totalWidth * percentage;
            EmptyBorder.Width = totalWidth - ProgressBorder.Width;
        }

        private void StartTestAnimation()
        {
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(2); // ~60 FPS
            timer.Tick += (s, e) =>
            {
                angle += 0.01; // Скорость анимации, подкорректируйте по вкусу
                Value = 50 + 50 * Math.Sin(angle);
            };
            timer.Start();
        }
    }
}
