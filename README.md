<img src="shadowsocks-csharp/Resources/ssw128.png" width="48"/> ShadowsocksR for Windows — Modernized
=======================

A modernization of [ShadowsocksR-Windows](https://github.com/HMBSbige/ShadowsocksR-Windows):
migrated to **.NET 10**, rebuilt around a **single-window Fluent UI** with dependency
injection and MVVM, and freed of the commercial Syncfusion dependency.

> **Status: active modernization in progress.** The new UI compiles and unit tests pass,
> but the WPF UI has not yet been exhaustively runtime-tested, and some features are still
> being wired up (see *Roadmap*). Not yet recommended for daily use.

## What's new

- **.NET 10** (`net10.0-windows`), upgraded from .NET 7.
- **Single-window Fluent shell** (WPF-UI `NavigationView`) replacing the old 7 separate
  windows. Pages: Dashboard, Servers, Subscriptions, Statistics, Settings (incl. DNS),
  Port Forwarding, Logs.
- **Dashboard** home page: real-time up/down speed chart (LiveCharts2), flow-layout cards,
  proxy-mode quick switches.
- **No Syncfusion** — the commercial `SfTreeView` / `SfDataGrid` were replaced with a native
  WPF `TreeView` (drag-and-drop via gong-wpf-dragdrop) and `DataGrid`; the package and its
  build-time license-injection step were removed entirely.
- **Architecture**: dependency injection (`Microsoft.Extensions.Hosting`), MVVM via
  `CommunityToolkit.Mvvm`, and share-link (`ssr://` / `ss://`) parsing extracted into a
  dedicated parser service.
- The system-tray app remains; it now opens the single Fluent window.

The SSR protocol/obfs/encryption core is unchanged from upstream.

## Build

Requires the **.NET 10 SDK** and Windows.

```pwsh
dotnet build shadowsocks-csharp.sln -c Release
```

> **Dependency note:** the project depends on a private fork `ARSoft.Tools.Net 2.3.0`
> (adds DNS-over-TLS) hosted on HMBSbige's GitHub Packages feed. To restore it, add the
> feed with a GitHub token that has `read:packages`:
>
> ```pwsh
> dotnet nuget add source "https://nuget.pkg.github.com/HMBSbige/index.json" `
>   -n GitHub-HMBSbige -u <your-github-user> -p <token> --store-password-in-clear-text
> ```
>
> Without that credential, build locally with `-p:LocalDnsShim=true`, which substitutes
> the public ARSoft.Tools.Net 3.0.0 plus a compile-only DNS-over-TLS shim
> (`shadowsocks-csharp/Build/LocalDnsShim.cs`). Do not ship a build produced that way.

Packaged builds: `.\build.ps1 all` (framework-dependent + self-contained x64/x86).

## Develop

Visual Studio 2022 (17.12+) or the .NET 10 SDK CLI.

## Roadmap

- Collapse the remaining `Global` static service-locator into pure DI.
- Separate the domain models (`Configuration` / `Server`) from `ViewModelBase`.
- Localization (i18n) for the new pages + persisted light/dark theme.
- Runtime verification and UI polish.

## License

GPLv3

Copyright © 2019 - 2022 HMBSbige. Forked from ShadowsocksR by BreakWa11.
Modernization fork maintained in this repository.
