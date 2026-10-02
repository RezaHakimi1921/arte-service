namespace Arte.Api.Security;

/// <summary>The API only ever returns JSON, so it denies everything a browser could do with a response.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        var h = http.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        h["Cross-Origin-Resource-Policy"] = "same-origin";
        h["Cache-Control"] = "no-store";
        return next(http);
    }
}
