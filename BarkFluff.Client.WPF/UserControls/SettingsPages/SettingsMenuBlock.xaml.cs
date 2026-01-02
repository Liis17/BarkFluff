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

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Логика взаимодействия для SettingsMenuBlock.xaml
    /// </summary>
    public partial class SettingsMenuBlock : UserControl
    {
        public static readonly DependencyProperty IconSymbolProperty =
            DependencyProperty.Register(nameof(IconSymbol), typeof(Wpf.Ui.Controls.SymbolRegular), typeof(SettingsMenuBlock), new PropertyMetadata(Wpf.Ui.Controls.SymbolRegular.ArrowReply24));

        public static readonly DependencyProperty IconColorProperty =
            DependencyProperty.Register(nameof(IconColor), typeof(Brush), typeof(SettingsMenuBlock), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x0E, 0x8F, 0x92))));

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(SettingsMenuBlock), new PropertyMetadata("name"));

        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(SettingsMenuBlock), new PropertyMetadata(null));

        public static readonly DependencyProperty CommandParameterProperty =
            DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(SettingsMenuBlock), new PropertyMetadata(null));

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

        public SettingsMenuBlock()
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
