# CrossdexBar

A cross-platform (Windows + Linux) system-tray app that shows your AI coding-tool usage — Codex, Claude, Cursor, Grok, and Ollama — at a glance, built with [Avalonia UI](https://avaloniaui.net/) and .NET.

It's a spiritual port of [CodexBar](https://github.com/steipete/CodexBar) (macOS) and [Win-CodexBar](https://github.com/Finesssee/Win-CodexBar) (Windows) into a single codebase that runs on both platforms, with a provider architecture designed so adding a new provider is a small, self-contained task.

## How it works

For most providers, CrossdexBar doesn't ask you for an API key at all — it reuses the session each tool's own official CLI/app already created when you signed in normally:

| Provider | Source | Usage data |
| --- | --- | --- |
| **Codex** | `~/.codex/auth.json` (written by `codex login`) | Real session/weekly percentages |
| **Claude** | `~/.claude/.credentials.json` (written by the `claude` CLI) | Real 5-hour/weekly percentages |
| **Cursor** | Cursor.app's local `state.vscdb` session | Real plan usage percentage |
| **Ollama** | A pasted API key, or a pasted browser `Cookie` header for real quota bars | Key alone only confirms it's valid; a cookie is needed for actual numbers (Ollama's API doesn't expose usage) |
| **Grok** | `~/.grok/auth.json` (written by `grok login`) | Identity/sign-in only for now — no confirmed source for a usage percentage yet (see `GrokAuthFileStrategy`'s doc comment for what's been tried) |

No passwords or browser-cookie decryption — the only place you'd ever paste something is Ollama's optional cookie field (Settings → Ollama → Edit), and that's a plain string paste, not automatic browser access.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows 10+, or Linux with a system tray host (KDE/XFCE work out of the box; GNOME needs the [AppIndicator extension](https://extensions.gnome.org/extension/615/appindicator-support/))

## Build & run

```bash
dotnet build CrossdexBar.slnx
dotnet run --project src/CrossdexBar.App
dotnet test CrossdexBar.slnx   # 52 tests across Core + one project per provider
```

Click the tray icon to open the usage popover. Right-click (or the ⚙ button) for Settings, where you can enable/disable providers, add multiple accounts of the same provider (e.g. two Codex logins), and edit per-instance settings like a custom credentials-file path or an Ollama cookie header.

Config lives at `%APPDATA%\CrossdexBar\config.json` (Windows) or `$XDG_CONFIG_HOME/crossdexbar/config.json` / `~/.config/crossdexbar/config.json` (Linux).

## Architecture

```
src/
  CrossdexBar.Core/              Provider abstractions, host APIs (HTTP/CLI/config), refresh loop
  CrossdexBar.Providers.<Name>/  One project per provider — a descriptor + one or more fetch strategies
  CrossdexBar.App/               Avalonia tray app, popover/settings UI, MVVM (CommunityToolkit.Mvvm)
tests/
  One test project per provider, plus CrossdexBar.Core.Tests
```

Each provider is a `ProviderDescriptor` (id, display name, branding, the per-instance settings it needs — e.g. an optional credentials-file path) plus one or more `IFetchStrategy` implementations that turn a `ProviderInstance` into a `ProviderFetchOutcome` (`Success` / `NotSignedIn` / `Unavailable` / `Failure`). Strategies only touch the filesystem/network through the host APIs on `ProviderFetchContext` (`ICliRunner`, `IHttpApi`, `IConfigStore`, `IPlatformPaths`), which keeps them portable and easy to unit test with fakes — see any `*StrategyTests.cs` for the pattern.

### Adding a provider

1. New project `src/CrossdexBar.Providers.<Name>/` referencing `CrossdexBar.Core`.
2. A `<Name>Descriptor.cs` (id, branding, instance-settings schema) and one or more `IFetchStrategy` implementations.
3. Register it in `App.axaml.cs`'s `ComposeServices()` (`_registry.Register(...)` + a line in the default-instance seeding).
4. A matching test project mirroring an existing one (e.g. `CrossdexBar.Providers.Codex.Tests`).

No reflection-based auto-discovery on purpose — the registered provider list is always just one file to read.

## Known limitations

- Grok has no confirmed usage-percentage source yet — see the doc comment on `GrokAuthFileStrategy` for what's been investigated (a gRPC-web billing endpoint that turned out not to carry usage data, and a promising `rest/rate-limits` JSON endpoint that wasn't confirmed end-to-end due to lack of a test account).
- No automatic browser-cookie import (Chrome/Edge DPAPI decryption, Linux keyring handling) — Ollama's cookie field is manual-paste only, by design, since automatic decryption is the most fragile part of both reference apps.
- No installers/packaging yet (winget, AUR, Flatpak) — `dotnet run` only for now.
