using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Numeris.Services.Auth;

internal static class OAuthRegressionTests
{
    public static void AcceptsValidCallback()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var receive = typeof(GoogleOAuthFlow).GetMethod("ReceiveCallbackAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            var pending = (Task<string>)receive.Invoke(null, new object[] { listener, "test-state", cancellation.Token })!;
            using var peer = new TcpClient();
            peer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            peer.GetStream().Write(Encoding.ASCII.GetBytes($"GET /oauth2callback?code=test-code&state=test-state HTTP/1.1\r\nHost: localhost:{((IPEndPoint)listener.LocalEndpoint).Port}\r\n\r\n"));
            Require(pending.GetAwaiter().GetResult() == "test-code", "A valid callback must return the authorization code for exchange");
            using var reader = new System.IO.StreamReader(peer.GetStream());
            var response = reader.ReadToEnd();
            Require(response.StartsWith("HTTP/1.1 200 OK", StringComparison.Ordinal)
                && response.Contains("Authorization response received", StringComparison.Ordinal)
                && !response.Contains("Authentication complete", StringComparison.Ordinal),
                "The browser must acknowledge the callback without claiming a completed token exchange");
        }
        finally { listener.Stop(); }
    }

    public static void TimesOutWithIncompleteCallback()
    {
        using var peer = new TcpClient();
        var flow = new GoogleOAuthFlow(new GoogleOAuthClient());
        var pending = flow.AuthorizeAsync("test-client", "test-secret", uri =>
        {
            var redirect = new Uri(HttpUtility.ParseQueryString(uri.Query)["redirect_uri"]!);
            peer.Connect(IPAddress.Loopback, redirect.Port);
            peer.GetStream().Write(Encoding.ASCII.GetBytes("GET /oauth2callback"));
            return Task.CompletedTask;
        }, OAuthRegressionTestsInputs.Vector1, TimeSpan.FromMilliseconds(200));
        try
        {
            Require(Task.WhenAny(pending, Task.Delay(2000)).GetAwaiter().GetResult() == pending,
                "An accepted but incomplete callback must not bypass the authorization timeout");
            try { pending.GetAwaiter().GetResult(); }
            catch (TimeoutException) { return; }
            throw new InvalidOperationException("Expired authorization must report a timeout");
        }
        finally
        {
            peer.Dispose();
            try { pending.GetAwaiter().GetResult(); } catch { }
        }
    }

    public static void TimesOutWithoutCallback()
    {
        var flow = new GoogleOAuthFlow(new GoogleOAuthClient());
        var pending = flow.AuthorizeAsync("test-client", "test-secret", _ => Task.CompletedTask, OAuthRegressionTestsInputs.Vector1, TimeSpan.FromMilliseconds(100));
        Require(Task.WhenAny(pending, Task.Delay(2000)).GetAwaiter().GetResult() == pending,
            "Authorization must expire when the browser never returns");
        try { pending.GetAwaiter().GetResult(); }
        catch (TimeoutException) { return; }
        throw new InvalidOperationException("Missing callback must report a timeout");
    }

    public static void RejectsDeniedAndMismatchedCallbacks()
    {
        foreach (var denied in new[] { true, false })
        {
            using var peer = new TcpClient();
            var flow = new GoogleOAuthFlow(new GoogleOAuthClient());
            var pending = flow.AuthorizeAsync("test-client", "test-secret", uri =>
            {
                var query = HttpUtility.ParseQueryString(uri.Query);
                var redirect = new Uri(query["redirect_uri"]!);
                var path = denied ? "?error=access_denied&state=" + HttpUtility.UrlEncode(query["state"]) : "?code=test-code&state=wrong-state";
                peer.Connect(IPAddress.Loopback, redirect.Port);
                peer.GetStream().Write(Encoding.ASCII.GetBytes($"GET {redirect.AbsolutePath}{path} HTTP/1.1\r\nHost: localhost:{redirect.Port}\r\n\r\n"));
                return Task.CompletedTask;
            }, OAuthRegressionTestsInputs.Vector1, TimeSpan.FromSeconds(2));
            try
            {
                pending.GetAwaiter().GetResult();
                throw new InvalidOperationException("Rejected callback must not authorize the app");
            }
            catch (InvalidOperationException ex)
            {
                Require(ex.Message.Contains(denied ? "denied" : "state mismatch", StringComparison.OrdinalIgnoreCase),
                    "Callback rejection must identify denial or state mismatch");
            }
            using var reader = new System.IO.StreamReader(peer.GetStream());
            Require(!reader.ReadToEnd().Contains("Authentication complete", StringComparison.Ordinal),
                "The browser must not claim successful authentication for rejected callbacks");
        }
    }

    public static void ValidatesTokenResponses()
    {
        var parse = typeof(GoogleOAuthClient).GetMethod("ParseTokenResponseAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var valid = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"test-access\",\"refresh_token\":\"test-refresh\"}") };
        var tokens = ((Task<OAuthTokens>)parse.Invoke(null, new object[] { valid })!).GetAwaiter().GetResult();
        Require(tokens.AccessToken == "test-access" && tokens.RefreshToken == "test-refresh", "Token responses must preserve both token fields");
        foreach (var body in new[]
        {
            "{\"access_token\":\"test-access\"}",
            "{\"access_token\":\"test-access\",\"refresh_token\":null}",
            "{\"access_token\":\"test-access\",\"refresh_token\":\"\"}",
            "{\"access_token\":\"test-access\",\"refresh_token\":\" \\t\\r\\n\"}",
        })
        {
            using var optional = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            var optionalTokens = ((Task<OAuthTokens>)parse.Invoke(null, new object[] { optional })!).GetAwaiter().GetResult();
            Require(optionalTokens.AccessToken == "test-access" && optionalTokens.RefreshToken is null,
                "Absent or blank refresh tokens must not replace an existing credential");
        }
        using var invalid = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        try
        {
            ((Task<OAuthTokens>)parse.Invoke(null, new object[] { invalid })!).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("A successful HTTP status without an access token must not count as authorization");
    }

    public static void BuildsReadOnlyAuthorizationRequest()
    {
        var query = HttpUtility.ParseQueryString(new Uri(GoogleOAuthClient.BuildAuthUrl("test client", "http://127.0.0.1:12345/oauth2callback", "test state", OAuthRegressionTestsInputs.Vector2)).Query);
        Require(query["client_id"] == "test client" && query["redirect_uri"] == "http://127.0.0.1:12345/oauth2callback"
            && query["state"] == "test state" && query["scope"] == "scope.readonly" && query["access_type"] == "offline"
            && query["prompt"] == "consent" && query["response_type"] == "code", "Authorization must retain the callback, state and requested scope");
    }

    public static void BrowserFailureIsObservedAndListenerIsReleased()
    {
        var port = 0;
        var flow = new GoogleOAuthFlow(new GoogleOAuthClient());
        var pending = flow.AuthorizeAsync("test-client", "test-secret", async uri =>
        {
            port = new Uri(HttpUtility.ParseQueryString(uri.Query)["redirect_uri"]!).Port;
            await Task.Yield();
            throw new InvalidOperationException("Synthetic browser launch failure");
        }, OAuthRegressionTestsInputs.Vector1, TimeSpan.FromSeconds(2));
        try { pending.GetAwaiter().GetResult(); }
        catch (InvalidOperationException ex)
        {
            Require(ex.Message == "Synthetic browser launch failure", "Async browser failure must reach the caller");
            var replacement = new TcpListener(IPAddress.Loopback, port);
            try { replacement.Start(); }
            finally { replacement.Stop(); }
            return;
        }
        throw new InvalidOperationException("Browser failure was ignored");
    }

    public static void IgnoresUnrelatedRequestsBeforeValidCallback()
    {
        foreach (var unrelated in new[] { "GET /favicon.ico", "POST /oauth2callback?code=wrong-code&state=test-state", "GET /oauth2callback?code=wrong-code&state=test-state" })
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var receive = typeof(GoogleOAuthFlow).GetMethod("ReceiveCallbackAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
                var pending = (Task<string>)receive.Invoke(null, new object[] { listener, "test-state", cancellation.Token })!;
                using (var probe = new TcpClient())
                {
                    probe.Connect(IPAddress.Loopback, port);
                    var host = unrelated.StartsWith("GET /oauth2callback", StringComparison.Ordinal) ? "wrong.example" : $"127.0.0.1:{port}";
                    probe.GetStream().Write(Encoding.ASCII.GetBytes($"{unrelated} HTTP/1.1\r\nHost: {host}\r\n\r\n"));
                    using var reader = new System.IO.StreamReader(probe.GetStream());
                    Require(reader.ReadToEnd().StartsWith("HTTP/1.1 400", StringComparison.Ordinal), "Unrelated requests must be rejected safely");
                }
                Require(!pending.IsCompleted, "An unrelated request must leave the real callback available");
                using var peer = new TcpClient();
                peer.Connect(IPAddress.Loopback, port);
                peer.GetStream().Write(Encoding.ASCII.GetBytes($"GET /oauth2callback?code=test-code&state=test-state HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n\r\n"));
                Require(pending.GetAwaiter().GetResult() == "test-code", "The following valid callback must complete");
            }
            finally { listener.Stop(); }
        }
    }

    public static void RejectsBlankOrAmbiguousCallbackValues()
    {
        foreach (var query in new[] { "code=%20&state=test-state", "code=a&code=b&state=test-state", "code=test-code&state=test-state&state=wrong", "code=test-code", "code=test-code&state=test-state&error=access_denied&error_description=synthetic-secret" })
            ExpectRejectedRequest($"GET /oauth2callback?{query} HTTP/1.1\r\n", "");
    }

    public static void RejectsMalformedAndOversizedCallbackRequests()
    {
        ExpectBrokenRequestRecovery("GET /oauth2callback?code=test-code&state=test-state INVALID\r\n", "");
        ExpectBrokenRequestRecovery("GET /oauth2callback?code=test-code&state=test-state&padding=" + new string('a', 9000) + " HTTP/1.1\r\n", "");
        var headers = new StringBuilder();
        for (var i = 0; i < 110; i++) headers.Append("X-Test: a\r\n");
        ExpectBrokenRequestRecovery("GET /oauth2callback?code=test-code&state=test-state HTTP/1.1\r\n", headers.ToString());
        ExpectBrokenRequestRecovery("GET /oauth2callback?code=test-code&state=test-state HTTP/1.1\r\n", "X-Test: " + new string('a', 9000) + "\r\n");
        ExpectBrokenRequestRecovery("GET /oauth2callback", "", incomplete: true);
    }

    private static void ExpectBrokenRequestRecovery(string requestLine, string headers, bool incomplete = false)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var receive = typeof(GoogleOAuthFlow).GetMethod("ReceiveCallbackAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            var pending = (Task<string>)receive.Invoke(null, new object[] { listener, "test-state", cancellation.Token })!;
            using var probe = new TcpClient();
            probe.Connect(IPAddress.Loopback, port);
            var request = incomplete ? requestLine : requestLine + $"Host: 127.0.0.1:{port}\r\n" + headers + "\r\n";
            probe.GetStream().Write(Encoding.ASCII.GetBytes(request));
            using var peer = new TcpClient();
            peer.Connect(IPAddress.Loopback, port);
            peer.GetStream().Write(Encoding.ASCII.GetBytes($"GET /oauth2callback?code=following-code&state=test-state HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n\r\n"));
            Require(pending.GetAwaiter().GetResult() == "following-code",
                "A malformed, oversized or stalled connection must leave the following valid callback available");
        }
        finally { listener.Stop(); }
    }

    private static void ExpectRejectedRequest(string requestLine, string headers)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var receive = typeof(GoogleOAuthFlow).GetMethod("ReceiveCallbackAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            var pending = (Task<string>)receive.Invoke(null, new object[] { listener, "test-state", cancellation.Token })!;
            using var peer = new TcpClient();
            peer.Connect(IPAddress.Loopback, port);
            peer.GetStream().Write(Encoding.ASCII.GetBytes(requestLine + $"Host: 127.0.0.1:{port}\r\n" + headers + "\r\n"));
            try { pending.GetAwaiter().GetResult(); }
            catch (InvalidOperationException ex)
            {
                Require(!ex.Message.Contains("synthetic-secret", StringComparison.Ordinal), "Callback errors must not echo input");
                return;
            }
            throw new InvalidOperationException("Invalid callback was accepted");
        }
        finally { listener.Stop(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal static class OAuthRegressionTestsInputs
{
    internal static readonly string[] Vector1 = new[] { "test-scope" };
    internal static readonly string[] Vector2 = new[] { "scope.readonly", "scope.readonly" };
}
