using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Arte.Api.Sms.SmartX;

/// توکن را کش می‌کند و ۲ دقیقه قبل از انقضا خودکار تمدید می‌کند. Thread-safe.
public sealed class SmartXTokenProvider
{
    private sealed record Cached(string Token, DateTimeOffset ExpiresAt);

    private readonly IHttpClientFactory _factory;
    private readonly SmartXSmsOptions _o;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile Cached? _cached;

    public SmartXTokenProvider(IHttpClientFactory factory, IOptions<SmartXSmsOptions> options)
    {
        _factory = factory;
        _o = options.Value;
    }

    /// rejectedToken: اگر API همین توکن را با 401 رد کرد، آن را بده تا توکن تازه گرفته شود.
    public async Task<string> GetTokenAsync(CancellationToken ct, string? rejectedToken = null)
    {
        if (TryCached(rejectedToken, out var token)) return token;

        await _lock.WaitAsync(ct);
        try
        {
            if (TryCached(rejectedToken, out token)) return token;

            _o.Validate();
            var http = _factory.CreateClient(SmartXSmsClient.HttpClientName);
            using var resp = await http.PostAsync("/connect/token", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "tenant_service",
                    ["client_id"] = _o.ClientId,
                    ["client_secret"] = _o.ClientSecret,
                    ["tenant_id"] = _o.TenantId,
                    ["scope"] = _o.Scope,
                }), ct);

            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"SmartX token request failed: HTTP {(int)resp.StatusCode} {Truncate(text)}", null, resp.StatusCode);

            using var doc = JsonDocument.Parse(text);
            var access = doc.RootElement.GetProperty("access_token").GetString()
                         ?? throw new InvalidOperationException("SmartX token response has no access_token.");
            var seconds = doc.RootElement.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number
                ? e.GetInt32()
                : 3600;

            _cached = new Cached(access, DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, seconds - 120)));
            return access;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool TryCached(string? rejectedToken, out string token)
    {
        var c = _cached;
        if (c is not null && c.Token != rejectedToken && DateTimeOffset.UtcNow < c.ExpiresAt)
        {
            token = c.Token;
            return true;
        }
        token = "";
        return false;
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
