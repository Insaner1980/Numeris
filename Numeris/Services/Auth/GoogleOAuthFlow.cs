using System;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Numeris.Services.Auth;

public sealed class GoogleOAuthFlow
{
    private readonly GoogleOAuthClient _client;

    public GoogleOAuthFlow(GoogleOAuthClient client) => _client = client;

    public async Task<OAuthTokens> AuthorizeAsync(
        string clientId,
        string clientSecret,
        Func<Uri, Task> openBrowser,
        IReadOnlyList<string> scopes,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromMinutes(2);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = $"http://127.0.0.1:{port}/oauth2callback";
        var state = $"numeris-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}-{Guid.NewGuid():N}";

        try
        {
            var authUrl = GoogleOAuthClient.BuildAuthUrl(clientId, redirectUri, state, scopes);
            await openBrowser(new Uri(authUrl));

            using var cts = new CancellationTokenSource(timeout.Value);
            string code;
            try
            {
                code = await ReceiveCallbackAsync(listener, state, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new TimeoutException("Google authorization timed out. Press Connect Google account to try again.");
            }

            return await _client.ExchangeCodeAsync(clientId, clientSecret, code, redirectUri).ConfigureAwait(false);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<string> ReceiveCallbackAsync(TcpListener listener, string expectedState, CancellationToken cancellationToken)
    {
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        while (true)
        {
            using var tcpClient = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            using var stream = tcpClient.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectionTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            bool isCallback;
            string code;
            string? error;
            try
            {
                var requestLine = await ReadCallbackLineAsync(reader, connectionTimeout.Token).ConfigureAwait(false);
                var parts = ParseRequestLine(requestLine);
                var host = await ReadCallbackHostAsync(reader, connectionTimeout.Token).ConfigureAwait(false);
                var queryStart = parts[1].IndexOf('?');
                var path = queryStart >= 0 ? parts[1][..queryStart] : parts[1];
                isCallback = parts[0] == "GET" && path == "/oauth2callback"
                    && (string.Equals(host, $"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(host, $"localhost:{port}", StringComparison.OrdinalIgnoreCase));
                var parsed = HttpUtility.ParseQueryString(queryStart >= 0 ? parts[1][(queryStart + 1)..] : "");
                code = parsed["code"] ?? "";
                error = ValidateCallback(isCallback, parsed, expectedState, code);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                isCallback = false;
                code = "";
                error = "Malformed OAuth callback request";
            }

            var message = error is null ? "Authorization response received. Return to Numeris to finish." : "Authorization did not complete. Return to Numeris to try again.";
            var responseBody = $"<html><body style='font-family:sans-serif;text-align:center;padding-top:80px'><h1>Numeris</h1><p>{message}</p></body></html>";
            var status = error is null ? "200 OK" : "400 Bad Request";
            var responseBytes = Encoding.UTF8.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {Encoding.UTF8.GetByteCount(responseBody)}\r\nConnection: close\r\n\r\n" + responseBody);
            try
            {
                await stream.WriteAsync(responseBytes, connectionTimeout.Token).ConfigureAwait(false);
                await stream.FlushAsync(connectionTimeout.Token).ConfigureAwait(false);
            }
            catch (IOException) { }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }

            if (!isCallback) continue;
            if (error is not null) throw new InvalidOperationException(error);
            return code;
        }
    }

    private static string[] ParseRequestLine(string requestLine)
    {
        var parts = requestLine.Split(' ');
        if (parts.Length != 3 || parts[1].Length == 0 || (parts[2] != "HTTP/1.1" && parts[2] != "HTTP/1.0"))
            throw new InvalidOperationException("Malformed OAuth callback request");
        return parts;
    }

    private static string? ValidateCallback(bool isCallback, System.Collections.Specialized.NameValueCollection parsed, string expectedState, string code)
    {
        if (!isCallback) return "Unrelated OAuth callback request";
        if (parsed.GetValues("state")?.Length != 1 || parsed["state"] != expectedState)
            return "OAuth state mismatch — possible cross-site request forgery";
        if (!string.IsNullOrEmpty(parsed["error"])) return "Google authorization was denied or cancelled.";
        if (parsed.GetValues("code")?.Length != 1 || string.IsNullOrWhiteSpace(code))
            return "OAuth flow returned no unambiguous code";
        return null;
    }

    private static async Task<string?> ReadCallbackHostAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        string? host = null;
        var headerChars = 0;
        var headerCount = 0;
        while (true)
        {
            var line = await ReadCallbackLineAsync(reader, cancellationToken).ConfigureAwait(false);
            if (line.Length == 0) break;
            headerChars += line.Length;
            if (++headerCount > 100 || headerChars > 32768)
                throw new InvalidOperationException("OAuth callback headers are too large");
            var colon = line.IndexOf(':');
            if (colon <= 0) throw new InvalidOperationException("Malformed OAuth callback header");
            if (line[..colon].Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                if (host is not null) throw new InvalidOperationException("Ambiguous OAuth callback host");
                host = line[(colon + 1)..].Trim();
            }
        }
        return host;
    }

    private static async Task<string> ReadCallbackLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character.AsMemory(), cancellationToken).ConfigureAwait(false) != 0)
        {
            if (character[0] == '\n') return line.ToString().TrimEnd('\r');
            if (line.Length >= 8192) throw new InvalidOperationException("OAuth callback line is too large");
            line.Append(character[0]);
        }
        throw new InvalidOperationException("Incomplete OAuth callback request");
    }
}
