using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;


namespace MyTelU_Launcher.Helpers;
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is bool flag && flag) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return (value is Visibility visibility && visibility == Visibility.Visible);
        }
    }
