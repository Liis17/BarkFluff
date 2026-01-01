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
using System.Windows.Media.Animation;

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

        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register("MaxValue", typeof(double), typeof(ProgressUserControl),
                new PropertyMetadata(100.0, OnMaxValueChanged));

        public static readonly DependencyProperty IsCyclicProperty =
            DependencyProperty.Register("IsCyclic", typeof(bool), typeof(ProgressUserControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty ProgressBrushProperty =
            DependencyProperty.Register("ProgressBrush", typeof(Brush), typeof(ProgressUserControl),
                new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#78422e"))));

        public static readonly DependencyProperty EmptyBrushProperty =
            DependencyProperty.Register("EmptyBrush", typeof(Brush), typeof(ProgressUserControl),
                new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCE8F69"))));

        private DispatcherTimer timer;
        private double angle = 0;
        private Storyboard animationStoryboard;

        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public double MaxValue
        {
            get { return (double)GetValue(MaxValueProperty); }
            set { SetValue(MaxValueProperty, value); }
        }

        public bool IsCyclic
        {
            get { return (bool)GetValue(IsCyclicProperty); }
            set { SetValue(IsCyclicProperty, value); }
        }

        public Brush ProgressBrush
        {
            get { return (Brush)GetValue(ProgressBrushProperty); }
            set { SetValue(ProgressBrushProperty, value); }
        }

        public Brush EmptyBrush
        {
            get { return (Brush)GetValue(EmptyBrushProperty); }
            set { SetValue(EmptyBrushProperty, value); }
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

        private static void OnMaxValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((ProgressUserControl)d).UpdateProgress();
        }

        private void UpdateProgress()
        {
            if (MaxValue <= 0)
                return;

            double clampedValue = Math.Clamp(Value, 0, MaxValue);
            double percentage = clampedValue / MaxValue;
            double totalWidth = ActualWidth;
            ProgressBorder.Width = totalWidth * percentage;
            EmptyBorder.Width = totalWidth - ProgressBorder.Width;
        }

        public void AnimStart()
        {
            if (animationStoryboard != null)
            {
                animationStoryboard.Stop();
            }

            double targetValue = Value;
            Value = 0;

            animationStoryboard = new Storyboard();
            
            DoubleAnimation animation = new DoubleAnimation
            {
                From = 0,
                To = targetValue,
                Duration = TimeSpan.FromMilliseconds(800),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(animation, this);
            Storyboard.SetTargetProperty(animation, new PropertyPath(ValueProperty));
            
            animationStoryboard.Children.Add(animation);
            animationStoryboard.Begin();
        }

        private void StartTestAnimation()
        {
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += (s, e) =>
            {
                angle += 0.01;
                Value = (MaxValue / 2) + (MaxValue / 2) * Math.Sin(angle);
            };
            timer.Start();
        }
    }
}
