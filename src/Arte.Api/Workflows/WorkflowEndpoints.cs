using Arte.Api.Security;
using Arte.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Workflows;

public static class WorkflowEndpoints
{
    public static void MapWorkflows(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/workflow", async (ArteDbContext db, CancellationToken ct) =>
        {
            var wf = await db.Workflows.AsNoTracking()
                .Include(w => w.Stages)
                .Include(w => w.Transitions)
                .SingleOrDefaultAsync(w => w.IsDefault, ct);
            if (wf is null) return Results.NotFound();

            return Results.Ok(new
            {
                wf.Id, wf.Name,
                Stages = wf.Stages.Where(s => s.IsActive).OrderBy(s => s.Order)
                    .Select(s => new { s.Id, s.Key, s.Name, s.Category, s.Color, s.IsTerminal }),
                Transitions = wf.Transitions
                    .Select(t => new { t.Id, t.FromStageId, t.ToStageId, t.Label, t.IsPrimary, t.RequiresReason, t.RequiredPermission }),
            });
        }).RequireTenant();
    }
}
