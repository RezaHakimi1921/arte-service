/**
 * The unfinished new-case form («پذیرش جدید»), kept in this browser so moving to another page (or a reload) does not lose
 * what was typed. One draft per person and branch, kept for 3 days, removed when the case is created or the draft is cleared.
 * Only this phone's browser holds it: nothing is sent to the server until the intake is submitted.
 */
const DAYS = 3;
const keyOf = (scope: string) => `arte-intake-draft:${scope}`;

export function loadDraft<T>(scope: string): T | null {
  try {
    const raw = localStorage.getItem(keyOf(scope));
    if (!raw) return null;
    const { at, data } = JSON.parse(raw) as { at: number; data: T };
    if (Date.now() - at > DAYS * 864e5) { localStorage.removeItem(keyOf(scope)); return null; }
    return data;
  } catch {
    return null;
  }
}

export function saveDraft(scope: string, data: unknown) {
  try { localStorage.setItem(keyOf(scope), JSON.stringify({ at: Date.now(), data })); } catch { /* storage blocked or full */ }
}

export function clearDraft(scope: string) {
  try { localStorage.removeItem(keyOf(scope)); } catch { /* ignore */ }
}

export const hasDraft = (scope: string) => loadDraft(scope) !== null;

/** A form is worth keeping only when the person typed or picked something. */
export function isBlank(data: Record<string, unknown>): boolean {
  return Object.values(data).every((v) =>
    v === "" || v === null || v === undefined || v === false || (Array.isArray(v) && v.length === 0)
    || (typeof v === "object" && !Array.isArray(v) && isBlank(v as Record<string, unknown>)));
}
