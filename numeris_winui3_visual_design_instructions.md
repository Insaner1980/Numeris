# Numeris WinUI 3 Visual Design Instructions

## Goal

Redesign Numeris as a polished native Windows 11 WinUI 3 application.

The app should feel calm, clear, premium, and native to Windows 11. Do **not** make it look like a generic dark web dashboard.

Use **Mica** as the native window backdrop/fallback, then draw Numeris' own branded bitmap backdrop at the shell level and build a proper Fluent/WinUI layer system on top of it.

## Non-negotiable background decision

The visible app background is `Numeris/Assets/AppBackdrop.webp`, decoded once in `MainWindow` behind navigation and page content.

The bitmap must be:

- dark black/charcoal overall
- soft glass/satin ribbon abstraction
- linework-free, text-free, logo-free, watermark-free
- subtly accented with honey amber, never orange-dominant
- calm and low contrast in the main content area
- weighted more toward the top-left and upper edge than the center-right

The bitmap is decorative, not content. Always keep it subdued behind data surfaces with `AppBackdropOpacity` and `AppBackdropScrimBrush`.

Use this as the base/fallback color:

```text
App background / Mica fallback: #444444
RGB: 68, 68, 68
```

Do not replace `#444444` as the fallback color. The bitmap may be darker than the fallback, but it must be restrained enough for translucent cards, charts, and tables to stay readable.

## Mica usage

Use Mica as the single native backdrop material for the app window. The visible art layer is the shell-level `AppBackdropImage` above Mica.

Important rules:

- Mica is for the app backdrop, not for every card.
- Do not apply Mica repeatedly to individual cards, charts, tables, or panels.
- The title bar and main app shell should visually belong to the same Mica/fallback/backdrop stack.
- Use `#444444` as the fallback color when Mica is unavailable.
- Avoid acrylic/glass effects on data-heavy areas unless absolutely necessary.
- Remove hard NavigationView content-grid borders; the shell should read as one continuous backdrop stack.

Preferred direction:

```text
Base layer: Mica with #444444 fallback
Art layer: AppBackdrop.webp with AppBackdropScrimBrush readability overlay
Navigation layer: transparent or subtly separated from backdrop
Content layer: cards and panels with subtle contour/elevation
```

Use normal Mica unless there is a clear reason to use Mica Alt.

## Overall visual direction

Use this style:

```text
Native Windows 11 analytics tool
Soft graphite gray
Calm data surfaces
Minimal but not empty
Premium, precise, and readable
```

Avoid:

- pure black flat backgrounds
- neon dashboard styling
- heavy glassmorphism
- strong shadows
- web-admin-dashboard aesthetics
- oversized rounded corners
- random accent colors
- inconsistent spacing

## Color tokens

Use these as the starting design tokens:

```text
AppBackground: #444444
AppBackdropOpacity: 0.72
AppBackdropScrim: rgba(0,0,0,0.70)
ContentLayer: rgba(0,0,0,0.15)
NavigationLayer: rgba(0,0,0,0.07)

CardBackground: rgba(255,255,255,0.14)
CardBackgroundHover: rgba(255,255,255,0.18)
CardBorder: rgba(255,255,255,0.20)

ControlBackground: rgba(255,255,255,0.13)
ControlBackgroundHover: rgba(255,255,255,0.17)
ControlBackgroundPressed: rgba(255,255,255,0.09)

ChartPanelBackground: rgba(0,0,0,0.26)
ChartGridLine: rgba(255,255,255,0.22)

TextPrimary: #F5F5F5
TextSecondary: #D8D8D8
TextTertiary: #B8B8B8
TextDisabled: #8F8F8F

Accent: #D9A052
AccentHover: #E3AD64
AccentPressed: #C78D3F

Success: #82C98F
Warning: #D9A052
Danger: #E07A7A
Info: #8FB8E8
```

Use accent color sparingly. It should guide attention, not decorate everything.

Amber/gold is the main Numeris accent. Use it for:

- selected navigation indicator
- selected tabs
- primary chart line
- important active states
- subtle KPI highlights

Use green only for good/healthy states.
Use red only for real problems/errors.
Use gray for unknown, neutral, or pending states.

## Typography

Use **Segoe UI Variable** throughout the app.

Do not use web fonts or custom decorative fonts.

Suggested type scale:

```text
Page title: 32 px, weight 600
Section title: 18 px, weight 600
Card label: 13 px, weight 400 or 500
KPI value: 28-32 px, weight 600
Body/table text: 14 px, weight 400
Table header: 13 px, weight 600
Metadata/help text: 12-13 px, weight 400
Navigation label: 14 px, weight 400 or 500
Button text: 14 px, weight 500
```

Use tabular numerals for analytics values if available.

Do not overuse bold text. Most hierarchy should come from size, spacing, and placement.

## Layout and spacing

Use an 8 px spacing rhythm.

Recommended layout tokens:

```text
Window content outer padding: 28-32 px
Navigation rail collapsed width: 64 px
Navigation expanded width: 240-260 px
Page title to tabs/filter row: 24 px
Tabs/filter row to content: 24 px
Card gap: 16 px
KPI card padding: 20-24 px
Chart card padding: 20-24 px
Table row height: 40-44 px
Toolbar/control height: 40 px
Small icon button: 36-40 px
Large refresh button: 44 px
```

The app should not feel cramped, but it also should not waste space.

Keep spacing consistent across Overview, Cloudflare, Google Search, Bing, Performance, Health, and Sources.

## Navigation

Use a left-side WinUI-style NavigationView.

Preferred behavior:

- collapsed rail around 64 px wide
- optional expanded width around 240-260 px
- settings pinned at the bottom
- consistent icons from Segoe Fluent Icons or native WinUI icons
- no mixed icon libraries

Selected navigation item:

- subtle background overlay
- small amber indicator
- no heavy filled rectangle
- no overly bright highlight

The navigation should feel integrated with the shared bitmap/Mica/fallback backdrop stack, not like a separate black sidebar.

## Title bar and top controls

Use native title bar color customization to make the app feel seamless with the dark backdrop. A fully custom drag region is optional, but the default gray title bar should not remain.

Top-right controls should include:

```text
Domain selector
Date range selector
Refresh button
Optional last-sync text or tooltip
```

These controls should feel like WinUI controls, not web buttons.

Use consistent height, corner radius, hover states, and spacing.

Do not make the top bar visually heavy.

Overview, Cloudflare, Google Search, Bing, Performance, and Health must use the shared `PageHeaderBorderStyle` surface for the page title, domain selector, period selector, refresh action, and page-specific tabs. Do not let these controls float independently over the backdrop.

Period labels must come from `PeriodOptions.All` / `Period.DisplayLabel()` so the UI shows user-facing labels such as `Last 7 days`, not enum names such as `Last7Days`.

## Cards

Cards are content surfaces on top of the shared bitmap/Mica/fallback backdrop stack.

Use:

```text
Corner radius: 10-12 px
Border: 1 px subtle contour
Elevation: low, Windows-like, not web-style
Padding: 20-24 px
```

Do not use strong drop shadows.

Cards should feel lightly raised or separated through contour and surface overlay.

KPI cards should be compact and scannable.

Recommended KPI structure:

```text
Small label
Large value
Small trend/status line
Optional tiny indicator or sparkline
```

Avoid huge empty KPI cards.

## Charts

Do not use pure black chart backgrounds.

Use:

```text
ChartPanelBackground: rgba(0,0,0,0.26)
```

Charts should be embedded inside cards and visually belong to the app.

Chart rules:

- grid lines very subtle
- axis labels small and secondary/tertiary
- line thickness around 2 px
- area fills low opacity
- legend should not waste vertical space
- avoid dramatic scaling when data volume is tiny
- tooltips should use a higher-elevation flyout surface

Primary data line: amber.
Secondary data line: muted salmon/rose or another restrained secondary color.

Avoid making charts look like black Excel panels pasted into the app.

## Tables

Tables should feel native and calm, not like HTML tables.

Use:

- 13 px semi-bold headers
- 14 px body text
- 40-44 px row height
- right-aligned numeric columns
- subtle row hover overlay
- very light separators or no visible separators
- clear sort state
- readable spacing between columns

For query/search tables, make clicks, impressions, CTR, and position easy to scan.

## Tabs

Use a consistent tab style across Cloudflare, Google Search, Bing, Performance, and Health.

Selected tab:

- subtle amber underline or indicator
- not a large heavy button
- clear but calm active state

Tab spacing should be consistent across all sections.

## Overview page

Make Overview the visual reference page for the whole app.

Recommended structure:

1. Page title and context row
2. Top-right domain/date/refresh controls
3. Four KPI cards
4. Two main chart cards: Traffic Trend and Search Trend
5. Compact health/status strip

The Overview page should define the final design language. Once it looks right, apply the same card, chart, table, tab, and control system to all other pages.

## Cloudflare page

The Cloudflare page should include:

- large traffic chart
- top countries card
- top pages card
- cache/security/status sections where relevant

Top countries and top pages should use compact native list rows with bars.

Avoid raw-looking web dashboard components.

## Search page

The Search page should include:

- overview KPIs
- query table
- pages/devices/indexing tabs
- optional right-side insights panel

The right-side panel can show:

- new queries
- opportunities
- gaining queries
- losing queries
- pages with impressions but no clicks

Do not make the Search page visually heavier than Overview.

## Indexing page

Avoid showing a raw URL list only.

Each URL should eventually have a status, for example:

```text
Submitted
Indexed
Not indexed
Redirect
Error
Unknown
```

If real indexing status is unavailable, label it clearly.

Do not imply URLs are unindexed unless the data source actually confirms that.

## Health page

Do not show large empty charts.

If there is no historical data, show a polished empty state:

```text
Response time history will appear after scheduled checks run.
```

Health should show:

- site status
- uptime
- incidents
- last checked
- response time
- SSL status
- sitemap status
- robots.txt status if available

Use green only when the state is genuinely healthy.

## Sources page

Turn Sources into a proper integration settings screen.

Each source should be a card with:

```text
Integration name
Connection status
What data it provides
Last sync
Action button: Connect / Configure / Reconnect / Disable
```

Examples:

```text
Cloudflare
Connected
Traffic, cache, security events
Last synced: 12:58

Google Search Console
Connected
Clicks, impressions, pages, indexing
Last synced: 12:54

Bing Webmaster Tools
Needs setup
Search visibility and indexing data
Connect
```

Remove or hide development-like text such as:

```text
Without configuration, mock data is used.
```

If demo/mock data is needed, show a deliberate “Demo mode” banner instead.

## Empty states and pending states

Every empty or pending area should have a designed state.

Do not leave blank panels or empty charts.

Use short, clear messages:

```text
No data for this period.
Connect Google Search Console to view search queries.
Response time history will appear after scheduled checks run.
Sitemap data has not been refreshed yet.
```

Empty states should look intentional, not broken.

## Controls and interaction states

Every interactive element needs:

- default state
- hover state
- pressed state
- disabled state
- focus state

Keyboard focus must remain visible.

Use native WinUI behavior where possible instead of custom rebuilding.

## Accessibility

Keep contrast readable on the dark bitmap backdrop and the #444444 fallback background.

Do not rely on color alone for status.

Use text labels, icons, or status names in addition to color.

Respect Windows text scaling where possible.

## Implementation priority

Do not redesign every screen randomly.

Work in this order:

1. Create shared design tokens for colors, typography, radius, spacing, borders, and elevation.
2. Apply Mica shell with #444444 fallback and the global AppBackdrop image layer.
3. Redesign the app shell: title bar, navigation, top controls.
4. Redesign Overview completely.
5. Extract reusable components from Overview.
6. Apply those components to Cloudflare.
7. Apply them to Search.
8. Apply them to Health.
9. Apply them to Sources.
10. Remove all temporary/mock-looking UI text from production views.

## Core instruction

Do not create a generic dark dashboard.

Build a Windows 11 WinUI 3 app using Mica as the native window backdrop/fallback, `#444444` as the base fallback gray, and `AppBackdrop.webp` as the single visible branded app background.

Use Fluent-style layering:

```text
Mica/#444444 fallback layer
AppBackdrop image and scrim layer
command/navigation layer
content card layer
flyout/dialog layer
```

Use Segoe UI Variable, subtle contours, low elevation, restrained amber accent, native controls, and no pure black chart backgrounds.
