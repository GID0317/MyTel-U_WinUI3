using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using MyTelU_Launcher.Services;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace MyTelU_Launcher.Views;

public sealed partial class ScanAttendanceDialog : ContentDialog
{
    public enum InputMethod { Camera, Image, Link }

    private readonly string? _initialImagePath;
    private readonly AttendanceQrService _qrService = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _cameraGate = new(1, 1);
    private readonly SoftwareBitmapSource _preview = new();
    private CancellationTokenSource? _inputCancellation;
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private string? _qrValue;
    private int _cameraGeneration;
    private int _framePending;
    private long _lastFrameTime;
    private bool _submitting;
    private bool _closed;
    public bool AttendanceSubmitted { get; private set; }

    public ScanAttendanceDialog(InputMethod inputMethod = InputMethod.Camera, string? imagePath = null)
    {
        _initialImagePath = imagePath;
        InitializeComponent();
        CameraPreview.Source = _preview;
        InputModes.SelectedItem = inputMethod switch
        {
            InputMethod.Camera => CameraMode,
            InputMethod.Image => ImageMode,
            InputMethod.Link => LinkMode,
            _ => throw new ArgumentOutOfRangeException(nameof(inputMethod))
        };
        UpdateInputPanels(InputModes.SelectedItem);
        if (imagePath != null)
        {
            Title = "Attendance from image";
            InputModes.Visibility = Visibility.Collapsed;
        }
    }

    private async void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        if (_initialImagePath != null)
            await ReadImageAsync(_initialImagePath);
        else if (InputModes.SelectedItem == CameraMode)
            await LoadCamerasAsync();
    }

    private async void InputMode_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (CameraPanel == null || _closed) return;
        _inputCancellation?.Cancel();
        ClearQr();
        ClearImagePreview();
        StatusBar.IsOpen = false;
        UpdateInputPanels(sender.SelectedItem);
        await StopCameraAsync();
        if (!_closed && InputModes.SelectedItem == CameraMode)
            await LoadCamerasAsync();
    }

    private void UpdateInputPanels(SelectorBarItem? selected)
    {
        InputPanels.MinHeight = selected == CameraMode ? 288 : 0;
        CameraPanel.Visibility = selected == CameraMode ? Visibility.Visible : Visibility.Collapsed;
        ImagePanel.Visibility = selected == ImageMode ? Visibility.Visible : Visibility.Collapsed;
        LinkPanel.Visibility = selected == LinkMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadCamerasAsync()
    {
        await _cameraGate.WaitAsync();
        try
        {
            if (_closed || InputModes.SelectedItem != CameraMode) return;
            CameraPicker.IsEnabled = CameraButton.IsEnabled = false;
            CameraPicker.PlaceholderText = "Looking for cameras...";
            string? previousId = (CameraPicker.SelectedItem as DeviceInformation)?.Id;
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask(_lifetime.Token);
            if (_closed || InputModes.SelectedItem != CameraMode) return;
            // DeviceInformationCollection cannot be marshalled as ItemsSource in Native AOT.
            CameraPicker.Items.Clear();
            foreach (var device in devices) CameraPicker.Items.Add(device);
            CameraPicker.SelectedItem = devices.FirstOrDefault(d => d.Id == previousId) ?? devices.FirstOrDefault();
            CameraPicker.IsEnabled = CameraButton.IsEnabled = devices.Count > 0;
            CameraPicker.PlaceholderText = devices.Count > 0 ? "Select camera" : "No cameras found";
            if (devices.Count == 0)
                ShowStatus(InfoBarSeverity.Warning, "No camera found", "Connect a camera, then reopen Camera, or use Image or Link.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is UnauthorizedAccessException or COMException)
        {
            if (!_closed && InputModes.SelectedItem == CameraMode)
            {
                CameraPicker.PlaceholderText = "Camera unavailable";
                ShowStatus(InfoBarSeverity.Error, "Could not find cameras", "Check camera access in Windows Settings, or use Image or Link.");
            }
        }
        finally { _cameraGate.Release(); }
    }

    private CancellationToken BeginRead()
    {
        _inputCancellation?.Cancel();
        _inputCancellation?.Dispose();
        _inputCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        ClearQr();
        ClearImagePreview();
        StatusBar.IsOpen = false;
        SetReading(true);
        return _inputCancellation.Token;
    }

    private void SetReading(bool value)
    {
        LoadingBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        ImageButton.IsEnabled = ReadLinkButton.IsEnabled = !value;
    }

    private void ClearQr()
    {
        _qrValue = null;
        IsPrimaryButtonEnabled = false;
    }

    private void ClearImagePreview()
    {
        var source = ImagePreview.Source as SoftwareBitmapSource;
        ImagePreview.Source = null;
        ImagePreviewPanel.Visibility = ImagePreviewFrame.Visibility = ImageName.Visibility = Visibility.Collapsed;
        ImageName.Text = "";
        source?.Dispose();
    }

    private void SetQr(string value)
    {
        _qrValue = AttendanceQrService.ValidateQrValue(value);
        IsPrimaryButtonEnabled = true;
        ShowStatus(InfoBarSeverity.Informational, "QR code ready", "");
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private void LinkInput_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (StatusBar == null || _closed) return;
        _inputCancellation?.Cancel();
        ClearQr();
        ClearImagePreview();
        StatusBar.IsOpen = false;
    }

    private async void ReadLink_Click(object sender, RoutedEventArgs args)
    {
        var token = BeginRead();
        try
        {
            using var bitmap = await _qrService.LoadLinkImageAsync(LinkInput.Text, token);
            await ShowPreviewAndReadQrAsync(bitmap, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or COMException or TaskCanceledException or ObjectDisposedException)
        {
            if (!token.IsCancellationRequested) ShowReadError(ex);
        }
        finally { if (!_closed && _inputCancellation?.Token == token) SetReading(false); }
    }

    internal static async Task<string?> PickImagePathAsync(XamlRoot root)
    {
        var picker = new FileOpenPicker(root.ContentIslandEnvironment.AppWindowId);
        foreach (string extension in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff" })
            picker.FileTypeFilter.Add(extension);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    private async void ChooseImage_Click(object sender, RoutedEventArgs args)
        => await ReadImageAsync();

    private async Task ReadImageAsync(string? imagePath = null)
    {
        var token = BeginRead();
        try
        {
            imagePath ??= await PickImagePathAsync(XamlRoot);
            token.ThrowIfCancellationRequested();
            if (imagePath == null) return;
            ImageName.Text = Path.GetFileName(imagePath);
            ImagePreviewPanel.Visibility = ImageName.Visibility = Visibility.Visible;
            using var input = File.OpenRead(imagePath);
            using var stream = input.AsRandomAccessStream();
            using var bitmap = await AttendanceQrService.LoadImageAsync(stream, token);
            await ShowPreviewAndReadQrAsync(bitmap, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or COMException or ArgumentException)
        {
            if (!token.IsCancellationRequested) ShowReadError(ex);
        }
        finally { if (!_closed && _inputCancellation?.Token == token) SetReading(false); }
    }

    private async Task ShowPreviewAndReadQrAsync(SoftwareBitmap bitmap, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var source = new SoftwareBitmapSource();
        try
        {
            await source.SetBitmapAsync(bitmap);
            token.ThrowIfCancellationRequested();
            ImagePreview.Source = source;
        }
        catch
        {
            source.Dispose();
            throw;
        }
        ImagePreviewPanel.Visibility = ImagePreviewFrame.Visibility = Visibility.Visible;

        var value = await Task.Run(() => AttendanceQrService.ReadBitmap(bitmap), token);
        token.ThrowIfCancellationRequested();
        SetQr(value ?? throw new InvalidDataException("No attendance QR code was found in this image."));
    }

    private void ShowReadError(Exception error)
        => ShowStatus(InfoBarSeverity.Error, "Could not read QR code",
            error is InvalidDataException ? error.Message : "Choose another image or check your connection and try the link again.");

    private async void Camera_Click(object sender, RoutedEventArgs args)
    {
        if (_capture != null) { await StopCameraAsync(); return; }
        CameraButton.IsEnabled = false;
        ClearQr();
        StatusBar.IsOpen = false;
        await _cameraGate.WaitAsync();
        try
        {
            if (_closed || InputModes.SelectedItem != CameraMode) return;
            if (CameraPicker.SelectedItem is not DeviceInformation camera)
                throw new InvalidOperationException("No camera was found. Connect a camera, or use Image or Link.");

            _capture = new MediaCapture();
            await _capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = camera.Id, StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly, MemoryPreference = MediaCaptureMemoryPreference.Cpu
            }).AsTask(_lifetime.Token);
            if (_closed || InputModes.SelectedItem != CameraMode) return;
            var source = _capture.FrameSources.Values
                .Where(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
                .OrderByDescending(s => s.Info.MediaStreamType == MediaStreamType.VideoPreview).FirstOrDefault()
                ?? throw new InvalidOperationException("This camera does not support scanning. Use Image or Link instead.");
            var format = source.CurrentFormat.VideoFormat;
            if (format.Width == 0 || format.Height == 0)
                throw new InvalidOperationException("This camera returned an unsupported image size. Use Image or Link instead.");
            double scale = Math.Min(1, 640.0 / Math.Max(format.Width, format.Height));
            uint width = Math.Max(1, (uint)(format.Width * scale));
            uint height = Math.Max(1, (uint)(format.Height * scale));
            _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8,
                new BitmapSize { Width = width, Height = height }).AsTask(_lifetime.Token);
            _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            _reader.FrameArrived += Frame_Arrived;
            if (await _reader.StartAsync().AsTask(_lifetime.Token) != MediaFrameReaderStartStatus.Success)
                throw new InvalidOperationException("The camera could not start. Close other camera apps and try again.");
            CameraPicker.IsEnabled = false;
            CameraActionText.Text = "Stop camera";
            CameraActionIcon.Symbol = Symbol.Stop;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            if (!_closed && InputModes.SelectedItem == CameraMode) ShowStatus(InfoBarSeverity.Error, "Camera unavailable", ex is UnauthorizedAccessException
                ? "Allow camera access in Windows Settings, or use Image or Link."
                : ex is InvalidOperationException ? ex.Message : "The camera could not start. Use Image or Link instead.");
        }
        finally
        {
            _cameraGate.Release();
            CameraButton.IsEnabled = CameraPicker.SelectedItem != null;
            if (_closed || InputModes.SelectedItem != CameraMode || _reader == null || CameraPicker.IsEnabled)
                await StopCameraAsync();
        }
    }

    private void Frame_Arrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastFrameTime) < 200 || Interlocked.Exchange(ref _framePending, 1) != 0) return;
        Interlocked.Exchange(ref _lastFrameTime, now);
        int generation = Volatile.Read(ref _cameraGeneration);
        SoftwareBitmap? preview = null;
        bool queued = false;
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            using var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap == null) return;
            preview = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            string? value = null;
            string? error = null;
            try { value = AttendanceQrService.ReadBitmap(preview); }
            catch (InvalidDataException ex) { error = ex.Message; }
            var display = preview;
            queued = DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (_closed || generation != _cameraGeneration || InputModes.SelectedItem != CameraMode) return;
                    await _preview.SetBitmapAsync(display);
                    CameraPlaceholder.Visibility = Visibility.Collapsed;
                    if (value != null)
                    {
                        await StopCameraAsync();
                        if (!_closed && InputModes.SelectedItem == CameraMode && _cameraGeneration == generation + 1) SetQr(value);
                    }
                    else if (error != null) ShowStatus(InfoBarSeverity.Warning, "QR code not supported", error);
                }
                catch (Exception ex) when (ex is COMException or ObjectDisposedException)
                {
                    await StopCameraAsync();
                    if (!_closed) ShowStatus(InfoBarSeverity.Error, "Camera unavailable", "The camera stopped. Use Image or Link instead.");
                }
                finally { display.Dispose(); Volatile.Write(ref _framePending, 0); }
            });
        }
        catch (Exception ex) when (ex is COMException or ObjectDisposedException)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (!_closed && generation == _cameraGeneration)
                {
                    await StopCameraAsync();
                    ShowStatus(InfoBarSeverity.Error, "Camera unavailable", "The camera stopped. Use Image or Link instead.");
                }
            });
        }
        finally
        {
            if (!queued) { preview?.Dispose(); Volatile.Write(ref _framePending, 0); }
        }
    }

    private async Task StopCameraAsync()
    {
        Interlocked.Increment(ref _cameraGeneration);
        await _cameraGate.WaitAsync();
        try
        {
            if (_reader != null)
            {
                _reader.FrameArrived -= Frame_Arrived;
                try { await _reader.StopAsync(); }
                catch (COMException) { /* A disconnected camera can no longer be stopped. */ }
                _reader.Dispose();
                _reader = null;
            }
            _capture?.Dispose();
            _capture = null;
            CameraPicker.IsEnabled = CameraButton.IsEnabled = CameraPicker.SelectedItem != null;
            CameraActionText.Text = "Start camera";
            CameraActionIcon.Symbol = Symbol.Camera;
        }
        finally { _cameraGate.Release(); }
    }

    private async void Submit_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_qrValue == null || _submitting || AttendanceSubmitted) return;
        var deferral = args.GetDeferral();
        string value = _qrValue;
        _submitting = true;
        IsPrimaryButtonEnabled = false;
        SetInputsEnabled(false);
        LoadingBar.Visibility = Visibility.Visible;
        StatusBar.IsOpen = false;
        try
        {
            var result = await App.GetService<MobileAcademicService>().SubmitAttendanceAsync(value, _lifetime.Token);
            AttendanceSubmitted = result.Success;
            ClearQr();
            ShowStatus(result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error,
                result.Success ? "Attendance submitted" : "Attendance not accepted", result.Message);
            if (result.Success) CloseButtonText = "Done";
        }
        catch (InvalidOperationException ex)
        {
            ClearQr();
            ShowStatus(InfoBarSeverity.Error, "Could not submit attendance", ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException)
        {
            ClearQr();
            ShowStatus(InfoBarSeverity.Warning, "Result not confirmed",
                "Check your attendance history before trying again. The request was not retried.");
        }
        finally
        {
            _submitting = false;
            SetInputsEnabled(!AttendanceSubmitted);
            LoadingBar.Visibility = Visibility.Collapsed;
            deferral.Complete();
        }
    }

    private void SetInputsEnabled(bool enabled)
    {
        InputModes.IsEnabled = CameraButton.IsEnabled = ImageButton.IsEnabled = LinkInput.IsEnabled = ReadLinkButton.IsEnabled = enabled;
        CameraPicker.IsEnabled = enabled && _capture == null;
    }

    private async void Dialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_submitting) { args.Cancel = true; return; }
        var deferral = args.GetDeferral();
        _closed = true;
        ClearQr();
        ClearImagePreview();
        LinkInput.Text = "";
        _lifetime.Cancel();
        _inputCancellation?.Cancel();
        try { await StopCameraAsync(); }
        finally
        {
            CameraPreview.Source = null;
            _preview.Dispose();
            _qrService.Dispose();
            _inputCancellation?.Dispose();
            _lifetime.Dispose();
            deferral.Complete();
        }
    }
}
