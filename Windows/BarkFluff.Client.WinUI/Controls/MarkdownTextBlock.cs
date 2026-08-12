using BarkFluff.Client.Core.Markdown;
using BarkFluff.Client.WinUI.Infrastructure.Converters;

using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

using Windows.UI;
using Windows.UI.Text;

namespace BarkFluff.Client.WinUI.Controls;

/// <summary>
/// Показывает текст сообщения с разметкой. Разбор живёт в <see cref="MarkdownParser"/>,
/// здесь только сборка визуального дерева: заголовки, списки и разделители кладутся в
/// <see cref="RichTextBlock"/>, а цитаты, блоки кода, таблицы и изображения требуют
/// собственных контейнеров — как отдельные View в Android-рендере.
/// </summary>
public sealed class MarkdownTextBlock : UserControl
{
    private const double MaxImageWidth = 480;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(MarkdownTextBlock),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty TextForegroundProperty = DependencyProperty.Register(
        nameof(TextForeground),
        typeof(Brush),
        typeof(MarkdownTextBlock),
        new PropertyMetadata(null, OnTextForegroundChanged));

    private readonly SolidColorBrush _codeBackgroundBrush = new(Colors.Transparent);
    private readonly SolidColorBrush _dimBrush = new(Colors.Transparent);
    private readonly SolidColorBrush _borderBrush = new(Colors.Transparent);
    private readonly SolidColorBrush _tableHeaderBrush = new(Colors.Transparent);

    private SolidColorBrush? _watchedForeground;
    private long _foregroundToken;

    public MarkdownTextBlock()
    {
        // Подписка живёт от Loaded до Unloaded: пузыри в ленте переиспользуются,
        // и висящий колбэк на кисти-синглтоне пережил бы сам элемент.
        Loaded += (_, _) => WatchForeground(TextForeground);
        Unloaded += (_, _) => StopWatchingForeground();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush? TextForeground
    {
        get => (Brush?)GetValue(TextForegroundProperty);
        set => SetValue(TextForegroundProperty, value);
    }

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((MarkdownTextBlock)sender).Rebuild();

    private static void OnTextForegroundChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (MarkdownTextBlock)sender;
        control.WatchForeground(args.NewValue as Brush);
        control.UpdateDerivedBrushes();
        control.Rebuild();
    }

    /// <summary>
    /// Кисти пузыря — синглтоны, которым <c>ApplicationThemeService</c> переписывает
    /// <see cref="SolidColorBrush.Color"/> при смене темы. Производные цвета иначе
    /// остались бы снимком старой темы, поэтому пересчитываются по подписке.
    /// </summary>
    private void WatchForeground(Brush? brush)
    {
        StopWatchingForeground();
        if (brush is not SolidColorBrush solid)
        {
            return;
        }

        _watchedForeground = solid;
        _foregroundToken = solid.RegisterPropertyChangedCallback(SolidColorBrush.ColorProperty, (_, _) => UpdateDerivedBrushes());
    }

    private void StopWatchingForeground()
    {
        _watchedForeground?.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, _foregroundToken);
        _watchedForeground = null;
    }

    private void UpdateDerivedBrushes()
    {
        var baseColor = (TextForeground as SolidColorBrush)?.Color ?? Colors.Black;
        _codeBackgroundBrush.Color = WithAlpha(baseColor, 0x22);
        _dimBrush.Color = WithAlpha(baseColor, 0x99);
        _borderBrush.Color = WithAlpha(baseColor, 0x33);
        _tableHeaderBrush.Color = WithAlpha(baseColor, 0x1C);
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private void Rebuild()
    {
        // Лента виртуализирована: переиспользованный пузырь без очистки показал бы
        // содержимое предыдущего сообщения.
        if (string.IsNullOrEmpty(Text))
        {
            Content = null;
            return;
        }

        var document = MarkdownParser.Parse(Text);
        if (document.TryGetPlainText(out var plain))
        {
            Content = CreatePlainText(plain);
            return;
        }

        var panel = new StackPanel();
        foreach (var block in document.Blocks)
        {
            panel.Children.Add(CreateBlock(block));
        }

        Content = panel;
    }

    /// <summary>
    /// Собственное меню выделения снимается: правый клик должен доходить до пузыря с его
    /// действиями (ответить, переслать, закрепить), а копирование текста есть и там.
    /// </summary>
    private TextBlock CreatePlainText(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
        SelectionFlyout = null,
        Foreground = TextForeground
    };

    private UIElement CreateBlock(MarkdownBlock block) => block switch
    {
        MarkdownTextGroup group => CreateTextGroup(group),
        MarkdownCodeBlock code => CreateCodeBlock(code),
        MarkdownQuote quote => CreateQuote(quote),
        MarkdownTable table => CreateTable(table),
        MarkdownImage image => CreateImage(image),
        _ => new TextBlock()
    };

    private UIElement CreateTextGroup(MarkdownTextGroup group)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var line in group.Lines)
        {
            panel.Children.Add(line.Kind is MarkdownLineKind.Rule
                ? CreateRule()
                : CreateLine(line, group.Alignment, TextForeground));
        }

        return panel;
    }

    /// <summary>
    /// Строка — отдельный <see cref="RichTextBlock"/>: диапазоны подсветки inline-кода
    /// считаются по одному абзацу, и складывать смещения между абзацами не требуется.
    /// </summary>
    private RichTextBlock CreateLine(MarkdownLine line, MarkdownAlignment alignment, Brush? foreground)
    {
        var rich = new RichTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            SelectionFlyout = null,
            Foreground = foreground,
            TextAlignment = alignment switch
            {
                MarkdownAlignment.Center => TextAlignment.Center,
                MarkdownAlignment.End => TextAlignment.Right,
                _ => TextAlignment.Left
            }
        };

        var paragraph = new Paragraph();
        var offset = 0;
        var highlights = new List<TextRange>();

        if (line.Kind is MarkdownLineKind.Heading)
        {
            rich.FontSize = FontSize * HeadingScale(line.HeadingLevel);
            rich.FontWeight = FontWeights.Bold;
        }

        var marker = line.Kind switch
        {
            MarkdownLineKind.Bullet => "• ",
            MarkdownLineKind.Ordered => $"{line.OrderedMarker}. ",
            _ => string.Empty
        };

        if (marker.Length > 0)
        {
            paragraph.Inlines.Add(new Run { Text = marker });
            offset += marker.Length;
            paragraph.TextIndent = -14;
            rich.Margin = new Thickness(14, 0, 0, 0);
        }

        foreach (var inline in line.Inlines)
        {
            paragraph.Inlines.Add(CreateInline(inline, foreground));
            if (inline.Style.HasFlag(MarkdownInlineStyle.Code))
            {
                highlights.Add(new TextRange { StartIndex = offset, Length = inline.Text.Length });
            }

            offset += inline.Text.Length;
        }

        rich.Blocks.Add(paragraph);
        if (highlights.Count > 0)
        {
            var highlighter = new TextHighlighter { Background = _codeBackgroundBrush };
            foreach (var range in highlights)
            {
                highlighter.Ranges.Add(range);
            }

            rich.TextHighlighters.Add(highlighter);
        }

        return rich;
    }

    private Inline CreateInline(MarkdownInline inline, Brush? foreground)
    {
        var run = new Run { Text = inline.Text };
        if (inline.Style.HasFlag(MarkdownInlineStyle.Bold))
        {
            run.FontWeight = FontWeights.Bold;
        }

        if (inline.Style.HasFlag(MarkdownInlineStyle.Italic))
        {
            run.FontStyle = FontStyle.Italic;
        }

        if (inline.Style.HasFlag(MarkdownInlineStyle.Strikethrough))
        {
            run.TextDecorations = TextDecorations.Strikethrough;
        }

        if (inline.Style.HasFlag(MarkdownInlineStyle.Code))
        {
            run.FontFamily = new FontFamily("Consolas");
        }

        if (inline.Style.HasFlag(MarkdownInlineStyle.Small))
        {
            run.FontSize = FontSize * 0.8;
        }

        if (inline.Link is null || !Uri.TryCreate(inline.Link, UriKind.Absolute, out var uri))
        {
            return run;
        }

        // Акцентный цвет по умолчанию нечитаем на цветном пузыре, поэтому ссылка
        // остаётся в цвете текста и отличается подчёркиванием.
        var hyperlink = new Hyperlink { NavigateUri = uri, Foreground = foreground };
        hyperlink.Inlines.Add(run);
        return hyperlink;
    }

    private UIElement CreateRule() => new Border
    {
        Height = 1,
        Margin = new Thickness(0, 6, 0, 6),
        Background = _dimBrush,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private UIElement CreateCodeBlock(MarkdownCodeBlock block) => new Border
    {
        Margin = new Thickness(0, 4, 0, 4),
        Padding = new Thickness(10, 6, 10, 6),
        CornerRadius = new CornerRadius(6),
        Background = _codeBackgroundBrush,
        Child = new TextBlock
        {
            Text = block.Code,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            SelectionFlyout = null,
            Foreground = TextForeground
        }
    };

    private UIElement CreateQuote(MarkdownQuote quote)
    {
        var panel = new StackPanel();
        foreach (var line in quote.Lines)
        {
            panel.Children.Add(CreateLine(line, MarkdownAlignment.Start, _dimBrush));
        }

        return new Border
        {
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(8, 0, 0, 0),
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = _dimBrush,
            Child = panel
        };
    }

    private UIElement CreateTable(MarkdownTable table)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        for (var column = 0; column < table.Headers.Count; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddTableRow(grid, table.Headers, table.Alignments, row: 0, header: true);

        for (var row = 0; row < table.Rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddTableRow(grid, table.Rows[row].Cells, table.Alignments, row + 1, header: false);
        }

        // Пузырь ограничен по ширине, поэтому широкая таблица прокручивается, а не распирает вёрстку.
        return new ScrollViewer
        {
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = grid
        };
    }

    private void AddTableRow(
        Grid grid,
        IReadOnlyList<MarkdownTableCell> cells,
        IReadOnlyList<MarkdownAlignment> alignments,
        int row,
        bool header)
    {
        for (var column = 0; column < cells.Count; column++)
        {
            var line = new MarkdownLine(MarkdownLineKind.Paragraph, cells[column].Inlines);
            var content = CreateLine(line, alignments[column], TextForeground);
            if (header)
            {
                content.FontWeight = FontWeights.Bold;
            }

            var cell = new Border
            {
                Padding = new Thickness(10, 6, 10, 6),
                BorderThickness = new Thickness(1),
                BorderBrush = _borderBrush,
                Background = header ? _tableHeaderBrush : null,
                Child = content
            };

            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }
    }

    private UIElement CreateImage(MarkdownImage image)
    {
        if (image.Url is null || StringToImageSourceConverter.TryCreate(image.Url) is not { } source)
        {
            return CreatePlainText(string.IsNullOrWhiteSpace(image.Alt) ? "🖼" : image.Alt);
        }

        var view = new Image
        {
            Source = source,
            Stretch = Stretch.Uniform,
            MaxWidth = MaxImageWidth,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        if (image.Width is { } width)
        {
            view.Width = Math.Min(width, MaxImageWidth);
        }

        if (image.Height is { } height)
        {
            view.Height = height;
        }

        AutomationProperties.SetName(view, image.Alt);

        FrameworkElement element = image.LinkUrl is not null && Uri.TryCreate(image.LinkUrl, UriKind.Absolute, out var uri)
            ? new HyperlinkButton { NavigateUri = uri, Padding = new Thickness(0), Content = view }
            : view;

        element.Margin = new Thickness(0, 4, 0, 4);
        element.HorizontalAlignment = image.Alignment switch
        {
            MarkdownAlignment.Center => HorizontalAlignment.Center,
            MarkdownAlignment.End => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left
        };

        return element;
    }

    private static double HeadingScale(int level) => level switch
    {
        1 => 1.5,
        2 => 1.3,
        3 => 1.15,
        _ => 1.05
    };
}
