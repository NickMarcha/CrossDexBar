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
| **Ollama** | A pasted browser `Cookie` header from `ollama.com/settings` (Settings → Ollama → Edit) | Real, live-updating Session/Weekly percentages — this is the recommended setup. An API key alone works as a lesser fallback that only confirms the key is valid, since Ollama's API doesn't expose usage (only its website does) |
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
3. Register it in `App.axaml.cs`'s `ComposeServices()`: `_registry.Register(...)` **and** add it to the `SeedDefaultInstanceIfMissing(...)` chain. Easy to forget the second part — without it, the provider works fine for fresh installs (which seed every registered provider) but silently never appears for anyone with an existing `config.json`, since seeding only fills in providers missing from the file.
4. A matching test project mirroring an existing one (e.g. `CrossdexBar.Providers.Codex.Tests`) — see "What we learned" below before assuming an API/file shape without a real test against it.
5. If the provider needs a new NuGet package (especially anything with a native/crypto component), check `dotnet build` output for `NU1903` vulnerability warnings and pin a patched transitive version explicitly if needed — see the `SQLitePCLRaw.bundle_e_sqlite3` pin in `CrossdexBar.Providers.Cursor.csproj` for the pattern (Microsoft.Data.Sqlite pulled in a version with a real CVE by default).

No reflection-based auto-discovery on purpose — the registered provider list is always just one file to read.

## What we learned building this (read before touching provider code)

None of these providers have a documented API — every auth-file shape and endpoint here was reverse-engineered from steipete/CodexBar (Swift, macOS) and Finesssee/Win-CodexBar (Rust, Windows), and every single one of our first-pass guesses turned out wrong in some detail once tested against a real account:

- **Check both reference repos, not just one.** They sometimes take meaningfully different approaches for the same provider. Grok is the clearest example: CodexBar's Swift code relies on an interactive `grok agent stdio` JSON-RPC session (a capability this app doesn't have) with a cookie-based web fallback; Win-CodexBar's Rust code instead sends the same `auth.json` token as a Bearer header straight to a gRPC-web billing endpoint — a completely different, and initially more promising-looking, approach that only reference #2 revealed.
- **Undocumented endpoints deserve a heuristic parser, a clean failure path, and nothing more.** Codex's `reset_at` field turned out to be an absolute Unix timestamp, not the "seconds remaining" duration the field name implied — wrong for weeks until caught by a live test. Grok's gRPC-web response, decoded by hand from real captured bytes, had reset-window timestamps and status flags but zero usage-percentage field anywhere in it — the endpoint we guessed at (`GetGrokCreditsConfig`) looks like it returns billing *configuration*, not consumption; a plain JSON REST endpoint (`grok.com/rest/rate-limits`, returning `remainingQueries`/`totalQueries`) looked far more promising but was never confirmed against a real subscription. When a scan finds nothing usable, return a clean typed `Failure`/`Unavailable` — never fabricate a number.
- **A synchronous fetch strategy can permanently freeze an instance's refreshes.** `RefreshService.RunFetchAsync` used to remove itself from the in-flight-task dictionary inside the same synchronous call stack that `ConcurrentDictionary.GetOrAdd` uses to insert it — invisible as long as every strategy did real async I/O, but a strategy that short-circuits without ever truly awaiting (e.g. a `NotSignedIn` right after a `File.Exists` check) would let the removal race ahead of the insert, leaking a permanently-stuck completed task and silently freezing that instance forever. Fixed with one `await Task.Yield();` at the top of the method — see the comment there for the full mechanism. Caught only because a throttle test happened to call the same instance twice in a row with a fully synchronous fake strategy; worth remembering if this method ever gets "simplified."

## Known limitations

- Grok has no confirmed usage-percentage source yet — see the doc comment on `GrokAuthFileStrategy` for what's been investigated (a gRPC-web billing endpoint that turned out not to carry usage data, and a promising `rest/rate-limits` JSON endpoint that wasn't confirmed end-to-end due to lack of a test account).
- No automatic browser-cookie import (Chrome/Edge DPAPI decryption, Linux keyring handling) — Ollama's cookie field is manual-paste only, by design, since automatic decryption is the most fragile part of both reference apps.
- No installers/packaging yet (winget, AUR, Flatpak) — `dotnet run` only for now.
