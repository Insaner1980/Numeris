using System;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Dapper;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Database.Repositories;

internal static class RawPolicyRegressionTests
{
    public static void OmitsMalformedSecretBearingRawJson()
    {
        foreach (var input in new[] { "{\"api_key\":\"synthetic-secret\",", "{\"nested\":{\"password\":\"synthetic-secret\"", "password=synthetic-secret" })
        {
            var stored = RawJsonStoragePolicy.TrimRawJson(input);
            Require(!stored.Contains("synthetic-secret", StringComparison.Ordinal), "Malformed JSON must not retain unknown secret text");
            using var parsed = JsonDocument.Parse(stored);
        }
    }

    public static void RedactsEncodedAndRepeatedSecretQueryNames()
    {
        foreach (var name in new[] { "key", "api_key", "access-token", "refresh_token", "id_token", "token", "client-secret", "secret", "password", "authorization", "code", "%63ode", "%61uthorization" })
        {
            var url = $"https://test.example/?{name}=synthetic-secret&q=public";
            Require(!ApiErrorMessage.Sanitize(url)!.Contains("synthetic-secret", StringComparison.Ordinal),
                "UI errors must hide every recognized secret query name");
            var raw = RawJsonStoragePolicy.TrimRawJson(JsonSerializer.Serialize(new { url }));
            Require(!raw.Contains("synthetic-secret", StringComparison.Ordinal) && raw.Contains("q=public", StringComparison.Ordinal),
                "Stored URLs must use the same secret query policy and preserve ordinary query values");
            try { Numeris.Helpers.SiteIdentity.NormalizeHttpsPageUrl(url); }
            catch (ArgumentException) { continue; }
            throw new InvalidOperationException("Page targets must reject the same secret query names");
        }
        var input = JsonSerializer.Serialize(new { url = "https://test.example/?api%5Fkey=synthetic-first&%61ccess_token=synthetic-second&key=synthetic-third&q=public" });
        var stored = RawJsonStoragePolicy.TrimRawJson(input);
        Require(!stored.Contains("synthetic-", StringComparison.Ordinal) && stored.Contains("q=public", StringComparison.Ordinal),
            "Known encoded and repeated keys must be redacted without dropping ordinary queries");
        var message = ApiErrorMessage.FromBody(JsonSerializer.Serialize(new { message = "https://test.example/?api%5Fkey=synthetic-first&%61ccess_token=synthetic-second&q=public" }));
        Require(message is not null && !message.Contains("synthetic-", StringComparison.Ordinal) && message.Contains("q=public", StringComparison.Ordinal),
            "Displayed errors must redact the same encoded credential names");
    }

    public static void NonObjectErrorBodiesPreserveHttpClassification()
    {
        foreach (var body in new[] { "[]", "null", "4", "\"synthetic-body\"", "<html>synthetic-body</html>" })
            foreach (var status in new[] { HttpStatusCode.NotFound, (HttpStatusCode)429, HttpStatusCode.InternalServerError })
            {
                var exception = new ApiRequestException("synthetic-provider", "synthetic-operation", status, ApiErrorMessage.FromBody(body));
                Require(exception.StatusCode == status && !exception.Message.Contains("synthetic-body", StringComparison.Ordinal),
                    "Unrecognized error bodies must be omitted while retaining status");
            }
    }

    public static void UptimePersistenceSanitizesErrorText()
    {
        using var database = (SqliteDatabase)typeof(DatabaseReviewRegressionTests)
            .GetMethod("CreateDatabase", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        new HealthRepository(database).RecordUptimeProbeAsync(new UptimeProbeResult
        {
            Domain = "test.example",
            Status = "down",
            ErrorMessage = "request ?api_key=synthetic-key Authorization: Bearer synthetic-bearer",
        }).GetAwaiter().GetResult();
        var stored = database.ReadAsync(c => c.ExecuteScalar<string>("SELECT error_message FROM uptime_checks")).GetAwaiter().GetResult();
        Require(stored is not null && !stored.Contains("synthetic-", StringComparison.Ordinal), "Uptime errors must be sanitized before persistence");
    }

    public static void TraversalAndBoundsPreserveValidAnalytics()
    {
        var raw = """{"count":5,"nested":[[{"API-Key":"synthetic-key","valid":true}],"Bearer synthetic-bearer",null]}""";
        var stored = RawJsonStoragePolicy.TrimRawJson(raw);
        using var parsed = JsonDocument.Parse(stored);
        Require(!stored.Contains("synthetic-", StringComparison.Ordinal) && parsed.RootElement.GetProperty("count").GetInt32() == 5,
            "Recursive traversal must preserve ordinary analytics");
        foreach (var threshold in new[] { RawJsonStoragePolicy.MaxRawJsonChars, RawJsonStoragePolicy.MaxDetailsJsonChars })
            foreach (var delta in new[] { -1, 0, 1 })
            {
                var input = JsonSerializer.Serialize(new { text = new string('a', threshold - 11 + delta) });
                Require(input.Length == threshold + delta, "Fixture must exercise the exact character threshold");
                var output = threshold == RawJsonStoragePolicy.MaxRawJsonChars ? RawJsonStoragePolicy.TrimRawJson(input) : RawJsonStoragePolicy.TrimDetailsJson(input)!;
                using var wrapper = JsonDocument.Parse(output);
                Require(wrapper.RootElement.TryGetProperty("truncated", out _) == (delta > 0), "Only above-threshold input must be truncated");
            }
        var escapedInput = JsonSerializer.Serialize(new { text = string.Concat(System.Linq.Enumerable.Repeat("\"\\\n\u2603", 60000)) });
        using var escapedWrapper = JsonDocument.Parse(RawJsonStoragePolicy.TrimRawJson(escapedInput));
        Require(escapedWrapper.RootElement.GetProperty("truncated").GetBoolean()
            && escapedWrapper.RootElement.GetProperty("prefix").ValueKind == JsonValueKind.String,
            "Escaping-heavy truncated data must remain a valid JSON wrapper");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
