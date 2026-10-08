# Numeris Core Completion Plan

## Summary

This plan turns the current Numeris core migration work into a verified, committable milestone. Most product work is already implemented: legacy Numeris import, source-specific Sources view models, Test/Sync actions, settings persistence, MVVM Toolkit partial properties, CrUX/PageSpeed/Bing integrations, bounded raw JSON storage, and architecture documentation updates.

The remaining work is verification and hardening: keep the local build/test loop green, sanitize all UI-facing integration errors, verify the implemented architecture against the codebase, run live/manual smoke tests with local credentials, then commit the work in coherent batches.

## Current Status

| Area | Done | Needs verification | Remaining |
| --- | --- | --- | --- |
| Legacy import | `LegacyDataMigrationService` imports Web Analytics, Cloudflare, and Search Console from the Numeris database and legacy Windows credentials. | Run against the user's real `%AppData%\com.finnvek.numeris\numeris.db`. | None known after live smoke testing. |
| Secrets | New and imported tokens use `CredentialVault`; delete paths remove vault entries. | Confirm with real migrated credentials that existing Numeris secrets are preserved. | None known. |
| Sources UX | Sources behavior is split into source-specific view models with Save/Test/Sync/Delete paths. | Click through all source panels in the app. | Only manual UI smoke testing remains. |
| Sync services | Sync services orchestrate API calls and repositories; repositories own SQLite writes. | Run test harness and live syncs. | None known. |
| Settings persistence | `SettingsStore` persists selected period, selected domain, and last page. | Restart app after changing shell state. | None known. |
| MVVM Toolkit | View models use partial property syntax required for WinUI/WinRT AOT compatibility. | Build and test harness. | None known. |
| Performance and Bing | CrUX, PageSpeed, and Bing Webmaster core read-only integrations exist with bounded raw JSON. | Run API-key based Test/Sync flows. | Dashboard modeling can be refined later. |
| Local verification | Solution build works after test project deployment model is aligned. | `dotnet build` and `dotnet run` must pass before final commit. | Keep fixed if SDK/platform defaults change. |
| Live smoke tests | Not automated because they require local credentials and Visual Studio launch profiles. | User-assisted packaged/unpackaged and live API pass. | Record results before shipping. |

## Implementation Order

1. Stabilize the local verification loop.
   - Keep `Numeris.Tests` framework-dependent so it can reference the non-self-contained WinUI executable project.
   - Keep the solution verification platform on `x64` so `dotnet build C:\Dev\Numeris\Numeris.slnx` does not mix ARM64 app output with x64 tests. The app project still declares x86, x64, and ARM64 runtime identifiers for publishing.
   - Coerce the WinUI app project from MSBuild's `AnyCPU` default to `x64`, because packaged .NET app host builds cannot be processor-architecture neutral.
   - Run `dotnet build C:\Dev\Numeris\Numeris.slnx`.
   - Run `dotnet run --project C:\Dev\Numeris\Numeris.Tests\Numeris.Tests.csproj`.

2. Harden integration error behavior.
   - Use `ApiErrorMessage.Sanitize(ex)` for UI-facing integration errors.
   - Keep `ConnectionTestResult` limited to `Ok` and `Message`; status codes remain part of `ApiRequestException` and sanitized messages.
   - Preserve differentiated messages for missing local secrets, missing site tags/properties, unauthorized upstream responses, mismatches, and successful empty datasets.

3. Verify implemented architecture.
   - Confirm legacy import reads Numeris config centrally through `LegacyAppSource`.
   - Confirm imported secrets are copied only into `CredentialVault`.
   - Confirm Web Analytics sync uses saved `web_analytics_sites` and does not require Discover.
   - Confirm `connections.status` and `connections.last_sync` update after successful sync only.
   - Confirm CrUX/PageSpeed/Bing default URL/site rows include `https://finnvek.com/` and `https://knittoolsapp.com/`.
   - Confirm Bing automatic sync remains read-only and limited to `GetUserSites`, `GetRankAndTrafficStats`, `GetQueryStats`, `GetPageStats`, `GetCrawlStats`, and `GetCrawlIssues`.

4. Update project documentation.
   - Keep this file as the root execution plan.
   - Keep `migration-plan.md` focused on current roadmap status, not detailed execution logs.
   - Update `AGENTS.md` and `memory/MEMORY.md` only for durable architecture or workflow facts.

5. Run manual/live smoke tests.
   - Do not run `lint-check`, `lc`, `security-check`, or `sc`; the user runs those scripts.
   - Launch Numeris with the user's local Numeris data and confirm imported Web Analytics account/site tags, Cloudflare domain/zone rows, and Search Console client/token state.
   - From Sources, test and sync Cloudflare, Web Analytics, Search Console, CrUX, PageSpeed, and Bing.
   - Restart the app and confirm selected period, selected domain, and last page are restored.
   - In Visual Studio, smoke-test both packaged and unpackaged launch paths.

6. Commit in coherent batches.
   - `Korjaa testiharnessin build-konfiguraatio`
   - `Koveta integraatioiden virheviestit ja testitulokset`
   - `Päivitä core completion -suunnitelma ja dokumentaatio`
   - `Varmista live-integraatioiden smoke-testit` only after the manual smoke-test pass is actually complete.

## Public Contracts

- `LegacyDataMigrationService` is the general legacy import entrypoint.
- `CredentialVault` is the only storage location for new and migrated secrets.
- `ConnectionTestResult` has a small UI contract: `Ok` and `Message`.
- `SettingsStore` owns shell-level persistence for selected period, selected domain, and last page.
- `SiteIdentity` is the single source of truth for domain, origin, and home-page URL normalization.

## Required Verification

- `dotnet build C:\Dev\Numeris\Numeris.slnx` returns `0 Error(s)`.
- `dotnet run --project C:\Dev\Numeris\Numeris.Tests\Numeris.Tests.csproj` returns exit code 0.
- Live API and Visual Studio smoke tests are recorded before final shipping.
- If the user later runs lint/security scripts, read reports from `reports/` instead of rerunning the scripts.

## Reference Docs Checked

- Microsoft Learn: Unpackaged WinUI apps still use `<WindowsPackageType>None</WindowsPackageType>` for Windows App SDK auto-initialization.
- Microsoft Learn: MVVM Toolkit `MVVMTK0045` requires partial properties for WinUI/WinRT AOT compatibility.
- Microsoft Learn: `NETSDK1150` is caused by incompatible executable project-reference deployment models.
