using System.Text.Json;
using CrossdexBar.Core.Providers;

namespace CrossdexBar.Providers.Grok;

/// <summary>
/// Reads the official `grok` CLI's <c>~/.grok/auth.json</c> (or <c>$GROK_HOME/auth.json</c>) to confirm
/// sign-in and show identity. Verified against steipete/CodexBar's Swift source (GrokAuth.swift as of
/// 2026-07): the file is a map keyed by OAuth scope URL rather than a flat object, so entries are read
/// generically and the OIDC (SuperGrok) scope is preferred over the legacy session scope.
/// </summary>
/// <remarks>
/// This does NOT return a usage percentage. A real (non-fabricated) response captured from Win-CodexBar's
/// gRPC-web billing endpoint (`grok.com/grok_api_v2.GrokBuildBilling/GetGrokCreditsConfig`, Bearer-authed
/// with this same access token) contained reset-window timestamps and status flags but no usage field at
/// all — that endpoint appears to return billing *configuration*, not consumption. A separate JSON REST
/// endpoint (`grok.com/rest/rate-limits`) does return `remainingQueries`/`totalQueries`, but the account
/// used to investigate this had no active Grok subscription, so it couldn't be confirmed end-to-end. Left
/// as identity-only until someone with a working subscription can verify a real usage source.
/// </remarks>
public sealed class GrokAuthFileStrategy : IFetchStrategy
{
    private const string OidcScopePrefix = "https://auth.x.ai::";
    private const string LegacySessionScope = "https://accounts.x.ai/sign-in";

    public string Id => "grok.auth-file";

    public Task<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken ct) =>
        Task.FromResult(File.Exists(ResolveAuthFilePath(context)));

    public async Task<ProviderFetchOutcome> FetchAsync(ProviderFetchContext context, CancellationToken ct)
    {
        var authFilePath = ResolveAuthFilePath(context);
        if (!File.Exists(authFilePath))
            return new ProviderFetchOutcome.NotSignedIn("No Grok credentials found. Run `grok login` to sign in.");

        JsonDocument document;
        try
        {
            var json = await File.ReadAllTextAsync(authFilePath, ct);
            document = JsonDocument.Parse(json);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new ProviderFetchOutcome.Failure($"Could not read Grok credentials: {ex.Message}", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new ProviderFetchOutcome.NotSignedIn("Grok credentials file is not in the expected format. Run `grok login` again.");

            var entry = SelectPreferredEntry(document.RootElement);
            if (entry is not { } accessTokenEntry)
                return new ProviderFetchOutcome.NotSignedIn("Grok credentials file has no usable session. Run `grok login` again.");

            var email = accessTokenEntry.TryGetProperty("email", out var emailProp) && emailProp.ValueKind == JsonValueKind.String
                ? emailProp.GetString()
                : null;

            var signedInAs = string.IsNullOrEmpty(email) ? "" : $" as {email}";
            return new ProviderFetchOutcome.Unavailable(
                $"Signed in{signedInAs} — no confirmed source for Grok usage data yet (see provider source comments).");
        }
    }

    private static JsonElement? SelectPreferredEntry(JsonElement root)
    {
        JsonElement? oidcCandidate = null;
        JsonElement? legacyCandidate = null;

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
                continue;
            if (!property.Value.TryGetProperty("key", out var keyProp) || keyProp.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(keyProp.GetString()))
                continue;

            if (property.Name.StartsWith(OidcScopePrefix, StringComparison.Ordinal))
                oidcCandidate = property.Value;
            else if (property.Name == LegacySessionScope || property.Name.Contains("/sign-in", StringComparison.Ordinal))
                legacyCandidate = property.Value;
        }

        return oidcCandidate ?? legacyCandidate;
    }

    private static string ResolveAuthFilePath(ProviderFetchContext context)
    {
        if (context.GetSetting("authFilePath") is { Length: > 0 } custom)
            return custom;

        var grokHome = Environment.GetEnvironmentVariable("GROK_HOME");
        var baseDir = string.IsNullOrEmpty(grokHome) ? Path.Combine(context.PlatformPaths.HomeDirectory, ".grok") : grokHome;
        return Path.Combine(baseDir, "auth.json");
    }
}
