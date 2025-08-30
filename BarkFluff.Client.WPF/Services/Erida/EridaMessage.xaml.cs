using System.Windows;
using System.Windows.Controls;

namespace BarkFluff.Client.WPF.Services.Erida
{

    public partial class EridaMessage : UserControl
    {
        public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(EridaMessage),
            new PropertyMetadata(string.Empty, OnTextChanged));

        public static readonly DependencyProperty MsgTypeProperty =
    DependencyProperty.Register(
        nameof(MsgType),
        typeof(MessageType),
        typeof(EridaMessage),
        new PropertyMetadata(null, OnMsgTypeChanged));

        private static void OnMsgTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EridaMessage control && control.Icon != null && e.NewValue is MessageType msgType)
            {
                var iconSymbol = Wpf.Ui.Controls.SymbolRegular.Info16; // Default
                switch (msgType.Type)
                {
                    case MessageType.MessageTypeEnum.Warning:
                        iconSymbol = Wpf.Ui.Controls.SymbolRegular.Warning16;
                        break;
                    case MessageType.MessageTypeEnum.Error:
                        iconSymbol = Wpf.Ui.Controls.SymbolRegular.ErrorCircle16;
                        break;
                    case MessageType.MessageTypeEnum.Success:
                        iconSymbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle16;
                        break;
                    case MessageType.MessageTypeEnum.Debug:
                        iconSymbol = Wpf.Ui.Controls.SymbolRegular.Code16;
                        break;
                }
                control.Icon.Symbol = iconSymbol;
            }
        }
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public MessageType MsgType
        {
            get => (MessageType)GetValue(MsgTypeProperty);
            set => SetValue(MsgTypeProperty, value);
        }
        public EridaMessage()
        {
            InitializeComponent();
            Content.Text = Text;
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EridaMessage control && control.Content != null)
            {
                ((TextBlock)control.Content).Text = e.NewValue as string;
            }
        }
    }
}
