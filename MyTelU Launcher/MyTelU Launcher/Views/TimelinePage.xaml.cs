using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using MyTelU_Launcher.Helpers;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Services;
using MyTelU_Launcher.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;

namespace MyTelU_Launcher.Views;

public sealed partial class TimelinePage : Page
{
    private const double PostPreviewHeight = 220;
    private const int MaxPreviewPixels = 800;

    public TimelineViewModel ViewModel { get; } = App.GetService<TimelineViewModel>();

    private DataTransferManager? _shareManager;
    private TimelinePost? _sharePost;
    private bool _postDialogOpen;
    private readonly SemaphoreSlim _imageGate = new(2);
    private readonly Dictionary<Image, CancellationTokenSource> _imageLoads = [];

    public TimelinePage() => InitializeComponent();

    private async void NewPost_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.NeedsLogin)
        {
            ShowActionError("Sign in to post", "Sign in to iGracias before creating a Timeline post.");
            return;
        }
        var dialog = new TimelineComposeDialog { XamlRoot = XamlRoot };
        App.GetService<AccentColorService>().ApplyToContentDialog(dialog);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            TimelineSearch.Text = "";
            await ViewModel.SearchAsync("");
        }
    }

    private async void Search_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs e)
        => await ViewModel.SearchAsync(e.QueryText);

    private async void Pager_SelectedIndexChanged(PagerControl sender, PagerControlSelectedIndexChangedEventArgs e)
    {
        if (ViewModel.IsLoading || ViewModel.CurrentPage == 0) return;
        await ViewModel.GoToPageAsync(sender.SelectedPageIndex + 1);
        sender.SelectedPageIndex = ViewModel.PagerIndex;
    }

    private async void Post_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TimelinePost post }) await ShowPostAsync(post);
    }

    private async void Timeline_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TimelinePost post) await ShowPostAsync(post);
    }

    private async Task ShowPostAsync(TimelinePost post)
    {
        if (_postDialogOpen) return;
        _postDialogOpen = true;
        try
        {
            var dialog = new TimelinePostDialog(post, ViewModel.ToggleLikeAsync) { XamlRoot = XamlRoot };
            App.GetService<AccentColorService>().ApplyToContentDialog(dialog);
            await dialog.ShowAsync();
        }
        finally { _postDialogOpen = false; }
    }

    private async void PostImage_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image image) await LoadPostImageAsync(image);
    }

    private async void PostImage_Retry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Parent: StackPanel { Parent: Grid panel } })
            await LoadPostImageAsync(panel.Children.OfType<Image>().Single());
    }

    private async void PostImage_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs e)
    {
        if (sender is not Image image) return;
        ReleasePostImage(image);
        if (image.IsLoaded && e.NewValue is TimelinePost post)
            await LoadPostImageAsync(image, post);
    }

    private async Task LoadPostImageAsync(Image image, TimelinePost? post = null)
    {
        post ??= image.Tag as TimelinePost;
        if (post is not { HasImage: true }
            || image.Source != null || _imageLoads.ContainsKey(image)) return;
        var panel = (Grid)image.Parent;
        var progress = panel.Children.OfType<ProgressRing>().Single();
        var error = panel.Children.OfType<StackPanel>().Single();
        var cancellation = new CancellationTokenSource();
        _imageLoads.Add(image, cancellation);
        var ct = cancellation.Token;
        bool entered = false;
        progress.IsActive = true;
        progress.Visibility = Visibility.Visible;
        error.Visibility = Visibility.Collapsed;
        panel.Height = PostPreviewHeight;
        try
        {
            await _imageGate.WaitAsync(ct);
            entered = true;
            var bytes = await App.GetService<MobileAcademicService>().GetTimelineImageAsync(post.ImageUrl, ct);
            using var input = new MemoryStream(bytes, writable: false);
            using var stream = input.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);
            double scale = image.XamlRoot?.RasterizationScale ?? 1;
            double maxWidth = panel.ActualWidth > 0 ? panel.ActualWidth * scale : MaxPreviewPixels;
            double ratio = Math.Min(1, Math.Min(
                Math.Min(maxWidth, MaxPreviewPixels) / decoder.OrientedPixelWidth,
                Math.Min(PostPreviewHeight * scale, MaxPreviewPixels) / decoder.OrientedPixelHeight));
            var preview = new BitmapImage
            {
                DecodePixelWidth = Math.Max(1, (int)Math.Ceiling(decoder.OrientedPixelWidth * ratio))
            };
            stream.Seek(0);
            await preview.SetSourceAsync(stream);
            ct.ThrowIfCancellationRequested();
            image.Source = preview;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or COMException
            or ArgumentException or OperationCanceledException or InvalidOperationException)
        {
            if (!ct.IsCancellationRequested)
            {
                error.Children.OfType<TextBlock>().Single().Text = ex is FileNotFoundException
                    ? "Image unavailable." : "Couldn't load image.";
                panel.Height = double.NaN;
                error.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (entered) _imageGate.Release();
            if (_imageLoads.TryGetValue(image, out var current) && ReferenceEquals(current, cancellation))
            {
                _imageLoads.Remove(image);
                progress.IsActive = false;
                progress.Visibility = Visibility.Collapsed;
            }
            cancellation.Dispose();
        }
    }

    private void PostImage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image image) ReleasePostImage(image);
    }

    private void ReleasePostImage(Image image)
    {
        if (_imageLoads.Remove(image, out var cancellation)) cancellation.Cancel();
        image.Source = null;
    }

    private async void Like_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: TimelinePost post } button) return;
        button.IsChecked = post.IsLiked;
        button.IsEnabled = false;
        ActionStatus.IsOpen = false;
        try { await ViewModel.ToggleLikeAsync(post); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException
            or InvalidOperationException or OperationCanceledException)
        {
            ShowActionError("Couldn't update like", "Refresh Timeline to check your reaction before trying again.");
        }
        finally
        {
            button.IsChecked = post.IsLiked;
            button.IsEnabled = true;
        }
    }

    private void Share_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TimelinePost post }) return;
        ActionStatus.IsOpen = false;
        try
        {
            var window = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            if (_shareManager == null)
            {
                _shareManager = ShareUIInterop.GetForWindow(window);
                _shareManager.DataRequested += Share_DataRequested;
            }
            _sharePost = post;
            ShareUIInterop.ShowForWindow(window);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException)
        {
            _sharePost = null;
            ShowActionError("Couldn't open Share", "Try sharing the post again.");
        }
    }

    private async void Share_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var post = _sharePost;
        _sharePost = null;
        if (post == null) return;
        var data = args.Request.Data;
        data.Properties.Title = "Telutizen post";
        data.SetText($"{post.Metadata}\n\n{post.Content}");
        data.RequestedOperation = DataPackageOperation.Copy;
        if (!post.HasImage) return;

        var deferral = args.Request.GetDeferral();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var bytes = await App.GetService<MobileAcademicService>().GetTimelineImageAsync(post.ImageUrl, timeout.Token);
            await TimelineShareData.SetImageAsync(data, bytes, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
            or COMException or ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            args.Request.FailWithDisplayText("Couldn't load the post image. Check your connection and try sharing again.");
        }
        finally { deferral.Complete(); }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        foreach (var cancellation in _imageLoads.Values.ToArray()) cancellation.Cancel();
        if (_shareManager != null) _shareManager.DataRequested -= Share_DataRequested;
        _shareManager = null;
        _sharePost = null;
    }

    private void ShowActionError(string title, string message)
    {
        ActionStatus.Title = title;
        ActionStatus.Message = message;
        ActionStatus.Severity = InfoBarSeverity.Error;
        ActionStatus.IsOpen = true;
    }
}
