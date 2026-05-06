using System;
using System.Globalization;
using System.Threading.Tasks;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database.Repositories;
using Numeris.Services.Secrets;

namespace Numeris.Services.Sync;

public sealed class CloudflareSyncService
{
    private readonly CloudflareGraphqlClient _client;
    private readonly CloudflareRepository _cloudflareRepo;
    private readonly CredentialVault _vault;
    private readonly ConnectionsRepository _connectionsRepo;

    public CloudflareSyncService(CloudflareGraphqlClient client, CloudflareRepository cloudflareRepo, CredentialVault vault, ConnectionsRepository connectionsRepo)
    {
        _client = client;
        _cloudflareRepo = cloudflareRepo;
        _vault = vault;
        _connectionsRepo = connectionsRepo;
    }

    public async Task<SyncResult> SyncDomainAsync(string domain, string zoneId, int days)
    {
        days = Math.Clamp(days, 1, 3650);
        var apiToken = _vault.GetCloudflareToken(domain)
            ?? throw new InvalidOperationException($"No API token saved for {domain} — re-add it on Sources page");

        var endDate = DateOnly.FromDateTime(DateTime.Today);
        var startDate = endDate.AddDays(-(days - 1));
        var startStr = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endStr = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var traffic = await _client.FetchDailyTrafficAsync(apiToken, zoneId, startStr, endStr).ConfigureAwait(false);

        var nowStr = _connectionsRepo.FormatNow();
        var records = await _cloudflareRepo.UpsertTrafficAsync(domain, traffic, nowStr).ConfigureAwait(false);

        await _connectionsRepo.UpdateCloudflareLastSyncAsync(domain, nowStr, "connected").ConfigureAwait(false);

        return new SyncResult
        {
            Domain = domain,
            DaysSynced = traffic.Daily.Count,
            RecordsUpserted = records,
        };
    }

    public async Task<ConnectionTestResult> TestDomainAsync(string domain, string zoneId)
    {
        domain = domain.Trim().ToLowerInvariant();
        zoneId = zoneId.Trim();
        var apiToken = _vault.GetCloudflareToken(domain);
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            return new ConnectionTestResult { Ok = false, Message = $"No API token saved for {domain}" };
        }

        try
        {
            await _client.ValidateZoneAsync(apiToken, zoneId, domain).ConfigureAwait(false);

            var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var traffic = await _client.FetchDailyTrafficAsync(apiToken, zoneId, today, today).ConfigureAwait(false);
            return new ConnectionTestResult
            {
                Ok = true,
                Message = traffic.Daily.Count == 0
                    ? $"Cloudflare token works for {domain}, but today's analytics dataset is empty"
                    : $"Cloudflare token works for {domain}; GraphQL returned {traffic.Daily.Count} day row(s)",
            };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult { Ok = false, Message = ApiErrorMessage.Sanitize(ex) };
        }
    }
}
