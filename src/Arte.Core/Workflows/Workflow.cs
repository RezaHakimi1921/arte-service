using Arte.Core.Tenancy;

namespace Arte.Core.Workflows;

public static class StageCategories
{
    public const string Open = "open";
    public const string Waiting = "waiting";
    public const string Active = "active";
    public const string Done = "done";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Open, Waiting, Active, Done, Cancelled };
}

public sealed class Workflow : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public bool IsDefault { get; set; }
    public string? SourceTemplateKey { get; set; }
    public List<Stage> Stages { get; set; } = [];
    public List<Transition> Transitions { get; set; } = [];
}

public sealed class Stage : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid WorkflowId { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public string Color { get; set; } = "gray";
    public int Order { get; set; }
    public bool IsTerminal { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Transition : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid WorkflowId { get; set; }
    public Guid FromStageId { get; set; }
    public Guid ToStageId { get; set; }
    public required string Label { get; set; }
    public bool IsPrimary { get; set; }
    public bool RequiresReason { get; set; }
    /// <summary>Permission needed to run it, e.g. cases.work or cases.create.</summary>
    public required string RequiredPermission { get; set; }
    /// <summary>Only offered while the stage with this key is switched off (e.g. "finish → ready" when there is no review).</summary>
    public string? UnlessStageKey { get; set; }
}
