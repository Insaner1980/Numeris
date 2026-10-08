using System;

namespace Numeris.Services.Api;

public sealed class PageSpeedRunException : InvalidOperationException
{
    public PageSpeedRunException() : base("PageSpeed Lighthouse run failed") { }
}
