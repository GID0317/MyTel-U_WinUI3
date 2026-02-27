using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;


namespace MyTelU_Launcher.Helpers;
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var flag = value is bool b && b;

            // Support ConverterParameter="Invert" to negate the boolean
            if (parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            {
                flag = !flag;
            }

            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return (value is Visibility visibility && visibility == Visibility.Visible);
        }
    }
