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

    // Static cache so ApplyCachedAccentEarly() can be called before DI is ready
    private static Color s_cachedColor;
    private static bool s_hasCachedColor = false;

    /// <summary>
    /// The dominant color most recently extracted from the background image.
    /// Can be used by other components to determine background brightness.
    /// </summary>
    public Color? LastExtractedColor { get; private set; }

    // Sets a resource in the flat dict AND in every theme dictionary so WinUI picks it up
    private static void SetResource(ResourceDictionary resources, string key, object value)
    {
        resources[key] = value;
        foreach (var themeKey in new[] { "Default", "Light", "Dark", "HighContrast" })
        {
            if (resources.ThemeDictionaries.TryGetValue(themeKey, out var themeObj)
                && themeObj is ResourceDictionary themeDict)
                themeDict[key] = value;
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
            return GetSystemAccentColor();
        }

        try
        {
            // Log detailed image info
            var fileInfo = new FileInfo(imagePath);
            System.Diagnostics.Debug.WriteLine($"===== COLOR EXTRACTION FROM IMAGE =====");
            System.Diagnostics.Debug.WriteLine($"Image Path: {imagePath}");
            
            var quantizedColor = await Task.Run(async () =>
            {
                // Create a copy of the helper bitmap to avoid locking issues
                // LoadImageAsync handles GDI+ and WinRT/WIC fallback (WEBP support)
                using var bitmap = await LoadImageAsync(imagePath);
                
                System.Diagnostics.Debug.WriteLine($"Image Dimensions: {bitmap.Width}x{bitmap.Height} pixels");
                
                // Use quality=1 for best color extraction (analyzes all pixels)
                // If the dominant color is white/black (which is often the background), we might want to ignore it 
                // to get the actual accent color.
                var result = ColorThief.GetColor(bitmap, quality: 1, ignoreWhite: true);
                
                // Fallback if ignoreWhite caused issues or returned empty (though GetColor usually returns *something*)
                // If the color is too close to white or black, we might want to try again or pick a palette
                
                return result;
            });

            var baseColor = Color.FromArgb(255, quantizedColor.Color.R, quantizedColor.Color.G, quantizedColor.Color.B);
            
            // Boost saturation by 1.2x like Collapse Launcher does - makes colors more vibrant
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

        // Lightweight brush application – no FrameworkElement / theme-toggle required
        // so this is safe even before the Window is created.
        var color = s_cachedColor;
        var brush        = new SolidColorBrush(color);
        var hoverBrush   = new SolidColorBrush(color) { Opacity = 0.9 };
        var pressedBrush = new SolidColorBrush(color) { Opacity = 0.8 };

        string[] keys = {
            "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush",
            "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed",
            "AccentButtonBorderBrush",
            "SystemControlHighlightAccentBrush", "SystemControlForegroundAccentBrush",
            "SystemControlBackgroundAccentBrush",
            "SegmentedItemSelectedBackgroundThemeBrush",
            "SegmentedItemSelectedBackgroundPointerOverThemeBrush",
            "SegmentedItemSelectedBackgroundPressedThemeBrush",
        };

        foreach (var key in keys)
        {
            if (resources.ContainsKey(key))
                resources[key] = brush;
            else
                resources.Add(key, brush);
        }

        System.Diagnostics.Debug.WriteLine($"[AccentColorService] ApplyCachedAccentEarly applied R={color.R},G={color.G},B={color.B}");
    }

    public void ApplyAccentColor(Color color)
    {
        if (_isApplying) return;

        _lastAppliedColor = color;
        _hasAppliedColor = true;

        // Keep static cache in sync so ApplyCachedAccentEarly() always has the latest color
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

                // Subscribe to theme changes on first apply so accent re-applies on theme switch
                if (!_themeChangeSubscribed)
                {
                    _themeChangeSubscribed = true;
                    rootElement.ActualThemeChanged += (sender, _) =>
                    {
                        // Ignore the artificial toggle we fire for refresh — only react to real theme changes
                        if (_hasAppliedColor && (DateTime.Now - _lastRefreshTime).TotalMilliseconds > 1000)
                        {
                            System.Diagnostics.Debug.WriteLine($"Theme changed to {sender.ActualTheme}, re-applying accent color");
                            ApplyAccentColor(_lastAppliedColor);
                        }
                    };
                }
                
                var resources = Application.Current.Resources;

                // Detect current theme
                var isLightTheme = rootElement.ActualTheme == ElementTheme.Light;
                System.Diagnostics.Debug.WriteLine($"Applying accent for {rootElement.ActualTheme} theme");

                // Core color resources — write into theme dicts so WinUI can't override them
                SetResource(resources, "SystemAccentColor", color);
                SetResource(resources, "SystemAccentColorLight1", LightenColor(color, 0.2f));
                SetResource(resources, "SystemAccentColorLight2", LightenColor(color, 0.4f));
                SetResource(resources, "SystemAccentColorLight3", LightenColor(color, 0.6f));
                SetResource(resources, "SystemAccentColorDark1",  DarkenColor(color, 0.2f));
                SetResource(resources, "SystemAccentColorDark2",  DarkenColor(color, 0.4f));
                SetResource(resources, "SystemAccentColorDark3",  DarkenColor(color, 0.6f));

                // Create theme-appropriate brush variants
                SolidColorBrush accentBrush, accentHoverBrush, accentPressedBrush, accentDisabledBrush;
                if (isLightTheme)
                {
                    // Light theme: base color needs to be dark enough to contrast with white background
                    // Ensure lightness is at most 0.45 (allowing colors like brownish #765b43 which is ~0.36 lightness)
                    // Previously overly aggressive at 0.15
                    var baseDarkened = EnsureLightness(color, 0.45, false);
                    accentBrush       = new SolidColorBrush(baseDarkened);
                    accentHoverBrush  = new SolidColorBrush(GetDarkColor(baseDarkened, 0.1));
                    accentPressedBrush= new SolidColorBrush(GetDarkColor(baseDarkened, 0.2));
                    accentDisabledBrush= new SolidColorBrush(GetLightColor(baseDarkened, 0.2));
                }
                else
                {
                    // Dark theme: base color needs to be light enough to contrast with dark background
                    // Ensure lightness is at least 0.6 (similar to Collapse Launcher's #728ecb)
                    var baseLightened = EnsureLightness(color, 0.6, true);
                    accentBrush       = new SolidColorBrush(baseLightened);
                    accentHoverBrush  = new SolidColorBrush(GetLightColor(baseLightened, 0.1));
                    accentPressedBrush= new SolidColorBrush(GetDarkColor(baseLightened, 0.1));
                    accentDisabledBrush= new SolidColorBrush(GetDarkColor(baseLightened, 0.3));
                }

                // Primary fill
                SetResource(resources, "AccentFillColorDefaultBrush",  accentBrush);
                SetResource(resources, "AccentFillColorSecondaryBrush",accentHoverBrush);
                SetResource(resources, "AccentFillColorTertiaryBrush", accentPressedBrush);
                SetResource(resources, "AccentFillColorDisabledBrush", accentDisabledBrush);

                // Determine text color based on theme (dark text for dark theme buttons, white text for light theme buttons)
                var buttonTextBrush = isLightTheme ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Colors.Black);

                // Buttons
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

                // CheckBox
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

                // ToggleSwitch
                SetResource(resources, "ToggleSwitchFillOn",              accentBrush);
                SetResource(resources, "ToggleSwitchFillOnPointerOver",   accentHoverBrush);
                SetResource(resources, "ToggleSwitchFillOnPressed",       accentPressedBrush);
                SetResource(resources, "ToggleSwitchStrokeOn",            accentBrush);
                SetResource(resources, "ToggleSwitchStrokeOnPointerOver", accentHoverBrush);
                SetResource(resources, "ToggleSwitchStrokeOnPressed",     accentPressedBrush);
                SetResource(resources, "ToggleSwitchKnobFillOn",          checkGlyphBrush);
                SetResource(resources, "ToggleSwitchKnobFillOnPointerOver",checkGlyphBrush);
                SetResource(resources, "ToggleSwitchKnobFillOnPressed",   checkGlyphBrush);

                // Slider
                SetResource(resources, "SliderTrackValueFill",            accentBrush);
                SetResource(resources, "SliderTrackValueFillPointerOver", accentHoverBrush);
                SetResource(resources, "SliderTrackValueFillPressed",     accentPressedBrush);
                SetResource(resources, "SliderThumbBackground",           accentBrush);
                SetResource(resources, "SliderThumbBackgroundPointerOver",accentHoverBrush);
                SetResource(resources, "SliderThumbBackgroundPressed",    accentPressedBrush);

                // TextBox
                // SetResource(resources, "TextControlBorderBrushFocused",   accentBrush); // Disabled to keep default focus visual
                SetResource(resources, "TextControlSelectionHighlightColor", color);

                // ComboBox selection pill (vertical indicator bar)
                SetResource(resources, "ComboBoxItemPillFillBrush",                   accentBrush);
                SetResource(resources, "ComboBoxItemPillFillBrushPointerOver",        accentHoverBrush);
                SetResource(resources, "ComboBoxItemPillFillBrushPressed",            accentPressedBrush);
                SetResource(resources, "ComboBoxItemPillFillBrushDisabled",           accentDisabledBrush);
                SetResource(resources, "ComboBoxItemPillFillBrushSelected",           accentBrush);
                SetResource(resources, "ComboBoxItemPillFillBrushSelectedPointerOver",accentHoverBrush);
                SetResource(resources, "ComboBoxItemPillFillBrushSelectedPressed",    accentPressedBrush);
                
                // Fallbacks for older WinUI versions or different control templates
                SetResource(resources, "ComboBoxItemBorderBrushChecked",              accentBrush);
                SetResource(resources, "ComboBoxItemBorderBrushCheckedPointerOver",   accentHoverBrush);
                SetResource(resources, "ComboBoxItemBorderBrushCheckedPressed",       accentPressedBrush);
                
                // WinUI 3 specific ComboBox selection indicator keys
                SetResource(resources, "ComboBoxItemSelectedPointerOverBackground",   accentBrush);
                SetResource(resources, "ComboBoxItemSelectedPressedBackground",       accentPressedBrush);
                SetResource(resources, "ComboBoxItemSelectedBackground",              accentBrush);
                SetResource(resources, "ComboBoxItemRevealBorderBrushSelected",       accentBrush);
                SetResource(resources, "ComboBoxItemRevealBorderBrushSelectedPointerOver", accentHoverBrush);
                SetResource(resources, "ComboBoxItemRevealBorderBrushSelectedPressed", accentPressedBrush);

                // NavigationView selection pill
                SetResource(resources, "NavigationViewSelectionIndicatorForeground",  accentBrush);

                // ListView selection indicator
                SetResource(resources, "ListViewItemSelectionIndicatorBrush",         accentBrush);
                SetResource(resources, "ListViewItemSelectionIndicatorPointerOverBrush", accentHoverBrush);
                SetResource(resources, "ListViewItemSelectionIndicatorPressedBrush",  accentPressedBrush);

                // Segmented Control (Community Toolkit)
                SetResource(resources, "SegmentedItemSelectedBackgroundThemeBrush",            accentBrush);
                SetResource(resources, "SegmentedItemSelectedBackgroundPointerOverThemeBrush", accentHoverBrush);
                SetResource(resources, "SegmentedItemSelectedBackgroundPressedThemeBrush",     accentPressedBrush);
                SetResource(resources, "SegmentedItemSelectedForegroundThemeBrush",            buttonTextBrush);
                SetResource(resources, "SegmentedItemSelectedForegroundPointerOverThemeBrush", buttonTextBrush);
                SetResource(resources, "SegmentedItemSelectedForegroundPressedThemeBrush",     buttonTextBrush);
                
                // Segmented Control - Additional keys for newer toolkit versions or specific styles
                // Some versions use these keys for the "pill" indicator
                SetResource(resources, "SegmentedItemSelectionIndicatorBrush",                 accentBrush);
                SetResource(resources, "SegmentedItemSelectionIndicatorPointerOverBrush",      accentHoverBrush);
                SetResource(resources, "SegmentedItemSelectionIndicatorPressedBrush",          accentPressedBrush);
                SetResource(resources, "SegmentedItemSelectionIndicatorDisabledBrush",         accentDisabledBrush);
                
                // For Dark Mode specifically, ensures contrast isn't lost if the control ignores the above
                if (!isLightTheme)
                {
                    // Dark theme specific overrides if needed
                    SetResource(resources, "SegmentedItemForegroundSelected", buttonTextBrush);
                    SetResource(resources, "SegmentedItemForegroundSelectedPointerOver", buttonTextBrush);
                    SetResource(resources, "SegmentedItemForegroundSelectedPressed", buttonTextBrush);
                }

                // Progress controls
                SetResource(resources, "ProgressRingForegroundThemeBrush",    accentBrush);
                SetResource(resources, "ProgressBarForeground",               accentBrush);
                SetResource(resources, "ProgressBarIndeterminateForeground",  accentBrush);

                // Hyperlinks — must be in theme dict to override WinUI defaults
                SetResource(resources, "HyperlinkForeground",            accentBrush);
                SetResource(resources, "HyperlinkForegroundPointerOver", accentHoverBrush);
                SetResource(resources, "HyperlinkForegroundPressed",     accentPressedBrush);
                
                // HyperlinkButton
                SetResource(resources, "HyperlinkButtonForeground",             accentBrush);
                SetResource(resources, "HyperlinkButtonForegroundPointerOver",  accentHoverBrush);
                SetResource(resources, "HyperlinkButtonForegroundPressed",      accentPressedBrush);
                SetResource(resources, "HyperlinkButtonForegroundDisabled",     accentDisabledBrush);

                // System control highlight brushes (used internally by many controls)
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

                // Refresh controls by briefly toggling theme (guarded by timestamp so ActualThemeChanged won't loop)
                _lastRefreshTime = DateTime.Now;
                var currentTheme = rootElement.RequestedTheme;
                
                // Force a full cycle to ensure all bindings update
                rootElement.RequestedTheme = ElementTheme.Dark;
                rootElement.RequestedTheme = ElementTheme.Light;
                rootElement.RequestedTheme = currentTheme;
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
        // Deprecated - now done inline in ApplyAccentColor
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
            // Fallback to a default blue
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

    // Collapse Launcher's color manipulation methods
    private Color SetSaturation(Color color, double saturation)
    {
        // Convert RGB to HSL
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

        // Apply saturation multiplier
        s = Math.Min(1.0, s * saturation);

        // Convert HSL back to RGB
        return HslToRgb(h, s, l, color.A);
    }

    public void ApplyToContentDialog(Microsoft.UI.Xaml.Controls.ContentDialog dialog)
    {
        if (dialog == null) return;

        // Force the dialog to use the current app theme
        if (_themeSelectorService != null)
        {
            dialog.RequestedTheme = _themeSelectorService.Theme;
        }

        var resources = Application.Current.Resources;
        
        // Apply accent button resources directly to the dialog to bypass popup caching issues
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

    private Color EnsureLightness(Color color, double targetLightness, bool isDarkTheme)
    {
        // Get HSL values
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

        // Adjust lightness
        if (isDarkTheme)
        {
            // For dark theme, ensure the color is light enough
            l = Math.Max(l, targetLightness);
        }
        else
        {
            // For light theme, ensure the color is dark enough
            l = Math.Min(l, targetLightness);
        }

        return HslToRgb(h, s, l, color.A);
    }

    private Color GetLightColor(Color color, double amount)
    {
        // Get HSL values
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

        // Increase lightness
        l = Math.Min(1.0, l + amount);

        return HslToRgb(h, s, l, color.A);
    }

    private Color GetDarkColor(Color color, double amount)
    {
        // Get HSL values
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

        // Decrease lightness
        l = Math.Max(0.0, l - amount);

        return HslToRgb(h, s, l, color.A);
    }

    private Color HslToRgb(double h, double s, double l, byte alpha)
    {
        double r, g, b;

        if (s == 0)
        {
            r = g = b = l; // Achromatic
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
            // Attempt 1: Standard GDI+ Bitmap (fastest for BMP/PNG/JPG)
            // We read to memory first to avoid file locking issues
            var bytes = await File.ReadAllBytesAsync(path);
            using var ms = new MemoryStream(bytes);
            
            // This constructor throws ArgumentException if the format is invalid (e.g. WEBP on older GDI+)
            // We clone it to detach from the stream so we can dispose the stream
            using var tempBitmap = new Drawing.Bitmap(ms);
            return new Drawing.Bitmap(tempBitmap);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is System.Runtime.InteropServices.ExternalException)
        {
            // Attempt 2: WinRT WIC Decoder (supports WEBP, HEIF, etc. on Windows 10/11)
            System.Diagnostics.Debug.WriteLine($"GDI+ load failed for {path} ({ex.Message}). Trying WinRT fallback...");
            
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var stream = await file.OpenAsync(FileAccessMode.Read);
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                
                // Get pixels as BGRA8 (standard 32-bit format)
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
                
                // Use row-by-row copy if stride doesn't match width * 4 (rare for 32bpp but possible)
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
                throw; // Rethrow original or fallback error? Probably original context matters more but both failed.
            }
        }
    }
}
