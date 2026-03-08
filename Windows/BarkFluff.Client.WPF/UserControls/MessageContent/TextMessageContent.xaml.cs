using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.MessageContent
{
    public partial class TextMessageContent : UserControl
    {
        public TextMessageContent()
        {
            InitializeComponent();
        }

        public TextMessageContent(string text) : this()
        {
            MessageText.Text = text ?? string.Empty;
        }

        public string Text
        {
            get => MessageText.Text;
            set => MessageText.Text = value ?? string.Empty;
        }

        public void SelectAll()
        {
            MessageText.Focus();
            MessageText.SelectAll();
        }

        public string GetSelectedText()
        {
            return MessageText.SelectedText;
        }

        public void CopySelectedText()
        {
            if (!string.IsNullOrEmpty(MessageText.SelectedText))
            {
                System.Windows.Clipboard.SetText(MessageText.SelectedText);
            }
            else
            {
                System.Windows.Clipboard.SetText(MessageText.Text);
            }
        }
    }
}
