using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для SettingsMenuDeviceBlock.xaml
    /// </summary>
    public partial class SettingsMenuDeviceBlock : UserControl
    {
        public static readonly DependencyProperty IconSymbolProperty =
            DependencyProperty.Register(nameof(IconSymbol), typeof(Wpf.Ui.Controls.SymbolRegular), typeof(SettingsMenuDeviceBlock), new PropertyMetadata(Wpf.Ui.Controls.SymbolRegular.ArrowReply24));

        public static readonly DependencyProperty IconColorProperty =
            DependencyProperty.Register(nameof(IconColor), typeof(Brush), typeof(SettingsMenuDeviceBlock), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x0E, 0x8F, 0x92))));

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(SettingsMenuDeviceBlock), new PropertyMetadata("name"));

        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(SettingsMenuDeviceBlock), new PropertyMetadata(null));

        public static readonly DependencyProperty CommandParameterProperty =
            DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(SettingsMenuDeviceBlock), new PropertyMetadata(null));

        public Wpf.Ui.Controls.SymbolRegular IconSymbol
        {
            get => (Wpf.Ui.Controls.SymbolRegular)GetValue(IconSymbolProperty);
            set => SetValue(IconSymbolProperty, value);
        }

        public Brush IconColor
        {
            get => (Brush)GetValue(IconColorProperty);
            set => SetValue(IconColorProperty, value);
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public ICommand Command
        {
            get => (ICommand)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        public object CommandParameter
        {
            get => GetValue(CommandParameterProperty);
            set => SetValue(CommandParameterProperty, value);
        }

        public SettingsMenuDeviceBlock()
        {
            InitializeComponent();
            this.MouseEnter += OnMouseEnter;
            this.MouseLeave += OnMouseLeave;
            this.MouseLeftButtonDown += OnMouseLeftButtonDown;
        }

        private void OnMouseEnter(object sender, MouseEventArgs e)
        {
            this.Cursor = Cursors.Hand;
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            this.Cursor = Cursors.Arrow;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Command != null && Command.CanExecute(CommandParameter))
            {
                Command.Execute(CommandParameter);
            }
        }
    }
}
