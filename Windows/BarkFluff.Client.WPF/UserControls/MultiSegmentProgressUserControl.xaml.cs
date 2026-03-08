using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BarkFluff.Client.WPF.UserControls
{
    public partial class MultiSegmentProgressUserControl : UserControl
    {
        public class Segment
        {
            public int Value { get; set; }
            public Brush Brush { get; set; }
            public Border Border { get; set; }
        }

        private List<Segment> segments = new List<Segment>();
        private Dictionary<Segment, Storyboard> animationStoryboards = new Dictionary<Segment, Storyboard>();

        public static readonly DependencyProperty IsCyclicProperty =
            DependencyProperty.Register("IsCyclic", typeof(bool), typeof(MultiSegmentProgressUserControl),
                new PropertyMetadata(false, OnIsCyclicChanged));

        public static readonly DependencyProperty EmptyBrushProperty =
            DependencyProperty.Register("EmptyBrush", typeof(Brush), typeof(MultiSegmentProgressUserControl),
                new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCE8F69"))));

        public bool IsCyclic
        {
            get { return (bool)GetValue(IsCyclicProperty); }
            set { SetValue(IsCyclicProperty, value); }
        }

        public Brush EmptyBrush
        {
            get { return (Brush)GetValue(EmptyBrushProperty); }
            set { SetValue(EmptyBrushProperty, value); }
        }

        public MultiSegmentProgressUserControl()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                UpdateProgress();
                if (IsCyclic)
                {
                    StartCyclicAnimation();
                }
            };
            SizeChanged += (s, e) => UpdateProgress();
        }

        private static void OnIsCyclicChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (MultiSegmentProgressUserControl)d;
            if ((bool)e.NewValue && control.IsLoaded)
            {
                control.StartCyclicAnimation();
            }
            else
            {
                control.StopCyclicAnimation();
            }
        }

        public Brush AddSegment(int value)
        {
            return AddSegment(value, null);
        }

        public Brush AddSegment(int value, Brush brush)
        {
            if (value <= 0)
                throw new ArgumentException("Segment value must be greater than zero", nameof(value));

            if (brush == null)
            {
                brush = GenerateRandomBrush();
            }

            var border = new Border
            {
                Background = brush,
                CornerRadius = new CornerRadius(5),
                Height = 10,
                Margin = new Thickness(1, 0, 1, 0)
            };

            var segment = new Segment
            {
                Value = value,
                Brush = brush,
                Border = border
            };

            segments.Add(segment);
            SegmentsContainer.Children.Add(border);

            UpdateProgress();
            return brush;
        }

        public void ClearSegments()
        {
            StopAllAnimations();
            segments.Clear();
            SegmentsContainer.Children.Clear();
        }

        public int GetMaxValue()
        {
            return segments.Sum(s => s.Value);
        }

        private Brush GenerateRandomBrush()
        {
            var random = new Random();
            byte r = (byte)random.Next(50, 200);
            byte g = (byte)random.Next(50, 200);
            byte b = (byte)random.Next(50, 200);
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private void UpdateProgress()
        {
            if (!IsLoaded || ActualWidth <= 0)
                return;

            int maxValue = GetMaxValue();
            if (maxValue <= 0)
                return;

            double totalWidth = ActualWidth;
            double marginTotal = (segments.Count - 1) * 4;
            double availableWidth = totalWidth - marginTotal;

            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                double percentage = (double)segment.Value / maxValue;
                double segmentWidth = availableWidth * percentage;

                segment.Border.Width = segmentWidth;
            }
        }

        public void AnimStart()
        {
            StopAllAnimations();

            int maxValue = GetMaxValue();
            if (maxValue <= 0)
                return;

            double totalWidth = ActualWidth;
            double marginTotal = (segments.Count - 1) * 4;
            double availableWidth = totalWidth - marginTotal;

            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                double percentage = (double)segment.Value / maxValue;
                double targetWidth = availableWidth * percentage;

                segment.Border.Width = 0;

                var storyboard = new Storyboard();

                DoubleAnimation animation = new DoubleAnimation
                {
                    From = 0,
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(800),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                Storyboard.SetTarget(animation, segment.Border);
                Storyboard.SetTargetProperty(animation, new PropertyPath(Border.WidthProperty));

                storyboard.Children.Add(animation);
                animationStoryboards[segment] = storyboard;
                storyboard.Begin();
            }
        }

        private void StartCyclicAnimation()
        {
            StopCyclicAnimation();

            int maxValue = GetMaxValue();
            if (maxValue <= 0)
                return;

            double totalWidth = ActualWidth;
            double marginTotal = (segments.Count - 1) * 4;
            double availableWidth = totalWidth - marginTotal;

            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                double percentage = (double)segment.Value / maxValue;
                double targetWidth = availableWidth * percentage;

                var storyboard = new Storyboard
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    AutoReverse = true
                };

                DoubleAnimation animation = new DoubleAnimation
                {
                    From = 0,
                    To = targetWidth,
                    Duration = TimeSpan.FromSeconds(2),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };

                Storyboard.SetTarget(animation, segment.Border);
                Storyboard.SetTargetProperty(animation, new PropertyPath(Border.WidthProperty));

                storyboard.Children.Add(animation);
                animationStoryboards[segment] = storyboard;
                storyboard.Begin();
            }
        }

        private void StopCyclicAnimation()
        {
            StopAllAnimations();
        }

        private void StopAllAnimations()
        {
            foreach (var kvp in animationStoryboards)
            {
                kvp.Value.Stop();
            }
            animationStoryboards.Clear();
        }
    }
}
