using System;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
        Action<Uri> openBrowser,
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
            var authUrl = _client.BuildAuthUrl(clientId, redirectUri, state, scopes);
            openBrowser(new Uri(authUrl));

            using var cts = new System.Threading.CancellationTokenSource(timeout.Value);
            using var registration = cts.Token.Register(listener.Stop);

            var (code, returnedState) = await ReceiveCallbackAsync(listener).ConfigureAwait(false);
            if (returnedState != state)
            {
                throw new InvalidOperationException("OAuth state mismatch — possible cross-site request forgery");
            }
            if (string.IsNullOrEmpty(code))
            {
                throw new InvalidOperationException("OAuth flow returned no code");
            }

            return await _client.ExchangeCodeAsync(clientId, clientSecret, code, redirectUri).ConfigureAwait(false);
        }
        finally
        {
            try { listener.Stop(); } catch { }
        }
    }

    private static async Task<(string Code, string State)> ReceiveCallbackAsync(TcpListener listener)
    {
        using var tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
        using var stream = tcpClient.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        var requestLine = await reader.ReadLineAsync().ConfigureAwait(false) ?? "";
        var parts = requestLine.Split(' ');
        if (parts.Length < 2)
        {
            throw new InvalidOperationException("Malformed OAuth callback request");
        }
        var path = parts[1];
        var queryStart = path.IndexOf('?');
        var query = queryStart >= 0 ? path[(queryStart + 1)..] : "";
        var parsed = HttpUtility.ParseQueryString(query);
        var code = parsed["code"] ?? "";
        var state = parsed["state"] ?? "";

        // drain headers
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false))) { }

        var responseBody = "<html><body style='font-family:sans-serif;text-align:center;padding-top:80px'><h1>Numeris</h1><p>Authentication complete — you can close this window.</p></body></html>";
        var responseBytes = Encoding.UTF8.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {Encoding.UTF8.GetByteCount(responseBody)}\r\nConnection: close\r\n\r\n" +
            responseBody);
        await stream.WriteAsync(responseBytes).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);

        return (code, state);
    }
}
