namespace Arte.Api.Diagnostics;

/// <summary>
/// Errors from the PWA are posted here and forwarded to error tracking (Bugsink), so the tracker itself never has
/// to be reachable from the internet. Bounded sizes, rate limited, and no request data beyond what is listed.
/// </summary>
public static class ClientErrorEndpoints
{
    public sealed record ClientError(string? Message, string? Stack, string? Url, string? Release);

    private static string? Cut(string? s, int max) => string.IsNullOrEmpty(s) ? null : s.Length > max ? s[..max] : s;

    public static void MapClientErrors(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/client-errors", (ClientError e, HttpContext http) =>
        {
            var message = Cut(e.Message, 500);
            if (message is null) return Results.NoContent();
            // Only the path: a query string could carry data that does not belong in the tracker.
            var path = Cut(e.Url is { } u && Uri.TryCreate(u, UriKind.Absolute, out var parsed) ? parsed.AbsolutePath : null, 200);
            SentrySdk.CaptureMessage($"[web] {message}", scope =>
            {
                scope.Level = SentryLevel.Error;
                scope.SetTag("source", "web");
                if (path is not null) scope.SetTag("page", path);
                if (Cut(e.Release, 40) is { } release) scope.SetTag("web_release", release);
                if (Cut(e.Stack, 4000) is { } stack) scope.SetExtra("stack", stack);
                scope.SetExtra("userAgent", Cut(http.Request.Headers.UserAgent.ToString(), 300));
            });
            return Results.NoContent();
        }).RequireRateLimiting("auth");
    }
}
