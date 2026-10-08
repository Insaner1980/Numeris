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
            if (root.ValueKind != JsonValueKind.Object) return "Upstream error body omitted";
            var message = TryGetString(root, "message")
                ?? TryGetNestedErrorMessage(root)
                ?? TryGetOAuthErrorMessage(root);

            return Sanitize(message);
        }
        catch (JsonException)
        {
            return "Upstream error body omitted";
        }
    }

    public static string Sanitize(Exception exception)
        => Sanitize(exception.Message) ?? exception.GetType().Name;

    public static string? Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var sanitized = SecretQueryRegex().Replace(message.Trim(), match =>
        {
            var name = Uri.UnescapeDataString(match.Groups[1].Value).Replace("_", "").Replace("-", "").ToLowerInvariant();
            return name is "key" or "apikey" or "accesstoken" or "refreshtoken" or "clientsecret" or "idtoken" or "token" or "secret" or "password"
                ? $"{match.Groups[1].Value}=<redacted>" : match.Value;
        });
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

    private static string? TryGetNestedErrorMessage(JsonElement root)
        => root.TryGetProperty("error", out var error)
            ? TryGetString(error, "message") ?? TryGetString(error, "error_description")
            : null;

    private static string? TryGetOAuthErrorMessage(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var code = error.GetString();
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var description = TryGetString(root, "error_description");
        var baseMessage = string.IsNullOrWhiteSpace(description)
            ? code
            : $"{code}: {description}";

        return code switch
        {
            "invalid_grant" => $"{baseMessage}. Connect Google account again.",
            "invalid_client" => $"{baseMessage}. Check Google OAuth client ID and client secret.",
            "deleted_client" => $"{baseMessage}. Restore or recreate the Google OAuth client.",
            _ => baseMessage,
        };
    }

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9_%])([A-Za-z0-9_%+-]+)=([^&\s]+)")]
    private static partial Regex SecretQueryRegex();

    [GeneratedRegex(@"(?i)(Bearer\s+)[A-Za-z0-9._~+/=-]+")]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
