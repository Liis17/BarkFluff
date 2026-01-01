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
                new PropertyMetadata(false, OnIsCyclicChanged));

        public static readonly DependencyProperty ProgressBrushProperty =
            DependencyProperty.Register("ProgressBrush", typeof(Brush), typeof(ProgressUserControl),
                new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#78422e"))));

        public static readonly DependencyProperty EmptyBrushProperty =
            DependencyProperty.Register("EmptyBrush", typeof(Brush), typeof(ProgressUserControl),
                new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCE8F69"))));

        private Storyboard cyclicAnimationStoryboard;
        private Storyboard animStartStoryboard;

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
                if (IsCyclic)
                {
                    StartCyclicAnimation();
                }
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

        private static void OnIsCyclicChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ProgressUserControl)d;
            if ((bool)e.NewValue && control.IsLoaded)
            {
                control.StartCyclicAnimation();
            }
            else
            {
                control.StopCyclicAnimation();
            }
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
            StopAllAnimations();

            double targetValue = Value;
            Value = 0;

            animStartStoryboard = new Storyboard();
            
            DoubleAnimation animation = new DoubleAnimation
            {
                From = 0,
                To = targetValue,
                Duration = TimeSpan.FromMilliseconds(800),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(animation, this);
            Storyboard.SetTargetProperty(animation, new PropertyPath(ValueProperty));
            
            animStartStoryboard.Children.Add(animation);
            animStartStoryboard.Begin();
        }

        private void StartCyclicAnimation()
        {
            StopCyclicAnimation();

            cyclicAnimationStoryboard = new Storyboard
            {
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = true
            };

            DoubleAnimation animation = new DoubleAnimation
            {
                From = 0,
                To = MaxValue,
                Duration = TimeSpan.FromSeconds(2),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            Storyboard.SetTarget(animation, this);
            Storyboard.SetTargetProperty(animation, new PropertyPath(ValueProperty));

            cyclicAnimationStoryboard.Children.Add(animation);
            cyclicAnimationStoryboard.Begin();
        }

        private void StopCyclicAnimation()
        {
            if (cyclicAnimationStoryboard != null)
            {
                cyclicAnimationStoryboard.Stop();
                cyclicAnimationStoryboard = null;
            }
        }

        private void StopAllAnimations()
        {
            StopCyclicAnimation();
            
            if (animStartStoryboard != null)
            {
                animStartStoryboard.Stop();
                animStartStoryboard = null;
            }
        }
    }
}
