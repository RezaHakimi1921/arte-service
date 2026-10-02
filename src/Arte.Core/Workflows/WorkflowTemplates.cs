using System.Text.Json;
using Arte.Core.Identity;

namespace Arte.Core.Workflows;

public static class WorkflowTemplates
{
    public const string MotorcycleRepair = "motorcycle_repair";

    public static readonly IReadOnlySet<string> Available = new HashSet<string> { MotorcycleRepair };

    private sealed record TemplateFile(string Key, string Name, List<TemplateStage> Stages, List<TemplateTransition> Transitions);
    private sealed record TemplateStage(string Key, string Name, string Category, string Color, bool Terminal = false);
    private sealed record TemplateTransition(string From, string To, string Label, string Permission, bool Primary = false, bool Reason = false);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Copies a template into a new tenant. Later template changes do not touch existing tenants.</summary>
    public static Workflow Instantiate(string templateKey, Guid tenantId)
    {
        if (!Available.Contains(templateKey)) throw new ArgumentException($"Unknown template '{templateKey}'.");

        using var stream = typeof(WorkflowTemplates).Assembly
            .GetManifestResourceStream($"Arte.Core.Workflows.Templates.{templateKey}.json")
            ?? throw new InvalidOperationException($"Template resource '{templateKey}' is missing.");
        var file = JsonSerializer.Deserialize<TemplateFile>(stream, Json)
            ?? throw new InvalidOperationException("Template is empty.");

        var workflow = new Workflow { TenantId = tenantId, Name = file.Name, IsDefault = true, SourceTemplateKey = file.Key };
        var byKey = new Dictionary<string, Stage>();
        var order = 0;
        foreach (var s in file.Stages)
        {
            if (!StageCategories.All.Contains(s.Category)) throw new InvalidOperationException($"Bad category '{s.Category}'.");
            var stage = new Stage
            {
                TenantId = tenantId, WorkflowId = workflow.Id, Key = s.Key, Name = s.Name,
                Category = s.Category, Color = s.Color, Order = order++, IsTerminal = s.Terminal,
            };
            byKey.Add(s.Key, stage);
            workflow.Stages.Add(stage);
        }

        foreach (var t in file.Transitions)
        {
            if (!Permissions.All.Contains(t.Permission)) throw new InvalidOperationException($"Bad permission '{t.Permission}'.");
            workflow.Transitions.Add(new Transition
            {
                TenantId = tenantId, WorkflowId = workflow.Id,
                FromStageId = byKey[t.From].Id, ToStageId = byKey[t.To].Id,
                Label = t.Label, IsPrimary = t.Primary, RequiresReason = t.Reason, RequiredPermission = t.Permission,
            });
        }

        return workflow;
    }
}
