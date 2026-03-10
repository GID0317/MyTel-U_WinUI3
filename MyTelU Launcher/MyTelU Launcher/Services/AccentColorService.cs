using System;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using ColorThiefDotNet;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using MyTelU_Launcher.Contracts.Services;
using Windows.Storage;
using Windows.UI;
using Drawing = System.Drawing;

namespace MyTelU_Launcher.Services;

public class AccentColorService
{
    private readonly IThemeSelectorService _themeSelectorService;
    private Color _lastAppliedColor;
    private bool _hasAppliedColor = false;
    private bool _themeChangeSubscribed = false;
    private bool _isApplying = false;
    private DateTime _lastRefreshTime = DateTime.MinValue;

    // Early startup applies the last accent before DI and the main window exist.
    private static Color s_cachedColor;
    private static bool s_hasCachedColor = false;

    /// <summary>Raised after accent resources are rewritten.</summary>
    public static event EventHandler? AccentColorsUpdated;

    /// <summary>The dominant color most recently extracted from the background image.</summary>
    public Color? LastExtractedColor { get; private set; }

    /// <summary>The average brightness of the last processed background image, from 0 to 255.</summary>
    public double? LastBackgroundBrightness { get; private set; }

    // Sharing one brush instance across multiple resource slots can crash WinUI on theme changes.
    private static object CloneBrushIfNeeded(object value)
    {
        if (value is SolidColorBrush scb)
            return new SolidColorBrush(scb.Color) { Opacity = scb.Opacity };
        return value;
    }

    // Write to both the root dictionary and theme dictionaries so template lookups stay in sync.
    private static void SetResource(ResourceDictionary resources, string key, object value)
    {
        resources[key] = CloneBrushIfNeeded(value);
        foreach (var themeKey in new[] { "Default", "Light", "Dark", "HighContrast" })
        {
            if (resources.ThemeDictionaries.TryGetValue(themeKey, out var themeObj)
                && themeObj is ResourceDictionary themeDict)
                themeDict[key] = CloneBrushIfNeeded(value);
        }
    }

    public AccentColorService(IThemeSelectorService themeSelectorService)
    {
        _themeSelectorService = themeSelectorService;
    }

    public async Task<Color> ExtractDominantColorAsync(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
        {
            System.Diagnostics.Debug.WriteLine("No valid image path provided, using system accent");
            var currentTheme = _themeSelectorService.Theme;
            LastBackgroundBrightness = currentTheme == ElementTheme.Light ? 200.0 : 50.0;
            return GetSystemAccentColor();
        }

        try
        {
            var fileInfo = new FileInfo(imagePath);
            System.Diagnostics.Debug.WriteLine($"===== COLOR EXTRACTION FROM IMAGE =====");
            System.Diagnostics.Debug.WriteLine($"Image Path: {imagePath}");
            
            var quantizedColor = await Task.Run(async () =>
            {
                using var bitmap = await LoadImageAsync(imagePath);
                
                System.Diagnostics.Debug.WriteLine($"Image Dimensions: {bitmap.Width}x{bitmap.Height} pixels");
                
                long totalBrightness = 0;
                int pixelCount = 0;
                int sampleStep = Math.Max(1, bitmap.Width / 100);
                
                for (int y = 0; y < bitmap.Height; y += sampleStep)
                {
                    for (int x = 0; x < bitmap.Width; x += sampleStep)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        double luminance = 0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B;
                        totalBrightness += (long)luminance;
                        pixelCount++;
                    }
                }
                
                double avgBrightness = pixelCount > 0 ? (double)totalBrightness / pixelCount : 128;
                LastBackgroundBrightness = avgBrightness;
                System.Diagnostics.Debug.WriteLine($"Average Background Brightness: {avgBrightness:F2}");
                
                var result = ColorThief.GetColor(bitmap, quality: 1, ignoreWhite: true);

                return result;
            });

            var baseColor = Color.FromArgb(255, quantizedColor.Color.R, quantizedColor.Color.G, quantizedColor.Color.B);
            
            // Slightly boost saturation so muted wallpapers still produce a usable accent.
            var saturatedColor = SetSaturation(baseColor, 1.2);
            
            System.Diagnostics.Debug.WriteLine($"Color Transformation:");
            System.Diagnostics.Debug.WriteLine($"  Original   : RGB({quantizedColor.Color.R},{quantizedColor.Color.G},{quantizedColor.Color.B})");
            System.Diagnostics.Debug.WriteLine($"  Saturated  : RGB({saturatedColor.R},{saturatedColor.G},{saturatedColor.B})");
            System.Diagnostics.Debug.WriteLine($"======================================");

            LastExtractedColor = saturatedColor;
            return saturatedColor;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Color extraction failed: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            var currentTheme = _themeSelectorService.Theme;
            LastBackgroundBrightness = currentTheme == ElementTheme.Light ? 200.0 : 50.0;
        }

        var fallback = GetSystemAccentColor();
        LastExtractedColor = fallback;
        return fallback;
    }

    /// <summary>
    /// Re-applies the last cached accent color immediately, without triggering extraction.
    /// Safe to call at any point (including early startup before DI is available).
    /// </summary>
    public static void ApplyCachedAccentEarly()
    {
        if (!s_hasCachedColor) return;

        var resources = Application.Current?.Resources;
        if (resources == null) return;

        var color = s_cachedColor;

        string[] keys = {
            "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush",
            "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed",
            "AccentButtonBorderBrush",
            "SystemControlHighlightAccentBrush", "SystemControlForegroundAccentBrush",
            "SystemControlBackgroundAccentBrush",
            "SegmentedPillBackground",
            "SegmentedPillBackgroundPointerOver",
            "SegmentedPillBackgroundPressed",
        };

        foreach (var key in keys)
        {
            var freshBrush = new SolidColorBrush(color);
            if (resources.ContainsKey(key))
                resources[key] = freshBrush;
            else
                resources.Add(key, freshBrush);
        }

        System.Diagnostics.Debug.WriteLine($"[AccentColorService] ApplyCachedAccentEarly applied R={color.R},G={color.G},B={color.B}");
    }

    public void ApplyAccentColor(Color color)
    {
        if (_isApplying) return;

        _lastAppliedColor = color;
        _hasAppliedColor = true;

        s_cachedColor = color;
        s_hasCachedColor = true;

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue == null) return;

        dispatcherQueue.TryEnqueue(() =>
        {
            if (_isApplying) return;
            _isApplying = true;
            try
            {
                if (App.MainWindow?.Content is not FrameworkElement rootElement)
                    return;

                if (!_themeChangeSubscribed)
                {
                    _themeChangeSubscribed = true;
                    rootElement.ActualThemeChanged += (sender, _) =>
                    {
                        if (_hasAppliedColor && (DateTime.Now - _lastRefreshTime).TotalMilliseconds > 1000)
                        {
                            System.Diagnostics.Debug.WriteLine($"Theme changed to {sender.ActualTheme}, re-applying accent color");
                            ApplyAccentColor(_lastAppliedColor);
                        }
                    };
                }
                
                var resources = Application.Current.Resources;

                var isLightTheme = rootElement.ActualTheme == ElementTheme.Light;
                System.Diagnostics.Debug.WriteLine($"Applying accent for {rootElement.ActualTheme} theme");

                // Keep these in the theme dictionaries so WinUI templates do not revert to the system accent.
                SetResource(resources, "SystemAccentColor", color);
                SetResource(resources, "SystemAccentColorLight1", LightenColor(color, 0.2f));
                SetResource(resources, "SystemAccentColorLight2", LightenColor(color, 0.4f));
                SetResource(resources, "SystemAccentColorLight3", LightenColor(color, 0.6f));
                SetResource(resources, "SystemAccentColorDark1",  DarkenColor(color, 0.2f));
                SetResource(resources, "SystemAccentColorDark2",  DarkenColor(color, 0.4f));
                SetResource(resources, "SystemAccentColorDark3",  DarkenColor(color, 0.6f));

                SolidColorBrush accentBrush, accentHoverBrush, accentPressedBrush, accentDisabledBrush;
                if (isLightTheme)
                {
                    var baseDarkened = EnsureLightness(color, 0.45, false);
                    accentBrush       = new SolidColorBrush(baseDarkened);
                    accentHoverBrush  = new SolidColorBrush(GetDarkColor(baseDarkened, 0.1));
                    accentPressedBrush= new SolidColorBrush(GetDarkColor(baseDarkened, 0.2));
                    accentDisabledBrush= new SolidColorBrush(GetLightColor(baseDarkened, 0.2));
                }
                else
                {
                    var baseLightened = EnsureLightness(color, 0.6, true);
                    accentBrush       = new SolidColorBrush(baseLightened);
                    accentHoverBrush  = new SolidColorBrush(GetLightColor(baseLightened, 0.1));
                    accentPressedBrush= new SolidColorBrush(GetDarkColor(baseLightened, 0.1));
                    accentDisabledBrush= new SolidColorBrush(GetDarkColor(baseLightened, 0.3));
                }

                SetResource(resources, "AccentFillColorDefaultBrush",  accentBrush);
                SetResource(resources, "AccentFillColorSecondaryBrush",accentHoverBrush);
                SetResource(resources, "AccentFillColorTertiaryBrush", accentPressedBrush);
                SetResource(resources, "AccentFillColorDisabledBrush", accentDisabledBrush);

                var buttonTextBrush = isLightTheme ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Colors.Black);

                SetResource(resources, "AccentButtonBackground",             accentBrush);
                SetResource(resources, "AccentButtonBackgroundPointerOver",  accentHoverBrush);
                SetResource(resources, "AccentButtonBackgroundPressed",      accentPressedBrush);
                SetResource(resources, "AccentButtonBackgroundDisabled",     accentDisabledBrush);
                SetResource(resources, "AccentButtonForeground",             buttonTextBrush);
                SetResource(resources, "AccentButtonForegroundPointerOver",  buttonTextBrush);
                SetResource(resources, "AccentButtonForegroundPressed",      buttonTextBrush);
                SetResource(resources, "AccentButtonForegroundDisabled",     buttonTextBrush);
                SetResource(resources, "AccentButtonBorderBrush",            accentBrush);
                SetResource(resources, "AccentButtonBorderBrushPointerOver", accentHoverBrush);
                SetResource(resources, "AccentButtonBorderBrushPressed",     accentPressedBrush);

                var checkGlyphBrush = isLightTheme ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Colors.Black);
                SetResource(resources, "CheckBoxCheckGlyphForegroundChecked",              checkGlyphBrush);
                SetResource(resources, "CheckBoxCheckGlyphForegroundCheckedPointerOver",   checkGlyphBrush);
                SetResource(resources, "CheckBoxCheckGlyphForegroundCheckedPressed",       checkGlyphBrush);
                SetResource(resources, "CheckBoxCheckBackgroundFillChecked",               accentBrush);
                SetResource(resources, "CheckBoxCheckBackgroundFillCheckedPointerOver",    accentHoverBrush);
                SetResource(resources, "CheckBoxCheckBackgroundFillCheckedPressed",        accentPressedBrush);
                SetResource(resources, "CheckBoxCheckBackgroundStrokeChecked",             accentBrush);
                SetResource(resources, "CheckBoxCheckBackgroundStrokeCheckedPointerOver",  accentHoverBrush);
                SetResource(resources, "CheckBoxCheckBackgroundStrokeCheckedPressed",      accentPressedBrush);

                SetResource(resources, "ToggleSwitchFillOn",              accentBrush);
                SetResource(resources, "ToggleSwitchFillOnPointerOver",   accentHoverBrush);
                SetResource(resources, "ToggleSwitchFillOnPressed",       accentPressedBrush);
                SetResource(resources, "ToggleSwitchStrokeOn",            accentBrush);
                SetResource(resources, "ToggleSwitchStrokeOnPointerOver", accentHoverBrush);
                SetResource(resources, "ToggleSwitchStrokeOnPressed",     accentPressedBrush);
                SetResource(resources, "ToggleSwitchKnobFillOn",          checkGlyphBrush);
                SetResource(resources, "ToggleSwitchKnobFillOnPointerOver",checkGlyphBrush);
                SetResource(resources, "ToggleSwitchKnobFillOnPressed",   checkGlyphBrush);

                SetResource(resources, "SliderTrackValueFill",            accentBrush);
                SetResource(resources, "SliderTrackValueFillPointerOver", accentHoverBrush);
                SetResource(resources, "SliderTrackValueFillPressed",     accentPressedBrush);
                SetResource(resources, "SliderThumbBackground",           accentBrush);
                SetResource(resources, "SliderThumbBackgroundPointerOver",accentHoverBrush);
                SetResource(resources, "SliderThumbBackgroundPressed",    accentPressedBrush);

                // Leave the default focus border alone so focus visuals stay readable.
                SetResource(resources, "TextControlSelectionHighlightColor", color);

                SetResource(resources, "ComboBoxItemPillFillBrush",                   accentBrush);
                SetResource(resources, "ComboBoxItemPillFillBrushPointerOver",        accentHoverBrush);
                SetResource(resources, "ComboBoxItemPillFillBrushPressed",            accentPressedBrush);
                SetResource(resources, "ComboBoxItemPillFillBrushDisabled",           accentDisabledBrush);
                SetResource(resources, "ComboBoxItemPillFillBrushSelected",           accentBrush);
                SetResource(resources, "ComboBoxItemPillFillBrushSelectedPointerOver",accentHoverBrush);
                SetResource(resources, "ComboBoxItemPillFillBrushSelectedPressed",    accentPressedBrush);
                
                SetResource(resources, "ComboBoxItemBorderBrushChecked",              accentBrush);
                SetResource(resources, "ComboBoxItemBorderBrushCheckedPointerOver",   accentHoverBrush);
                SetResource(resources, "ComboBoxItemBorderBrushCheckedPressed",       accentPressedBrush);
                
                SetResource(resources, "ComboBoxItemSelectedPointerOverBackground",   accentBrush);
                SetResource(resources, "ComboBoxItemSelectedPressedBackground",       accentPressedBrush);
                SetResource(resources, "ComboBoxItemSelectedBackground",              accentBrush);
                SetResource(resources, "ComboBoxItemRevealBorderBrushSelected",       accentBrush);
                SetResource(resources, "ComboBoxItemRevealBorderBrushSelectedPointerOver", accentHoverBrush);
                SetResource(resources, "ComboBoxItemRevealBorderBrushSelectedPressed", accentPressedBrush);

                SetResource(resources, "NavigationViewSelectionIndicatorForeground",  accentBrush);

                SetResource(resources, "ListViewItemSelectionIndicatorBrush",         accentBrush);
                SetResource(resources, "ListViewItemSelectionIndicatorPointerOverBrush", accentHoverBrush);
                SetResource(resources, "ListViewItemSelectionIndicatorPressedBrush",  accentPressedBrush);

                // Segmented applies the same resources locally because its VSM storyboards cache brushes.
                SetResource(resources, "SegmentedPillBackground",            accentBrush);
                SetResource(resources, "SegmentedPillBackgroundPointerOver", accentHoverBrush);
                SetResource(resources, "SegmentedPillBackgroundPressed",     accentPressedBrush);
                SetResource(resources, "SegmentedPillBackgroundDisabled",    accentDisabledBrush);

                SetResource(resources, "ProgressRingForegroundThemeBrush",    accentBrush);
                SetResource(resources, "ProgressBarForeground",               accentBrush);
                SetResource(resources, "ProgressBarIndeterminateForeground",  accentBrush);

                SetResource(resources, "HyperlinkForeground",            accentBrush);
                SetResource(resources, "HyperlinkForegroundPointerOver", accentHoverBrush);
                SetResource(resources, "HyperlinkForegroundPressed",     accentPressedBrush);
                
                SetResource(resources, "HyperlinkButtonForeground",             accentBrush);
                SetResource(resources, "HyperlinkButtonForegroundPointerOver",  accentHoverBrush);
                SetResource(resources, "HyperlinkButtonForegroundPressed",      accentPressedBrush);
                SetResource(resources, "HyperlinkButtonForegroundDisabled",     accentDisabledBrush);

                SetResource(resources, "SystemControlHighlightAccentBrush",    accentBrush);
                SetResource(resources, "SystemControlForegroundAccentBrush",   accentBrush);
                SetResource(resources, "SystemControlHighlightAltAccentBrush", accentHoverBrush);
                SetResource(resources, "SystemControlBackgroundAccentBrush",   accentBrush);
                SetResource(resources, "SystemControlDisabledAccentBrush",     accentDisabledBrush);
                SetResource(resources, "SystemControlHighlightAccent2Brush",   accentHoverBrush);
                SetResource(resources, "SystemControlHighlightAccent3Brush",   accentPressedBrush);

                if (!resources.ContainsKey("AccentColor"))
                    resources.Add("AccentColor", accentBrush);
                else
                    resources["AccentColor"] = accentBrush;

                System.Diagnostics.Debug.WriteLine($"Applied accent color: R={color.R}, G={color.G}, B={color.B} for {(isLightTheme ? "LIGHT" : "DARK")} theme");

                _lastRefreshTime = DateTime.Now;
                var currentTheme = rootElement.RequestedTheme;
                
                rootElement.RequestedTheme = ElementTheme.Dark;
                rootElement.RequestedTheme = ElementTheme.Light;
                rootElement.RequestedTheme = currentTheme;
                
                _lastAppliedColor = color;
                _hasAppliedColor = true;
                
                AccentColorsUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to apply accent color: {ex.Message}");
            }
            finally
            {
                _isApplying = false;
            }
        });
    }

    private void RefreshTheme()
    {
    }

    public async Task UpdateAccentFromImageAsync(string imagePath)
    {
        var dominantColor = await ExtractDominantColorAsync(imagePath);
        ApplyAccentColor(dominantColor);
    }

    private Color GetSystemAccentColor()
    {
        try
        {
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            var accent = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            return Color.FromArgb(accent.A, accent.R, accent.G, accent.B);
        }
        catch
        {
            return Color.FromArgb(255, 0, 120, 215);
        }
    }

    private Color LightenColor(Color color, float amount)
    {
        var r = (byte)Math.Min(255, color.R + (255 - color.R) * amount);
        var g = (byte)Math.Min(255, color.G + (255 - color.G) * amount);
        var b = (byte)Math.Min(255, color.B + (255 - color.B) * amount);
        return Color.FromArgb(color.A, r, g, b);
    }

    private Color DarkenColor(Color color, float amount)
    {
        var r = (byte)(color.R * (1 - amount));
        var g = (byte)(color.G * (1 - amount));
        var b = (byte)(color.B * (1 - amount));
        return Color.FromArgb(color.A, r, g, b);
    }

    private Color SetSaturation(Color color, double saturation)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0, s = 0, l = (max + min) / 2.0;

        if (delta != 0)
        {
            s = l < 0.5 ? delta / (max + min) : delta / (2.0 - max - min);

            if (max == r)
                h = ((g - b) / delta) + (g < b ? 6 : 0);
            else if (max == g)
                h = ((b - r) / delta) + 2;
            else
                h = ((r - g) / delta) + 4;

            h /= 6.0;
        }

        s = Math.Min(1.0, s * saturation);

        return HslToRgb(h, s, l, color.A);
    }

    public void ApplyToContentDialog(Microsoft.UI.Xaml.Controls.ContentDialog dialog)
    {
        if (dialog == null) return;

        if (_themeSelectorService != null)
        {
            dialog.RequestedTheme = _themeSelectorService.Theme;
        }

        var resources = Application.Current.Resources;
        
        // Popups can hold stale resources, so copy the current accent set onto the dialog itself.
        string[] keys = {
            "AccentButtonBackground",
            "AccentButtonBackgroundPointerOver",
            "AccentButtonBackgroundPressed",
            "AccentButtonBackgroundDisabled",
            "AccentButtonForeground",
            "AccentButtonForegroundPointerOver",
            "AccentButtonForegroundPressed",
            "AccentButtonForegroundDisabled",
            "AccentButtonBorderBrush",
            "AccentButtonBorderBrushPointerOver",
            "AccentButtonBorderBrushPressed",
            "AccentFillColorDefaultBrush",
            "AccentFillColorSecondaryBrush",
            "AccentFillColorTertiaryBrush",
            "AccentFillColorDisabledBrush",
            "SystemControlHighlightAccentBrush",
            "SystemControlHighlightAccent2Brush",
            "SystemControlHighlightAccent3Brush",
            "SystemControlBackgroundAccentBrush",
            "SystemControlDisabledAccentBrush",
            "SystemControlForegroundAccentBrush",
            "TextControlSelectionHighlightColor",
            "AccentColor"
        };

        foreach (var key in keys)
        {
            if (resources.TryGetValue(key, out var brush))
            {
                dialog.Resources[key] = brush;
            }
        }
    }

    public void ApplyToSegmented(CommunityToolkit.WinUI.Controls.Segmented segmented)
    {
        if (segmented == null) return;

        var resources = Application.Current.Resources;
        
        // The segmented pill needs local resources because its VSM storyboards cache brush instances.
        string[] keys = {
            "SegmentedPillBackground",
            "SegmentedPillBackgroundPointerOver",
            "SegmentedPillBackgroundPressed",
            "SegmentedPillBackgroundDisabled"
        };

        foreach (var key in keys)
        {
            if (resources.TryGetValue(key, out var brush))
            {
                segmented.Resources[key] = brush;
            }
        }
        
        var selected = segmented.SelectedItem;
        if (selected != null)
        {
            segmented.SelectedItem = null;
            segmented.SelectedItem = selected;
        }
    }

    private Color EnsureLightness(Color color, double targetLightness, bool isDarkTheme)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0, s = 0, l = (max + min) / 2.0;

        if (delta != 0)
        {
            s = l < 0.5 ? delta / (max + min) : delta / (2.0 - max - min);

            if (max == r)
                h = ((g - b) / delta) + (g < b ? 6 : 0);
            else if (max == g)
                h = ((b - r) / delta) + 2;
            else
                h = ((r - g) / delta) + 4;

            h /= 6.0;
        }

        if (isDarkTheme)
        {
            l = Math.Max(l, targetLightness);
        }
        else
        {
            l = Math.Min(l, targetLightness);
        }

        return HslToRgb(h, s, l, color.A);
    }

    private Color GetLightColor(Color color, double amount)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0, s = 0, l = (max + min) / 2.0;

        if (delta != 0)
        {
            s = l < 0.5 ? delta / (max + min) : delta / (2.0 - max - min);

            if (max == r)
                h = ((g - b) / delta) + (g < b ? 6 : 0);
            else if (max == g)
                h = ((b - r) / delta) + 2;
            else
                h = ((r - g) / delta) + 4;

            h /= 6.0;
        }

        l = Math.Min(1.0, l + amount);

        return HslToRgb(h, s, l, color.A);
    }

    private Color GetDarkColor(Color color, double amount)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0, s = 0, l = (max + min) / 2.0;

        if (delta != 0)
        {
            s = l < 0.5 ? delta / (max + min) : delta / (2.0 - max - min);

            if (max == r)
                h = ((g - b) / delta) + (g < b ? 6 : 0);
            else if (max == g)
                h = ((b - r) / delta) + 2;
            else
                h = ((r - g) / delta) + 4;

            h /= 6.0;
        }

        l = Math.Max(0.0, l - amount);

        return HslToRgb(h, s, l, color.A);
    }

    private Color HslToRgb(double h, double s, double l, byte alpha)
    {
        double r, g, b;

        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3.0);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3.0);
        }

        return Color.FromArgb(alpha, (byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }

    private async Task<Drawing.Bitmap> LoadImageAsync(string path)
    {
        try
        {
            // Read into memory first so the file is not left locked.
            var bytes = await File.ReadAllBytesAsync(path);
            using var ms = new MemoryStream(bytes);
            
            using var tempBitmap = new Drawing.Bitmap(ms);
            return new Drawing.Bitmap(tempBitmap);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is System.Runtime.InteropServices.ExternalException)
        {
            System.Diagnostics.Debug.WriteLine($"GDI+ load failed for {path} ({ex.Message}). Trying WinRT fallback...");
            
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var stream = await file.OpenAsync(FileAccessMode.Read);
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                
                var transform = new Windows.Graphics.Imaging.BitmapTransform();
                var pixelData = await decoder.GetPixelDataAsync(
                    Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    transform,
                    Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
                    Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
                
                var pixels = pixelData.DetachPixelData();
                var width = (int)decoder.PixelWidth;
                var height = (int)decoder.PixelHeight;
                
                var bitmap = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppArgb);
                
                var bounds = new Drawing.Rectangle(0, 0, width, height);
                var mapData = bitmap.LockBits(bounds, Drawing.Imaging.ImageLockMode.WriteOnly, bitmap.PixelFormat);
                
                var stride = mapData.Stride;
                var rowBytes = width * 4;
                
                if (stride == rowBytes)
                {
                    System.Runtime.InteropServices.Marshal.Copy(pixels, 0, mapData.Scan0, pixels.Length);
                }
                else
                {
                    for (int y = 0; y < height; y++)
                    {
                        var sourceIndex = y * rowBytes;
                        var destPtr = IntPtr.Add(mapData.Scan0, y * stride);
                        System.Runtime.InteropServices.Marshal.Copy(pixels, sourceIndex, destPtr, rowBytes);
                    }
                }
                
                bitmap.UnlockBits(mapData);
                return bitmap;
            }
            catch (Exception ex2)
            {
                System.Diagnostics.Debug.WriteLine($"WinRT fallback also failed: {ex2.Message}");
                throw;
            }
        }
    }
}
