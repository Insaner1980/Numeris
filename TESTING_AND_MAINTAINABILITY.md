# Testing and Maintainability Notes

## SonarQube Cloud

Run `sonar` in PowerShell from this repository or a subdirectory. The existing
profile resolves `tools/sonar.ps1`, loads the saved Sonar CLI credential and
allows the upload to `Insaner1980_Numeris` in organization `insaner1980`.
Use `sonar -PlanOnly` to preview the steps without building or uploading.

The script restores the pinned local .NET tools, restores and builds
`Numeris.Tests` and its referenced app in Debug/x64, runs the default console harness under
`dotnet-coverage`, validates its XML report, and uploads the analysis.
It does not enable the opt-in live credential or Sources persistence tests.
Build or test failure stops the upload. Logs are saved to `reports/sonar.txt`
and coverage to `reports/sonar-coverage.xml`; generated output is Git-ignored.
The console harness does not produce a VSTest/TRX test execution report.

When invoking the script directly, set `SONAR_TOKEN` in the process environment
and run `./tools/sonar.ps1 -AllowExternalUpload`. Normal `sonar` use needs no
extra switches. Successful upload does not itself prove a passing Quality Gate;
check the linked SonarQube Cloud results after server processing.

## Unit-Testable Integration Code

The best candidates for unit tests are the integration parts that can run without live API keys:

- Site and URL normalization: `SiteIdentity`, `PerformanceUrl`, Search Console property selection, and Bing/PageSpeed URL handling.
- API response parsing: PageSpeed scores, warnings, audit extraction, CrUX history deserialization, Bing response unwrapping, sitemap XML parsing, and Cloudflare/Web Analytics response mapping.
- API error handling: `ApiRequestException`, `ApiErrorMessage`, HTTP status classification, sanitized messages, and raw payload redaction in `RawJsonStoragePolicy`.
- Request construction: OAuth authorization URLs, Google token payloads, Bing query parameters, PageSpeed strategy URLs, and Cloudflare GraphQL operation names.
- Sync orchestration with fakes: missing credentials, saved connection status updates, transient/rate-limited API failures, retry decisions, and repository calls.
- Repository behavior against a temporary SQLite database: migrations, upserts, transaction boundaries, retention cleanup, and dated Bing history.
- Legacy migration with a sample Numeris database and fake credential vault: idempotency, keyring preference, and preservation of existing Numeris secrets.
- Source ViewModels with fake services: busy state, status messages, add/test/sync/delete flows, and vault deletion instead of blank secret writes.

Live API tests should stay separate from unit tests. They should be opt-in, use developer-provided local credentials, and never run in default CI.

## Test Framework Recommendation

The current `Numeris.Tests` project is a console harness. It is useful for fast invariant checks, but it should not remain the only test setup because it lacks standard test discovery, per-test execution, fixtures, filtering, IDE integration, and CI-friendly reporting.

Add a real test framework. MSTest is the conservative default for this WinUI 3 app because Microsoft documents WinUI 3 test-project setup for MSTest, NUnit, and xUnit, and MSTest fits the Windows App SDK toolchain well.

Recommended direction:

- Keep the console harness temporarily for repository invariants while migrating tests.
- Add a framework-based `Numeris.Tests` project using MSTest.
- Match the app target framework: `net8.0-windows10.0.19041.0`.
- Configure Windows runtime identifiers for `win-x86`, `win-x64`, and `win-arm64` where needed.
- Set `WindowsAppSdkBootstrapInitialize` for tests that load Windows App SDK runtime components.
- Prefer moving pure logic into a `Numeris.Core` library over referencing the WinUI `WinExe` app directly from tests.
- Use fake `HttpMessageHandler` or injected API abstractions instead of static live `HttpClient` paths for API-client unit tests.

## Current MVVM Toolkit State

The ViewModels use the WinUI-safe CommunityToolkit.Mvvm partial property pattern:

```csharp
[ObservableProperty]
public partial string StatusMessage { get; set; } = "";
```

This is the correct direction for MVVMTK0045 in WinUI/WinRT scenarios. The project also uses `LangVersion=preview`, which is required by the current partial-property generator path.

Binding health checks to keep:

- Build the WinUI project after ViewModel property changes; `x:Bind` catches missing members at compile time.
- Keep page constructors assigning `ViewModel` before `InitializeComponent()`.
- Keep chart updates covered by explicit `PropertyChanged` wiring, since the LiveCharts controls are created in code-behind.
- Keep a harness/framework test that rejects old `[ObservableProperty] private` fields in ViewModels.

## Pre-MSIX Smoke Tests

Run these before packaged/MSIX distribution:

- Build packaged Release outputs for x64, x86, and ARM64.
- Run Windows App Certification Kit against the package.
- Install on a clean machine or clean user profile and verify first launch creates the database, seeds mock data when needed, and runs legacy import safely.
- Restart the app and verify legacy import is idempotent and does not duplicate connections or overwrite newer Numeris credentials.
- Upgrade from an older local database and verify `PRAGMA user_version`, `meta.schema_version`, and all migrations.
- Navigate every page: Dashboard, Cloudflare, Search Console, Health, and Sources.
- Change domain and period, restart, and verify shell settings persist.
- Add, test with invalid credentials, sync, and delete each integration source; verify secrets are stored only in `CredentialVault`.
- Inspect SQLite after source operations and failed API calls to confirm no API keys, tokens, client secrets, refresh tokens, or bearer tokens are stored in plain text.
- Run PageSpeed/CrUX/Bing sync paths with fake or controlled responses to verify raw JSON trimming, redaction, and retention.
- Verify Release trimming does not break JSON deserialization, source generators, XAML binding, or WinUI startup.
- Verify packaged credential save/read/delete for Cloudflare, Web Analytics, Search Console, CrUX, PageSpeed, and Bing.
- Verify app startup without a legacy Numeris database and with a sample legacy Numeris database.
- Verify package assets, display name, splash screen, and manifest capabilities.

## Near-Term Maintenance Work

- Split pure logic out of the WinUI executable into a testable library.
- Convert the highest-value console harness checks into named framework tests.
- Add API-client tests around fake HTTP responses before adding more live integrations.
- Add repository tests for each migration and retention policy.
- Add a small packaged smoke checklist to release notes or PR templates before distribution.
