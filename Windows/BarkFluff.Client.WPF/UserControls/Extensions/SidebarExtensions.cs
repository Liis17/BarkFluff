using System.Windows;

using Wpf.Ui.Controls;

namespace BarkFluff.Client.WPF.UserControls.Extensions
{
    public class SidebarExtensions
    {
        public static readonly DependencyProperty IconSymbolProperty =
            DependencyProperty.RegisterAttached(
                "IconSymbol",
                typeof(SymbolRegular),
                typeof(SidebarExtensions),
                new PropertyMetadata(SymbolRegular.Bookmark20));

        public static SymbolRegular GetIconSymbol(DependencyObject obj)
            => (SymbolRegular)obj.GetValue(IconSymbolProperty);

        public static void SetIconSymbol(DependencyObject obj, SymbolRegular value)
            => obj.SetValue(IconSymbolProperty, value);
    }
}
