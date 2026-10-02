using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;

namespace Arte.Api.Security;

public sealed class Audit(ArteDbContext db, IHttpContextAccessor http, IClock clock)
{
    /// <summary>Queued on the context; saved with the caller's SaveChanges.</summary>
    public void Record(string type, Guid? tenantId, Guid? userId, string? detail = null) =>
        db.AuditEvents.Add(new AuditEvent
        {
            Type = type,
            TenantId = tenantId,
            UserId = userId,
            Detail = detail is { Length: > 1000 } ? detail[..1000] : detail,
            Ip = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            OccurredAt = clock.UtcNow,
        });
}
