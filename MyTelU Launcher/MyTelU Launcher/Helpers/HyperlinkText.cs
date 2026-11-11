using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace MyTelU_Launcher.Helpers;

public static class HyperlinkText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(HyperlinkText), new PropertyMetadata("", OnTextChanged));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);
    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not TextBlock block) return;
        string text = args.NewValue as string ?? "";
        var parts = WebLinkParser.Parse(text).ToArray();
        block.Inlines.Clear();
        if (!parts.Any(part => part.Link != null))
        {
            block.Text = text;
            return;
        }
        block.Text = "";
        foreach (var part in parts)
        {
            var run = new Run { Text = part.Text };
            if (part.Link == null) block.Inlines.Add(run);
            else
            {
                var link = new Hyperlink { NavigateUri = part.Link };
                link.Inlines.Add(run);
                block.Inlines.Add(link);
            }
        }
    }
}
