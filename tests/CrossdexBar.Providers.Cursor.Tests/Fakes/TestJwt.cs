using System.Text;
using System.Text.Json;

namespace CrossdexBar.Providers.Cursor.Tests.Fakes;

internal static class TestJwt
{
    public static string Create(string sub, long? expiresAtUnixSeconds = null)
    {
        var payload = new Dictionary<string, object?> { ["sub"] = sub };
        if (expiresAtUnixSeconds is { } exp)
            payload["exp"] = exp;

        var json = JsonSerializer.Serialize(payload);
        var payloadSegment = Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        return $"header.{payloadSegment}.signature";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
