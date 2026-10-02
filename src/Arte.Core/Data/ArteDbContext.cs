using System.Linq.Expressions;
using Arte.Core.Cases;
using Arte.Core.Common;
using Arte.Core.Customers;
using Arte.Core.Identity;
using Arte.Core.Tenancy;
using Arte.Core.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Arte.Core.Data;

public sealed class ArteDbContext(DbContextOptions<ArteDbContext> options, ITenantContext tenant) : DbContext(options)
{
    public const string TenantFilter = "tenant";
    public const string SoftDeleteFilter = "soft_delete";

    /// <summary>Read by the global query filters on every query, so it must stay a member of the context.</summary>
    public Guid? CurrentTenantId => tenant.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<Stage> Stages => Set<Stage>();
    public DbSet<Transition> Transitions => Set<Transition>();
    public DbSet<Case> Cases => Set<Case>();
    public DbSet<CaseEvent> CaseEvents => Set<CaseEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Tenant>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Vertical).HasMaxLength(40);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Address).HasMaxLength(300);
        });

        b.Entity<User>(e =>
        {
            e.Property(x => x.Mobile).HasMaxLength(11);
            e.HasIndex(x => x.Mobile).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(80);
            e.Property(x => x.Username).HasMaxLength(40);
            e.HasIndex(x => x.Username).IsUnique().HasFilter("\"Username\" IS NOT NULL");
            e.Property(x => x.PasswordHash).HasMaxLength(200);
        });

        b.Entity<Membership>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.Property(x => x.Role).HasMaxLength(20);
            e.Property(x => x.PayModel).HasMaxLength(20);
            e.Property(x => x.CommissionPercent).HasPrecision(5, 2);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OtpChallenge>(e =>
        {
            e.Property(x => x.Mobile).HasMaxLength(11);
            e.Property(x => x.CodeHash).HasMaxLength(64);
            e.Property(x => x.RequestIp).HasMaxLength(45);
            e.HasIndex(x => new { x.Mobile, x.CreatedAt });
            e.HasIndex(x => new { x.RequestIp, x.CreatedAt });
            e.Property(x => x.Version).IsRowVersion();
        });

        b.Entity<RefreshToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.FamilyId);
            e.Property(x => x.RevokedReason).HasMaxLength(40);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(60);
            e.Property(x => x.Ip).HasMaxLength(45);
            e.Property(x => x.Detail).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.OccurredAt });
        });

        b.Entity<Customer>(e =>
        {
            e.Property(x => x.Mobile).HasMaxLength(11);
            e.Property(x => x.FullName).HasMaxLength(120);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => new { x.TenantId, x.Mobile }).IsUnique().HasFilter("\"DeletedAt\" IS NULL");
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.HasMany(x => x.Assets).WithOne().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Asset>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(120);
            e.Property(x => x.Identifier).HasMaxLength(60);
            e.HasIndex(x => new { x.TenantId, x.CustomerId });
            e.HasIndex(x => new { x.TenantId, x.Identifier });
        });

        b.Entity<Workflow>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.SourceTemplateKey).HasMaxLength(60);
            e.HasIndex(x => x.TenantId);
            e.HasMany(x => x.Stages).WithOne().HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Transitions).WithOne().HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Stage>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(40);
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Category).HasMaxLength(20);
            e.Property(x => x.Color).HasMaxLength(20);
            e.HasIndex(x => new { x.WorkflowId, x.Key }).IsUnique();
        });

        b.Entity<Transition>(e =>
        {
            e.Property(x => x.Label).HasMaxLength(60);
            e.Property(x => x.RequiredPermission).HasMaxLength(40);
            e.HasIndex(x => new { x.WorkflowId, x.FromStageId });
            e.HasOne<Stage>().WithMany().HasForeignKey(x => x.FromStageId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Stage>().WithMany().HasForeignKey(x => x.ToStageId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Case>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.StageId });
            e.HasIndex(x => new { x.TenantId, x.AssigneeId });
            e.HasIndex(x => new { x.TenantId, x.CustomerId });
            e.HasIndex(x => new { x.TenantId, x.AssetId });
            e.Property(x => x.Request).HasMaxLength(2000);
            e.Property(x => x.Diagnosis).HasMaxLength(4000);
            e.Property(x => x.CustodyStatus).HasMaxLength(20);
            e.Property(x => x.Relation).HasMaxLength(20);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Stage>().WithMany().HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Workflow>().WithMany().HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Membership>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Case>().WithMany().HasForeignKey(x => x.ParentCaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CaseEvent>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(60);
            e.HasIndex(x => new { x.TenantId, x.CaseId, x.Id });
            e.HasOne<Case>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Restrict);
        });

        ApplyTenantFilters(b);
    }

    /// <summary>
    /// Every ITenantOwned entity only ever returns rows of the current tenant.
    /// With no tenant selected the filter matches nothing. Bypassing it needs an explicit IgnoreQueryFilters().
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
        {
            var p = Expression.Parameter(entity.ClrType, "e");
            var rowTenant = Expression.Convert(Expression.Property(p, nameof(ITenantOwned.TenantId)), typeof(Guid?));
            var current = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
            var body = Expression.Equal(rowTenant, current);
            b.Entity(entity.ClrType).HasQueryFilter(TenantFilter, Expression.Lambda(body, p));
        }

        foreach (var entity in b.Model.GetEntityTypes().Where(t => typeof(ISoftDeletable).IsAssignableFrom(t.ClrType)))
        {
            var p = Expression.Parameter(entity.ClrType, "e");
            var deletedAt = Expression.Property(p, nameof(ISoftDeletable.DeletedAt));
            var body = Expression.Equal(deletedAt, Expression.Constant(null, typeof(DateTimeOffset?)));
            b.Entity(entity.ClrType).HasQueryFilter(SoftDeleteFilter, Expression.Lambda(body, p));
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        GuardTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    /// <summary>
    /// New tenant rows get the current tenant; any write that touches another tenant's row is refused,
    /// even if a query bypassed the filter.
    /// </summary>
    private void GuardTenantWrites()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;

            var current = CurrentTenantId
                ?? throw new TenantViolationException($"Write to {entry.Metadata.ClrType.Name} without a tenant.");

            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = current;

            if (entry.Entity.TenantId != current)
                throw new TenantViolationException($"Write to {entry.Metadata.ClrType.Name} of another tenant.");

            if (entry.State == EntityState.Modified && entry.Property(nameof(ITenantOwned.TenantId)).IsModified)
                throw new TenantViolationException("TenantId cannot change.");
        }
    }
}
