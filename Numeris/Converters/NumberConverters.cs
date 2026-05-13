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
            return d.ToString("0.0", CultureInfo.InvariantCulture);
        }
        if (value is float f)
        {
            return f.ToString("0.0", CultureInfo.InvariantCulture);
        }
        return value?.ToString() ?? "—";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public sealed class IsoDateTimeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "—";
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
        {
            return dto.ToString("MMM d, yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        return text;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public sealed class StatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Unknown";
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.Replace('_', ' ').ToLowerInvariant());
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public sealed class EmptyFallbackConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var text = value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? "—" : text;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public sealed class MillisecondsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value switch
        {
            long ms => $"{ms} ms",
            int ms => $"{ms} ms",
            _ => "—",
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
