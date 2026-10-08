using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Numeris.Helpers;
using Numeris.Models;
using Numeris.Services.Api;
using Numeris.Services.Database;
using Numeris.Services.Insights;
using Numeris.Services.Performance;
using Numeris.ViewModels;
using Numeris.ViewModels.Sources;

if (args.Contains("--ui-coverage", StringComparer.Ordinal))
{
    Run("WinUI reports load using isolated services", NativeUiRegressionTests.Run);
    return;
}

if (args.Contains("--live-credential-vault", StringComparer.Ordinal))
{
    if (args.Length != 1)
    {
        Console.Error.WriteLine("--live-credential-vault must be used alone.");
        Environment.ExitCode = 1;
        return;
    }

    Run("Live vault saves replaces and deletes its temporary credential", CredentialVaultRegressionTests.LiveStoreSavesReplacesAndDeletesTemporaryCredential);
    return;
}

if (args.Contains("--credential-vault", StringComparer.Ordinal))
{
    if (args.Length != 1)
    {
        Console.Error.WriteLine("--credential-vault must be used alone.");
        Environment.ExitCode = 1;
        return;
    }

    // These tests use a synthetic credential reader and in-memory SQLite only.
    RunCredentialVaultTests();
    return;
}


    Run("Cloudflare saves replaces reloads and deletes credentials", SourcesPersistenceRegressionTests.CloudflareSavesReplacesAndDeletes);
    Run("Web Analytics saves replaces reloads and deletes mappings and credentials", SourcesPersistenceRegressionTests.WebAnalyticsSavesMappingsAndDeletes);
    Run("Search Console saves replaces reloads and deletes OAuth credentials", SourcesPersistenceRegressionTests.SearchConsoleSavesReplacesAndDeletes);
    Run("Performance saves replaces reloads and deletes keys and URLs", SourcesPersistenceRegressionTests.PerformanceSavesUrlsAndDeletes);
    Run("Bing saves replaces reloads and deletes keys and sites", SourcesPersistenceRegressionTests.BingSavesSitesAndDeletes);

Run("Settings restore invalid periods without losing valid preferences", SettingsRegressionTests.RestoresInvalidPeriodWithoutLosingOtherPreferences);
Run("Settings replacement failures preserve the previous file", SettingsRegressionTests.FailedReplacementPreservesPreviousSettings);
Run("Settings save failures preserve selection and report recovery", SettingsRegressionTests.SaveFailurePreservesSelectionAndReportsRecovery);
Run("Site identity rejects hyphens at DNS label boundaries", SiteIdentityRegressionTests.RejectsInvalidDnsLabels);
Run("Vault domain keys share site identity without credential access", SiteIdentityRegressionTests.VaultDomainKeysUseSiteIdentity);
Run("Page targets reject secrets and preserve encoded query keys", SiteIdentityRegressionTests.RejectsSecretTargetsAndPreservesPageKeys);
Run("Bing domain reports include configured path sites", SiteIdentityRegressionTests.BingDomainReportsIncludeConfiguredPathSites);
Run("Search device replacement rolls back both related tables", DatabaseReviewRegressionTests.SearchReplacementRollsBackBothDeviceTables);
Run("Search page-query replacement rolls back completely", DatabaseReviewRegressionTests.SearchPageQueriesRollBackReplacement);
Run("Failed schema upgrades resume at the completed version", DatabaseReviewRegressionTests.FailedUpgradeResumesAtCompletedVersion);
Run("Future and malformed schema versions preserve database contents", DatabaseReviewRegressionTests.RejectsFutureAndMalformedVersionsWithoutMutation);
Run("Future schema pragma is rejected before schema creation", DatabaseReviewRegressionTests.RejectsFuturePragmaWithoutCreatingSchema);
Run("A failed data clear preserves the complete previous state", DatabaseReviewRegressionTests.FailedClearPreservesCompleteData);
RunCredentialVaultTests();
Run("Legacy import skips incompatible schema shapes", LegacyImportRegressionTests.SkipsIncompatibleLegacySchemas);
Run("Legacy import skips malformed rows without losing valid sources", LegacyImportRegressionTests.SkipsMalformedRowsAndKeepsValidIndependentSources);
Run("Legacy account reads preserve source bytes without optional mappings", LegacyImportRegressionTests.ReadsLegacyAccountWithoutOptionalMappingsAndPreservesSource);
Run("Legacy site mappings retain reflection-populated metadata", LegacyImportRegressionTests.ReadsLegacySiteMappingMetadata);
Run("Legacy mock status normalization preserves native connected state", LegacyImportRegressionTests.NormalizesMockStatusAndPreservesConnected);
Run("Disposed reports do not publish pending local loads", LoadLifetimeRegressionTests.DisposedReportsDoNotPublishPendingLoads);
Run("Malformed raw JSON cannot retain secret text", RawPolicyRegressionTests.OmitsMalformedSecretBearingRawJson);
Run("Encoded query secret names are redacted", RawPolicyRegressionTests.RedactsEncodedAndRepeatedSecretQueryNames);
Run("Unrecognized error bodies preserve HTTP classification", RawPolicyRegressionTests.NonObjectErrorBodiesPreserveHttpClassification);
Run("Uptime error persistence sanitizes secrets", RawPolicyRegressionTests.UptimePersistenceSanitizesErrorText);
Run("Raw redaction preserves analytics and exact character thresholds", RawPolicyRegressionTests.TraversalAndBoundsPreserveValidAnalytics);
Run("Legacy import skips invalid domains and preserves valid neighboring rows", LegacyImportRegressionTests.SkipsInvalidDomainsWithoutDiscardingValidImports);

Run("OAuth expires while an accepted callback is incomplete", OAuthRegressionTests.TimesOutWithIncompleteCallback);
Run("OAuth accepts a valid callback before token exchange", OAuthRegressionTests.AcceptsValidCallback);
Run("OAuth expires when the browser does not return", OAuthRegressionTests.TimesOutWithoutCallback);
Run("OAuth rejects denied and mismatched callbacks without claiming success", OAuthRegressionTests.RejectsDeniedAndMismatchedCallbacks);
Run("OAuth requires an access token in a successful response", OAuthRegressionTests.ValidatesTokenResponses);
Run("Search metadata saves preserve sites only for the same client", SearchConsoleReviewRegressionTests.PreservesSitesOnSameClientMetadataSave);
Run("Search inspection rejects unrelated property URLs before HTTP", SearchConsoleReviewRegressionTests.RejectsInspectionUrlsOutsideThePropertyBeforeHttp);
Run("Search inspection accepts authorized property boundaries", SearchConsoleReviewRegressionTests.AcceptsInspectionPropertyMembershipBoundaries);
Run("Search sync preserves stored state without authorized property matches", SearchConsoleReviewRegressionTests.SyncPreservesStateWithoutAuthorizedMatches);
Run("Search HTTP dimensions and successful empty windows map correctly", SearchConsoleReviewRegressionTests.SyncMapsDimensionsAndReplacesSuccessfulEmptyWindows);
Run("Search inspection preserves partial results and reports progress", SearchConsoleReviewRegressionTests.InspectionPreservesPartialResultsAndReportsProgress);
Run("Search inspection stops after three initial failures", SearchConsoleReviewRegressionTests.InspectionStopsAfterThreeInitialFailures);
Run("OAuth HTTP failures preserve sync state and sanitize errors", SearchConsoleReviewRegressionTests.OAuthFailuresPreserveSyncStateAndSanitizeErrors);
Run("OAuth requests the configured read-only scope and offline access", OAuthRegressionTests.BuildsReadOnlyAuthorizationRequest);
Run("OAuth ignores unrelated requests before the actual callback", OAuthRegressionTests.IgnoresUnrelatedRequestsBeforeValidCallback);
Run("OAuth rejects blank and ambiguous callback values", OAuthRegressionTests.RejectsBlankOrAmbiguousCallbackValues);
Run("OAuth bounds and validates callback request lines and headers", OAuthRegressionTests.RejectsMalformedAndOversizedCallbackRequests);
Run("OAuth observes asynchronous browser failure and releases the listener", OAuthRegressionTests.BrowserFailureIsObservedAndListenerIsReleased);
Run("Shell report loads observe repository read failures", LoadLifetimeRegressionTests.ShellLoadsReportReadFailures);
Run("Superseded report loads do not publish stale data", LoadLifetimeRegressionTests.SupersededReportsDoNotPublishPendingLoads);

Run("Analytics removal preserves other sources and database operations", AnalyticsRemovalRegressionTests.RemovesLegacyDataAndPreservesOtherSources);
Run("Analytics removal clears navigation and falls back from saved page", () => AnalyticsRemovalRegressionTests.RemovesNavigationAndSettings(FindRepositoryRoot()));

Run("Configured Cloudflare domains appear before their first sync", DomainSelectionRegressionTests.IncludesConfiguredDomainsBeforeFirstSync);
Run("Domain selection follows saved and deleted Cloudflare connections", DomainSelectionRegressionTests.UpdatesDomainsAfterConnectionChanges);
Run("Domain selection and sync include sites without Cloudflare credentials", DomainSelectionRegressionTests.IncludesSitesWithoutCloudflareConnection);
Run("Domain discovery ignores disabled and deleted Bing metadata targets", DomainSelectionRegressionTests.IgnoresDisabledOrDeletedBingMetadataTargets);
Run("Web Analytics discovery includes standalone hostname sites", CloudflareRefreshRegressionTests.DiscoversStandaloneWebAnalyticsSites);
Run("Health sitemap refresh rolls back the complete result on failure", HealthRegressionTests.RollsBackFailedSitemapRefresh);
Run("Health retains rapid uptime samples", HealthRegressionTests.RetainsRapidUptimeSamples);
Run("Health response averages exclude missing timings", HealthRegressionTests.ExcludesMissingResponseTimesFromAverages);
Run("Sitemap rejects unsupported document shapes", HealthRegressionTests.RejectsUnsupportedSitemapShapes);
Run("Sitemap parses only membership locations", HealthRegressionTests.ParsesOnlySitemapMembershipLocations);
Run("Health discards old-domain sitemap action results", HealthRegressionTests.DiscardsSitemapActionResultsAfterDomainSwitch);
Run("Health sitemap summary follows the selected domain", HealthRegressionTests.UpdatesSitemapSummaryAfterDomainChange);
Run("Health publishes a large sitemap with one list update", HealthRegressionTests.PublishesLargeSitemapInOneUpdate);
Run("Health actions prevent overlapping checks", HealthRegressionTests.PreventsConcurrentChecks);
Run("Search indexing includes all sites and reports failed authorization", HealthRegressionTests.IncludesAllSitesInIndexing);
Run("Bing method failures retain diagnostics without claiming success", HealthRegressionTests.DoesNotCountBingFailuresAsSavedData);
Run("Cloudflare refetch replaces the complete breakdown date range", DailyBreakdownRegressionTests.ReplacesCloudflareBreakdownRange);
Run("Web Analytics breakdowns retain their actual dates", DailyBreakdownRegressionTests.PreservesWebAnalyticsCategoryDates);
Run("Web Analytics rollup rolls back completely on failure", DailyBreakdownRegressionTests.RollsBackFailedWebAnalyticsRollup);
Run("Web Analytics discovery saves mappings atomically", DailyBreakdownRegressionTests.RollsBackFailedWebAnalyticsDiscovery);
Run("Cloudflare rejects incomplete or undated Zone responses", CloudflareReviewRegressionTests.RejectsMalformedZoneResponses);
Run("Web Analytics rejects incomplete or undated RUM responses", CloudflareReviewRegressionTests.RejectsMalformedRumResponses);
Run("Cloudflare parsing preserves provider dates and distinct metrics", CloudflareReviewRegressionTests.PreservesProviderDatesAndDistinctMetrics);
Run("Cloudflare groups only actual 5xx responses as server errors", CloudflareReviewRegressionTests.GroupsOnlyActual5xxAsServerErrors);
Run("Web Analytics discovery reload includes manually saved mappings", CloudflareReviewRegressionTests.ReloadsAllSavedMappingsAfterDiscoveryUpsert);
Run("Cloudflare HTTP sync preserves data after malformed responses", CloudflareReviewRegressionTests.SyncsZoneHttpResponsesWithoutReplacingDataOnMalformedPayloads);
Run("Web Analytics HTTP sync preserves last sync after failures", CloudflareReviewRegressionTests.SyncsRumHttpResponsesWithoutAdvancingLastSyncOnFailure);
Run("Cloudflare saved HTTP tests remain read-only", CloudflareReviewRegressionTests.KeepsSavedZoneHttpTestsReadOnly);
Run("Cloudflare unsaved source test rejects a mismatched zone", CloudflareReviewRegressionTests.RejectsUnsavedMismatchedZoneThroughSourceTest);
Run("Web Analytics HTTP discovery preserves manual source mappings", CloudflareReviewRegressionTests.DiscoversSitesThroughHttpAndPreservesManualMappingsInSource);
Run("Daily breakdown migration preserves traffic and configured sites", DailyBreakdownRegressionTests.MigratesOnlyInvalidBreakdownCaches);
Run("Search Console aggregates weight position and CTR by traffic", DailyBreakdownRegressionTests.WeightsSearchMetricsByTraffic);
Run("Search Console reads zero and nonzero metrics together", DailyBreakdownRegressionTests.ReadsZeroAndNonzeroSearchMetricsTogether);
Run("Bing aggregates weight positions by traffic", DailyBreakdownRegressionTests.WeightsBingPositionsByTraffic);

foreach (var sourceType in new[]
{
    typeof(CloudflareSourceViewModel), typeof(WebAnalyticsSourceViewModel), typeof(SearchConsoleSourceViewModel),
    typeof(PerformanceSourceViewModel), typeof(BingSourceViewModel),
})
{
    Run($"{sourceType.Name} prevents concurrent actions", () => SourceActionRegressionTests.PreventsConcurrentActions(sourceType));
    Run($"{sourceType.Name} reports delete failures", () => SourceActionRegressionTests.ReportsDeleteFailure(sourceType));
}

foreach (var webAnalytics in new[] { false, true })
{
    var report = webAnalytics ? "Web Analytics" : "Cloudflare";
    Run($"{report} refresh reports missing configuration", () => CloudflareRefreshRegressionTests.ReportsMissingConnection(webAnalytics));
    Run($"{report} refresh exposes sync failures", () => CloudflareRefreshRegressionTests.ReportsSyncFailure(webAnalytics));
    Run($"{report} refresh prevents concurrent requests", () => CloudflareRefreshRegressionTests.PreventsConcurrentRefresh(webAnalytics));
}

Run("Overview summaries use exactly the selected days", OverviewRegressionTests.UsesExactlySelectedDays);
Run("Overview keeps missing data and changes missing", OverviewRegressionTests.DoesNotInventMissingTrafficOrChanges);
Run("Overview hides comparisons for incomplete periods", OverviewRegressionTests.HidesChangesForPartialAndNewData);
Run("Overview comparisons require every contributing site", OverviewRegressionTests.HidesComparisonsForComplementaryDomainCoverage);
Run("Insights preserve the sign of large growth ratios", InsightReviewRegressionTests.FormatsLargeGrowthWithoutReversingItsSign);
Run("Insights include per-domain Cloudflare freshness", InsightReviewRegressionTests.IncludesPerDomainCloudflareFreshness);
Run("Insights empty state describes rule evaluation only", InsightReviewRegressionTests.EmptyStateDoesNotCertifyAllConnectedSources);
Run("Insights include exact mobile score decline thresholds", InsightReviewRegressionTests.IncludesExactMobileScoreDropBoundary);
Run("Insights retain established thresholds and deterministic ordering", InsightReviewRegressionTests.VerifiesRuleThresholdsAndOrdering);
Run("Insights distinguish missing threat history from measured zero", InsightReviewRegressionTests.DistinguishesMissingThreatHistoryFromMeasuredZero);
Run("Insights do not invent zero Bing clicks", InsightReviewRegressionTests.DoesNotInventZeroBingClicks);
Run("Insights require observed page and query comparison rows", InsightReviewRegressionTests.RequiresObservedPageAndQueryComparisonRows);
Run("Insights exclude missing mobile score evidence", InsightReviewRegressionTests.KeepsMissingMobileEvidenceOutOfTheExplanation);
Run("Insights preserve availability for every aggregate", InsightReviewRegressionTests.PreservesMetricAvailabilityForAllAggregates);
Run("Insights do not compare disjoint site histories", InsightReviewRegressionTests.DoesNotCompareDisjointSiteHistories);
Run("Insights do not hide missing Bing clicks behind other sites", InsightReviewRegressionTests.DoesNotHideMissingBingClicksBehindOtherSites);
Run("Bing normalizes provider date formats", OverviewRegressionTests.ConvertsBingDates);
Run("Bing date migration preserves history and newer rows", OverviewRegressionTests.RepairsStoredBingDatesWithoutDuplicates);
Run("Cloudflare limits breakdowns without widening short periods", OverviewRegressionTests.LimitsOnlyCloudflareBreakdowns);
Run("Overview refresh checks configured sources", () => OverviewRegressionTests.RefreshesConfiguredSources(false, false));
Run("Overview refresh exposes each source failure", () => OverviewRegressionTests.RefreshesConfiguredSources(true, false));
Run("Overview refresh prevents concurrent requests", () => OverviewRegressionTests.RefreshesConfiguredSources(false, true));

Run("CrUX reads numeric strings using invariant culture", ReportRefreshRegressionTests.ReadsCruxNumericStrings);
Run("CrUX rejects malformed numeric strings", ReportRefreshRegressionTests.RejectsMalformedCruxNumbers);
Run("PageSpeed chart preserves missing scores", ReportRefreshRegressionTests.PreservesMissingPageSpeedScores);
Run("PageSpeed summary includes every selected URL", ReportRefreshRegressionTests.IncludesEveryPageSpeedUrlInSummary);
Run("Measured zero cached bytes remain zero", ReportRefreshRegressionTests.FormatsMeasuredZeroCachedBytes);
Run("CrUX percentile displays preserve values and units", ReportRefreshRegressionTests.FormatsCruxPercentilesWithUnits);
Run("CrUX retains actual collection dates when no replacement is stored", PerformanceReviewRegressionTests.RetainsCruxCollectionDatesWhenNoReplacementIsStored);
Run("CrUX HTTP requests preserve targets and collection dates", PerformanceReviewRegressionTests.RequestsCruxTargetsAndPreservesCollectionDates);
Run("CrUX missing field data preserves history and neutral feedback", PerformanceReviewRegressionTests.AllCruxNotFoundResponsesPreserveHistoryAndNeutralFeedback);
Run("CrUX authorization failures preserve stored data and stop PageSpeed", PerformanceReviewRegressionTests.Non404CruxFailuresPreserveStoredDataAndAbortPageSpeed);
Run("PageSpeed mixed HTTP outcomes count only saved reports", PerformanceReviewRegressionTests.PageSpeedMixedResultsCountSavedReportsAndPreserveFailedStrategy);
Run("PageSpeed failures cannot mark Overview as updated", PerformanceReviewRegressionTests.PageSpeedFailuresDoNotMarkOverviewUpdated);
Run("PageSpeed runtime errors preserve previous reports", PerformanceReviewRegressionTests.RejectsPageSpeedRuntimeErrorsWithoutReplacingHistory);
Run("PageSpeed invalid analysis times preserve previous reports", PerformanceReviewRegressionTests.RejectsInvalidPageSpeedAnalysisTimesWithoutReplacingHistory);
Run("PageSpeed nonfinite scores preserve previous reports", PerformanceReviewRegressionTests.RejectsNonfinitePageSpeedScoresWithoutReplacingHistory);
Run("PageSpeed warning-only reports retain their identity", PerformanceReviewRegressionTests.PreservesPageSpeedWarningsAndRequestedIdentity);
foreach (var report in new[] { "Search Console", "Performance", "Bing" })
{
    Run($"{report} refresh reports sync failures", () => ReportRefreshRegressionTests.ShowsSyncFailure(report));
    Run($"{report} refresh reports missing configuration", () => ReportRefreshRegressionTests.ShowsMissingConfiguration(report));
    Run($"{report} refresh stays busy and prevents duplicate requests", () => ReportRefreshRegressionTests.PreventsConcurrentRefresh(report));
}

Run("normalizes domain origin and home page into one site identity", () =>
{
    var fromDomain = SiteIdentity.FromDomainOrUrl("finnvek.com");
    var fromOrigin = SiteIdentity.FromDomainOrUrl("https://finnvek.com");
    var fromHome = SiteIdentity.FromDomainOrUrl("https://finnvek.com/");

    Equal("finnvek.com", fromDomain.Domain);
    Equal(fromDomain.Domain, fromOrigin.Domain);
    Equal(fromDomain.Domain, fromHome.Domain);
    Equal("https://finnvek.com", fromDomain.OriginUrl);
    Equal("https://finnvek.com/", fromDomain.HomePageUrl);
});

Run("rejects invalid site identity input", () =>
{
    Throws<ArgumentException>(() => SiteIdentity.FromDomainOrUrl("not a valid host name"));
});

Run("normalizes home page url to origin without trailing slash", () =>
{
    Equal("https://finnvek.com", PerformanceUrl.NormalizeOrigin("https://finnvek.com/"));
    Equal("https://knittoolsapp.com", PerformanceUrl.NormalizeOrigin("https://knittoolsapp.com/"));
});

Run("keeps page url with trailing slash for PageSpeed", () =>
{
    Equal("https://finnvek.com/", PerformanceUrl.NormalizePageUrl("https://finnvek.com"));
    Equal("https://knittoolsapp.com/tools/", PerformanceUrl.NormalizePageUrl(" https://knittoolsapp.com/tools "));
});

Run("insight trend math uses ratios and avoids fake percentage jumps", () =>
{
    Equal(nameof(TrendState.NoData), Trend.Classify(new MetricWindow(0, 0)).ToString());
    Equal(nameof(TrendState.NewActivity), Trend.Classify(new MetricWindow(12, 0)).ToString());
    Equal(nameof(TrendState.Flat), Trend.Classify(new MetricWindow(105, 100)).ToString());
    Equal(nameof(TrendState.Up), Trend.Classify(new MetricWindow(125, 100)).ToString());
    Equal(nameof(TrendState.Down), Trend.Classify(new MetricWindow(90, 100)).ToString());

    Equal("+34%", Trend.FormatRatio(0.34));
    Equal("-12%", Trend.FormatRatio(-0.12));
    Equal("0%", Trend.FormatRatio(0));
});

Run("InsightEngine explains Google visibility without clicks", () =>
{
    var cards = GenerateInsights(Metrics(
        googleImpressions: new MetricWindow(141, 100),
        googleClicks: new MetricWindow(103, 100)));

    var card = FindInsight(cards, "People see your pages, but do not click");
    Contains(card.Message, "Google impressions are rising");
    Contains(card.WhyShown, "41%");
    Contains(card.WhyShown, "3%");
    Contains(card.NextStep, "Open Google Search");
});

Run("InsightEngine explains indexing issues with counts", () =>
{
    var cards = GenerateInsights(Metrics(
        indexing: new IndexingSummary(ActiveSitemapUrls: 42, InspectedUrls: 42, IndexedUrls: 24)));

    var card = FindInsight(cards, "Some sitemap pages may not be indexed");
    Contains(card.Message, "sitemap URLs");
    Contains(card.WhyShown, "24 of 42");
    Contains(card.NextStep, "Open Google Search");
});

Run("InsightEngine explains HTTP errors without technical titles", () =>
{
    var cards = GenerateInsights(Metrics(
        httpStatus: new StatusCodeSummary(TotalRequests: 1000, ClientErrorResponses: 30, ServerErrorResponses: 14)));

    var card = FindInsight(cards, "Some visitors may be hitting errors");
    Contains(card.Message, "HTTP error responses");
    Contains(card.WhyShown, "1.4%");
    Contains(card.NextStep, "Open Cloudflare");
    AssertPlainInsightTitles(cards);
});

Run("InsightEngine explains Bing visibility without clicks", () =>
{
    var cards = GenerateInsights(Metrics(
        bingImpressions: new MetricWindow(320, 100),
        bingClicks: new MetricWindow(0, 0)));

    var card = FindInsight(cards, "Bing sees the site, but brings no visitors");
    Contains(card.WhyShown, "320 impressions");
    Contains(card.WhyShown, "0 clicks");
    Contains(card.NextStep, "Open Bing");
});

Run("InsightEngine returns clean empty state when connected data has no rule matches", () =>
{
    var metrics = Metrics(
        cloudflareVisitors: new MetricWindow(100, 100),
        googleImpressions: new MetricWindow(100, 100),
        googleClicks: new MetricWindow(10, 10),
        freshness: new SourceFreshnessSummary(ConnectedSources: 3, MissingLastSyncSources: 0, StaleSources: 0));

    Equal("0", GenerateInsights(metrics).Count.ToString());
    Equal("No insight rules were triggered by the stored data for this period.", GetInsightEmptyState(metrics));
});

Run("InsightEngine sorts by severity then priority and caps rows at four", () =>
{
    var cards = GenerateInsights(Metrics(
        cloudflareVisitors: new MetricWindow(67, 50),
        googleImpressions: new MetricWindow(141, 100),
        googleClicks: new MetricWindow(103, 100),
        bingImpressions: new MetricWindow(320, 100),
        bingClicks: new MetricWindow(0, 0),
        httpStatus: new StatusCodeSummary(1000, 70, 14),
        indexing: new IndexingSummary(42, 42, 24),
        freshness: new SourceFreshnessSummary(3, 1, 1)));

    Equal("4", cards.Count.ToString());
    for (var index = 1; index < cards.Count; index++)
    {
        var previous = cards[index - 1];
        var current = cards[index];
        if (previous.Severity < current.Severity
            || (previous.Severity == current.Severity && previous.Priority > current.Priority))
        {
            throw new InvalidOperationException("Insights are not sorted by severity then priority");
        }
    }

    Equal("Some visitors may be hitting errors", cards[0].Title);
});

Run("InsightEngine avoids false positives for tiny or missing windows", () =>
{
    var cards = GenerateInsights(Metrics(
        cloudflareVisitors: new MetricWindow(2, 1),
        googleImpressions: new MetricWindow(80, 40),
        googleClicks: new MetricWindow(0, 0),
        bingImpressions: new MetricWindow(49, 100),
        bingClicks: new MetricWindow(0, 0),
        httpStatus: new StatusCodeSummary(99, 10, 9),
        indexing: new IndexingSummary(4, 0, 0),
        freshness: new SourceFreshnessSummary(1, 0, 0)));

    Equal("0", cards.Count.ToString());
    Equal("Connect or refresh sources to generate insights.", GetInsightEmptyState(Metrics(
        freshness: new SourceFreshnessSummary(0, 0, 0))));
});

Run("viewmodels do not use ObservableProperty fields", () =>
{
    var root = FindRepositoryRoot();
    var viewModelDir = Path.Combine(root, "Numeris", "ViewModels");
    foreach (var file in Directory.EnumerateFiles(viewModelDir, "*.cs"))
    {
        var text = File.ReadAllText(file);
        if (text.Contains("[ObservableProperty] private", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{Path.GetFileName(file)} still uses ObservableProperty on a private field");
        }
    }
});

Run("SourcesViewModel delegates source behavior to child viewmodels", () =>
{
    var root = FindRepositoryRoot();
    var sourceViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "SourcesViewModel.cs"));

    if (sourceViewModel.Contains("CloudflareGraphqlClient", StringComparison.Ordinal)
        || sourceViewModel.Contains("CloudflareRumClient", StringComparison.Ordinal)
        || sourceViewModel.Contains("SearchConsoleClient", StringComparison.Ordinal)
        || sourceViewModel.Contains("CredentialVault", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("SourcesViewModel still depends on low-level integration clients or secrets");
    }
});

Run("sync services write through repositories instead of raw database access", () =>
{
    var root = FindRepositoryRoot();
    var syncDir = Path.Combine(root, "Numeris", "Services", "Sync");
    foreach (var file in Directory.EnumerateFiles(syncDir, "*.cs"))
    {
        var text = File.ReadAllText(file);
        if (text.Contains("SqliteDatabase", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{Path.GetFileName(file)} still depends on SqliteDatabase");
        }
    }
});

Run("connection registry tracks CrUX and PageSpeed independently", () =>
{
    var root = FindRepositoryRoot();
    var migrations = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Migrations.cs"));
    if (!migrations.Contains("('crux', 'crux'", StringComparison.Ordinal)
        || !migrations.Contains("('pagespeed', 'pagespeed'", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("connections defaults must include separate crux and pagespeed rows");
    }
});

Run("database migrations use explicit schema versions", () =>
{
    var root = FindRepositoryRoot();
    var migrations = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Migrations.cs"));

    Contains(migrations, "CurrentSchemaVersion = 11");
    Contains(migrations, "PRAGMA user_version");
    Contains(migrations, "RunPendingMigrations");
});

Run("application does not seed mock data on startup", () =>
{
    var root = FindRepositoryRoot();
    var app = File.ReadAllText(Path.Combine(root, "Numeris", "App.xaml.cs"));
    var servicesDir = Path.Combine(root, "Numeris", "Services");

    NotContains(app, "MockSeeder");
    NotContains(app, "SeedAsync");
    if (Directory.Exists(Path.Combine(servicesDir, "MockData")))
    {
        var files = Directory.EnumerateFiles(Path.Combine(servicesDir, "MockData"), "*", SearchOption.AllDirectories);
        if (files.Any())
        {
            throw new InvalidOperationException("MockData service files should not exist");
        }
    }
});

Run("repository batches can run inside SQLite transactions", () =>
{
    var root = FindRepositoryRoot();
    var database = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "SqliteDatabase.cs"));
    var performanceRepository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "PerformanceRepository.cs"));

    Contains(database, "WriteTransactionAsync");
    Contains(database, "BeginTransaction");
    Contains(performanceRepository, "WriteTransactionAsync");
});

Run("raw API payload storage is centrally bounded", () =>
{
    var root = FindRepositoryRoot();
    var policyPath = Path.Combine(root, "Numeris", "Services", "Database", "RawJsonStoragePolicy.cs");
    if (!File.Exists(policyPath))
    {
        throw new InvalidOperationException("RawJsonStoragePolicy.cs is missing");
    }

    var policy = File.ReadAllText(policyPath);
    var performanceSync = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Sync", "PerformanceSyncService.cs"));
    var bingSync = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Sync", "BingWebmasterSyncService.cs"));

    Contains(policy, "MaxRawJsonChars");
    Contains(policy, "TrimRawJson");
    Contains(performanceSync, "RawJsonStoragePolicy.TrimRawJson");
    Contains(bingSync, "RawJsonStoragePolicy.TrimRawJson");
});

Run("raw API payload storage redacts secrets before persistence", () =>
{
    var raw = """
        {
          "access_token": "access-secret",
          "refreshToken": "refresh-secret",
          "nested": {
            "client_secret": "client-secret",
            "url": "https://example.test/path?api_key=api-secret&safe=value",
            "header": "Authorization: Bearer bearer-secret"
          }
        }
        """;

    var stored = RawJsonStoragePolicy.TrimRawJson(raw);

    Contains(stored, RawJsonStoragePolicy.RedactedSecret);
    NotContains(stored, "access-secret");
    NotContains(stored, "refresh-secret");
    NotContains(stored, "client-secret");
    NotContains(stored, "api-secret");
    NotContains(stored, "bearer-secret");
});

Run("raw API payload storage redacts PageSpeed detail arrays without reparenting nodes", () =>
{
    var raw = """
        {
          "type": "table",
          "items": [
            {
              "url": "https://example.test/?key=api-secret",
              "node": {
                "type": "node",
                "snippet": "<script src=\"https://example.test/app.js?token=token-secret\"></script>"
              }
            }
          ]
        }
        """;

    var stored = RawJsonStoragePolicy.TrimDetailsJson(raw) ?? "";

    Contains(stored, RawJsonStoragePolicy.RedactedSecret);
    NotContains(stored, "api-secret");
    NotContains(stored, "token-secret");
});

Run("CrUX test treats NotFound as missing field data instead of key failure", () =>
{
    var root = FindRepositoryRoot();
    var performanceSync = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Sync", "PerformanceSyncService.cs"));

    Contains(performanceSync, "catch (ApiRequestException ex) when (ex.IsNotFound)");
    Contains(performanceSync, "no CrUX field data");
    Contains(performanceSync, "API key validity was not verified");
});

Run("Bing page and raw stats keep dated history", () =>
{
    var root = FindRepositoryRoot();
    var migrations = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Migrations.cs"));
    var repository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "BingRepository.cs"));
    var sync = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Sync", "BingWebmasterSyncService.cs"));

    Contains(migrations, "CREATE TABLE IF NOT EXISTS bing_page_stats");
    Contains(migrations, "PRIMARY KEY (site_url, page_url, date)");
    Contains(repository, "INSERT INTO bing_page_stats (site_url, page_url, date, clicks, impressions, raw_json, fetched_at)");
    Contains(repository, "ON CONFLICT(site_url, page_url, date)");
    Contains(sync, "BuildRawItemKey(query, date)");
    Contains(sync, "BuildRawItemKey(page, date)");
});

Run("Bing and Performance pages are first-class navigation routes", () =>
{
    var root = FindRepositoryRoot();
    var mainWindow = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml"));
    var mainWindowCode = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml.cs"));
    var shellViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "ShellViewModel.cs"));
    var appCode = File.ReadAllText(Path.Combine(root, "Numeris", "App.xaml.cs"));

    Contains(mainWindow, "Content=\"Google Search\"");
    Contains(mainWindow, "Tag=\"bing\" Content=\"Bing\"");
    Contains(mainWindow, "Tag=\"performance\" Content=\"Performance\"");
    Contains(mainWindowCode, "[\"bing\"] = typeof(BingPage)");
    Contains(mainWindowCode, "[\"performance\"] = typeof(PerformancePage)");
    Contains(shellViewModel, "or \"bing\" or \"performance\"");
    Contains(appCode, "services.AddTransient<BingViewModel>();");
    Contains(appCode, "services.AddTransient<PerformanceViewModel>();");
    Contains(appCode, "services.AddTransient<BingPage>();");
    Contains(appCode, "services.AddTransient<PerformancePage>();");
});

Run("navigation items use packaged filled image icons", () =>
{
    var root = FindRepositoryRoot();
    var mainWindow = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml"));
    var projectFile = File.ReadAllText(Path.Combine(root, "Numeris", "Numeris.csproj"));
    var iconDir = Path.Combine(root, "Numeris", "Assets", "Icons");
    var icons = ProgramInputs.Vector1;

    Contains(mainWindow, "Width=\"20\"");
    Contains(mainWindow, "Height=\"20\"");
    foreach (var icon in icons)
    {
        Contains(mainWindow, $"Source=\"ms-appx:///Assets/Icons/{icon}\"");
        Contains(projectFile, $"Assets\\Icons\\{icon}");
        if (!File.Exists(Path.Combine(iconDir, icon)))
        {
            throw new InvalidOperationException($"Navigation icon asset is missing from Numeris/Assets/Icons: {icon}");
        }
    }

    if (mainWindow.Contains("<FontIcon", StringComparison.Ordinal) || mainWindow.Contains("-outline.png", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Navigation should use one filled ImageIcon style instead of mixing FontIcon and outline assets.");
    }
});

Run("Bing and Performance reports read through repositories", () =>
{
    var root = FindRepositoryRoot();
    var bingRepository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "BingRepository.cs"));
    var performanceRepository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "PerformanceRepository.cs"));
    var bingViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "BingViewModel.cs"));
    var performanceViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "PerformanceViewModel.cs"));

    Contains(bingRepository, "GetTrafficDailyAsync");
    Contains(bingRepository, "GetQueriesAsync");
    Contains(bingRepository, "GetPagesAsync");
    Contains(bingRepository, "GetRawMethodSummaryAsync");
    Contains(bingRepository, "GetCrawlIssueItemsAsync");
    Contains(performanceRepository, "GetLatestCruxCoreVitalsAsync");
    Contains(performanceRepository, "GetCruxTrendAsync");
    Contains(performanceRepository, "GetLatestPageSpeedRunsAsync");
    Contains(performanceRepository, "GetPageSpeedScoreTrendAsync");
    Contains(performanceRepository, "GetPageSpeedAuditIssuesAsync");
    NotContains(bingViewModel, "SqliteDatabase");
    NotContains(performanceViewModel, "SqliteDatabase");
});

Run("Google OAuth token handling is centralized for Search Console", () =>
{
    var root = FindRepositoryRoot();
    var oauthClient = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Auth", "GoogleOAuthClient.cs"));
    var oauthFlow = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Auth", "GoogleOAuthFlow.cs"));
    var searchClient = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Api", "SearchConsoleClient.cs"));

    Contains(oauthClient, "BuildAuthUrl");
    Contains(oauthClient, "ExchangeCodeAsync");
    Contains(oauthClient, "RefreshAccessTokenAsync");
    Contains(oauthFlow, "IReadOnlyList<string> scopes");
    Contains(oauthFlow, "BuildAuthUrl(clientId, redirectUri, state, scopes)");
    NotContains(searchClient, "TokenEndpoint");
    NotContains(searchClient, "ExchangeCodeAsync");
    NotContains(searchClient, "RefreshAccessTokenAsync");
});

Run("Insight metrics aggregation reads local repositories without schema or API drift", () =>
{
    var root = FindRepositoryRoot();
    var repositoryPath = Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "InsightMetricsRepository.cs");
    if (!File.Exists(repositoryPath))
    {
        throw new InvalidOperationException("InsightMetricsRepository.cs is missing");
    }

    var repository = File.ReadAllText(repositoryPath);
    var migrations = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Migrations.cs"));

    Contains(repository, "GetInsightMetricsAsync");
    Contains(repository, "cloudflare_traffic");
    Contains(repository, "cloudflare_status_codes");
    Contains(repository, "search_console");
    Contains(repository, "search_devices");
    Contains(repository, "sitemap_urls");
    Contains(repository, "pagespeed_runs");
    Contains(repository, "bing_rank_traffic");
    Contains(repository, "connections");
    Contains(repository, "SiteIdentity.NormalizeDomain");
    Contains(repository, "SiteIdentity.NormalizeHomePageUrl");
    NotContains(repository, "CloudflareGraphqlClient");
    NotContains(repository, "SearchConsoleClient");
    NotContains(repository, "PageSpeedClient");
    NotContains(repository, "BingWebmasterClient");
    NotContains(repository, "RunReportAsync");
    NotContains(repository, "RunPageSpeedAsync");
    NotContains(repository, "SyncConfiguredAsync");
    Contains(migrations, "CurrentSchemaVersion = 11");
    NotContains(migrations, "CREATE TABLE IF NOT EXISTS insights");
    NotContains(migrations, "insight_metrics");
});

Run("Dashboard ViewModel consumes insight engine and metrics repository only", () =>
{
    var root = FindRepositoryRoot();
    var app = File.ReadAllText(Path.Combine(root, "Numeris", "App.xaml.cs"));
    var viewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "DashboardViewModel.cs"));

    Contains(app, "services.AddSingleton<InsightMetricsRepository>();");
    Contains(File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Insights", "InsightEngine.cs")), "public static class InsightEngine");
    Contains(viewModel, "InsightMetricsRepository");
    Contains(viewModel, "InsightEngine");
    Contains(viewModel, "ObservableCollection<InsightCard> Insights");
    Contains(viewModel, "InsightsSummaryText");
    Contains(viewModel, "HasInsightRows");
    Contains(viewModel, "GetInsightMetricsAsync(domain, range.Days)");
    Contains(viewModel, "InsightEngine.Generate");
    Contains(viewModel, "InsightEngine.GetEmptyStateText");
    NotContains(viewModel, "SqliteDatabase");
});

Run("Dashboard Insights section binds rows with shared UI resources", () =>
{
    var root = FindRepositoryRoot();
    var dashboard = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));

    Contains(dashboard, "x:Name=\"InsightsCard\"");
    Contains(dashboard, "AutomationProperties.Name=\"Overview insights\"");
    Contains(dashboard, "Style=\"{StaticResource ChartCardBorderStyle}\"");
    Contains(dashboard, "ItemsSource=\"{x:Bind ViewModel.Insights, Mode=OneWay}\"");
    Contains(dashboard, "Text=\"{x:Bind ViewModel.InsightsSummaryText, Mode=OneWay}\"");
    Contains(dashboard, "x:DataType=\"models:InsightCard\"");
    Contains(dashboard, "AutomationProperties.Name=\"Insight row\"");
    Contains(dashboard, "Text=\"Why shown\"");
    Contains(dashboard, "Text=\"Next step\"");
    Contains(dashboard, "Text=\"{x:Bind SeverityText, Mode=OneTime}\"");
    Contains(dashboard, "Text=\"{x:Bind WhyShown, Mode=OneTime}\"");
    Contains(dashboard, "Text=\"{x:Bind NextStep, Mode=OneTime}\"");
    Contains(dashboard, "BorderBrush=\"{StaticResource NumerisCardBorderBrush}\"");
    NotContains(dashboard, "CTR");
    NotContains(dashboard, "CrUX");
    NotContains(dashboard, "5xx");
    NotContains(dashboard, "GA4");
});

Run("Performance page formats PageSpeed query window from DateOnly safely", () =>
{
    var root = FindRepositoryRoot();
    var performanceViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "PerformanceViewModel.cs"));

    NotContains(performanceViewModel, "range.Start.ToString(\"yyyy-MM-ddTHH:mm:ss\"");
    NotContains(performanceViewModel, "range.End.AddDays(1).ToString(\"yyyy-MM-ddTHH:mm:ss\"");
    Contains(performanceViewModel, "ToDateTime(TimeOnly.MinValue)");
    Contains(performanceViewModel, "ToDateTime(TimeOnly.MaxValue)");
});

Run("SLNX keeps Visual Studio project configuration mappings simple", () =>
{
    var root = FindRepositoryRoot();
    var solution = File.ReadAllText(Path.Combine(root, "Numeris.slnx"));

    Contains(solution, "<Platform Name=\"x64\" />");
    Contains(solution, "<Project Path=\"Numeris/Numeris.csproj\"");
    Contains(solution, "<Project Path=\"Numeris.Tests/Numeris.Tests.csproj\"");
    NotContains(solution, "Solution=\"*|x64\"");
});

Run("Bing sync keeps narrow Webmaster API scope", () =>
{
    var root = FindRepositoryRoot();
    var sync = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Sync", "BingWebmasterSyncService.cs"));

    Contains(sync, "\"GetUserSites\"");
    Contains(sync, "\"GetRankAndTrafficStats\"");
    Contains(sync, "\"GetQueryStats\"");
    Contains(sync, "\"GetPageStats\"");
    Contains(sync, "\"GetCrawlStats\"");
    Contains(sync, "\"GetCrawlIssues\"");
    NotContains(sync, "GetChildrenUrlInfo");
    NotContains(sync, "GetUrlLinks");
});

Run("Windows check scripts and command shims are documented", () =>
{
    var root = FindRepositoryRoot();
    var lintScript = Path.Combine(root, "tools", "lint-check.ps1");
    var securityScript = Path.Combine(root, "tools", "security-check.ps1");
    if (!File.Exists(lintScript) || !File.Exists(securityScript))
    {
        throw new InvalidOperationException("Windows check scripts are missing");
    }

    var lint = File.ReadAllText(lintScript);
    var security = File.ReadAllText(securityScript);
    var agents = File.ReadAllText(Path.Combine(root, "AGENTS.md"));
    var gitignore = File.ReadAllText(Path.Combine(root, ".gitignore"));

    Contains(lint, "reports/ktlint.txt");
    Contains(lint, "reports/detekt.txt");
    Contains(lint, "reports/lint.txt");
    Contains(lint, "Show-CheckSummary");
    Contains(lint, "lint-check summary");
    Contains(lint, "Tee-Object -FilePath $reportPath -Append | Out-Host");
    Contains(security, "reports/security-code.txt");
    Contains(security, "reports/security-deps.txt");
    Contains(security, "--config $semgrepConfig --error --metrics=off");
    Contains(security, "Write-CheckSummary");
    Contains(security, "security-check summary");
    Contains(security, "Tee-Object -FilePath $reportPath -Append | Out-Host");
    Contains(agents, "function lc");
    Contains(agents, "function sc");
    Contains(gitignore, "reports/");
});

Run("Windows check scripts resolve repository root from child directories", () =>
{
    var root = FindRepositoryRoot();
    var childDir = Path.Combine(root, "Numeris");

    Equal(root, RunPowerShellScript(Path.Combine(root, "tools", "lint-check.ps1"), childDir, "-ResolveOnly"));
    Equal(root, RunPowerShellScript(Path.Combine(root, "tools", "security-check.ps1"), childDir, "-ResolveOnly"));
});

Run("SQL scalar helpers stay parameterized for security scans", () =>
{
    var root = FindRepositoryRoot();
    var summaryRepository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "SummaryRepository.cs"));
    var migrations = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Migrations.cs"));

    if (summaryRepository.Contains("cmd.CommandText = sql", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("SummaryRepository must not use raw CommandText scalar helpers");
    }
    Contains(summaryRepository, "ExecuteScalar<long>");
    Contains(summaryRepository, "(\"$package\", Domains.PlayStorePackage)");
    Contains(migrations, "FROM pragma_table_info(@table)");
    Contains(migrations, "cmd.Parameters.AddWithValue(\"@column\", column)");
});

Run("legacy import uses Numeris service names and central source aliases", () =>
{
    var root = FindRepositoryRoot();
    var migrationDir = Path.Combine(root, "Numeris", "Services", "Migration");
    var files = Directory.EnumerateFiles(migrationDir, "*.cs").Select(Path.GetFileName).ToList();
    var oldProductName = string.Concat("Pu", "lse");
    var oldProductNameLower = oldProductName.ToLowerInvariant();
    if (files.Any(file => file is not null && file.StartsWith(oldProductName, StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("Migration files should use neutral legacy names, not old-product-prefixed service names");
    }

    var app = File.ReadAllText(Path.Combine(root, "Numeris", "App.xaml.cs"));
    var appPaths = File.ReadAllText(Path.Combine(root, "Numeris", "Helpers", "AppPaths.cs"));
    var legacyMigration = File.ReadAllText(Path.Combine(migrationDir, "LegacyDataMigrationService.cs"));
    var legacyCredentials = File.ReadAllText(Path.Combine(migrationDir, "LegacyCredentialReader.cs"));

    Contains(app, "LegacyDataMigrationService");
    Contains(legacyMigration, "LegacyAppSource");
    Contains(appPaths, "LegacyNumerisDatabasePath");
    Contains(appPaths, "Path.Combine(DataDir, \"legacy\", \"numeris\")");
    Contains(legacyMigration, "AppPaths.LegacyNumerisDatabasePath");
    NotContains(legacyMigration, "Environment.SpecialFolder.ApplicationData");
    NotContains(legacyMigration, "com.finnvek." + oldProductNameLower);
    NotContains(legacyMigration, "CredentialService = \"" + oldProductName + "\"");
    Contains(legacyCredentials, "ReadKeyringPassword");
});

Run("repository files no longer use the old product name", () =>
{
    var root = FindRepositoryRoot();
    var oldProductName = string.Concat("Pu", "lse");
    var oldProductNameLower = oldProductName.ToLowerInvariant();
    var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".xaml",
        ".md",
        ".json",
        ".csproj",
        ".slnx",
        ".props",
        ".targets",
        ".ps1",
    };

    foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(root, file);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(segment => segment is ".git" or "bin" or "obj" or ".vs" or "reports"))
        {
            continue;
        }

        if (relativePath.Contains(oldProductName, StringComparison.Ordinal)
            || relativePath.Contains(oldProductNameLower, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Old product name remains in file path: {relativePath}");
        }

        if (!textExtensions.Contains(Path.GetExtension(file)))
        {
            continue;
        }

        var text = File.ReadAllText(file);
        if (text.Contains(oldProductName, StringComparison.Ordinal)
            || text.Contains(oldProductNameLower, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Old product name remains in file content: {relativePath}");
        }
    }
});

Run("legacy credential reader falls back to renamed credential targets", () =>
{
    var root = FindRepositoryRoot();
    var legacyCredentials = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Migration", "LegacyCredentialReader.cs"));
    var oldProductName = string.Concat("Pu", "lse");
    var oldTarget = "cloudflare:example.com." + oldProductName;
    var currentTarget = "cloudflare:example.com.Numeris";

    var readerType = Type.GetType("Numeris.Services.Migration.LegacyCredentialReader, Numeris")
        ?? throw new InvalidOperationException("LegacyCredentialReader type was not found");
    var orderMethod = readerType.GetMethod("OrderCredentialTargets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
        ?? throw new InvalidOperationException("LegacyCredentialReader.OrderCredentialTargets was not found");
    var candidates = ((IEnumerable<string>?)orderMethod.Invoke(
            null,
            new object[]
            {
                "cloudflare:example.com",
                "Numeris",
                new[]
                {
                    "web_analytics:account." + oldProductName,
                    oldTarget,
                    "cloudflare:example.com.au." + oldProductName,
                    "cloudflare:example.com.evil." + oldProductName,
                    "cloudflare:example.com.",
                    currentTarget,
                },
            }))
        ?.ToList() ?? throw new InvalidOperationException("Legacy credential candidate ordering returned null");

    Equal("2", candidates.Count.ToString());
    Equal(currentTarget, candidates[0]);
    Equal(oldTarget, candidates[1]);
    var client = "test-client.apps.googleusercontent.com";
    var clientCandidates = ((IEnumerable<string>)orderMethod.Invoke(null,
        new object[] { client, "Numeris", new[] { client + "." + oldProductName, client + ".evil." + oldProductName } })!).ToList();
    Equal("2", clientCandidates.Count.ToString());
    Equal(client + "." + oldProductName, clientCandidates[1]);
    Contains(legacyCredentials, "CredEnumerate");
    NotContains(legacyCredentials, oldProductName);
});

Run("legacy import preserves existing Numeris secrets", () =>
{
    var root = FindRepositoryRoot();
    var legacyMigration = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Migration", "LegacyDataMigrationService.cs"));

    Contains(legacyMigration, "string.IsNullOrWhiteSpace(_vault.GetWebAnalyticsToken(source.AccountId))");
    Contains(legacyMigration, "string.IsNullOrWhiteSpace(_vault.GetCloudflareToken(domain))");
    Contains(legacyMigration, "string.IsNullOrWhiteSpace(_vault.GetSearchConsoleClientSecret(clientId))");
    Contains(legacyMigration, "string.IsNullOrWhiteSpace(_vault.GetSearchConsoleRefreshToken(clientId))");
    Contains(legacyMigration, "LegacyCredentialReader.ReadKeyringPassword(");
});

Run("source deletes remove vault secrets instead of storing blanks", () =>
{
    var root = FindRepositoryRoot();
    var vault = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Secrets", "CredentialVault.cs"));
    var webAnalytics = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "Sources", "WebAnalyticsSourceViewModel.cs"));
    var searchConsole = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "Sources", "SearchConsoleSourceViewModel.cs"));
    var performance = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "Sources", "PerformanceSourceViewModel.cs"));
    var bing = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "Sources", "BingSourceViewModel.cs"));

    Contains(vault, "DeleteWebAnalyticsToken");
    Contains(vault, "DeleteSearchConsoleClientSecret");
    Contains(vault, "DeleteSearchConsoleRefreshToken");
    Contains(vault, "DeleteCruxApiKey");
    Contains(vault, "DeletePageSpeedApiKey");
    Contains(vault, "DeleteBingApiKey");
    Contains(webAnalytics, "_vault.DeleteWebAnalyticsToken");
    Contains(searchConsole, "_vault.DeleteSearchConsoleClientSecret");
    Contains(searchConsole, "_vault.DeleteSearchConsoleRefreshToken");
    Contains(performance, "_vault.DeleteCruxApiKey");
    Contains(performance, "_vault.DeletePageSpeedApiKey");
    Contains(bing, "_vault.DeleteBingApiKey");

    if (webAnalytics.Contains("SetWebAnalyticsToken(Connection.AccountId, \"\")", StringComparison.Ordinal)
        || searchConsole.Contains("SetSearchConsoleClientSecret(Connection.ClientId, \"\")", StringComparison.Ordinal)
        || searchConsole.Contains("SetSearchConsoleRefreshToken(Connection.ClientId, \"\")", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Delete paths must remove vault entries instead of saving blank secrets");
    }
});

Run("Search Console client omits raw API error bodies", () =>
{
    var root = FindRepositoryRoot();
    var client = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Api", "SearchConsoleClient.cs"));

    Contains(client, "ApiRequestException");
    Contains(client, "ApiErrorMessage.FromBody(body)");
    if (client.Contains(": {body}", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("SearchConsoleClient must not include raw response bodies in exception messages");
    }
});

Run("Google OAuth token errors are actionable and sanitized", () =>
{
    var message = ApiErrorMessage.FromBody(
        """
        {
          "error": "invalid_grant",
          "error_description": "Bad Request",
          "refresh_token": "refresh-secret",
          "client_secret": "client-secret"
        }
        """);

    Contains(message ?? "", "invalid_grant");
    Contains(message ?? "", "Connect Google account again");
    NotContains(message ?? "", "refresh-secret");
    NotContains(message ?? "", "client-secret");
});

Run("integration status messages sanitize upstream exceptions", () =>
{
    var root = FindRepositoryRoot();
    var sourcesDir = Path.Combine(root, "Numeris", "ViewModels", "Sources");
    var syncDir = Path.Combine(root, "Numeris", "Services", "Sync");
    foreach (var file in Directory.EnumerateFiles(sourcesDir, "*.cs").Concat(Directory.EnumerateFiles(syncDir, "*.cs")))
    {
        var text = File.ReadAllText(file);
        if (text.Contains("ex.Message", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{Path.GetFileName(file)} exposes raw exception messages");
        }
    }

    var apiErrors = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Api", "ApiErrorMessage.cs"));
    var configs = File.ReadAllText(Path.Combine(root, "Numeris", "Models", "ConnectionConfigs.cs"));
    Contains(apiErrors, "BearerRegex");
    NotContains(configs, "StatusCode");
});

Run("GraphQL operation names use Numeris branding", () =>
{
    var root = FindRepositoryRoot();
    var cloudflareClient = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Api", "CloudflareGraphqlClient.cs"));
    var rumClient = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Api", "CloudflareRumClient.cs"));
    var oldProductName = string.Concat("Pu", "lse");

    Contains(cloudflareClient, "query NumerisDailyTraffic");
    Contains(rumClient, "query NumerisWebAnalytics");
    if (cloudflareClient.Contains("query " + oldProductName, StringComparison.Ordinal)
        || rumClient.Contains("query " + oldProductName, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("GraphQL operation names still use old product branding");
    }
});

Run("WinUI visual system uses Mica shell and centralized surface tokens", () =>
{
    var root = FindRepositoryRoot();
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    var mainWindow = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml"));
    var dashboard = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));
    var kpiCard = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "KpiCard.xaml"));

    Contains(mainWindow, "<MicaBackdrop");
    NotContains(mainWindow, "<DesktopAcrylicBackdrop");
    Contains(mainWindow, "Background=\"Transparent\"");
    Contains(mainWindow, "PaneDisplayMode=\"Left\"");
    Contains(mainWindow, "IsPaneToggleButtonVisible=\"False\"");
    Contains(tokens, "DarkVoidColor\">#151419");
    Contains(tokens, "AppBackgroundColor\">#151419");
    Contains(tokens, "NavigationLayerBrush");
    Contains(tokens, "NavigationViewDefaultPaneBackground");
    Contains(tokens, "ContentLayerBrush");
    Contains(tokens, "CardSurfaceBrush");
    Contains(tokens, "ChartPanelBrush");
    Contains(tokens, "NumerisCardBorderBrush");
    Contains(tokens, "Segoe UI Variable");
    Contains(dashboard, "Style=\"{StaticResource ChartCardBorderStyle}\"");
    Contains(dashboard, "Style=\"{StaticResource StatusStripBorderStyle}\"");
    Contains(kpiCard, "Style=\"{StaticResource KpiCardSurfaceStyle}\"");
});

Run("Shell uses one matte background color without a bitmap backdrop", () =>
{
    var root = FindRepositoryRoot();
    var mainWindow = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml"));
    var mainWindowCode = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml.cs"));
    var dashboard = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    var project = File.ReadAllText(Path.Combine(root, "Numeris", "Numeris.csproj"));

    Contains(mainWindow, "<Grid Background=\"{StaticResource AppBackgroundBrush}\"");
    Contains(tokens, "AppBackgroundColor\">#151419");
    NotContains(project, "Assets\\AppBackdrop.webp");
    NotContains(project, "Assets\\AppBackdrop.png");
    NotContains(mainWindow, "AppBackdropImage");
    NotContains(mainWindow, "AppBackdropScrim");
    NotContains(mainWindow, "AppBackdropImage_Loaded");
    NotContains(mainWindowCode, "AppBackdrop");
    NotContains(mainWindowCode, "BitmapDecoder");
    NotContains(mainWindowCode, "SoftwareBitmapSource");
    NotContains(tokens, "AppBackdrop");
    NotContains(project, "Assets\\OverviewHeroBackdrop.png");
    NotContains(tokens, "OverviewHeroBackdrop");
    NotContains(tokens, "OverviewHeroFade");
    NotContains(dashboard, "OverviewHeroBackdrop.png");
    NotContains(dashboard, "HeroBackdropImage");
});

Run("Unpackaged shell no longer loads decorative backdrop assets", () =>
{
    var root = FindRepositoryRoot();
    var project = File.ReadAllText(Path.Combine(root, "Numeris", "Numeris.csproj"));
    var mainWindowCode = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml.cs"));

    Contains(project, "<WindowsPackageType>None</WindowsPackageType>");
    NotContains(project, "Assets\\AppBackdrop.webp");
    NotContains(mainWindowCode, "AppContext.BaseDirectory");
    NotContains(mainWindowCode, "StorageFile.GetFileFromPathAsync");
    NotContains(mainWindowCode, "StorageFile.GetFileFromApplicationUriAsync");
    NotContains(mainWindowCode, "catch (Exception ex)");
});

Run("Global backdrop uses a bold dark void shell behind readable surfaces", () =>
{
    var root = FindRepositoryRoot();
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    var mainWindow = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml"));
    var mainWindowCode = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml.cs"));
    var chartPalette = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "ChartPalette.cs"));

    Contains(tokens, "AppBackgroundColor\">#151419");
    Contains(tokens, "ContentLayerColor\">#00000000");
    Contains(tokens, "NavigationLayerColor\">#00000000");
    Contains(tokens, "CardSurfaceColor\">#F017181D");
    Contains(tokens, "ControlSurfaceColor\">#F0222526");
    Contains(tokens, "ChartPanelColor\">#E8151419");
    Contains(tokens, "NumerisCardBorderColor\">#5CBFBFBF");
    Contains(tokens, "ChartGridLineColor\">#40BFBFBF");
    Contains(tokens, "ChartMutedColor");
    Contains(chartPalette, "ChartMutedColor");
    Contains(tokens, "NavigationViewContentBackground");
    Contains(tokens, "NavigationViewDefaultPaneBackground\" ResourceKey=\"NavigationLayerBrush");
    Contains(tokens, "NavigationViewExpandedPaneBackground\" ResourceKey=\"NavigationLayerBrush");
    Contains(tokens, "NavigationViewContentGridBorderBrush");
    Contains(tokens, "NavigationViewContentGridBorderThickness\">0");
    Contains(mainWindowCode, "ConfigureTitleBarColors");
    Contains(mainWindowCode, "AppWindowTitleBar.IsCustomizationSupported");
    Contains(mainWindow, "Background=\"Transparent\"");
});

Run("Matte chart and delta resources are centralized in Tokens.xaml", () =>
{
    var root = FindRepositoryRoot();
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));

    Contains(tokens, "NumerisAccentColor\">#F56E0F");
    Contains(tokens, "NumerisAccentHoverColor\">#FF7E1F");
    Contains(tokens, "NumerisAccentPressedColor\">#D95A0A");
    NotContains(tokens, "NumerisAccentColor\">#4F73FF");
    Contains(tokens, "ChartSecondaryColor\">#E09145");
    Contains(tokens, "ChartMutedColor\">#BFBFBF");
    Contains(tokens, "ChartBarNeutralTopColor\">#FF878787");
    Contains(tokens, "ChartBarNeutralMidColor\">#FF353A3E");
    Contains(tokens, "ChartBarNeutralBottomColor\">#FF17181D");
    Contains(tokens, "ChartBarHighlightTopColor\">#FFFF7A1A");
    Contains(tokens, "ChartBarHighlightMidColor\">#FFF56E0F");
    Contains(tokens, "ChartBarHighlightBottomColor\">#FFA93A08");
    Contains(tokens, "SuccessColor\">#67D391");
    Contains(tokens, "WarningColor\">#E09145");
    Contains(tokens, "DangerColor\">#FF6B76");
    Contains(tokens, "InfoColor\">#83B8C0");
    Contains(tokens, "ChartBarNeutralTopColor");
    Contains(tokens, "ChartBarNeutralMidColor");
    Contains(tokens, "ChartBarNeutralBottomColor");
    Contains(tokens, "ChartBarHighlightTopColor");
    Contains(tokens, "ChartBarHighlightMidColor");
    Contains(tokens, "ChartBarHighlightBottomColor");
    Contains(tokens, "ChartBarCapColor");
    Contains(tokens, "ChartReferenceLineColor");
    Contains(tokens, "PositiveDeltaColor");
    Contains(tokens, "NegativeDeltaColor");
    Contains(tokens, "NeutralDeltaColor");
    Contains(tokens, "PositiveDeltaBrush");
    Contains(tokens, "NegativeDeltaBrush");
    Contains(tokens, "NeutralDeltaBrush");
});

Run("Dark void liquid lava palette exposes text accent and navigation selection resources", () =>
{
    var root = FindRepositoryRoot();
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    var agents = File.ReadAllText(Path.Combine(root, "AGENTS.md"));

    Contains(tokens, "DarkVoidColor\">#151419");
    Contains(tokens, "GraphiteColor\">#222526");
    Contains(tokens, "DeepGraphiteColor\">#17181D");
    Contains(tokens, "LiquidLavaColor\">#F56E0F");
    Contains(tokens, "WarmCopperColor\">#E09145");
    Contains(tokens, "WarmPlatinumColor\">#FCD9B8");
    Contains(tokens, "NumerisTextPrimaryColor\">#FBFBFB");
    Contains(tokens, "NumerisTextSecondaryColor\">#DAD4CC");
    Contains(tokens, "NumerisTextTertiaryColor\">#BFBFBF");
    Contains(tokens, "NumerisTextDisabledColor\">#878787");
    Contains(tokens, "NumerisAccentForegroundColor\">#151419");
    Contains(tokens, "NumerisAccentForegroundBrush");
    Contains(tokens, "NumerisAccentHoverBrush");
    Contains(tokens, "NumerisAccentPressedBrush");
    Contains(tokens, "PeriodSelectorSelectedBrush");
    Contains(tokens, "PeriodSelectorSelectedForegroundBrush");
    Contains(tokens, "NavigationViewItemBackgroundSelected\" ResourceKey=\"NavigationViewItemBackgroundSelectedBrush");
    Contains(tokens, "NavigationViewItemBackgroundSelectedPointerOver\" ResourceKey=\"NavigationViewItemBackgroundSelectedPointerOverBrush");
    Contains(tokens, "NavigationViewItemBackgroundPointerOver\" ResourceKey=\"NavigationViewItemBackgroundPointerOverBrush");
    Contains(tokens, "NavigationViewSelectionIndicatorForeground\" ResourceKey=\"NumerisAccentBrush");
    Contains(tokens, "NavigationViewItemBackgroundSelectedColor\">#FF222526");
    Contains(tokens, "NavigationViewItemBackgroundPointerOverColor\">#00000000");
    Contains(agents, "Dark Void / Graphite / Liquid Lava");
    Contains(agents, "AppBackgroundColor #151419");
    Contains(agents, "NavigationLayerColor #00000000");
});

Run("main window uses a custom transparent title bar integrated into the shell", () =>
{
    var root = FindRepositoryRoot();
    var mainWindow = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml"));
    var mainWindowCode = File.ReadAllText(Path.Combine(root, "Numeris", "MainWindow.xaml.cs"));
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));

    Contains(mainWindow, "x:Name=\"AppTitleBar\"");
    Contains(mainWindow, "Text=\"Numeris\"");
    Contains(mainWindow, "Style=\"{StaticResource AppTitleTextBlockStyle}\"");
    Contains(mainWindow, "Background=\"Transparent\"");
    Contains(mainWindowCode, "ConfigureCustomTitleBar");
    Contains(mainWindowCode, "ExtendsContentIntoTitleBar = true");
    Contains(mainWindowCode, "SetTitleBar(AppTitleBar)");
    Contains(mainWindowCode, "ReadColorResource");
    Contains(tokens, "AppTitleBarHeight");
    Contains(tokens, "AppTitleTextBlockStyle");
});

Run("Pages use unified header surfaces and page-level scrolling", () =>
{
    var root = FindRepositoryRoot();
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    var dashboardPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));
    var cloudflarePage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "CloudflarePage.xaml"));
    var searchPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SearchConsolePage.xaml"));
    var healthPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "HealthPage.xaml"));

    Contains(tokens, "PageHeaderBorderStyle");
    Contains(tokens, "PageHeaderMargin");
    Contains(tokens, "<Thickness x:Key=\"PageHeaderPadding\">0,20,0,18</Thickness>");
    Contains(tokens, "PageScrollContentPadding");
    Contains(tokens, """
    <Style x:Key="PageHeaderBorderStyle" TargetType="Border">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderBrush" Value="Transparent" />
        <Setter Property="BorderThickness" Value="0" />
""");
    Contains(tokens, "PrimaryActionButtonStyle");
    Contains(tokens, "TopTabSelectorBarStyle");
    Contains(dashboardPage, "Style=\"{StaticResource PageHeaderBorderStyle}\"");
    Contains(cloudflarePage, "Style=\"{StaticResource PageHeaderBorderStyle}\"");
    Contains(searchPage, "Style=\"{StaticResource PageHeaderBorderStyle}\"");
    Contains(healthPage, "Style=\"{StaticResource PageHeaderBorderStyle}\"");
    Contains(cloudflarePage, "x:Name=\"PageScrollViewer\"");
    Contains(cloudflarePage, "Margin=\"{StaticResource PageHeaderMargin}\"");
    Contains(cloudflarePage, "Padding=\"{StaticResource PageScrollContentPadding}\"");
    Contains(cloudflarePage, "HorizontalScrollBarVisibility=\"Disabled\"");
    Contains(cloudflarePage, "TrafficPanel");
    NotContains(cloudflarePage, "<ScrollViewer Grid.Row=\"2\"");
});

Run("Overview charts use a shared Fluent chart theme", () =>
{
    var root = FindRepositoryRoot();
    var chartThemePath = Path.Combine(root, "Numeris", "Themes", "ChartTheme.cs");
    if (!File.Exists(chartThemePath))
    {
        throw new InvalidOperationException("ChartTheme.cs is missing");
    }

    var dashboardPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml.cs"));
    var dashboardXaml = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));
    var chartTheme = File.ReadAllText(chartThemePath);

    Contains(dashboardPage, "ChartTheme.CreateCartesianChart()");
    Contains(dashboardPage, "ChartTheme.CreateChartSurface(chart)");
    Contains(dashboardXaml, "ChartCardGrid");
    Contains(chartTheme, "new SolidColorPaint(ChartPalette.GridLine)");
    Contains(chartTheme, "new SolidColorPaint(ChartPalette.AxisText)");
    Contains(chartTheme, "CreateChartSurface");
    Contains(chartTheme, "ChartMeshOpacity");
    Contains(chartTheme, "Polyline");
    Contains(chartTheme, "Ellipse");
    Contains(chartTheme, "LegendTextPaint");
    Contains(chartTheme, "HorizontalAlignment = HorizontalAlignment.Stretch");
    Contains(chartTheme, "VerticalAlignment = VerticalAlignment.Stretch");
    NotContains(chartTheme, "DrawMarginFrame = new DrawMarginFrame");
});

Run("Overview promotes traffic as a matte bar hero", () =>
{
    var root = FindRepositoryRoot();
    var dashboardPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));
    var dashboardCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml.cs"));
    var dashboardViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "DashboardViewModel.cs"));
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));

    Contains(dashboardPage, "x:Name=\"TrafficHeroCard\"");
    Contains(dashboardPage, "Text=\"Daily visitors (sum)\"");
    Contains(dashboardPage, "Text=\"{x:Bind ViewModel.VisitorsTotal, Mode=OneWay}\"");
    Contains(dashboardPage, "<controls:DeltaBadge");
    Contains(dashboardPage, "Value=\"{x:Bind ViewModel.VisitorsChangeValue, Mode=OneWay}\"");
    Contains(dashboardPage, "x:Name=\"KpiGrid\"");
    Contains(dashboardPage, "Label=\"Google Clicks\"");
    Contains(dashboardPage, "Label=\"Bing Clicks\"");
    Contains(dashboardPage, "Label=\"Web Vitals\"");
    Contains(dashboardPage, "Label=\"PageSpeed Mobile\"");
    Contains(dashboardPage, "x:Name=\"StatusStrip\"");
    NotContains(dashboardPage, "Label=\"Visitors\"");

    Contains(dashboardCode, "BuildBarChart(ref _trafficChart, TrafficChartHost)");
    Contains(dashboardCode, "ChartTheme.CreateBarChartSurface(chart)");

    Contains(dashboardViewModel, "ChartTheme.CreateMatteColumnSeries(\"Visitors\"");
    Contains(dashboardViewModel, "HighlightIndex(rows)");
    Contains(dashboardViewModel, "private static int? HighlightIndex(List<TrafficDay> rows)");
    Contains(dashboardViewModel, "rows[^1].UniqueVisitors");
    NotContains(dashboardViewModel, "Name = \"Pageviews\"");

    Contains(tokens, "StatusStripBorderStyle");
    Contains(tokens, "<Setter Property=\"Background\" Value=\"Transparent\" />");
    Contains(tokens, "<Setter Property=\"BorderThickness\" Value=\"0,1,0,0\" />");
});

Run("ChartTheme centralizes matte LiveCharts column styling", () =>
{
    var root = FindRepositoryRoot();
    var chartTheme = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "ChartTheme.cs"));

    Contains(chartTheme, "CreateMatteColumnSeries<T>(string name, IReadOnlyList<T> values, int? highlightIndex = null)");
    Contains(chartTheme, "CreateMutedColumnSeries<T>");
    Contains(chartTheme, "CreateHighlightColumnSeries<T>");
    Contains(chartTheme, "CreateMatteBarFill");
    Contains(chartTheme, "CreateHighlightBarFill");
    Contains(chartTheme, "LinearGradientPaint");
    Contains(chartTheme, "new SKPoint(0.5f, 0f)");
    Contains(chartTheme, "new SKPoint(0.5f, 1f)");
    Contains(chartTheme, "ChartPalette.BarNeutralTop");
    Contains(chartTheme, "ChartPalette.BarNeutralMid");
    Contains(chartTheme, "ChartPalette.BarNeutralBottom");
    Contains(chartTheme, "ChartPalette.BarHighlightTop");
    Contains(chartTheme, "ChartPalette.BarHighlightMid");
    Contains(chartTheme, "ChartPalette.BarHighlightBottom");
    Contains(chartTheme, "PointMeasured +=");
    Contains(chartTheme, "point.Index == highlightIndex.Value");
    Contains(chartTheme, "StyleColumnSeries<T>");
    Contains(chartTheme, "Stroke = null");
    Contains(chartTheme, "MaxBarWidth =");
    Contains(chartTheme, "Padding =");
    Contains(chartTheme, "Rx =");
    Contains(chartTheme, "Ry =");
    Contains(chartTheme, "DataPadding =");
    Contains(chartTheme, "StyleChartForBars");
    Contains(chartTheme, "AnimationsSpeed = TimeSpan.FromMilliseconds");
    Contains(chartTheme, "EasingFunction = EasingFunctions");
    Contains(chartTheme, "CreateBarChartSurface");
});

Run("Overview KPI and status surfaces carry context instead of empty boxes", () =>
{
    var root = FindRepositoryRoot();
    var dashboardPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));
    var kpiCard = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "KpiCard.xaml"));
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));

    Contains(kpiCard, "MetricContent");
    Contains(kpiCard, "Detail");
    Contains(dashboardPage, "x:Name=\"TrafficHeroCard\"");
    Contains(dashboardPage, "Detail=\"{x:Bind ViewModel.ClicksDetail, Mode=OneWay}\"");
    Contains(dashboardPage, "Label=\"Bing Clicks\"");
    Contains(dashboardPage, "Label=\"Web Vitals\"");
    Contains(dashboardPage, "Label=\"PageSpeed Mobile\"");
    Contains(dashboardPage, "StatusValueTextBlockStyle");
    Contains(tokens, "StatusValueTextBlockStyle");
    NotContains(kpiCard, "AccentIndicator");
    NotContains(dashboardPage, "StatusValueBadgeBorderStyle");
});

Run("KPI deltas use compact tokenized delta badges", () =>
{
    var root = FindRepositoryRoot();
    var kpiCard = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "KpiCard.xaml"));
    var kpiCardCode = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "KpiCard.xaml.cs"));
    var deltaBadgePath = Path.Combine(root, "Numeris", "Controls", "DeltaBadge.xaml");
    var deltaBadgeCodePath = Path.Combine(root, "Numeris", "Controls", "DeltaBadge.xaml.cs");

    if (!File.Exists(deltaBadgePath) || !File.Exists(deltaBadgeCodePath))
    {
        throw new InvalidOperationException("DeltaBadge control is missing");
    }

    var deltaBadge = File.ReadAllText(deltaBadgePath);
    var deltaBadgeCode = File.ReadAllText(deltaBadgeCodePath);

    Contains(kpiCard, "DeltaBadge");
    Contains(kpiCard, "ToolTipService.ToolTip");
    Contains(kpiCard, "ChangeTooltip");
    Contains(kpiCard, "ChangeValue");
    NotContains(kpiCard, "ChangeText");
    NotContains(kpiCard, "vs previous period");

    Contains(deltaBadge, "DeltaBadgePadding");
    Contains(deltaBadgeCode, "ValueProperty");
    Contains(deltaBadgeCode, "InvertProperty");
    Contains(deltaBadgeCode, "SuffixProperty");
    Contains(deltaBadgeCode, "ShowSignProperty");
    Contains(deltaBadgeCode, "CultureInfo.InvariantCulture");
    Contains(deltaBadgeCode, "\\u25B2");
    Contains(deltaBadgeCode, "\\u25BC");
    Contains(deltaBadgeCode, "PositiveDeltaBrush");
    Contains(deltaBadgeCode, "NegativeDeltaBrush");
    Contains(deltaBadgeCode, "NeutralDeltaBrush");
    Contains(kpiCardCode, "ChangeTooltip");
    NotContains(kpiCardCode, "TextFillColorSecondaryBrush");
});

Run("Overview summary KPIs honor selected domain", () =>
{
    var root = FindRepositoryRoot();
    var dashboardViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "DashboardViewModel.cs"));
    var summaryRepository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "SummaryRepository.cs"));

    Contains(dashboardViewModel, "GetSummaryAsync(domain, range.Days)");
    Contains(summaryRepository, "GetSummaryAsync(string domainOrAll, int days)");
    Contains(summaryRepository, "SummaryDomainWhereClause(domainOrAll, \"domain\")");
    Contains(summaryRepository, "SummaryDomainWhereClause(domainOrAll, \"site_url\")");
    Contains(summaryRepository, "SiteIdentity.NormalizeDomain(domainOrAll)");
});

Run("English UI uses English-facing numeric formatting", () =>
{
    var root = FindRepositoryRoot();
    var dashboardViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "DashboardViewModel.cs"));
    var kpiCardCode = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "KpiCard.xaml.cs"));

    Contains(dashboardViewModel, "EnglishCulture");
    Contains(dashboardViewModel, "CultureInfo.InvariantCulture");
    Contains(kpiCardCode, "CultureInfo.InvariantCulture");
    Contains(kpiCardCode, "ChangeTooltip");
});

Run("Overview controls use user-facing period labels and polished health states", () =>
{
    var root = FindRepositoryRoot();
    var dashboardPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));
    var dashboardCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml.cs"));
    var cloudflareCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "CloudflarePage.xaml.cs"));
    var searchCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SearchConsolePage.xaml.cs"));
    var healthCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "HealthPage.xaml.cs"));
    var periodSelectorPath = Path.Combine(root, "Numeris", "Controls", "PeriodSelector.xaml");
    var periodSelectorCodePath = Path.Combine(root, "Numeris", "Controls", "PeriodSelector.xaml.cs");
    var periodModel = File.ReadAllText(Path.Combine(root, "Numeris", "Models", "Period.cs"));
    var converters = File.ReadAllText(Path.Combine(root, "Numeris", "Converters", "NumberConverters.cs"));
    var healthPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "HealthPage.xaml"));

    if (!File.Exists(periodSelectorPath) || !File.Exists(periodSelectorCodePath))
    {
        throw new InvalidOperationException("PeriodSelector control is missing");
    }

    var periodSelector = File.ReadAllText(periodSelectorPath);
    var periodSelectorCode = File.ReadAllText(periodSelectorCodePath);

    Contains(dashboardPage, "LastSyncText");
    Contains(dashboardPage, "PageSpeed mobile");
    Contains(periodModel, "ShortLabel");
    Contains(periodModel, "DisplayLabel");
    Contains(periodModel, "Last 7 days");
    Contains(periodModel, "Period.Last7Days.ShortLabel()");
    Contains(periodSelector, "PeriodSelectorSegmentButtonStyle");
    Contains(periodSelector, "AutomationProperties.Name=\"Period\"");
    Contains(periodSelectorCode, "SelectedPeriodProperty");
    Contains(periodSelectorCode, "SelectionChanged");
    Contains(periodSelectorCode, "PeriodOptions.All");
    Contains(periodSelectorCode, "period.ShortLabel()");
    Contains(periodSelectorCode, "PeriodSelectorSelectedBrush");
    Contains(periodSelectorCode, "PeriodSelectorSelectedForegroundBrush");
    Contains(periodSelectorCode, "PeriodSelectorSelectedBorderBrush");
    Contains(dashboardCode, "PeriodSelector.SelectedPeriod = Shell.SelectedPeriod");
    Contains(cloudflareCode, "PeriodSelector.SelectedPeriod = Shell.SelectedPeriod");
    Contains(searchCode, "PeriodSelector.SelectedPeriod = Shell.SelectedPeriod");
    Contains(healthCode, "PeriodSelector.SelectedPeriod = Shell.SelectedPeriod");
    Contains(converters, "CultureInfo.InvariantCulture");
    Contains(healthPage, "PrimaryActionButtonStyle");
    NotContains(dashboardPage, "Text=\"Pending\"");
    NotContains(dashboardPage, "x:Name=\"PeriodCombo\"");
    NotContains(dashboardCode, "PeriodCombo");
    NotContains(cloudflareCode, "new[] { Period.Last7Days");
    NotContains(searchCode, "new[] { Period.Last7Days");
    NotContains(healthCode, "new[] { Period.Last7Days");
    NotContains(healthPage, "AccentButtonStyle");
});

Run("Report pages use shared PeriodSelector instead of period ComboBox", () =>
{
    var root = FindRepositoryRoot();
    var pages = ProgramInputs.Vector2;

    foreach (var page in pages)
    {
        var xaml = File.ReadAllText(Path.Combine(root, "Numeris", "Views", $"{page}.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "Numeris", "Views", $"{page}.xaml.cs"));

        Contains(xaml, "<controls:PeriodSelector");
        Contains(xaml, "x:Name=\"PeriodSelector\"");
        Contains(xaml, "SelectionChanged=\"PeriodSelector_SelectionChanged\"");
        NotContains(xaml, "x:Name=\"PeriodCombo\"");
        NotContains(xaml, "DisplayMemberPath=\"Label\"");
        Contains(code, "PeriodSelector_SelectionChanged");
        Contains(code, "Shell.SelectedPeriod = PeriodSelector.SelectedPeriod");
        NotContains(code, "PeriodCombo");
        NotContains(code, "PeriodOptions.All.FirstOrDefault");
    }
});

Run("Dashboard chart colors come from the shared visual palette", () =>
{
    var root = FindRepositoryRoot();
    var dashboardViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "DashboardViewModel.cs"));
    var palettePath = Path.Combine(root, "Numeris", "Themes", "ChartPalette.cs");

    if (!File.Exists(palettePath))
    {
        throw new InvalidOperationException("ChartPalette.cs is missing");
    }

    var palette = File.ReadAllText(palettePath);
    Contains(palette, "NumerisAccentColor");
    Contains(palette, "ChartSecondaryColor");
    Contains(palette, "ChartPanelColor");
    Contains(palette, "NumerisTextTertiaryColor");
    Contains(dashboardViewModel, "ChartPalette.Accent");
    Contains(dashboardViewModel, "ChartPalette.Secondary");
    NotContains(dashboardViewModel, "SKColor.Parse(\"#D9A24E\")");
    NotContains(dashboardViewModel, "SKColor.Parse(\"#C97B6A\")");
});

Run("ChartPalette exposes matte bar and delta colors from tokens", () =>
{
    var root = FindRepositoryRoot();
    var palette = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "ChartPalette.cs"));
    var chartTheme = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "ChartTheme.cs"));
    var viewModelDir = Path.Combine(root, "Numeris", "ViewModels");

    Contains(palette, "BarNeutralTop => FromResource(\"ChartBarNeutralTopColor\"");
    Contains(palette, "BarNeutralMid => FromResource(\"ChartBarNeutralMidColor\"");
    Contains(palette, "BarNeutralBottom => FromResource(\"ChartBarNeutralBottomColor\"");
    Contains(palette, "BarHighlightTop => FromResource(\"ChartBarHighlightTopColor\"");
    Contains(palette, "BarHighlightMid => FromResource(\"ChartBarHighlightMidColor\"");
    Contains(palette, "BarHighlightBottom => FromResource(\"ChartBarHighlightBottomColor\"");
    Contains(palette, "BarCap => FromResource(\"ChartBarCapColor\"");
    Contains(palette, "ReferenceLine => FromResource(\"ChartReferenceLineColor\"");
    Contains(palette, "PositiveDelta => FromResource(\"PositiveDeltaColor\"");
    Contains(palette, "NegativeDelta => FromResource(\"NegativeDeltaColor\"");
    Contains(palette, "NeutralDelta => FromResource(\"NeutralDeltaColor\"");
    Contains(chartTheme, "ChartPalette.BarNeutralTop");
    Contains(chartTheme, "ChartPalette.BarHighlightMid");
    NotContains(chartTheme, "GetColor(\"ChartBar");

    foreach (var file in Directory.EnumerateFiles(viewModelDir, "*.cs", SearchOption.AllDirectories))
    {
        var text = File.ReadAllText(file);
        NotContains(text, "SKColor.Parse(");
        NotContains(text, "#4F73FF");
        NotContains(text, "#D9A24E");
    }
});

Run("Cloudflare page uses shared visual surfaces and chart palette", () =>
{
    var root = FindRepositoryRoot();
    var cloudflarePage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "CloudflarePage.xaml"));
    var cloudflareCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "CloudflarePage.xaml.cs"));
    var cloudflareViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "CloudflareViewModel.cs"));
    var horizontalBars = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "HorizontalBars.xaml.cs"));
    var projectFile = File.ReadAllText(Path.Combine(root, "Numeris", "Numeris.csproj"));
    var countryMapXamlPath = Path.Combine(root, "Numeris", "Controls", "CountryTrafficMap.xaml");
    var countryMapCodePath = Path.Combine(root, "Numeris", "Controls", "CountryTrafficMap.xaml.cs");
    var mapAssetsPath = Path.Combine(root, "Numeris", "Assets", "Maps");

    Contains(cloudflarePage, "Style=\"{StaticResource PageTitleTextBlockStyle}\"");
    Contains(cloudflarePage, "Style=\"{StaticResource TopTabSelectorBarStyle}\"");
    Contains(cloudflarePage, "Style=\"{StaticResource ChartCardBorderStyle}\"");
    Contains(cloudflarePage, "Style=\"{StaticResource ContentCardBorderStyle}\"");
    Contains(cloudflarePage, "x:Name=\"TrafficListsGrid\"");
    Contains(cloudflarePage, "x:Name=\"TopCountriesCard\"");
    Contains(cloudflarePage, "x:Name=\"TrafficCountryBars\"");
    Contains(cloudflarePage, "Items=\"{x:Bind ViewModel.TrafficCountries, Mode=OneWay}\"");
    Contains(cloudflarePage, "x:Name=\"TopPagesCard\"");
    Contains(cloudflareViewModel, "ChartPalette.Accent");
    Contains(cloudflareViewModel, "ChartPalette.Muted");
    Contains(cloudflareViewModel, "ChartPalette.Success");
    Contains(horizontalBars, "HorizontalBarTrackBrush");
    Contains(horizontalBars, "NumerisTextTertiaryBrush");
    NotContains(cloudflarePage, "controls:CountryTrafficMap");
    NotContains(cloudflarePage, "x:Name=\"TopCountriesOverlay\"");
    NotContains(cloudflarePage, "Canvas.ZIndex");
    NotContains(cloudflarePage, "CountryMapButton_Click");
    NotContains(cloudflarePage, "CountryBarsButton_Click");
    NotContains(cloudflarePage, "TrafficCountryBars\"\r\n                                                         Margin=\"0,64,0,0\"\r\n                                                         Visibility=\"Collapsed\"");
    NotContains(cloudflareCode, "ShowCountryMap");
    NotContains(cloudflareCode, "TrafficCountryMap");
    NotContains(cloudflareCode, "CountryMapButton_Click");
    NotContains(cloudflareCode, "CountryBarsButton_Click");
    NotContains(projectFile, "Assets\\Maps\\");
    if (File.Exists(countryMapXamlPath) || File.Exists(countryMapCodePath))
    {
        throw new InvalidOperationException("CountryTrafficMap control should be removed; country traffic uses HorizontalBars only.");
    }
    if (Directory.Exists(mapAssetsPath))
    {
        throw new InvalidOperationException("Map assets directory should be removed; country traffic uses HorizontalBars only.");
    }
    NotContains(cloudflarePage, "Background=\"{StaticResource ControlSurfaceBrush}\"");
    NotContains(projectFile, "Assets\\Maps\\d3-geo.min.js");
    NotContains(projectFile, "Assets\\Maps\\d3-array.min.js");
    NotContains(cloudflareViewModel, "SKColor.Parse(\"#D9A24E\")");
    NotContains(cloudflareViewModel, "SKColor.Parse(\"#C97B6A\")");
});

Run("Cloudflare volume charts use the shared matte bar style", () =>
{
    var root = FindRepositoryRoot();
    var cloudflarePage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "CloudflarePage.xaml"));
    var cloudflareCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "CloudflarePage.xaml.cs"));
    var cloudflareViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "CloudflareViewModel.cs"));

    Contains(cloudflareViewModel, "ChartTheme.CreateMatteColumnSeries(\"Visitors\"");
    Contains(cloudflareViewModel, "ChartTheme.CreateMutedColumnSeries(\"Pageviews\"");
    Contains(cloudflareViewModel, "ChartTheme.CreateMatteColumnSeries(\"Threats\"");
    Contains(cloudflareViewModel, "ChartTheme.CreateMatteColumnSeries(\"Visits\"");
    Contains(cloudflareViewModel, "ChartTheme.CreateMutedColumnSeries(\"Page views\"");
    Contains(cloudflareViewModel, "private static int? HighlightIndex(long[] values)");
    Contains(cloudflareViewModel, "private static int? LargestValueIndex(long[] values)");
    Contains(cloudflareViewModel, "HighlightIndex(visitors)");
    Contains(cloudflareViewModel, "LargestValueIndex(threats)");
    Contains(cloudflareViewModel, "StatusCodeGroup.Success => ChartPalette.Success.WithAlpha");
    Contains(cloudflareViewModel, "StatusCodeGroup.Redirect => ChartPalette.Muted.WithAlpha");
    Contains(cloudflareViewModel, "StatusCodeGroup.ClientError => ChartPalette.Warning.WithAlpha");
    Contains(cloudflareViewModel, "StatusCodeGroup.ServerError => ChartPalette.Danger.WithAlpha");
    Contains(cloudflareViewModel, "ChartTheme.StyleStackedColumnSeries(new StackedColumnSeries<long>");
    NotContains(cloudflareViewModel, "CreateLine(\"Visitors\"");
    NotContains(cloudflareViewModel, "CreateLine(\"Threats\"");
    NotContains(cloudflareViewModel, "CreateLine(\"Visits\"");

    Contains(cloudflareCode, "BuildBarChart(ref _trafficChart, TrafficChartHost)");
    Contains(cloudflareCode, "BuildBarChart(ref _securityChart, SecurityChartHost)");
    Contains(cloudflareCode, "BuildBarChart(ref _statusChart, StatusChartHost)");
    Contains(cloudflareCode, "BuildBarChart(ref _waChart, WaChartHost)");
    Contains(cloudflareCode, "ChartTheme.CreateBarChartSurface(chart)");

    Contains(cloudflarePage, "x:Name=\"TrafficCountryBars\"");
    Contains(cloudflarePage, "Items=\"{x:Bind ViewModel.TrafficPages, Mode=OneWay}\"");
    Contains(cloudflarePage, "Items=\"{x:Bind ViewModel.WaReferrers, Mode=OneWay}\"");
});

Run("Cloudflare traffic sync persists top country and page breakdowns", () =>
{
    var root = FindRepositoryRoot();
    var client = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Api", "CloudflareGraphqlClient.cs"));
    var repository = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Repositories", "CloudflareRepository.cs"));
    var migrations = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "Migrations.cs"));
    var database = File.ReadAllText(Path.Combine(root, "Numeris", "Services", "Database", "SqliteDatabase.cs"));

    Contains(client, "topCountries: httpRequestsAdaptiveGroups");
    Contains(client, "topPages: httpRequestsAdaptiveGroups");
    Contains(client, "clientCountryName");
    Contains(client, "clientRequestPath");
    Contains(client, "requestSource: \"eyeball\"");
    Contains(client, "List<CloudflareCountryRow> Countries");
    Contains(client, "List<CloudflarePageRow> Pages");

    Contains(migrations, "CurrentSchemaVersion = 11");
    Contains(migrations, "CREATE TABLE IF NOT EXISTS cloudflare_pages");
    Contains(migrations, "UNIQUE(domain, date, path)");
    Contains(migrations, "RunV5Migration");
    Contains(database, "DELETE FROM cloudflare_pages;");

    Contains(repository, "INSERT INTO cloudflare_countries");
    Contains(repository, "INSERT INTO cloudflare_pages");
    Contains(repository, "DELETE FROM cloudflare_countries WHERE domain = @domain AND date >= @start AND date <= @end");
    Contains(repository, "DELETE FROM cloudflare_pages WHERE domain = @domain AND date >= @start AND date <= @end");
    Contains(repository, "FROM cloudflare_pages");
    NotContains(repository, "top_path AS Path");
});

Run("HorizontalBars rerenders when observable item collections change", () =>
{
    var root = FindRepositoryRoot();
    var horizontalBars = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "HorizontalBars.xaml.cs"));

    Contains(horizontalBars, "using System.Collections.Specialized;");
    Contains(horizontalBars, "INotifyCollectionChanged?");
    Contains(horizontalBars, "CollectionChanged +=");
    Contains(horizontalBars, "CollectionChanged -=");
    Contains(horizontalBars, "OnItemsCollectionChanged");
    Contains(horizontalBars, "bars.Render();");
});

Run("HorizontalBars renders matte gradient bars with optional top highlight", () =>
{
    var root = FindRepositoryRoot();
    var horizontalBars = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "HorizontalBars.xaml.cs"));
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));

    Contains(horizontalBars, "HighlightTopValueProperty");
    Contains(horizontalBars, "public bool HighlightTopValue");
    Contains(horizontalBars, "CompactProperty");
    Contains(horizontalBars, "public bool Compact");
    NotContains(horizontalBars, "ValueFormatProperty");

    Contains(horizontalBars, "CreateBarFill(isHighlight)");
    Contains(horizontalBars, "new LinearGradientBrush");
    Contains(horizontalBars, "new GradientStop");
    Contains(horizontalBars, "StartPoint = new Point(0, 0.5)");
    Contains(horizontalBars, "EndPoint = new Point(1, 0.5)");
    Contains(horizontalBars, "ChartBarHighlightTopColor");
    Contains(horizontalBars, "ChartBarNeutralTopColor");
    Contains(horizontalBars, "ChartBarNeutralMidColor");
    Contains(horizontalBars, "ChartBarNeutralBottomColor");
    Contains(horizontalBars, "row.Value == max");
    Contains(horizontalBars, "HighlightTopValue");
    Contains(horizontalBars, "NumerisTextSecondaryBrush");
    Contains(horizontalBars, "NumerisTextTertiaryBrush");
    Contains(horizontalBars, "Text = \"No data\"");
    Contains(horizontalBars, "Foreground = tertiaryText");
    NotContains(horizontalBars, "Fill = accent");

    Contains(tokens, "HorizontalBarHeight");
    Contains(tokens, "HorizontalBarCompactHeight");
    Contains(tokens, "HorizontalBarCornerRadius");
    Contains(tokens, "HorizontalBarCompactCornerRadius");
});

Run("Cloudflare status codes are summarized into understandable HTTP groups", () =>
{
    var rows = new[]
    {
        new StatusCodeDay { Date = "2026-05-01", StatusCode = 200, Requests = 900 },
        new StatusCodeDay { Date = "2026-05-01", StatusCode = 304, Requests = 50 },
        new StatusCodeDay { Date = "2026-05-01", StatusCode = 404, Requests = 25 },
        new StatusCodeDay { Date = "2026-05-01", StatusCode = 522, Requests = 5 },
        new StatusCodeDay { Date = "2026-05-02", StatusCode = 200, Requests = 100 },
        new StatusCodeDay { Date = "2026-05-02", StatusCode = 301, Requests = 50 },
        new StatusCodeDay { Date = "2026-05-02", StatusCode = 499, Requests = 20 },
        new StatusCodeDay { Date = "2026-05-02", StatusCode = 530, Requests = 5 },
    };

    var insight = CloudflareViewModel.BuildStatusCodeInsight(rows);

    Equal("86.6%", insight.SuccessRateText);
    Equal("45 (3.9%)", insight.ClientErrorsText);
    Equal("10 (0.9%)", insight.ServerErrorsText);
    Equal("404 Not Found - 25 requests", insight.TopIssueText);
    Equal("2xx Success", insight.Groups[0].Label);
    Equal("3xx Redirects", insight.Groups[1].Label);
    Equal("4xx Client errors", insight.Groups[2].Label);
    Equal("5xx Server / edge errors", insight.Groups[3].Label);
    Equal("404", insight.TopCodes[0].Code);
    Equal("Not Found", insight.TopCodes[0].Description);
    Equal("2.2%", insight.TopCodes[0].ShareText);
});

Run("Cloudflare status codes tab explains numbers instead of exposing a raw code legend", () =>
{
    var root = FindRepositoryRoot();
    var cloudflarePage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "CloudflarePage.xaml"));
    var cloudflareViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "CloudflareViewModel.cs"));
    var chartPalette = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "ChartPalette.cs"));

    Contains(cloudflarePage, "Text=\"HTTP responses by status group\"");
    Contains(cloudflarePage, "Text=\"Top status codes\"");
    Contains(cloudflarePage, "StatusTopCodes");
    Contains(cloudflarePage, "StatusSuccessRateText");
    Contains(cloudflarePage, "StatusTopIssueText");
    Contains(cloudflareViewModel, "BuildStatusCodeInsight");
    Contains(cloudflareViewModel, "StatusCodeGroup.Success");
    Contains(cloudflareViewModel, "StatusCodeDescription");
    Contains(chartPalette, "Warning");
    Contains(chartPalette, "Danger");
    Contains(chartPalette, "Info");
    NotContains(cloudflareViewModel, "Name = group.Key.ToString(CultureInfo.InvariantCulture)");
});

Run("Search page uses shared visual surfaces and chart palette", () =>
{
    var root = FindRepositoryRoot();
    var searchPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SearchConsolePage.xaml"));
    var searchViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "SearchConsoleViewModel.cs"));

    Contains(searchPage, "Style=\"{StaticResource PageTitleTextBlockStyle}\"");
    Contains(searchPage, "Style=\"{StaticResource TopTabSelectorBarStyle}\"");
    Contains(searchPage, "Style=\"{StaticResource ChartCardBorderStyle}\"");
    Contains(searchPage, "Style=\"{StaticResource ContentCardBorderStyle}\"");
    Contains(searchPage, "Style=\"{StaticResource DataListViewStyle}\"");
    Contains(searchPage, "Style=\"{StaticResource DataTableHeaderTextBlockStyle}\"");
    Contains(searchViewModel, "ChartPalette.Accent");
    Contains(searchViewModel, "ChartPalette.Secondary");
    NotContains(searchViewModel, "SKColor.Parse(\"#D9A24E\")");
    NotContains(searchViewModel, "SKColor.Parse(\"#C97B6A\")");
});

Run("Bing page uses shared visual surfaces and chart palette", () =>
{
    var root = FindRepositoryRoot();
    var bingPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "BingPage.xaml"));
    var bingPageCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "BingPage.xaml.cs"));
    var bingViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "BingViewModel.cs"));

    Contains(bingPage, "Style=\"{StaticResource PageTitleTextBlockStyle}\"");
    Contains(bingPage, "Style=\"{StaticResource TopTabSelectorBarStyle}\"");
    Contains(bingPage, "Style=\"{StaticResource ChartCardBorderStyle}\"");
    Contains(bingPage, "Style=\"{StaticResource ContentCardBorderStyle}\"");
    Contains(bingPage, "Style=\"{StaticResource DataListViewStyle}\"");
    Contains(bingPageCode, "ChartTheme.CreateCartesianChart()");
    Contains(bingPageCode, "ChartTheme.CreateBarChartSurface(chart)");
    Contains(bingViewModel, "ChartTheme.CreateMatteColumnSeries(\"Clicks\"");
    Contains(bingViewModel, "ChartTheme.CreateMutedColumnSeries(\"Impressions\"");
    NotContains(bingViewModel, "SKColor.Parse(\"#D9A24E\")");
});

Run("Performance page uses shared visual surfaces and chart palette", () =>
{
    var root = FindRepositoryRoot();
    var performancePage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "PerformancePage.xaml"));
    var performancePageCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "PerformancePage.xaml.cs"));
    var performanceViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "PerformanceViewModel.cs"));

    Contains(performancePage, "Style=\"{StaticResource PageTitleTextBlockStyle}\"");
    Contains(performancePage, "Style=\"{StaticResource TopTabSelectorBarStyle}\"");
    Contains(performancePage, "Style=\"{StaticResource ChartCardBorderStyle}\"");
    Contains(performancePage, "Style=\"{StaticResource ContentCardBorderStyle}\"");
    Contains(performancePage, "Style=\"{StaticResource DataListViewStyle}\"");
    Contains(performancePageCode, "ChartTheme.CreateCartesianChart()");
    Contains(performancePageCode, "ChartTheme.CreateChartSurface(chart)");
    Contains(performanceViewModel, "ChartPalette.Accent");
    Contains(performanceViewModel, "ChartTheme.CreateMutedColumnSeries(\"Desktop\"");
    NotContains(performanceViewModel, "SKColor.Parse(\"#D9A24E\")");
});

Run("Health page uses shared visual surfaces and chart palette", () =>
{
    var root = FindRepositoryRoot();
    var healthPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "HealthPage.xaml"));
    var healthViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "HealthViewModel.cs"));

    Contains(healthPage, "Style=\"{StaticResource PageTitleTextBlockStyle}\"");
    Contains(healthPage, "Style=\"{StaticResource TopTabSelectorBarStyle}\"");
    Contains(healthPage, "Style=\"{StaticResource ChartCardBorderStyle}\"");
    Contains(healthPage, "Style=\"{StaticResource ContentCardBorderStyle}\"");
    Contains(healthPage, "Style=\"{StaticResource DataListViewStyle}\"");
    Contains(healthPage, "HorizontalScrollBarVisibility=\"Auto\"");
    Contains(healthPage, "HorizontalScrollMode=\"Enabled\"");
    Contains(healthPage, "MinWidth=\"{StaticResource ReportDataMinWidth}\"");
    Contains(healthPage, "Margin=\"{StaticResource PageHeaderMargin}\"");
    Contains(healthPage, "Padding=\"{StaticResource PageScrollContentPadding}\"");
    Contains(healthPage, "UptimeOverviewText");
    Contains(healthPage, "SitemapLastUpdatedText");
    Contains(healthPage, "Last seen");
    Contains(healthPage, "IsoDateTimeDisplayConverter");
    Contains(healthViewModel, "ChartPalette.Accent");
    Contains(healthViewModel, "ChartTheme.CreateMatteColumnSeries(\"Failed probes\"");
    Contains(healthViewModel, "BuildResponseAxisMax");
    Contains(healthViewModel, "SitemapLastUpdatedText");
    NotContains(healthViewModel, "SKColor.Parse(\"#D9A24E\")");
    NotContains(healthViewModel, "SKColor.Parse(\"#C97B6A\")");
});

Run("matte action button styling is centralized for all regular buttons", () =>
{
    var root = FindRepositoryRoot();
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    var xamlFiles = Directory.EnumerateFiles(Path.Combine(root, "Numeris"), "*.xaml", SearchOption.AllDirectories)
        .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    var allXaml = string.Join(Environment.NewLine, xamlFiles.Select(File.ReadAllText));

    Contains(tokens, "PrimaryActionButtonStyle");
    Contains(tokens, "TargetType=\"Button\" BasedOn=\"{StaticResource PrimaryActionButtonStyle}\"");
    Contains(tokens, "MatteButtonSurfaceColor");
    Contains(tokens, "#FF222526");
    Contains(tokens, "AccentActionButtonStyle");
    Contains(tokens, "NumerisAccentForegroundBrush");
    Contains(tokens, "<ControlTemplate TargetType=\"Button\">");
    Contains(tokens, "<VisualState x:Name=\"PointerOver\">");
    Contains(tokens, "<VisualState x:Name=\"Pressed\">");
    Contains(tokens, "<Setter Property=\"BorderThickness\" Value=\"0\" />");
    Contains(tokens, "BorderThickness=\"0\"");
    NotContains(tokens, "MatteButtonDepthBrush");
    NotContains(tokens, "MatteButtonInnerShadeBrush");
    NotContains(tokens, "DepthEdge");
    NotContains(tokens, "PressTransform");
    NotContains(tokens, "Target=\"PressTransform.Y\"");
    NotContains(tokens, "TopHighlight");
    NotContains(tokens, "MatteButtonTopHighlightBrush");
    NotContains(tokens, "MatteButtonBorderBrush");
    NotContains(tokens, "MatteRaisedPreviewButtonStyle");
    NotContains(allXaml, "MatteRaisedPreviewButtonStyle");
    NotContains(allXaml, "AccentButtonStyle");
    Contains(allXaml, "Style=\"{StaticResource PrimaryActionButtonStyle}\"");
    Contains(allXaml, "Style=\"{StaticResource SecondaryActionButtonStyle}\"");
    Contains(allXaml, "Style=\"{StaticResource DangerActionButtonStyle}\"");
});

Run("Sources page uses accent actions only for committed source changes", () =>
{
    var root = FindRepositoryRoot();
    var sourcesPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml"));

    Equal("9", CountOccurrences(sourcesPage, "Style=\"{StaticResource AccentActionButtonStyle}\"").ToString());
    NotContains(sourcesPage, "Style=\"{StaticResource PrimaryActionButtonStyle}\"");
    Contains(sourcesPage, "Content=\"Connect Google account\" Click=\"ConnectScButton_Click\" IsEnabled=\"{x:Bind ViewModel.SearchConsole.CanRun, Mode=OneWay}\" Style=\"{StaticResource AccentActionButtonStyle}\"");
    Contains(sourcesPage, "Content=\"Save\" Style=\"{StaticResource AccentActionButtonStyle}\"");
    Contains(sourcesPage, "Content=\"Add\" VerticalAlignment=\"Bottom\" IsEnabled=\"{x:Bind ViewModel.WebAnalytics.CanRun, Mode=OneWay}\" Click=\"AddWaSiteButton_Click\" Style=\"{StaticResource AccentActionButtonStyle}\"");
    Contains(sourcesPage, "Content=\"Add\" VerticalAlignment=\"Bottom\" IsEnabled=\"{x:Bind ViewModel.Performance.CanRun, Mode=OneWay}\" Click=\"AddPerformanceUrlButton_Click\" Style=\"{StaticResource AccentActionButtonStyle}\"");
    Contains(sourcesPage, "Content=\"Add\" VerticalAlignment=\"Bottom\" IsEnabled=\"{x:Bind ViewModel.Bing.CanRun, Mode=OneWay}\" Click=\"AddBingSiteButton_Click\" Style=\"{StaticResource AccentActionButtonStyle}\"");
    Contains(sourcesPage, "Content=\"Test\" Click=\"TestCfButton_Click\" IsEnabled=\"{x:Bind ViewModel.Cloudflare.CanRun, Mode=OneWay}\" Style=\"{StaticResource SecondaryActionButtonStyle}\"");
    Contains(sourcesPage, "Content=\"Delete\" Click=\"DeleteBingButton_Click\" IsEnabled=\"{x:Bind ViewModel.Bing.CanRun, Mode=OneWay}\" Style=\"{StaticResource DangerActionButtonStyle}\"");
});

Run("Report volume charts use matte bars without forcing continuous metrics", () =>
{
    var root = FindRepositoryRoot();
    var searchCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SearchConsolePage.xaml.cs"));
    var searchViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "SearchConsoleViewModel.cs"));
    var bingCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "BingPage.xaml.cs"));
    var bingViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "BingViewModel.cs"));
    var performanceCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "PerformancePage.xaml.cs"));
    var performanceViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "PerformanceViewModel.cs"));
    var healthCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "HealthPage.xaml.cs"));
    var healthViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "HealthViewModel.cs"));

    Contains(searchViewModel, "ChartTheme.CreateMatteColumnSeries(device");
    NotContains(searchViewModel, "series.Add(CreateLine(dev");
    Contains(searchCode, "BuildChart(ref _overviewChart, OverviewChartHost)");
    Contains(searchCode, "BuildBarChart(ref _devicesChart, DevicesChartHost)");


    Contains(bingViewModel, "ChartTheme.CreateMatteColumnSeries(\"Clicks\"");
    Contains(bingViewModel, "ChartTheme.CreateMutedColumnSeries(\"Impressions\"");
    NotContains(bingViewModel, "CreateLine(\"Clicks\"");
    Contains(bingCode, "BuildBarChart(ref _trafficChart, TrafficChartHost)");

    Contains(performanceViewModel, "ChartTheme.CreateMatteColumnSeries(\"Mobile\"");
    Contains(performanceViewModel, "ChartTheme.CreateMutedColumnSeries(\"Desktop\"");
    NotContains(performanceViewModel, "CreateScoreLine(\"Mobile\"");
    Contains(performanceCode, "BuildChart(ref _cruxChart, CruxChartHost)");
    Contains(performanceCode, "BuildBarChart(ref _pageSpeedChart, PageSpeedChartHost)");

    Contains(healthViewModel, "ChartTheme.CreateMatteColumnSeries(\"Failed probes\"");
    Contains(healthViewModel, "new LineSeries<double?>");
    Contains(healthCode, "BuildChart(ref _responseChart, ResponseChartHost)");
    Contains(healthCode, "BuildBarChart(ref _incidentsChart, IncidentsChartHost)");

    Contains(searchCode + bingCode + performanceCode + healthCode, "ChartTheme.CreateBarChartSurface(chart)");
});

Run("Sources page presents integrations as shared settings cards", () =>
{
    var root = FindRepositoryRoot();
    var sourcesPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml"));

    Contains(sourcesPage, "Style=\"{StaticResource PageTitleTextBlockStyle}\"");
    Contains(sourcesPage, "Style=\"{StaticResource ContentCardBorderStyle}\"");
    Contains(sourcesPage, "HorizontalScrollBarVisibility=\"Disabled\"");
    NotContains(sourcesPage, "MaxWidth=\"980\"");
    Contains(sourcesPage, "<Style TargetType=\"Expander\">");
    Contains(sourcesPage, "<Setter Property=\"HorizontalAlignment\" Value=\"Stretch\" />");
    Contains(sourcesPage, "<Setter Property=\"HorizontalContentAlignment\" Value=\"Stretch\" />");
    Contains(sourcesPage, "Traffic, cache, security events");
    Contains(sourcesPage, "Clicks, impressions, pages, indexing");
    Contains(sourcesPage, "Core Web Vitals and PageSpeed lab data");
    Contains(sourcesPage, "Search visibility and indexing data");
    Contains(sourcesPage, "StatusMessage");
    NotContains(sourcesPage, "Without configuration, mock data is used");
});

Run("Sources page groups settings by provider", () =>
{
    var root = FindRepositoryRoot();
    var sourcesPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml"));

    Equal("3", CountOccurrences(sourcesPage, "Style=\"{StaticResource ContentCardBorderStyle}\"").ToString());
    Contains(sourcesPage, "Text=\"Cloudflare\"");
    Contains(sourcesPage, "Text=\"Google\"");
    Contains(sourcesPage, "Text=\"Bing\"");
    Contains(sourcesPage, "Zone Analytics");
    Contains(sourcesPage, "Web Analytics");
    Contains(sourcesPage, "Search Console and Performance");
    Contains(sourcesPage, "Search Console");
    Contains(sourcesPage, "Web Performance");
    Contains(sourcesPage, "Bing Webmaster");
    NotContains(sourcesPage, "Text=\"YouTube\"");
    NotContains(sourcesPage, "Text=\"Cloudflare Web Analytics\"");
    NotContains(sourcesPage, "Text=\"Google Search Console\"");
    NotContains(sourcesPage, "Text=\"Microsoft Bing Webmaster Tools\"");
});

Run("Sources page hides credential forms behind collapsed editors", () =>
{
    var root = FindRepositoryRoot();
    var sourcesPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml"));

    Contains(sourcesPage, "Header=\"Add Cloudflare Zone Analytics connection\"");
    Contains(sourcesPage, "Header=\"Edit Cloudflare Web Analytics credentials\"");
    Contains(sourcesPage, "Header=\"Edit Google Search Console credentials\"");
    Contains(sourcesPage, "Header=\"Edit Google Web Performance API keys\"");
    Contains(sourcesPage, "Header=\"Edit Bing Webmaster API key\"");
    Equal("5", CountOccurrences(sourcesPage, "IsExpanded=\"False\"").ToString());
    Contains(sourcesPage, "Content=\"Test CrUX\"");
    Contains(sourcesPage, "Content=\"Test PageSpeed\"");
    NotContains(sourcesPage, "Header=\"Advanced: override YouTube OAuth credentials\"");
    NotContains(sourcesPage, "Content=\"Sync\"");
    Contains(sourcesPage, "Content=\"Delete\"");
    Contains(sourcesPage, "API key saved");
    Contains(sourcesPage, "OAuth credentials saved");
});

Run("Sources page does not expose per-integration sync actions", () =>
{
    var root = FindRepositoryRoot();
    var sourcesPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml"));
    var sourcesCode = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml.cs"));

    NotContains(sourcesPage, "Content=\"Sync\"");
    NotContains(sourcesPage, "SyncButton_Click");
    NotContains(sourcesCode, "SyncButton_Click");
    NotContains(sourcesCode, ".SyncAsync(");
});

Run("Sources page provider headers reuse navigation brand icons", () =>
{
    var root = FindRepositoryRoot();
    var sourcesPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml"));

    Contains(sourcesPage, "Source=\"ms-appx:///Assets/Icons/cloudflare-filled.png\"");
    Contains(sourcesPage, "Source=\"ms-appx:///Assets/Icons/google-search-filled.png\"");
    Contains(sourcesPage, "Source=\"ms-appx:///Assets/Icons/bing-filled.png\"");
    NotContains(sourcesPage, "Source=\"ms-appx:///Assets/Icons/youtube-filled.png\"");
    Contains(sourcesPage, "<ImageIcon Grid.Column=\"0\"");
    Contains(sourcesPage, "Height=\"20\"");
    NotContains(sourcesPage, "<FontIcon Grid.Column=\"0\"");
});

Run("UI improvement implementation plan is saved at repository root", () =>
{
    var root = FindRepositoryRoot();
    var planPath = Path.Combine(root, "UI-IMPROVEMENT-IMPLEMENTATION-PLAN.md");
    if (!File.Exists(planPath))
    {
        throw new InvalidOperationException("UI improvement implementation plan is missing from repository root");
    }

    var plan = File.ReadAllText(planPath);
    Contains(plan, "SelectorBar");
    Contains(plan, "AutomationProperties");
    Contains(plan, "VisualStateManager");
    Contains(plan, "InfoBar");
    Contains(plan, "DataTable");
    Contains(plan, "Sources");
});

Run("Report pages use SelectorBar for local view switching", () =>
{
    var root = FindRepositoryRoot();
    var pages = ProgramInputs.Vector3;

    foreach (var page in pages)
    {
        var xaml = File.ReadAllText(Path.Combine(root, "Numeris", "Views", page));
        Contains(xaml, "<SelectorBar");
        Contains(xaml, "Style=\"{StaticResource TopTabSelectorBarStyle}\"");
        NotContains(xaml, "<NavigationView Grid.Row=\"1\"");
    }

    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    Contains(tokens, "TopTabSelectorBarStyle");
    NotContains(tokens, "TopTabNavigationViewStyle");
});

Run("Report toolbars expose automation names and loading guards", () =>
{
    var root = FindRepositoryRoot();
    var pages = ProgramInputs.Vector4;

    foreach (var page in pages)
    {
        var xaml = File.ReadAllText(Path.Combine(root, "Numeris", "Views", page));
        Contains(xaml, "AutomationProperties.Name=\"Period\"");
        Contains(xaml, "AutomationProperties.Name=\"Refresh current report\"");
        Contains(xaml, "IsEnabled=\"{x:Bind ViewModel.CanRefresh, Mode=OneWay}\"");
    }

    foreach (var page in pages)
    {
        var xaml = File.ReadAllText(Path.Combine(root, "Numeris", "Views", page));
        Contains(xaml, "AutomationProperties.Name=\"Domain\"");
    }
});

Run("Report toolbar controls use matte surfaces and complete refresh icons", () =>
{
    var root = FindRepositoryRoot();
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    var pages = ProgramInputs.Vector4;

    Contains(tokens, "DomainComboBoxStyle");
    Contains(tokens, "ToolbarRefreshIconStyle");
    Contains(tokens, "ToolbarRefreshContentTemplate");
    Contains(tokens, "ToolbarRefreshButtonStyle");
    Contains(tokens, "ComboBoxDropDownGlyphForeground");
    Contains(tokens, "ComboBoxBackground");
    Contains(tokens, "MatteButtonSurfaceBrush");
    Contains(tokens, "<TranslateTransform X=\"-2\" Y=\"-2\" />");
    NotContains(tokens, "<Geometry x:Key=\"RefreshIconData\"");
    NotContains(tokens, "<Setter Property=\"Data\"");

    foreach (var page in pages)
    {
        var xaml = File.ReadAllText(Path.Combine(root, "Numeris", "Views", page));
        Contains(xaml, "x:Name=\"DomainCombo\"");
        Contains(xaml, "Style=\"{StaticResource DomainComboBoxStyle}\"");
        Contains(xaml, "Style=\"{StaticResource ToolbarRefreshButtonStyle}\"");
        NotContains(xaml, "<PathIcon");
        NotContains(xaml, "FontIcon Glyph=\"&#xE72C;\"");
    }
});

Run("Report refresh buttons run service sync before reloading local data", () =>
{
    var root = FindRepositoryRoot();
    var reportPages = ProgramInputs.Vector5;

    foreach (var page in reportPages)
    {
        var code = File.ReadAllText(Path.Combine(root, "Numeris", "Views", page));
        Contains(code, "await ViewModel.RefreshAsync();");
        NotContains(code, "RefreshButton_Click(object sender, RoutedEventArgs e)\r\n    {\r\n        await ViewModel.LoadAsync();");
    }

    foreach (var viewModel in ProgramInputs.Vector6)
    {
        var code = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", viewModel));
        Contains(code, "public async Task RefreshAsync()");
        Contains(code, "SyncConfiguredAsync");
        Contains(code, "await LoadAsync();");
    }
});

Run("Report pages define responsive visual states", () =>
{
    var root = FindRepositoryRoot();
    var pages = ProgramInputs.Vector4;

    foreach (var page in pages)
    {
        var xaml = File.ReadAllText(Path.Combine(root, "Numeris", "Views", page));
        Contains(xaml, "<VisualStateManager.VisualStateGroups>");
        Contains(xaml, "x:Name=\"NarrowLayout\"");
        Contains(xaml, "x:Name=\"WideLayout\"");
    }
});

Run("Report layouts use responsive headers, hero spacing, and narrow KPI grids", () =>
{
    var root = FindRepositoryRoot();
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    var periodSelector = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "PeriodSelector.xaml"));
    var pages = ProgramInputs.Vector4;

    Contains(tokens, "ReportHeroMinHeight");
    Contains(tokens, "ReportHeroChartHeight");
    Contains(tokens, "ReportSecondaryChartHeight");
    Contains(tokens, "ReportCompactChartHeight");
    Contains(tokens, "PeriodSelectorMinWidth");
    Contains(tokens, "ChartCardPadding\">28,22,28,22");
    Contains(periodSelector, "MinWidth=\"{StaticResource PeriodSelectorMinWidth}\"");

    foreach (var page in pages)
    {
        var xaml = File.ReadAllText(Path.Combine(root, "Numeris", "Views", page));
        Contains(xaml, "x:Name=\"HeaderGrid\"");
        Contains(xaml, "x:Name=\"HeaderControls\"");
        Contains(xaml, "x:Name=\"ToolbarActionRow\"");
        Contains(xaml, "x:Name=\"ToolbarPeriodColumn\"");
        Contains(xaml, "Target=\"ToolbarActionRow.HorizontalAlignment\" Value=\"Stretch\"");
        Contains(xaml, "Target=\"ToolbarPeriodColumn.Width\" Value=\"*\"");
        Contains(xaml, "Target=\"HeaderControls.Orientation\" Value=\"Horizontal\"");
        Contains(xaml, "Target=\"HeaderControls.(Grid.Row)\" Value=\"1\"");
        Contains(xaml, "Target=\"HeaderControls.(Grid.Row)\" Value=\"0\"");
        Contains(xaml, "Target=\"HeaderControls.(Grid.ColumnSpan)\" Value=\"2\"");
        Contains(xaml, "Target=\"PeriodSelector.HorizontalAlignment\" Value=\"Stretch\"");
    }

    var dashboard = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "DashboardPage.xaml"));
    Contains(dashboard, "MinHeight=\"{StaticResource ReportHeroMinHeight}\"");
    Contains(dashboard, "Style=\"{StaticResource ResponsiveKpiGridStyle}\"");
    Contains(dashboard, "x:Name=\"GoogleClicksKpi\"");
    Contains(dashboard, "x:Name=\"BingClicksKpi\"");
    Contains(dashboard, "x:Name=\"WebVitalsKpi\"");
    Contains(dashboard, "x:Name=\"PageSpeedKpi\"");
    Contains(dashboard, "Target=\"WebVitalsKpi.(Grid.Row)\" Value=\"1\"");
    Contains(dashboard, "Target=\"PageSpeedKpi.(Grid.Row)\" Value=\"1\"");

    var cloudflare = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "CloudflarePage.xaml"));
    Contains(cloudflare, "MinHeight=\"{StaticResource ReportHeroChartHeight}\"");
    Contains(cloudflare, "x:Name=\"StatusMetricsGrid\"");
    Contains(cloudflare, "Target=\"StatusServerErrorsKpi.(Grid.Row)\" Value=\"1\"");
    Contains(cloudflare, "Target=\"StatusTopIssueCard.(Grid.Row)\" Value=\"1\"");
});

Run("Matte UI test coverage enforces centralized visual resources", () =>
{
    var root = FindRepositoryRoot();
    var tokens = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "Tokens.xaml"));
    var chartPalette = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "ChartPalette.cs"));
    var chartTheme = File.ReadAllText(Path.Combine(root, "Numeris", "Themes", "ChartTheme.cs"));
    var kpiCard = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "KpiCard.xaml"));
    var deltaBadgeCode = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "DeltaBadge.xaml.cs"));
    var horizontalBars = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "HorizontalBars.xaml.cs"));
    var periodSelectorCode = File.ReadAllText(Path.Combine(root, "Numeris", "Controls", "PeriodSelector.xaml.cs"));
    var dashboardViewModel = File.ReadAllText(Path.Combine(root, "Numeris", "ViewModels", "DashboardViewModel.cs"));
    var viewModelFiles = Directory.EnumerateFiles(Path.Combine(root, "Numeris", "ViewModels"), "*ViewModel.cs", SearchOption.AllDirectories)
        .Select(path => (Path: path, Text: File.ReadAllText(path)))
        .ToArray();
    var reportPages = ProgramInputs.Vector4;

    foreach (var resource in ProgramInputs.Vector7)
    {
        Contains(tokens, $"x:Key=\"{resource}\"");
        Contains(chartPalette, $"FromResource(\"{resource}\"");
    }

    Contains(chartTheme, "CreateMatteColumnSeries<T>");
    Contains(chartTheme, "CreateMatteBarFill");
    Contains(chartTheme, "CreateHighlightBarFill");
    Contains(chartTheme, "CreateVerticalGradient(");
    Contains(dashboardViewModel, "ChartTheme.CreateMatteColumnSeries(\"Visitors\"");
    Contains(kpiCard, "<controls:DeltaBadge");
    Contains(deltaBadgeCode, "PositiveDeltaBrush");
    Contains(deltaBadgeCode, "NegativeDeltaBrush");
    Contains(deltaBadgeCode, "NeutralDeltaBrush");
    Contains(horizontalBars, "GetBrush(\"HorizontalBarTrackBrush\")");
    Contains(horizontalBars, "GetBrush(\"NumerisTextSecondaryBrush\")");
    Contains(horizontalBars, "GetBrush(\"NumerisTextTertiaryBrush\")");
    Contains(horizontalBars, "GetColor(isHighlight ? \"ChartBarHighlightTopColor\" : \"ChartBarNeutralTopColor\")");

    foreach (var page in reportPages)
    {
        var xaml = File.ReadAllText(Path.Combine(root, "Numeris", "Views", page));
        Contains(xaml, "<controls:PeriodSelector");
        NotContains(xaml, "x:Name=\"PeriodCombo\"");
    }

    foreach (var viewModel in viewModelFiles)
    {
        NotContains(viewModel.Text, "LinearGradientPaint");
        NotContains(viewModel.Text, "LinearGradientBrush");
        NotContains(viewModel.Text, "GradientStop");
        NotContains(viewModel.Text, "SKColor.Parse(");
        NotContains(viewModel.Text, "#4F73FF");
        NotContains(viewModel.Text, "#D9A24E");
        NotContains(viewModel.Text, "#C97B6A");
    }

    NotContains(chartPalette, ", new SKColor(");
    NotContains(chartTheme, "Windows.UI.Color.FromArgb");
    NotContains(deltaBadgeCode, "Color.FromArgb");
    NotContains(horizontalBars, "Color.FromArgb");
    NotContains(periodSelectorCode, "Color.FromArgb");
    NotContains(kpiCard, "TextFillColorSecondaryBrush");
});

Run("Sources page exposes status bars and guarded actions", () =>
{
    var root = FindRepositoryRoot();
    var sourcesPage = File.ReadAllText(Path.Combine(root, "Numeris", "Views", "SourcesPage.xaml"));
    var sourceViewModels = Directory.EnumerateFiles(Path.Combine(root, "Numeris", "ViewModels", "Sources"), "*SourceViewModel.cs")
        .Select(File.ReadAllText)
        .ToArray();

    Contains(sourcesPage, "<InfoBar");
    Contains(sourcesPage, "AutomationProperties.Name=\"Cloudflare status\"");
    Contains(sourcesPage, "AutomationProperties.Name=\"Google Search Console status\"");
    Contains(sourcesPage, "AutomationProperties.Name=\"Web Performance status\"");
    Contains(sourcesPage, "AutomationProperties.Name=\"Bing status\"");
    Contains(sourcesPage, "Style=\"{StaticResource DangerActionButtonStyle}\"");
    Contains(sourcesPage, "IsEnabled=\"{x:Bind ViewModel.Cloudflare.CanRun, Mode=OneWay}\"");
    Contains(sourcesPage, "IsEnabled=\"{x:Bind ViewModel.WebAnalytics.CanRun, Mode=OneWay}\"");
    Contains(sourcesPage, "IsEnabled=\"{x:Bind ViewModel.SearchConsole.CanRun, Mode=OneWay}\"");
    Contains(sourcesPage, "IsEnabled=\"{x:Bind ViewModel.Performance.CanRun, Mode=OneWay}\"");
    Contains(sourcesPage, "IsEnabled=\"{x:Bind ViewModel.Bing.CanRun, Mode=OneWay}\"");

    foreach (var viewModel in sourceViewModels)
    {
        Contains(viewModel, "public bool CanRun => !IsBusy;");
        Contains(viewModel, "OnPropertyChanged(nameof(CanRun))");
    }
});

Run("Bing core sync persists dated reports and raw history", ProviderCoverageRegressionTests.BingPersistsCoreReportsAndHistory);
Run("Bing rejected and malformed responses retain safe diagnostics", ProviderCoverageRegressionTests.BingErrorsPreserveStateAndDiagnostics);
Run("Sitemap discovery and uptime map successful and failed HTTP responses", ProviderCoverageRegressionTests.SitemapDiscoveryAndUptimeMapResponses);
Run("WinUI reports and controls work with empty and populated isolated storage", NativeUiRegressionTests.Run);

static void RunCredentialVaultTests()
{
    Run("Missing credentials return null without accessing the Windows store", CredentialVaultRegressionTests.MissingCredentialsReturnNull);
    Run("Credential reads preserve provider identity", CredentialVaultRegressionTests.SuccessfulReadsKeepProviderIdentity);
    Run("Credential read failures remain distinct and keep private details out of UI", CredentialVaultRegressionTests.ReadFailuresAreReportedWithoutExposingDetails);
    Run("Cloudflare registry propagates credential-store failures", CredentialVaultRegressionTests.CloudflareRegistryPropagatesVaultFailures);
    Run("Source loads preserve saved state and independent providers on credential failure", CredentialVaultRegressionTests.SourceLoadsPreserveStateAndOtherProviders);
    Run("Credential read failures prevent metadata saves", CredentialVaultRegressionTests.FailedReadsPreventMetadataSaves);
}

static void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        Environment.ExitCode = 1;
    }
}

static void Equal(string expected, string actual)
{
    if (!StringComparer.Ordinal.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'");
    }
}

static void Contains(string text, string expected)
{
    if (!text.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected text to contain '{expected}'");
    }
}

static void NotContains(string text, string unexpected)
{
    if (text.Contains(unexpected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected text not to contain '{unexpected}'");
    }
}

static int CountOccurrences(string text, string value)
{
    var count = 0;
    var start = 0;
    while (start < text.Length)
    {
        var index = text.IndexOf(value, start, StringComparison.Ordinal);
        if (index < 0)
        {
            return count;
        }

        count++;
        start = index + value.Length;
    }

    return count;
}

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}");
}

static InsightMetrics Metrics(
    MetricWindow? cloudflareVisitors = null,
    MetricWindow? googleImpressions = null,
    MetricWindow? googleClicks = null,
    MetricWindow? googleMobileClicks = null,
    MetricWindow? googleMobileImpressions = null,
    MetricWindow? pageSpeedMobileScore = null,
    MetricWindow? bingImpressions = null,
    MetricWindow? bingClicks = null,
    MetricWindow? cloudflareCacheHitRatio = null,
    MetricWindow? cloudflareThreats = null,
    StatusCodeSummary? httpStatus = null,
    IndexingSummary? indexing = null,
    SourceFreshnessSummary? freshness = null,
    SearchPageTrend? worstDecliningPage = null,
    int newSearchQueryCount = 0,
    bool isSingleDomain = true)
{
    var empty = new MetricWindow(0, 0);
    return new InsightMetrics(
        CloudflareVisitors: cloudflareVisitors ?? empty,
        GoogleImpressions: googleImpressions ?? empty,
        GoogleClicks: googleClicks ?? empty,
        GoogleMobileClicks: googleMobileClicks ?? empty,
        GoogleMobileImpressions: googleMobileImpressions ?? empty,
        PageSpeedMobileScore: pageSpeedMobileScore ?? empty,
        BingImpressions: bingImpressions ?? empty,
        BingClicks: bingClicks ?? empty,
        CloudflareCacheHitRatio: cloudflareCacheHitRatio ?? empty,
        CloudflareThreats: cloudflareThreats ?? empty,
        HttpStatus: httpStatus ?? new StatusCodeSummary(0, 0, 0),
        Indexing: indexing ?? new IndexingSummary(0, 0, 0),
        Freshness: freshness ?? new SourceFreshnessSummary(1, 0, 0),
        WorstDecliningPage: worstDecliningPage,
        NewSearchQueryCount: newSearchQueryCount,
        IsSingleDomain: isSingleDomain);
}

static IReadOnlyList<InsightCard> GenerateInsights(InsightMetrics metrics) => InsightEngine.Generate(metrics);

static string GetInsightEmptyState(InsightMetrics metrics) => InsightEngine.GetEmptyStateText(metrics);

static InsightCard FindInsight(IReadOnlyList<InsightCard> cards, string title)
    => cards.FirstOrDefault(card => string.Equals(card.Title, title, StringComparison.Ordinal))
       ?? throw new InvalidOperationException($"Expected insight title '{title}'");

static void AssertPlainInsightTitles(IReadOnlyList<InsightCard> cards)
{
    foreach (var card in cards)
    {
        NotContains(card.Title, "CTR");
        NotContains(card.Title, "CrUX");
        NotContains(card.Title, "5xx");
        NotContains(card.Title, "GA4");
    }
}

static string RunPowerShellScript(string scriptPath, string root, string argument)
{
    using var process = Process.Start(new ProcessStartInfo
    {
        FileName = "pwsh",
        Arguments = $"-NoProfile -File \"{scriptPath}\" -Root \"{root}\" {argument}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    }) ?? throw new InvalidOperationException("Could not start pwsh");

    var output = process.StandardOutput.ReadToEnd().Trim();
    var error = process.StandardError.ReadToEnd().Trim();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"PowerShell script failed with {process.ExitCode}: {error}");
    }
    return output;
}

static string FindRepositoryRoot()
{
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir))
    {
        if (File.Exists(Path.Combine(dir, "Numeris.slnx")))
        {
            return dir;
        }
        dir = Directory.GetParent(dir)?.FullName;
    }
    throw new InvalidOperationException("Repository root not found");
}

internal static class ProgramInputs
{
    internal static readonly string[] Vector1 = new[]
    {
        "overview-filled.png",
        "cloudflare-filled.png",
        "google-search-filled.png",
        "bing-filled.png",
        "performance-filled.png",
        "health-filled.png",
        "sources-filled.png"
    };
    internal static readonly string[] Vector2 = new[]
    {
        "DashboardPage",
        "CloudflarePage",
        "SearchConsolePage",
        "BingPage",
        "PerformancePage",
        "HealthPage",
    };
    internal static readonly string[] Vector3 = new[]
    {
        "CloudflarePage.xaml",
        "SearchConsolePage.xaml",
        "BingPage.xaml",
        "PerformancePage.xaml",
        "HealthPage.xaml",
    };
    internal static readonly string[] Vector4 = new[]
    {
        "DashboardPage.xaml",
        "CloudflarePage.xaml",
        "SearchConsolePage.xaml",
        "BingPage.xaml",
        "PerformancePage.xaml",
        "HealthPage.xaml",
    };
    internal static readonly string[] Vector5 = new[]
    {
        "CloudflarePage.xaml.cs",
        "SearchConsolePage.xaml.cs",
        "BingPage.xaml.cs",
        "PerformancePage.xaml.cs",
    };
    internal static readonly string[] Vector6 = new[] { "CloudflareViewModel.cs", "SearchConsoleViewModel.cs", "BingViewModel.cs", "PerformanceViewModel.cs" };
    internal static readonly string[] Vector7 = new[]
    {
        "ChartBarNeutralTopColor",
        "ChartBarNeutralMidColor",
        "ChartBarNeutralBottomColor",
        "ChartBarHighlightTopColor",
        "ChartBarHighlightMidColor",
        "ChartBarHighlightBottomColor",
        "ChartBarCapColor",
        "ChartReferenceLineColor",
        "PositiveDeltaColor",
        "NegativeDeltaColor",
        "NeutralDeltaColor",
    };
}
