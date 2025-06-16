using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace MyTelU_Launcher.Helpers
{
    public class ThemeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value switch
            {
                ElementTheme.Light => "Light",
                ElementTheme.Dark => "Dark",
                ElementTheme.Default => "Use system setting",
                _ => "Unknown Theme"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                "Use system setting" => ElementTheme.Default,
                _ => ElementTheme.Default
            };
        }
    }
}
