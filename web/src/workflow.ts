import { useEffect, useState } from "react";
import { api } from "./api";

export type WorkflowStage = { id: string; key: string; name: string; category: string; color: string; isTerminal: boolean };
type Workflow = { id: string; name: string; stages: WorkflowStage[] };

let cache: Promise<Workflow> | null = null;

/** The business's active workflow, fetched once per page load (settings changes call `resetWorkflow`). */
export function useWorkflow(): Workflow | null {
  const [wf, setWf] = useState<Workflow | null>(null);
  useEffect(() => {
    cache ??= api<Workflow>("/api/v1/workflow");
    let alive = true;
    cache.then((w) => alive && setWf(w)).catch(() => { cache = null; });
    return () => { alive = false; };
  }, []);
  return wf;
}

export function resetWorkflow() {
  cache = null;
}

/**
 * The main path shown as a progress bar: active stages except side branches (waiting) and cancel.
 * A case in a waiting stage is drawn at the step it came from.
 */
export function progressOf(stages: WorkflowStage[], current: { key: string; category: string }) {
  const path = stages.filter((s) => s.category !== "cancelled" && s.category !== "waiting");
  let index = path.findIndex((s) => s.key === current.key);
  if (index < 0 && current.category === "waiting") {
    const all = stages.map((s) => s.key);
    const at = all.indexOf(current.key);
    index = path.reduce((best, s, i) => (all.indexOf(s.key) < at ? i : best), 0);
  }
  return { path, index };
}
