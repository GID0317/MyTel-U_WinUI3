using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Services;
using Windows.Graphics.Imaging;

namespace MyTelU_Launcher.Views;

public sealed partial class TimelineComposeDialog : ContentDialog
{
    private TimelinePostImage? _image;
    private bool _busy;

    public TimelineComposeDialog()
    {
        InitializeComponent();
        UpdatePostState();
    }

    private void PostContent_TextChanged(object sender, TextChangedEventArgs e)
        => UpdatePostState();

    private void UpdatePostState()
    {
        if (PostCount == null) return;
        string text = PostContent.Text;
        int count = TimelineApi.CountPostCharacters(text);
        bool overLimit = count > TimelineApi.MaxPostCharacters;
        PostCount.Text = $"{count} / {TimelineApi.MaxPostCharacters}";
        IsPrimaryButtonEnabled = !_busy && !overLimit && !string.IsNullOrWhiteSpace(text);
    }

    private async void AddImage_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        Status.IsOpen = false;
        try
        {
            var picker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
            foreach (string extension in new[] { ".jpg", ".jpeg", ".png", ".webp" })
                picker.FileTypeFilter.Add(extension);
            var selection = await picker.PickSingleFileAsync();
            if (selection == null) return;

            using var file = File.OpenRead(selection.Path);
            if (file.Length == 0 || file.Length > TimelineApi.MaxPostImageBytes)
                throw new InvalidDataException("Choose an image no larger than 5 MB.");
            byte[] bytes = new byte[(int)file.Length];
            await file.ReadExactlyAsync(bytes);
            using var buffer = new MemoryStream(bytes, writable: false);
            using var stream = buffer.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var preview = new BitmapImage();
            if (decoder.PixelWidth >= decoder.PixelHeight) preview.DecodePixelWidth = (int)Math.Min(640u, decoder.PixelWidth);
            else preview.DecodePixelHeight = (int)Math.Min(640u, decoder.PixelHeight);
            stream.Seek(0);
            await preview.SetSourceAsync(stream);
            _image = new(Path.GetFileName(selection.Path), bytes);
            ImagePreview.Source = preview;
            ImagePreview.Visibility = RemoveImage.Visibility = Visibility.Visible;
            AddImageLabel.Text = "Replace image";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or COMException or ArgumentException)
        {
            ShowError("Couldn't attach image", ex is InvalidDataException ? ex.Message : "Choose another image and try again.");
        }
        finally { SetBusy(false); }
    }

    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        _image = null;
        ImagePreview.Source = null;
        ImagePreview.Visibility = RemoveImage.Visibility = Visibility.Collapsed;
        AddImageLabel.Text = "Add image";
        Status.IsOpen = false;
    }

    private async void Post_Click(ContentDialog sender, ContentDialogButtonClickEventArgs e)
    {
        if (!IsPrimaryButtonEnabled) { e.Cancel = true; return; }
        var deferral = e.GetDeferral();
        SetBusy(true);
        Status.IsOpen = false;
        try
        {
            await App.GetService<MobileAcademicService>().CreateTimelinePostAsync(PostContent.Text, _image, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or InvalidOperationException
            or ArgumentException or OperationCanceledException or System.Text.Json.JsonException)
        {
            e.Cancel = true;
            ShowError("Couldn't confirm your post", "Check Timeline before trying again. Your draft is still here.");
        }
        finally
        {
            SetBusy(false);
            deferral.Complete();
        }
    }

    private void Dialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs e) => e.Cancel = _busy;

    private void SetBusy(bool busy)
    {
        _busy = busy;
        PostContent.IsEnabled = AddImage.IsEnabled = RemoveImage.IsEnabled = !busy;
        UpdatePostState();
        Working.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string title, string message)
    {
        Status.Title = title;
        Status.Message = message;
        Status.IsOpen = true;
    }
}
