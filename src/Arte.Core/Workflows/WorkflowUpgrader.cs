using Arte.Core.Cases;
using Arte.Core.Data;
using Arte.Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arte.Core.Workflows;

/// <summary>
/// Keeps businesses on the current template and applies business settings to optional stages.
/// Idempotent; runs at startup and whenever the business settings change.
/// Stages are never deleted (cases point at them); a stage that left the template is switched off and its
/// open cases move to the stage named in <see cref="Replacements"/>.
/// </summary>
public static class WorkflowUpgrader
{
    /// <summary>Old stage key → new stage key, for stages removed from a template.</summary>
    private static readonly Dictionary<string, string> Replacements = new() { ["testing"] = "review" };

    /// <summary>Run with an ArteDbContext whose tenant is <paramref name="tenant"/> (the write guard requires it).</summary>
    public static async Task<int> UpgradeTenantAsync(ArteDbContext db, Tenant tenant, CancellationToken ct)
    {
        var workflows = await db.Workflows.IgnoreQueryFilters()
            .Where(w => w.TenantId == tenant.Id && w.SourceTemplateKey != null)
            .Include(w => w.Stages).Include(w => w.Transitions)
            .ToListAsync(ct);
        var changes = 0;

        foreach (var wf in workflows)
        {
            if (!WorkflowTemplates.Available.Contains(wf.SourceTemplateKey!)) continue;
            var template = WorkflowTemplates.Load(wf.SourceTemplateKey!);
            if (wf.Name != template.Name) { wf.Name = template.Name; changes++; }

            // Stages: add missing, keep order/name/category in step, apply settings.
            var byKey = wf.Stages.ToDictionary(s => s.Key);
            var order = 0;
            foreach (var ts in template.Stages)
            {
                var enabled = WorkflowTemplates.StageEnabled(ts, tenant);
                if (!byKey.TryGetValue(ts.Key, out var stage))
                {
                    stage = new Stage
                    {
                        TenantId = tenant.Id, WorkflowId = wf.Id, Key = ts.Key, Name = ts.Name, Category = ts.Category,
                        Color = ts.Color, IsTerminal = ts.Terminal, Order = order, IsActive = enabled,
                    };
                    wf.Stages.Add(stage);
                    db.Stages.Add(stage);
                    byKey[ts.Key] = stage;
                    changes++;
                }
                else
                {
                    if (stage.Order != order) { stage.Order = order; changes++; }
                    if (stage.Category != ts.Category) { stage.Category = ts.Category; changes++; }
                    if (stage.IsTerminal != ts.Terminal) { stage.IsTerminal = ts.Terminal; changes++; }
                    // Only settings-driven stages are switched by the upgrader; others keep the owner's choice.
                    if (ts.Setting is not null && stage.IsActive != enabled) { stage.IsActive = enabled; changes++; }
                }
                order++;
            }

            // Stages that left the template: switch off, move their open cases on.
            var templateKeys = template.Stages.Select(s => s.Key).ToHashSet();
            foreach (var orphan in wf.Stages.Where(s => !templateKeys.Contains(s.Key)).ToList())
            {
                if (orphan.IsActive) { orphan.IsActive = false; changes++; }
                if (orphan.Order < 100) { orphan.Order = 100 + orphan.Order; changes++; }
                if (Replacements.TryGetValue(orphan.Key, out var target) && byKey.TryGetValue(target, out var to))
                    changes += await db.Cases.IgnoreQueryFilters()
                        .Where(c => c.TenantId == tenant.Id && c.StageId == orphan.Id)
                        .ExecuteUpdateAsync(x => x.SetProperty(c => c.StageId, to.Id), ct);
            }

            // Transitions are configuration, not history: make them exactly the template's.
            var wanted = template.Transitions.ToDictionary(t => (t.From, t.To));
            var keyOf = wf.Stages.ToDictionary(s => s.Id, s => s.Key);
            foreach (var existing in wf.Transitions.ToList())
            {
                var pair = (keyOf[existing.FromStageId], keyOf[existing.ToStageId]);
                if (!wanted.TryGetValue(pair, out var t))
                {
                    db.Transitions.Remove(existing);
                    wf.Transitions.Remove(existing);
                    changes++;
                    continue;
                }
                if (existing.Label != t.Label || existing.IsPrimary != t.Primary || existing.RequiresReason != t.Reason
                    || existing.RequiredPermission != t.Permission || existing.UnlessStageKey != t.Unless)
                {
                    existing.Label = t.Label;
                    existing.IsPrimary = t.Primary;
                    existing.RequiresReason = t.Reason;
                    existing.RequiredPermission = t.Permission;
                    existing.UnlessStageKey = t.Unless;
                    changes++;
                }
                wanted.Remove(pair);
            }
            foreach (var t in wanted.Values)
            {
                var transition = WorkflowTemplates.NewTransition(t, tenant.Id, wf.Id, byKey);
                wf.Transitions.Add(transition);
                db.Transitions.Add(transition);
                changes++;
            }
        }
        return changes;
    }
}
