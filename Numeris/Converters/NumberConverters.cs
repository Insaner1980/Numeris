using System;
using System.Globalization;
using Microsoft.UI.Xaml.Data;

namespace Numeris.Converters;

public sealed class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double d)
        {
            return $"{d * 100.0:0.0}%";
        }
        return "0.0%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public sealed class OneDecimalConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double d)
        {
            return d.ToString("0.0", CultureInfo.CurrentCulture);
        }
        if (value is float f)
        {
            return f.ToString("0.0", CultureInfo.CurrentCulture);
        }
        return value?.ToString() ?? "—";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
