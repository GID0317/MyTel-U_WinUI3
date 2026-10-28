using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Printing;
using MyTelU_Launcher.Helpers;
using Windows.Foundation;
using Windows.Graphics.Printing;

namespace MyTelU_Launcher.Services;

public sealed class AcademicPrintService
{
    private readonly List<UIElement> _pages = new();
    private PrintManager? _manager;
    private PrintDocument? _document;
    private IPrintDocumentSource? _documentSource;
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string[] _headers = [];
    private string[][] _rows = [];
    private double[] _widths = [];
    private XamlRoot? _xamlRoot;

    public async Task PrintAsync(XamlRoot xamlRoot, string title, string subtitle,
        string[] headers, double[] widths, IEnumerable<string[]> rows)
    {
        if (_document != null)
            return;

        _rows = rows.Select(row => row.ToArray()).ToArray();
        if (_rows.Length == 0)
        {
            await ShowMessageAsync(xamlRoot, "Nothing to print", "Load your academic data first.");
            return;
        }

        _title = title;
        _subtitle = subtitle;
        _headers = headers;
        _widths = widths;
        _xamlRoot = xamlRoot;
        try
        {
            if (!PrintManager.IsSupported())
                throw new InvalidOperationException("Printing is not available on this device.");
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            _manager = PrintManagerInterop.GetForWindow(handle);
            _document = new PrintDocument();
            _documentSource = _document.DocumentSource;
            _document.Paginate += Paginate;
            _document.GetPreviewPage += GetPreviewPage;
            _document.AddPages += AddPages;
            _manager.PrintTaskRequested += PrintTaskRequested;
            if (!await PrintUIInterop.ShowAsync(handle))
                Unregister();
        }
        catch (Exception ex)
        {
            Unregister();
            await ShowMessageAsync(xamlRoot, "Could not print", ex.Message);
        }
    }

    private void PrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
    {
        var source = _documentSource!;
        var task = args.Request.CreatePrintTask(_title, request => request.SetSource(source));
        task.Completed += PrintCompleted;
    }

    private void PrintCompleted(PrintTask sender, PrintTaskCompletedEventArgs args)
    {
        sender.Completed -= PrintCompleted;
        App.MainWindow.DispatcherQueue.TryEnqueue(async () =>
        {
            var root = _xamlRoot;
            Unregister();
            if (args.Completion == PrintTaskCompletion.Failed && root != null)
                await ShowMessageAsync(root, "Could not print", "Windows could not finish the print job. Try again or choose another printer.");
        });
    }

    private void Paginate(object sender, PaginateEventArgs args)
    {
        _pages.Clear();
        var description = ((PrintTaskOptions)args.PrintTaskOptions).GetPageDescription(0);
        var area = description.ImageableRect;
        const double padding = 24;
        double width = Math.Max(100, area.Width - padding * 2);
        double height = Math.Max(100, area.Height - padding * 2);
        int index = 0;
        while (index < _rows.Length)
        {
            var content = new StackPanel { Width = width, Spacing = 8 };
            content.Children.Add(new TextBlock { Text = _title, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = _subtitle, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(CreateRow(_headers, true, width));
            content.Measure(new Size(width, double.PositiveInfinity));
            double used = content.DesiredSize.Height + 32;
            int first = index;
            while (index < _rows.Length)
            {
                var row = CreateRow(_rows[index], false, width);
                row.Measure(new Size(width, double.PositiveInfinity));
                double rowHeight = row.DesiredSize.Height + content.Spacing;
                if (index > first && used + rowHeight > height)
                    break;
                content.Children.Add(row);
                used += rowHeight;
                index++;
            }

            var page = new Grid
            {
                Width = description.PageSize.Width, Height = description.PageSize.Height,
                RequestedTheme = ElementTheme.Light, Background = new SolidColorBrush(Colors.White)
            };
            var body = new Grid
            {
                Margin = new Thickness(area.X + padding, area.Y + padding,
                    description.PageSize.Width - area.X - area.Width + padding,
                    description.PageSize.Height - area.Y - area.Height + padding)
            };
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.VerticalAlignment = VerticalAlignment.Top;
            body.Children.Add(content);
            var footer = new TextBlock { Text = $"MyTelU Launcher | {_pages.Count + 1}", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(footer, 1);
            body.Children.Add(footer);
            page.Children.Add(body);
            page.Measure(description.PageSize);
            page.Arrange(new Rect(new Point(), description.PageSize));
            _pages.Add(page);
        }
        _document!.SetPreviewPageCount(_pages.Count, PreviewPageCountType.Final);
    }

    private Grid CreateRow(string[] values, bool header, double width)
    {
        const double spacing = 10;
        var grid = new Grid { Width = width, ColumnSpacing = spacing, Padding = new Thickness(0, 6, 0, 6) };
        double available = width - spacing * (_headers.Length - 1);
        double totalWeight = _widths.Sum();
        for (int column = 0; column < _headers.Length; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(available * _widths[column] / totalWeight) });
            var cell = new TextBlock
            {
                Text = column < values.Length ? values[column] : string.Empty,
                FontSize = 11, Foreground = new SolidColorBrush(Colors.Black),
                FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private void GetPreviewPage(object sender, GetPreviewPageEventArgs args)
        => _document!.SetPreviewPage(args.PageNumber, _pages[args.PageNumber - 1]);

    private void AddPages(object sender, AddPagesEventArgs args)
    {
        foreach (var page in _pages)
            _document!.AddPage(page);
        _document!.AddPagesComplete();
    }

    private void Unregister()
    {
        if (_manager != null)
            _manager.PrintTaskRequested -= PrintTaskRequested;
        if (_document != null)
        {
            _document.Paginate -= Paginate;
            _document.GetPreviewPage -= GetPreviewPage;
            _document.AddPages -= AddPages;
        }
        _document = null;
        _documentSource = null;
        _manager = null;
        _xamlRoot = null;
        _pages.Clear();
        _rows = [];
    }

    private static async Task ShowMessageAsync(XamlRoot root, string title, string message)
    {
        var dialog = new ContentDialog { XamlRoot = root, Title = title, Content = message, CloseButtonText = "Close" };
        App.GetService<AccentColorService>().ApplyToContentDialog(dialog);
        await dialog.ShowAsync();
    }
}
