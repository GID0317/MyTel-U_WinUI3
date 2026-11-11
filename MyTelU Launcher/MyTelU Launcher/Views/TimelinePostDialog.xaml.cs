using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using MyTelU_Launcher.Models;
using MyTelU_Launcher.Services;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace MyTelU_Launcher.Views;

public sealed partial class TimelinePostDialog : ContentDialog
{
    private readonly MobileAcademicService _mobile = App.GetService<MobileAcademicService>();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _sendingComment;
    private readonly Func<TimelinePost, Task> _toggleLike;
    private bool _liking;
    private bool _openingImage;
    private byte[]? _imageBytes;
    private string? _imageExtension;

    public TimelinePost Post { get; }

    public TimelinePostDialog(TimelinePost post, Func<TimelinePost, Task> toggleLike)
    {
        Post = post;
        _toggleLike = toggleLike;
        InitializeComponent();
        Title = new TextBlock { Text = post.Author, TextWrapping = TextWrapping.Wrap };
        CommentComposer.Visibility = post.CanComment ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Like_Click(object sender, RoutedEventArgs e)
    {
        if (_liking) return;
        _liking = true;
        LikeButton.IsChecked = Post.IsLiked;
        LikeButton.IsEnabled = false;
        ActionError.IsOpen = false;
        try { await _toggleLike(Post); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException
            or InvalidOperationException or OperationCanceledException)
        {
            ShowActionError("Couldn't update like", "Reopen Timeline to check your reaction before trying again.");
        }
        finally
        {
            _liking = false;
            LikeButton.IsChecked = Post.IsLiked;
            LikeButton.IsEnabled = true;
        }
    }

    private void Comments_Click(object sender, RoutedEventArgs e)
        => CommentsHeading.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });

    private async void PostImage_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_openingImage || _imageBytes is not { } bytes || _imageExtension is not { } extension) return;
        e.Handled = true;
        _openingImage = true;
        ActionError.IsOpen = false;
        try
        {
            var file = await StorageFile.CreateStreamedFileAsync($"Timeline-{Post.Id}{extension}", async request =>
            {
                try
                {
                    using var writer = new DataWriter(request);
                    try
                    {
                        writer.WriteBytes(bytes);
                        await writer.StoreAsync();
                    }
                    finally { writer.DetachStream(); }
                    request.Dispose();
                }
                catch (Exception ex) when (ex is IOException or COMException or InvalidOperationException)
                {
                    request.FailAndClose(StreamedFileFailureMode.Failed);
                }
            }, null);
            if (!await Windows.System.Launcher.LaunchFileAsync(file))
                ShowActionError("Couldn't open image", "No default image viewer could open this file.");
        }
        catch (Exception ex) when (ex is IOException or COMException or ArgumentException or InvalidOperationException)
        {
            ShowActionError("Couldn't open image", "Try opening the attachment again.");
        }
        finally { _openingImage = false; }
    }

    private void ShowActionError(string title, string message)
    {
        ActionError.Title = title;
        ActionError.Message = message;
        ActionError.IsOpen = true;
    }

    private async void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        var ct = _lifetime.Token;
        var comments = LoadCommentsAsync(ct);
        if (Post.HasImage)
            await Task.WhenAll(comments, LoadImageAsync(ct));
        else
            await comments;
    }

    private async Task LoadCommentsAsync(CancellationToken ct)
    {
        CommentsStatus.Visibility = Visibility.Collapsed;
        CommentsProgress.Visibility = Visibility.Visible;
        try
        {
            var comments = await _mobile.GetTimelineCommentsAsync(Post.Id, ct);
            ct.ThrowIfCancellationRequested();
            CommentItems.ItemsSource = comments;
            if (comments.Count == 0)
            {
                CommentsStatus.Text = "No comments yet.";
                CommentsStatus.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException
            or IOException or OperationCanceledException or InvalidOperationException)
        {
            if (ct.IsCancellationRequested) return;
            CommentsStatus.Text = "Couldn't load comments.";
            CommentsStatus.Visibility = Visibility.Visible;
        }
        finally { if (!ct.IsCancellationRequested) CommentsProgress.Visibility = Visibility.Collapsed; }
    }

    private void CommentInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CommentCount != null)
            CommentCount.Text = new System.Globalization.StringInfo(CommentInput.Text).LengthInTextElements.ToString();
        if (SendCommentButton != null)
            SendCommentButton.IsEnabled = !_sendingComment && !string.IsNullOrWhiteSpace(CommentInput.Text);
    }

    private async void SendComment_Click(object sender, RoutedEventArgs e)
    {
        string comment = CommentInput.Text.Trim();
        if (_sendingComment || !Post.CanComment || comment.Length == 0) return;
        _sendingComment = true;
        CommentInput.IsEnabled = SendCommentButton.IsEnabled = false;
        CommentResult.IsOpen = false;
        var ct = _lifetime.Token;
        try
        {
            await _mobile.AddTimelineCommentAsync(Post.Id, comment, ct);
            ct.ThrowIfCancellationRequested();
            Post.CommentAdded();
            CommentInput.Text = "";
            CommentResult.Title = "Comment posted";
            CommentResult.Message = "";
            CommentResult.Severity = InfoBarSeverity.Success;
            CommentResult.IsOpen = true;
            await LoadCommentsAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException
            or InvalidOperationException or OperationCanceledException or ArgumentException)
        {
            if (ct.IsCancellationRequested) return;
            CommentResult.Title = "Couldn't confirm your comment";
            CommentResult.Message = "Check the comments before trying again. Your text has been kept.";
            CommentResult.Severity = InfoBarSeverity.Error;
            CommentResult.IsOpen = true;
        }
        finally
        {
            _sendingComment = false;
            if (!ct.IsCancellationRequested)
            {
                CommentInput.IsEnabled = true;
                SendCommentButton.IsEnabled = !string.IsNullOrWhiteSpace(CommentInput.Text);
            }
        }
    }

    private async Task LoadImageAsync(CancellationToken ct)
    {
        ImageProgress.Visibility = Visibility.Visible;
        try
        {
            byte[] bytes = await _mobile.GetTimelineImageAsync(Post.ImageUrl, ct);
            using var input = new MemoryStream(bytes, writable: false);
            using var stream = input.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);
            var image = new BitmapImage();
            if (decoder.PixelWidth >= decoder.PixelHeight) image.DecodePixelWidth = 640;
            else image.DecodePixelHeight = 640;
            stream.Seek(0);
            await image.SetSourceAsync(stream);
            ct.ThrowIfCancellationRequested();
            _imageExtension = decoder.DecoderInformation.FileExtensions.FirstOrDefault(ext =>
                ext.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".tif" or ".tiff" or ".heic" or ".heif" or ".avif");
            if (_imageExtension != null) _imageBytes = bytes;
            PostImage.Source = image;
            PostImage.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException
            or COMException or ArgumentException or OperationCanceledException or InvalidOperationException)
        {
            if (!ct.IsCancellationRequested) ImageError.Visibility = Visibility.Visible;
        }
        finally { if (!ct.IsCancellationRequested) ImageProgress.Visibility = Visibility.Collapsed; }
    }

    private void Dialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_sendingComment || _liking || _openingImage) { args.Cancel = true; return; }
        _lifetime.Cancel();
        _lifetime.Dispose();
        PostImage.Source = null;
        _imageBytes = null;
    }
}
