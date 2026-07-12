using System.Text.Json;

namespace CrossdexBar.Providers.Cursor;

/// <summary>
/// Cursor.app stores its own session as a JWT in local storage. The user id embedded in the JWT's `sub`
/// claim combines with the raw token to form the same cookie Cursor's web app itself would send.
/// </summary>
internal sealed class CursorAppAuthSession(string accessToken)
{
    public string AccessToken { get; } = accessToken;

    // "%3A%3A" is the literal separator CodexBar's own client uses (percent-encoded "::"), not a general
    // URL-escape of the whole value — matching it exactly matters since Cursor's API compares it as-is.
    public string CookieHeader() => $"WorkosCursorSessionToken={UserId()}%3A%3A{AccessToken}";

    public string UserId()
    {
        var subject = ReadClaim("sub") ?? throw new FormatException("Cursor access token is missing a 'sub' claim.");
        var userId = subject.Contains('|') ? subject[(subject.LastIndexOf('|') + 1)..] : subject;
        if (string.IsNullOrEmpty(userId))
            throw new FormatException("Cursor access token has an empty user id.");
        return userId;
    }

    public DateTimeOffset? ExpiresAt()
    {
        if (ReadClaim("exp") is not { } expClaim || !long.TryParse(expClaim, out var seconds))
            return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    private string? ReadClaim(string name)
    {
        var parts = AccessToken.Split('.');
        if (parts.Length < 2)
            throw new FormatException("Cursor access token is not a JWT.");

        var payloadJson = DecodeBase64Url(parts[1]);
        using var document = JsonDocument.Parse(payloadJson);
        return document.RootElement.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;
    }

    private static string DecodeBase64Url(string input)
    {
        var base64 = input.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}
