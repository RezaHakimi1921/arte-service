using System.Text.Json;
using Arte.Core.Identity;
using Arte.Core.Tenancy;

namespace Arte.Core.Workflows;

public static class WorkflowTemplates
{
    public const string MotorcycleRepair = "motorcycle_repair";

    public static readonly IReadOnlySet<string> Available = new HashSet<string> { MotorcycleRepair };

    public sealed record TemplateFile(string Key, string Name, List<TemplateStage> Stages, List<TemplateTransition> Transitions);

    /// <param name="Setting">Business setting that switches this stage on/off (e.g. RequireFinalReview).</param>
    public sealed record TemplateStage(string Key, string Name, string Category, string Color, bool Terminal = false, string? Setting = null);

    /// <param name="Unless">Stage key: this transition only exists while that stage is switched off.</param>
    public sealed record TemplateTransition(string From, string To, string Label, string Permission, bool Primary = false, bool Reason = false, string? Unless = null);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TemplateFile Load(string templateKey)
    {
        if (!Available.Contains(templateKey)) throw new ArgumentException($"Unknown template '{templateKey}'.");
        using var stream = typeof(WorkflowTemplates).Assembly
            .GetManifestResourceStream($"Arte.Core.Workflows.Templates.{templateKey}.json")
            ?? throw new InvalidOperationException($"Template resource '{templateKey}' is missing.");
        var file = JsonSerializer.Deserialize<TemplateFile>(stream, Json) ?? throw new InvalidOperationException("Template is empty.");
        foreach (var s in file.Stages)
            if (!StageCategories.All.Contains(s.Category)) throw new InvalidOperationException($"Bad category '{s.Category}'.");
        foreach (var t in file.Transitions)
            if (!Permissions.All.Contains(t.Permission)) throw new InvalidOperationException($"Bad permission '{t.Permission}'.");
        return file;
    }

    /// <summary>Whether a business setting switches a stage on.</summary>
    public static bool StageEnabled(TemplateStage s, Tenant? tenant) => s.Setting switch
    {
        null => true,
        "RequireCustomerApproval" => tenant?.RequireCustomerApproval ?? false,
        "RequireFinalReview" => tenant?.RequireFinalReview ?? true,
        _ => true,
    };

    /// <summary>Copies a template into a new tenant. Later template changes are applied by WorkflowUpgrader.</summary>
    public static Workflow Instantiate(string templateKey, Guid tenantId, Tenant? tenant = null)
    {
        var file = Load(templateKey);
        var workflow = new Workflow { TenantId = tenantId, Name = file.Name, IsDefault = true, SourceTemplateKey = file.Key };
        var byKey = new Dictionary<string, Stage>();
        var order = 0;
        foreach (var s in file.Stages)
        {
            var stage = new Stage
            {
                TenantId = tenantId, WorkflowId = workflow.Id, Key = s.Key, Name = s.Name,
                Category = s.Category, Color = s.Color, Order = order++, IsTerminal = s.Terminal,
                IsActive = StageEnabled(s, tenant),
            };
            byKey.Add(s.Key, stage);
            workflow.Stages.Add(stage);
        }

        foreach (var t in file.Transitions)
            workflow.Transitions.Add(NewTransition(t, tenantId, workflow.Id, byKey));
        return workflow;
    }

    public static Transition NewTransition(TemplateTransition t, Guid tenantId, Guid workflowId, IReadOnlyDictionary<string, Stage> byKey) => new()
    {
        TenantId = tenantId, WorkflowId = workflowId,
        FromStageId = byKey[t.From].Id, ToStageId = byKey[t.To].Id,
        Label = t.Label, IsPrimary = t.Primary, RequiresReason = t.Reason, RequiredPermission = t.Permission,
        UnlessStageKey = t.Unless,
    };
}
