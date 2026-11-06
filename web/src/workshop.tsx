import { useEffect, useState, type ReactNode } from "react";
import { api, apiBlob, ApiError } from "./api";
import { useFeedback } from "./feedback";
import { BottomSheet, SelectField, SelectSheet } from "./sheet";
import { GripIcon, SortableList, type HandleProps } from "./sortable";
import { SubPage } from "./settings";
import { formatNumber } from "./ui";
import { MOTO_PROBLEMS, MOTO_SERVICE_CATEGORIES } from "./vehicles";

export const MOTOR_TYPES = [
  { key: "geared", label: "دنده‌ای" },
  { key: "automatic", label: "کلیچ / اتوماتیک" },
] as const;

export type IntakeOption = { id: string; kind: "problem" | "service" | "category" | "service_category"; group: string | null; title: string; sortOrder: number; isActive: boolean };
export type TemplateRow = { id: string; group: string | null; title: string; isRequired: boolean; sortOrder: number; isActive: boolean };
export type CaseChecklistRow = { id: string; group: string | null; title: string; isRequired: boolean; isChecked: boolean; note: string | null; checkedAt: string | null; checkedByName: string | null };
export type CaseFinding = { id: string; source: string; title: string; note: string | null; createdAt: string; createdByName: string | null };

/** The workshop's own problem and service lists; empty means "use the built-in lists". */
export function useIntakeOptions() {
  const [options, setOptions] = useState<IntakeOption[]>([]);
  useEffect(() => { api<IntakeOption[]>("/api/v1/intake-options").then(setOptions).catch(() => {}); }, []);
  const problems = options.filter((o) => o.kind === "problem").map((o) => o.title);
  const services = options.filter((o) => o.kind === "service");
  const serviceCategories = services.length === 0 ? null : Object.entries(
    services.reduce<Record<string, string[]>>((acc, s) => { (acc[s.group ?? "سایر"] ??= []).push(s.title); return acc; }, {}),
  ).map(([name, list]) => ({ name, services: list }));
  return { problems: problems.length ? problems : null, serviceCategories };
}

function groupRows<T extends { group: string | null }>(rows: T[]) {
  const out: { name: string; rows: T[] }[] = [];
  for (const r of rows) {
    const name = r.group ?? "";
    const last = out[out.length - 1];
    if (last && last.name === name) last.rows.push(r); else out.push({ name, rows: [r] });
  }
  return out;
}

/** Intake: choose the motor type, then tick the inspection list the manager set up. */
export function IntakeChecklist({ motorType, onMotorType, ticks, onTicks, error }: {
  motorType: string | null; onMotorType: (t: string | null) => void; ticks: Record<string, boolean>; onTicks: (t: Record<string, boolean>) => void; error?: string;
}) {
  const [rows, setRows] = useState<TemplateRow[] | null>(null);
  useEffect(() => {
    setRows(null);
    if (!motorType) return;
    api<TemplateRow[]>(`/api/v1/checklist-templates/${motorType}`).then(setRows).catch(() => setRows([]));
  }, [motorType]);

  return (
    <div className="stack">
      <div className="chips" role="radiogroup" aria-label="نوع موتور">
        {MOTOR_TYPES.map((t) => (
          <button type="button" key={t.key} role="radio" aria-checked={motorType === t.key} className={`chip-button${motorType === t.key ? " active" : ""}`}
            onClick={() => { onTicks({}); onMotorType(motorType === t.key ? null : t.key); }}>{t.label}</button>
        ))}
      </div>
      {motorType && !rows && <div className="splash" aria-busy="true" />}
      {motorType && rows && rows.length === 0 && <p className="hint">برای این نوع موتور هنوز چک‌لیستی تعریف نشده؛ از تنظیمات ← فهرست‌ها و چک‌لیست بسازید.</p>}
      {rows && rows.length > 0 && (
        <>
          <p className="hint">{formatNumber(rows.filter((r) => ticks[r.id]).length)} از {formatNumber(rows.length)} مورد بررسی شد</p>
          {groupRows(rows).map((g) => (
            <div key={g.name}>
              {g.name && <h4>{g.name}</h4>}
              <div className="checks" role="group" aria-label={g.name || "چک‌لیست"}>
                {g.rows.map((r) => (
                  <label key={r.id} className={`check-chip${ticks[r.id] ? " on" : ""}`}>
                    <input type="checkbox" checked={!!ticks[r.id]} onChange={() => onTicks({ ...ticks, [r.id]: !ticks[r.id] })} />
                    <span className="box" aria-hidden="true">{ticks[r.id] ? "✓" : ""}</span>
                    {r.title}{!r.isRequired && <span className="muted small"> (اختیاری)</span>}
                  </label>
                ))}
              </div>
            </div>
          ))}
          <button type="button" className="secondary" onClick={() => onTicks(Object.fromEntries(rows.map((r) => [r.id, true])))}>همه را بررسی‌شده بزن</button>
        </>
      )}
      {error && <span className="error">{error}</span>}
    </div>
  );
}

/** On the case: the saved checklist (tick later, see who ticked) and the problems found during work. */
export function CaseWorkshopSections({ caseId, checklist, findings, motorType, canEdit, onChange }: {
  caseId: string; checklist: CaseChecklistRow[]; findings: CaseFinding[]; motorType: string | null; canEdit: boolean; onChange: () => void;
}) {
  const { notify } = useFeedback();
  const [busy, setBusy] = useState(false);
  const [title, setTitle] = useState("");
  const [source, setSource] = useState<"mechanic" | "customer">("mechanic");
  const done = checklist.filter((r) => r.isChecked).length;
  const date = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long" });

  async function run(fn: () => Promise<unknown>, ok?: string) {
    if (busy) return;
    setBusy(true);
    try { await fn(); if (ok) notify(ok); onChange(); }
    catch (e) { notify(e instanceof ApiError ? e.message : "خطا در ارتباط با سرور", "error"); }
    finally { setBusy(false); }
  }

  return (
    <>
      {checklist.length > 0 && (
        <details className="case-section" open={done < checklist.length}>
          <summary>چک‌لیست بازدید{motorType ? ` · ${MOTOR_TYPES.find((t) => t.key === motorType)?.label ?? ""}` : ""} <span className="badge font-num">{formatNumber(done)}/{formatNumber(checklist.length)}</span></summary>
          {groupRows(checklist).map((g) => (
            <div key={g.name}>
              {g.name && <h4>{g.name}</h4>}
              <div className="checks">
                {g.rows.map((r) => (
                  <label key={r.id} className={`check-chip${r.isChecked ? " on" : ""}`}>
                    <input type="checkbox" checked={r.isChecked} disabled={!canEdit || busy}
                      onChange={() => run(() => api(`/api/v1/cases/${caseId}/checklist/${r.id}`, { method: "PUT", body: { isChecked: !r.isChecked } }))} />
                    <span className="box" aria-hidden="true">{r.isChecked ? "✓" : ""}</span>
                    {r.title}
                    {r.isChecked && r.checkedByName && <span className="muted small"> · {r.checkedByName}</span>}
                  </label>
                ))}
              </div>
            </div>
          ))}
        </details>
      )}

      <details className="case-section" open={findings.length > 0}>
        <summary>ایرادهای پیدا‌شده <span className="badge font-num">{formatNumber(findings.length)}</span></summary>
        {findings.length === 0 && <p className="hint">ایرادی که حین کار پیدا شد یا مشتری بعداً گفت را اینجا ثبت کنید؛ برای گزارش ایرادهای پرتکرار استفاده می‌شود.</p>}
        <ul className="plain-list">
          {findings.map((f) => (
            <li key={f.id} className="row-between">
              <span>{f.title} <span className="muted small">· {f.source === "customer" ? "مشتری" : "تعمیرکار"}{f.createdByName ? ` · ${f.createdByName}` : ""} · {date.format(new Date(f.createdAt))}</span></span>
              {canEdit && <button type="button" className="link-button" disabled={busy}
                onClick={() => run(() => api(`/api/v1/cases/${caseId}/findings/${f.id}`, { method: "DELETE" }), "حذف شد")}>حذف</button>}
            </li>
          ))}
        </ul>
        {canEdit && (
          <form className="stack" onSubmit={(e) => { e.preventDefault(); if (!title.trim()) return;
            run(async () => { await api(`/api/v1/cases/${caseId}/findings`, { body: { title: title.trim(), source } }); setTitle(""); }, "ثبت شد"); }}>
            <div className="chips" role="radiogroup" aria-label="چه کسی ایراد را گفت">
              {([["mechanic", "تعمیرکار پیدا کرد"], ["customer", "مشتری گفت"]] as const).map(([k, l]) => (
                <button type="button" key={k} role="radio" aria-checked={source === k} className={`chip-button${source === k ? " active" : ""}`} onClick={() => setSource(k)}>{l}</button>
              ))}
            </div>
            <input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={120} placeholder="ایراد (مثلاً زنجیر کشیده)" aria-label="ایراد" />
            <button type="submit" className="primary" disabled={busy || !title.trim()} aria-busy={busy}>{busy ? "در حال ثبت…" : "ثبت ایراد"}</button>
          </form>
        )}
      </details>
    </>
  );
}

// ───────── settings: lists and checklists ─────────



type EditTarget = { id: string; title: string; group: string | null; isRequired?: boolean; isActive: boolean };

/** One collapsible branch: the title (with a count) is always visible; tapping it opens what is under it. */
function Branch({ title, count, level = 1, defaultOpen = false, children }: { title: string; count?: number; level?: 1 | 2; defaultOpen?: boolean; children: ReactNode }) {
  const [open, setOpen] = useState(defaultOpen);
  return (
    <section className={`branch level-${level}${open ? " open" : ""}`}>
      <button type="button" className="branch-head" aria-expanded={open} onClick={() => setOpen(!open)}>
        <span className="branch-chev" aria-hidden="true">▾</span>
        <span className="branch-title">{title}</span>
        {count !== undefined && <span className="badge font-num">{formatNumber(count)}</span>}
      </button>
      {open && <div className="branch-body">{children}</div>}
    </section>
  );
}

/** One settings row: grip to drag, the title big, small badges under it, edit and on/off at the end. */
function ListRow({ title, badges, inactive, busy, handle, onEdit, onToggle, active }: {
  title: string; badges: string[]; inactive: boolean; busy: boolean; handle: HandleProps; onEdit: () => void; onToggle: () => void; active: boolean;
}) {
  return (
    <>
      <span {...handle}><GripIcon /></span>
      <div className={`opt-main${inactive ? " off" : ""}`}>
        <strong>{title}</strong>
        {badges.length > 0 && <span className="opt-badges">{badges.map((b) => <span key={b} className="badge subtle">{b}</span>)}</span>}
      </div>
      <div className="opt-actions">
        <button type="button" className="icon-btn" aria-label={`ویرایش ${title}`} disabled={busy} onClick={onEdit}>
          <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="M4 20h4l10.5-10.5a2.1 2.1 0 0 0-3-3L5 17v3Zm9.5-13.5 3 3" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" /></svg>
        </button>
        <input type="checkbox" role="switch" className="switch" aria-label={active ? `غیرفعال کردن ${title}` : `فعال کردن ${title}`} checked={active} disabled={busy} onChange={onToggle} />
      </div>
    </>
  );
}

/** Edit a row: its title, its category (picked from the category list when one is given, free text otherwise), and «required». */
function EditSheet({ target, withRequired, categories, groupLabel, busy, onClose, onSave }: {
  target: EditTarget | null; withRequired: boolean; categories?: string[]; groupLabel?: string; busy: boolean; onClose: () => void;
  onSave: (v: { title: string; group: string; isRequired?: boolean }) => void;
}) {
  const [title, setTitle] = useState("");
  const [group, setGroup] = useState("");
  const [req, setReq] = useState(true);
  const [pick, setPick] = useState(false);
  useEffect(() => { if (target) { setTitle(target.title); setGroup(target.group ?? ""); setReq(target.isRequired ?? true); } }, [target]);
  // A category that was renamed or removed still shows for rows that use it, so nothing is silently lost.
  const names = categories ? [...new Set([...categories, ...(group ? [group] : [])])] : [];
  return (
    <BottomSheet open={target !== null} title="ویرایش" onClose={onClose}>
      <form className="stack" onSubmit={(e) => { e.preventDefault(); if (title.trim()) onSave({ title: title.trim(), group: group.trim(), isRequired: withRequired ? req : undefined }); }}>
        <label className="field"><span>عنوان</span><input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={80} autoFocus /></label>
        {groupLabel && (categories ? (
          <div className="field">
            <span className="label">{groupLabel}</span>
            <SelectField label={group || undefined} placeholder="بدون دسته" onOpen={() => setPick(true)} />
            <SelectSheet open={pick} title={groupLabel} items={names.map((n) => ({ value: n, label: n }))} value={group || null} noneLabel="بدون دسته"
              onSelect={(v) => { setGroup(v ?? ""); setPick(false); }} onClose={() => setPick(false)} />
          </div>
        ) : (
          <label className="field"><span>{groupLabel}</span><input value={group} onChange={(e) => setGroup(e.target.value)} maxLength={40} /></label>
        ))}
        {withRequired && (
          <label className="setting-row">
            <span><strong>الزامی</strong><span className="muted small">پذیرش بدون تیک‌زدن این ردیف ثبت نمی‌شود.</span></span>
            <input type="checkbox" role="switch" className="switch" checked={req} onChange={(e) => setReq(e.target.checked)} />
          </label>
        )}
        <button type="submit" className="primary" disabled={busy || !title.trim()} aria-busy={busy}>{busy ? "در حال ذخیره…" : "ذخیره"}</button>
      </form>
    </BottomSheet>
  );
}

/** Loads a list and runs one change at a time with a toast and a reload; shared by every editor below. */
function useEditor<T>(path: string) {
  const { notify } = useFeedback();
  const [rows, setRows] = useState<T[] | null>(null);
  const [busy, setBusy] = useState(false);
  const load = () => api<T[]>(path).then(setRows).catch(() => setRows([]));
  useEffect(() => { setRows(null); load(); }, [path]);
  async function act(fn: () => Promise<unknown>, ok?: string) {
    if (busy) return;
    setBusy(true);
    try { await fn(); if (ok) notify(ok); await load(); }
    catch (e) { notify(e instanceof ApiError ? e.message : "خطا در ارتباط با سرور", "error"); }
    finally { setBusy(false); }
  }
  return { rows, busy, act, load };
}

/** Rows grouped by category: categories in the order of `groupOrder`, then any other, then rows without one. */
function groupByCategory<T extends { group: string | null }>(rows: T[], groupOrder: string[]) {
  const names = [...new Set(rows.map((r) => r.group ?? ""))];
  const rank = (n: string) => (n === "" ? 1e9 : groupOrder.indexOf(n) >= 0 ? groupOrder.indexOf(n) : 1e6 + names.indexOf(n));
  return names.sort((a, b) => rank(a) - rank(b)).map((name) => ({ name, rows: rows.filter((r) => (r.group ?? "") === name) }));
}

/** New full order after the rows of one group were reordered: the group keeps its slots, filled in the new order. */
function mergeOrder(all: { id: string }[], groupIds: string[], newGroupIds: string[]) {
  const set = new Set(groupIds);
  let k = 0;
  return all.map((r) => (set.has(r.id) ? newGroupIds[k++] : r.id));
}

function ProblemsEditor() {
  const { rows, busy, act } = useEditor<IntakeOption>("/api/v1/intake-options?kind=problem&includeInactive=true");
  const [title, setTitle] = useState("");
  const [edit, setEdit] = useState<EditTarget | null>(null);
  const defaults = MOTO_PROBLEMS.map((t) => ({ group: "", title: t }));
  if (!rows) return <div className="splash" aria-busy="true" />;
  return (
    <>
      {rows.length === 0 && (
        <div className="hint-action">
          <p className="hint">فعلاً فهرست پیش‌فرض برنامه نشان داده می‌شود. با «شروع از فهرست پیش‌فرض» یک نسخه‌ی قابل‌ویرایش از آن بسازید.</p>
          <button type="button" className="secondary" disabled={busy}
            onClick={() => act(() => api("/api/v1/intake-options/import", { body: { items: defaults.map((d) => ({ kind: "problem", group: d.group, title: d.title })) } }), "فهرست پیش‌فرض اضافه شد")}>شروع از فهرست پیش‌فرض</button>
        </div>
      )}
      <SortableList items={rows} disabled={busy} label={(r) => r.title}
        onReorder={(ids) => act(() => api("/api/v1/intake-options/order", { method: "PUT", body: { ids } }))}
        render={(r, handle) => (
          <ListRow title={r.title} badges={[]} inactive={!r.isActive} active={r.isActive} busy={busy} handle={handle}
            onEdit={() => setEdit({ id: r.id, title: r.title, group: null, isActive: r.isActive })}
            onToggle={() => act(() => api(`/api/v1/intake-options/${r.id}`, { method: "PATCH", body: { isActive: !r.isActive } }))} />
        )} />
      <EditSheet target={edit} withRequired={false} busy={busy} onClose={() => setEdit(null)}
        onSave={(v) => act(async () => { await api(`/api/v1/intake-options/${edit!.id}`, { method: "PATCH", body: { title: v.title } }); setEdit(null); }, "ذخیره شد")} />
      <form className="stack" onSubmit={(e) => { e.preventDefault(); if (!title.trim()) return;
        act(async () => { await api("/api/v1/intake-options", { body: { kind: "problem", title: title.trim() } }); setTitle(""); }, "اضافه شد"); }}>
        <label className="field"><span>ایراد جدید</span><input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={80} /></label>
        <button type="submit" className="primary" disabled={busy || !title.trim()} aria-busy={busy}>افزودن</button>
      </form>
    </>
  );
}

/** Services: one branch per category (the service's own group), each opening to its services. */
function ServicesEditor({ categories, onChanged }: { categories: string[]; onChanged: () => void }) {
  const { rows, busy, act } = useEditor<IntakeOption>("/api/v1/intake-options?kind=service&includeInactive=true");
  const [title, setTitle] = useState("");
  const [group, setGroup] = useState("");
  const [pick, setPick] = useState(false);
  const [edit, setEdit] = useState<EditTarget | null>(null);
  const defaults = MOTO_SERVICE_CATEGORIES.flatMap((c) => c.services.map((t) => ({ group: c.name, title: t })));
  if (!rows) return <div className="splash" aria-busy="true" />;
  return (
    <>
      {rows.length === 0 && (
        <div className="hint-action">
          <p className="hint">فعلاً فهرست پیش‌فرض برنامه نشان داده می‌شود. با «شروع از فهرست پیش‌فرض» یک نسخه‌ی قابل‌ویرایش از آن بسازید.</p>
          <button type="button" className="secondary" disabled={busy}
            onClick={() => act(async () => {
              // The default groups become the service categories too, so every service has a category to pick from.
              await api("/api/v1/intake-options/import", { body: { items: MOTO_SERVICE_CATEGORIES.map((c) => ({ kind: "service_category", title: c.name })) } });
              await api("/api/v1/intake-options/import", { body: { items: defaults.map((d) => ({ kind: "service", group: d.group, title: d.title })) } });
              onChanged();
            }, "فهرست پیش‌فرض اضافه شد")}>شروع از فهرست پیش‌فرض</button>
        </div>
      )}
      {groupByCategory(rows, categories).map((g) => (
        <Branch key={g.name} level={2} title={g.name || "بدون دسته"} count={g.rows.length}>
          <SortableList items={g.rows} disabled={busy} label={(r) => r.title}
            onReorder={(ids) => act(() => api("/api/v1/intake-options/order", { method: "PUT", body: { ids: mergeOrder(rows, g.rows.map((r) => r.id), ids) } }))}
            render={(r, handle) => (
              <ListRow title={r.title} badges={[]} inactive={!r.isActive} active={r.isActive} busy={busy} handle={handle}
                onEdit={() => setEdit({ id: r.id, title: r.title, group: r.group, isActive: r.isActive })}
                onToggle={() => act(() => api(`/api/v1/intake-options/${r.id}`, { method: "PATCH", body: { isActive: !r.isActive } }))} />
            )} />
        </Branch>
      ))}
      <EditSheet target={edit} withRequired={false} categories={categories} groupLabel="دسته‌بندی" busy={busy} onClose={() => setEdit(null)}
        onSave={(v) => act(async () => { await api(`/api/v1/intake-options/${edit!.id}`, { method: "PATCH", body: { title: v.title, group: v.group } }); setEdit(null); }, "ذخیره شد")} />
      <form className="stack" onSubmit={(e) => { e.preventDefault(); if (!title.trim()) return;
        act(async () => { await api("/api/v1/intake-options", { body: { kind: "service", title: title.trim(), group: group.trim() || undefined } }); setTitle(""); }, "اضافه شد"); }}>
        <label className="field"><span>خدمت جدید</span><input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={80} /></label>
        <div className="field">
          <span className="label">دسته‌بندی</span>
          <SelectField label={group || undefined} placeholder="بدون دسته" onOpen={() => setPick(true)} />
          <SelectSheet open={pick} title="دسته‌بندی خدمات" items={categories.map((n) => ({ value: n, label: n }))} value={group || null} noneLabel="بدون دسته"
            onSelect={(v) => { setGroup(v ?? ""); setPick(false); }} onClose={() => setPick(false)} />
        </div>
        <button type="submit" className="primary" disabled={busy || !title.trim()} aria-busy={busy}>افزودن</button>
      </form>
    </>
  );
}

/** One motor type's inspection list: a branch per category (from the category list), rows dragged inside it. */
function TemplateEditor({ motorType, categories }: { motorType: string; categories: string[] }) {
  const { rows, busy, act } = useEditor<TemplateRow>(`/api/v1/checklist-templates/${motorType}?includeInactive=true`);
  const [title, setTitle] = useState("");
  const [group, setGroup] = useState("");
  const [pick, setPick] = useState(false);
  const [edit, setEdit] = useState<EditTarget | null>(null);
  if (!rows) return <div className="splash" aria-busy="true" />;
  return (
    <>
      {rows.length === 0 && (
        <div className="hint-action">
          <p className="hint">هنوز چک‌لیستی نساخته‌اید. می‌توانید از یک فهرست آماده شروع کنید و هر ردیف را تغییر دهید.</p>
          <button type="button" className="secondary" disabled={busy}
            onClick={() => act(() => api(`/api/v1/checklist-templates/${motorType}/defaults`, { body: {} }), "فهرست آماده اضافه شد")}>شروع از فهرست آماده</button>
        </div>
      )}
      {groupByCategory(rows, categories).map((g) => (
        <Branch key={g.name} level={2} title={g.name || "بدون دسته"} count={g.rows.length}>
          <SortableList items={g.rows} disabled={busy} label={(r) => r.title}
            onReorder={(ids) => act(() => api(`/api/v1/checklist-templates/${motorType}/order`, { method: "PUT", body: { ids: mergeOrder(rows, g.rows.map((r) => r.id), ids) } }))}
            render={(r, handle) => (
              <ListRow title={r.title} badges={r.isRequired ? ["الزامی"] : ["اختیاری"]} inactive={!r.isActive} active={r.isActive} busy={busy} handle={handle}
                onEdit={() => setEdit({ id: r.id, title: r.title, group: r.group, isRequired: r.isRequired, isActive: r.isActive })}
                onToggle={() => act(() => api(`/api/v1/checklist-templates/items/${r.id}`, { method: "PATCH", body: { isActive: !r.isActive } }))} />
            )} />
        </Branch>
      ))}
      <EditSheet target={edit} withRequired categories={categories} groupLabel="دسته‌بندی" busy={busy} onClose={() => setEdit(null)}
        onSave={(v) => act(async () => { await api(`/api/v1/checklist-templates/items/${edit!.id}`, { method: "PATCH", body: { title: v.title, group: v.group, isRequired: v.isRequired } }); setEdit(null); }, "ذخیره شد")} />
      <form className="stack" onSubmit={(e) => { e.preventDefault(); if (!title.trim()) return;
        act(async () => { await api(`/api/v1/checklist-templates/${motorType}/items`, { body: { title: title.trim(), group: group || undefined } }); setTitle(""); }, "اضافه شد"); }}>
        <label className="field"><span>ردیف جدید</span><input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={80} /></label>
        <div className="field">
          <span className="label">دسته‌بندی</span>
          <SelectField label={group || undefined} placeholder="بدون دسته" onOpen={() => setPick(true)} />
          <SelectSheet open={pick} title="دسته‌بندی" items={categories.map((n) => ({ value: n, label: n }))} value={group || null} noneLabel="بدون دسته"
            onSelect={(v) => { setGroup(v ?? ""); setPick(false); }} onClose={() => setPick(false)} />
        </div>
        <button type="submit" className="primary" disabled={busy || !title.trim()} aria-busy={busy}>افزودن ردیف</button>
      </form>
    </>
  );
}

/**
 * A managed list of categories (checklist categories, or service categories). Rows elsewhere pick from it instead of typing.
 * `follow` is called after a reorder so the rows that use the categories are put in the same order, and `existing` lists
 * the group names already in use, offered as a one-tap starting point.
 */
function CategoryListEditor({ kind, hint, newLabel, follow, existing, onChanged }: {
  kind: "category" | "service_category"; hint: string; newLabel: string;
  follow: (orderedNames: string[]) => Promise<void>; existing: () => Promise<string[]>; onChanged: () => void;
}) {
  const { rows, busy, act } = useEditor<IntakeOption>(`/api/v1/intake-options?kind=${kind}&includeInactive=true`);
  const { notify } = useFeedback();
  const [title, setTitle] = useState("");
  const [edit, setEdit] = useState<EditTarget | null>(null);

  async function fromExisting() {
    const names = await existing();
    if (names.length === 0) { notify("هنوز دسته‌ای ثبت نشده؛ دسته‌ی جدید بسازید."); return; }
    await api("/api/v1/intake-options/import", { body: { items: names.map((n) => ({ kind, title: n })) } });
  }

  if (!rows) return <div className="splash" aria-busy="true" />;
  return (
    <>
      <p className="hint">{hint}</p>
      {rows.length === 0 && (
        <div className="hint-action">
          <button type="button" className="secondary" disabled={busy} onClick={() => act(async () => { await fromExisting(); onChanged(); }, "دسته‌ها ساخته شد")}>ساخت از دسته‌های فعلی</button>
        </div>
      )}
      <SortableList items={rows} disabled={busy} label={(r) => r.title}
        onReorder={(ids) => act(async () => {
          await api("/api/v1/intake-options/order", { method: "PUT", body: { ids } });
          await follow(ids.map((id) => rows.find((r) => r.id === id)!.title));
          onChanged();
        })}
        render={(r, handle) => (
          <ListRow title={r.title} badges={[]} inactive={!r.isActive} active={r.isActive} busy={busy} handle={handle}
            onEdit={() => setEdit({ id: r.id, title: r.title, group: null, isActive: r.isActive })}
            onToggle={() => act(async () => { await api(`/api/v1/intake-options/${r.id}`, { method: "PATCH", body: { isActive: !r.isActive } }); onChanged(); })} />
        )} />
      <EditSheet target={edit} withRequired={false} busy={busy} onClose={() => setEdit(null)}
        onSave={(v) => act(async () => { await api(`/api/v1/intake-options/${edit!.id}`, { method: "PATCH", body: { title: v.title } }); setEdit(null); onChanged(); }, "ذخیره شد")} />
      <form className="stack" onSubmit={(e) => { e.preventDefault(); if (!title.trim()) return;
        act(async () => { await api("/api/v1/intake-options", { body: { kind, title: title.trim() } }); setTitle(""); onChanged(); }, "اضافه شد"); }}>
        <label className="field"><span>{newLabel}</span><input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={40} /></label>
        <button type="submit" className="primary" disabled={busy || !title.trim()} aria-busy={busy}>افزودن دسته</button>
      </form>
    </>
  );
}

/** Checklist categories: reordering them reorders the rows of both checklists, so the intake form shows the same order. */
function ChecklistCategoryEditor({ onChanged }: { onChanged: () => void }) {
  async function follow(orderedNames: string[]) {
    for (const t of MOTOR_TYPES) {
      const items = await api<TemplateRow[]>(`/api/v1/checklist-templates/${t.key}?includeInactive=true`);
      if (items.length === 0) continue;
      const rank = (g: string | null) => { const i = g ? orderedNames.indexOf(g) : -1; return i >= 0 ? i : 1e6; };
      const ids = items.map((x, i) => ({ x, i })).sort((a, b) => rank(a.x.group) - rank(b.x.group) || a.i - b.i).map((p) => p.x.id);
      await api(`/api/v1/checklist-templates/${t.key}/order`, { method: "PUT", body: { ids } });
    }
  }
  async function existing() {
    const names = new Set<string>();
    for (const t of MOTOR_TYPES) (await api<TemplateRow[]>(`/api/v1/checklist-templates/${t.key}?includeInactive=true`)).forEach((x) => x.group && names.add(x.group));
    return [...names];
  }
  return <CategoryListEditor kind="category" newLabel="دسته‌ی جدید" onChanged={onChanged} follow={follow} existing={existing}
    hint="دسته‌ها را یک بار این‌جا بسازید؛ هر ردیف چک‌لیست یکی از آن‌ها را انتخاب می‌کند. با کشیدن، ترتیب دسته‌ها در هر دو چک‌لیست عوض می‌شود." />;
}

/** Service categories: the same idea for the requested services; reordering follows through to the services. */
function ServiceCategoryEditor({ onChanged }: { onChanged: () => void }) {
  async function follow(orderedNames: string[]) {
    const items = await api<IntakeOption[]>("/api/v1/intake-options?kind=service&includeInactive=true");
    if (items.length === 0) return;
    const rank = (g: string | null) => { const i = g ? orderedNames.indexOf(g) : -1; return i >= 0 ? i : 1e6; };
    const ids = items.map((x, i) => ({ x, i })).sort((a, b) => rank(a.x.group) - rank(b.x.group) || a.i - b.i).map((p) => p.x.id);
    await api("/api/v1/intake-options/order", { method: "PUT", body: { ids } });
  }
  async function existing() {
    const items = await api<IntakeOption[]>("/api/v1/intake-options?kind=service&includeInactive=true");
    return [...new Set(items.map((x) => x.group).filter((g): g is string => !!g))];
  }
  return <CategoryListEditor kind="service_category" newLabel="دسته‌ی خدمات جدید" onChanged={onChanged} follow={follow} existing={existing}
    hint="دسته‌های خدمات را یک بار این‌جا بسازید؛ هر خدمت یکی از آن‌ها را انتخاب می‌کند. با کشیدن، ترتیب دسته‌ها در فرم پذیرش هم عوض می‌شود." />;
}

/** Settings → «فهرست‌ها و چک‌لیست»: five branches, each closed until tapped. */
export function WorkshopListsPage({ onBack, onOpenCatalog }: { onBack: () => void; onOpenCatalog: () => void }) {
  const [cats, setCats] = useState<IntakeOption[]>([]);
  const [svcCats, setSvcCats] = useState<IntakeOption[]>([]);
  const [version, setVersion] = useState(0);
  const loadCats = () => {
    api<IntakeOption[]>("/api/v1/intake-options?kind=category").then(setCats).catch(() => {});
    api<IntakeOption[]>("/api/v1/intake-options?kind=service_category").then(setSvcCats).catch(() => {});
  };
  useEffect(() => { loadCats(); }, []);
  const names = cats.map((c) => c.title);
  const svcNames = svcCats.map((c) => c.title);
  const changed = () => { loadCats(); setVersion((v) => v + 1); };
  return (
    <SubPage title="فهرست‌ها و چک‌لیست" onBack={onBack}>
      <p className="hint">روی هر بخش بزنید تا باز شود. ردیف‌ها را با گرفتن آیکن شش‌نقطه و کشیدن جابه‌جا کنید؛ تغییرها فقط پذیرش‌های بعدی را عوض می‌کنند.</p>
      <Branch title="خدمات">
        <Branch level={2} title="ایرادهای اعلامی مشتری"><ProblemsEditor /></Branch>
        <ServicesEditor key={`svc-${version}`} categories={svcNames} onChanged={changed} />
        <Branch level={2} title="دسته‌بندی خدمات" count={svcCats.length}><ServiceCategoryEditor onChanged={changed} /></Branch>
      </Branch>
      <Branch title="کالا">
        <p className="hint">کالاها با قیمت خرید و فروش در «فهرست قیمت» نگهداری می‌شوند؛ آن‌جا برای هر کالا تاریخچه‌ی خرید و فروش هم هست.</p>
        <button type="button" className="secondary" onClick={onOpenCatalog}>باز کردن فهرست کالا و قیمت</button>
      </Branch>
      {MOTOR_TYPES.map((t) => (
        <Branch key={t.key} title={`چک‌لیست ${t.label}`}><TemplateEditor key={`${t.key}-${version}`} motorType={t.key} categories={names} /></Branch>
      ))}
      <Branch title="دسته‌بندی اصلی چک‌لیست" count={cats.length}><ChecklistCategoryEditor onChanged={changed} /></Branch>
    </SubPage>
  );
}

type TrashVideo = { id: string; caseId: string; number: number; sizeBytes: number; deletedAt: string; purgeAt: string; deletedByName: string | null };

function TrashVideoCard({ v, busy, onRestore }: { v: TrashVideo; busy: boolean; onRestore: () => void }) {
  const [url, setUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  useEffect(() => () => { if (url) URL.revokeObjectURL(url); }, [url]);
  const fmt = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" });
  const msLeft = new Date(v.purgeAt).getTime() - Date.now();
  const daysLeft = Math.max(0, Math.ceil(msLeft / 86_400_000));
  async function watch() {
    setLoading(true);
    try { setUrl(URL.createObjectURL(await apiBlob(`/api/v1/trash/videos/${v.id}/file`))); }
    finally { setLoading(false); }
  }
  return (
    <li className="card stack">
      <strong>CASE-{v.number}</strong>
      <span className="muted small">
        حذف‌شده در {fmt.format(new Date(v.deletedAt))}{v.deletedByName ? ` توسط ${v.deletedByName}` : ""} · {formatNumber(Math.round(v.sizeBytes / 1024 / 1024 * 10) / 10)} مگابایت
      </span>
      <span className={daysLeft <= 1 ? "warn small" : "small"}>
        {daysLeft === 0 ? "امروز برای همیشه پاک می‌شود" : `${formatNumber(daysLeft)} روز دیگر برای همیشه پاک می‌شود`}
      </span>
      {url && <video src={url} controls playsInline preload="metadata" style={{ width: "100%", borderRadius: 8 }} />}
      <div className="row">
        {!url && <button type="button" onClick={watch} disabled={loading} aria-busy={loading}>{loading ? "در حال بارگذاری…" : "دیدن فیلم"}</button>}
        <button type="button" className="primary" onClick={onRestore} disabled={busy} aria-busy={busy}>{busy ? "در حال بازگردانی…" : "بازگردانی"}</button>
      </div>
    </li>
  );
}

export function VideoTrashPage({ onBack }: { onBack: () => void }) {
  const { notify } = useFeedback();
  const [rows, setRows] = useState<TrashVideo[] | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const load = () => api<TrashVideo[]>("/api/v1/trash/videos").then(setRows).catch(() => notify("خطا در ارتباط با سرور", "error"));
  useEffect(() => { load(); }, []); // eslint-disable-line react-hooks/exhaustive-deps
  async function restore(id: string) {
    setBusyId(id);
    try { await api(`/api/v1/trash/videos/${id}/restore`, { method: "POST" }); notify("فیلم به پرونده برگشت"); await load(); }
    catch (e) { notify(e instanceof ApiError ? e.message : "خطا در ارتباط با سرور", "error"); }
    finally { setBusyId(null); }
  }
  return (
    <SubPage title="فیلم‌های حذف‌شده" onBack={onBack}>
      <p className="hint">فیلمی که حذف شود ۳ روز اینجا می‌ماند و بعد برای همیشه از سیستم پاک می‌شود. تا آن موقع می‌توانید آن را ببینید یا به پرونده برگردانید.</p>
      {!rows ? <div className="splash" aria-busy="true" /> : rows.length === 0 ? (
        <div className="empty"><strong>فیلم حذف‌شده‌ای نیست</strong><span className="muted">وقتی فیلمی را از یک پرونده حذف کنند، تا ۳ روز اینجا دیده می‌شود.</span></div>
      ) : (
        <ul className="stack list-plain">{rows.map((v) => <TrashVideoCard key={v.id} v={v} busy={busyId === v.id} onRestore={() => restore(v.id)} />)}</ul>
      )}
    </SubPage>
  );
}
