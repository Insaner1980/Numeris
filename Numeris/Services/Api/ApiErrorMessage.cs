using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Numeris.Services.Api;

public static partial class ApiErrorMessage
{
    private const int MaxLength = 300;

    public static string? FromBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var message = TryGetString(root, "message")
                ?? (root.TryGetProperty("error", out var error) ? TryGetString(error, "message") : null)
                ?? (root.TryGetProperty("error", out error) ? TryGetString(error, "error_description") : null);

            return Sanitize(message);
        }
        catch (JsonException)
        {
            return "Upstream error body omitted";
        }
    }

    public static string Sanitize(Exception exception)
        => Sanitize(exception.Message) ?? exception.GetType().Name;

    private static string? Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var sanitized = SecretQueryRegex().Replace(message.Trim(), "$1=<redacted>");
        sanitized = BearerRegex().Replace(sanitized, "$1<redacted>");
        sanitized = WhitespaceRegex().Replace(sanitized, " ");
        return sanitized.Length <= MaxLength ? sanitized : sanitized[..MaxLength];
    }

    private static string? TryGetString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    [GeneratedRegex(@"(?i)\b(key|apikey|api_key|access_token|refresh_token|client_secret)=([^&\s]+)")]
    private static partial Regex SecretQueryRegex();

    [GeneratedRegex(@"(?i)(Bearer\s+)[A-Za-z0-9._~+/=-]+")]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
