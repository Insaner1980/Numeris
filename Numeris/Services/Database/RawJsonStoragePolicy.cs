using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.RegularExpressions;
using Numeris.Helpers;

namespace Numeris.Services.Database;

public static partial class RawJsonStoragePolicy
{
    public const int MaxRawJsonChars = 200_000;
    public const int MaxDetailsJsonChars = 40_000;
    public const int PageSpeedRunsToKeepPerUrlAndStrategy = 30;
    public const int BingRawRetentionDays = 180;
    public const string RedactedSecret = "[redacted]";

    public static string TrimRawJson(string? value)
        => BoundJson(RedactSecrets(value ?? ""), MaxRawJsonChars);

    public static string? TrimDetailsJson(string? value)
        => string.IsNullOrEmpty(value) ? value : BoundJson(RedactSecrets(value), MaxDetailsJsonChars);

    private static string RedactSecrets(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        try
        {
            var node = JsonNode.Parse(value);
            node = RedactNode(node);
            return node?.ToJsonString() ?? value;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { invalidJson = true, originalChars = value.Length });
        }
    }

    private static JsonNode? RedactNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToList())
                {
                    if (IsSensitivePropertyName(property.Key))
                    {
                        obj[property.Key] = RedactedSecret;
                    }
                    else
                    {
                        RedactChild(property.Value, redacted => obj[property.Key] = redacted);
                    }
                }
                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var index = i;
                    RedactChild(array[i], redacted => array[index] = redacted);
                }
                return array;
            case JsonValue valueNode when valueNode.TryGetValue<string>(out var text):
                return JsonValue.Create(RedactSecretText(text));
            default:
                return node;
        }
    }

    private static void RedactChild(JsonNode? node, Action<JsonNode?> replace)
    {
        if (node is JsonValue valueNode && valueNode.TryGetValue<string>(out var text))
        {
            replace(JsonValue.Create(RedactSecretText(text)));
            return;
        }

        RedactNode(node);
    }

    private static bool IsSensitivePropertyName(string name)
    {
        var normalized = name.Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("credential", StringComparison.Ordinal)
            || normalized.Contains("authorization", StringComparison.Ordinal)
            || normalized is "key"
            || normalized.EndsWith("apikey", StringComparison.Ordinal)
            || normalized.EndsWith("accesskey", StringComparison.Ordinal)
            || normalized.EndsWith("subscriptionkey", StringComparison.Ordinal);
    }

    private static string RedactSecretText(string value)
    {
        var redacted = QuerySecretRegex().Replace(value, match =>
        {
            return SecretQueryNames.IsSecret(match.Groups[2].Value)
                ? $"{match.Groups[1].Value}{match.Groups[2].Value}={RedactedSecret}" : match.Value;
        });
        return BearerSecretRegex().Replace(redacted, match => $"{match.Groups[1].Value}{RedactedSecret}");
    }

    private static string BoundJson(string value, int maxChars)
    {
        if (value.Length <= maxChars)
        {
            return value;
        }

        var prefixLength = Math.Max(0, maxChars - 512);
        return JsonSerializer.Serialize(new
        {
            truncated = true,
            originalChars = value.Length,
            prefix = value[..Math.Min(value.Length, prefixLength)],
        });
    }

    [GeneratedRegex(@"([?&])([^=&\s""']+)=([^&\s""']*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuerySecretRegex();

    [GeneratedRegex(@"(Bearer\s+)[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerSecretRegex();
}
