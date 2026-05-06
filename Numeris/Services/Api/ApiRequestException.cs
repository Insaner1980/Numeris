using System;
using System.Net;

namespace Numeris.Services.Api;

public sealed class ApiRequestException : InvalidOperationException
{
    public ApiRequestException(string provider, string operation, HttpStatusCode statusCode, string? upstreamMessage = null)
        : base(BuildMessage(provider, operation, statusCode, upstreamMessage))
    {
        Provider = provider;
        Operation = operation;
        StatusCode = statusCode;
        UpstreamMessage = upstreamMessage;
    }

    public string Provider { get; }
    public string Operation { get; }
    public HttpStatusCode StatusCode { get; }
    public string? UpstreamMessage { get; }

    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
    public bool IsRateLimited => (int)StatusCode == 429;
    public bool IsTransient => StatusCode == HttpStatusCode.RequestTimeout || (int)StatusCode >= 500;

    private static string BuildMessage(string provider, string operation, HttpStatusCode statusCode, string? upstreamMessage)
    {
        var message = $"{provider} {operation} returned HTTP {(int)statusCode} ({statusCode})";
        return string.IsNullOrWhiteSpace(upstreamMessage) ? message : $"{message}: {upstreamMessage}";
    }
}
