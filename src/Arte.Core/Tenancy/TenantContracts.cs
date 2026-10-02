namespace Arte.Core.Tenancy;

/// <summary>Every row that belongs to one business carries its TenantId.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

/// <summary>The business the current request acts for. Null when the caller has not selected one.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
}

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    public void Set(Guid tenantId)
    {
        if (TenantId is { } current && current != tenantId)
            throw new InvalidOperationException("Tenant is already set for this scope.");
        TenantId = tenantId;
    }
}

public sealed class TenantViolationException(string message) : Exception(message);
