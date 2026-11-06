import { useEffect, useState, type ReactNode } from "react";
import { api, apiBlob, ApiError } from "./api";
import { useFeedback } from "./feedback";
import { SubPage } from "./settings";
import { formatNumber } from "./ui";
import { MOTO_PROBLEMS, MOTO_SERVICE_CATEGORIES } from "./vehicles";

export const MOTOR_TYPES = [
  { key: "geared", label: "دنده‌ای" },
  { key: "automatic", label: "کلیچ / اتوماتیک" },
] as const;

export type IntakeOption = { id: string; kind: "problem" | "service"; group: string | null; title: string; sortOrder: number; isActive: boolean };
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

function Block({ title, children }: { title: string; children: ReactNode }) {
  return <section className="stack"><h3>{title}</h3>{children}</section>;
}

function OptionsEditor({ kind, label, defaults }: { kind: "problem" | "service"; label: string; defaults: { group: string; title: string }[] }) {
  const { notify } = useFeedback();
  const [rows, setRows] = useState<IntakeOption[] | null>(null);
  const [title, setTitle] = useState("");
  const [group, setGroup] = useState("");
  const [busy, setBusy] = useState(false);
  const load = () => api<IntakeOption[]>(`/api/v1/intake-options?kind=${kind}&includeInactive=true`).then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); }, [kind]);

  async function act(fn: () => Promise<unknown>, ok?: string) {
    if (busy) return;
    setBusy(true);
    try { await fn(); if (ok) notify(ok); await load(); }
    catch (e) { notify(e instanceof ApiError ? e.message : "خطا در ارتباط با سرور", "error"); }
    finally { setBusy(false); }
  }

  if (!rows) return <div className="splash" aria-busy="true" />;
  return (
    <Block title={label}>
      {rows.length === 0 && (
        <div className="hint-action">
          <p className="hint">فعلاً فهرست پیش‌فرض برنامه نشان داده می‌شود. با «شروع از فهرست پیش‌فرض» یک نسخه‌ی قابل‌ویرایش از آن بسازید.</p>
          <button type="button" className="secondary" disabled={busy}
            onClick={() => act(() => api("/api/v1/intake-options/import", { body: { items: defaults.map((d) => ({ kind, group: d.group, title: d.title })) } }), "فهرست پیش‌فرض اضافه شد")}>
            شروع از فهرست پیش‌فرض
          </button>
        </div>
      )}
      <ul className="plain-list">
        {rows.map((r) => (
          <li key={r.id} className="row-between">
            <span className={r.isActive ? "" : "muted"}>{r.title}{r.group && <span className="muted small"> · {r.group}</span>}{!r.isActive && " (غیرفعال)"}</span>
            <button type="button" className="link-button" disabled={busy}
              onClick={() => act(() => api(`/api/v1/intake-options/${r.id}`, { method: "PATCH", body: { isActive: !r.isActive } }))}>{r.isActive ? "غیرفعال" : "فعال"}</button>
          </li>
        ))}
      </ul>
      <form className="stack" onSubmit={(e) => { e.preventDefault(); if (!title.trim()) return;
        act(async () => { await api("/api/v1/intake-options", { body: { kind, title: title.trim(), group: group.trim() || undefined } }); setTitle(""); }, "اضافه شد"); }}>
        <label className="field"><span>عنوان جدید</span><input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={80} /></label>
        <label className="field"><span>دسته (اختیاری)</span><input value={group} onChange={(e) => setGroup(e.target.value)} maxLength={40} /></label>
        <button type="submit" className="primary" disabled={busy || !title.trim()} aria-busy={busy}>افزودن</button>
      </form>
    </Block>
  );
}

function TemplateEditor({ motorType, label }: { motorType: string; label: string }) {
  const { notify } = useFeedback();
  const [rows, setRows] = useState<TemplateRow[] | null>(null);
  const [title, setTitle] = useState("");
  const [busy, setBusy] = useState(false);
  const load = () => api<TemplateRow[]>(`/api/v1/checklist-templates/${motorType}?includeInactive=true`).then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); }, [motorType]);

  async function act(fn: () => Promise<unknown>, ok?: string) {
    if (busy) return;
    setBusy(true);
    try { await fn(); if (ok) notify(ok); await load(); }
    catch (e) { notify(e instanceof ApiError ? e.message : "خطا در ارتباط با سرور", "error"); }
    finally { setBusy(false); }
  }
  const move = (i: number, d: number) => {
    if (!rows) return;
    const ids = rows.map((r) => r.id);
    [ids[i], ids[i + d]] = [ids[i + d], ids[i]];
    return act(() => api(`/api/v1/checklist-templates/${motorType}/order`, { method: "PUT", body: { ids } }));
  };

  if (!rows) return <div className="splash" aria-busy="true" />;
  return (
    <Block title={`چک‌لیست ${label}`}>
      {rows.length === 0 && (
        <div className="hint-action">
          <p className="hint">هنوز چک‌لیستی نساخته‌اید. می‌توانید از یک فهرست آماده شروع کنید و هر ردیف را تغییر دهید.</p>
          <button type="button" className="secondary" disabled={busy}
            onClick={() => act(() => api(`/api/v1/checklist-templates/${motorType}/defaults`, { body: {} }), "فهرست آماده اضافه شد")}>شروع از فهرست آماده</button>
        </div>
      )}
      <ul className="plain-list">
        {rows.map((r, i) => (
          <li key={r.id} className="row-between">
            <span className={r.isActive ? "" : "muted"}>{r.title}{r.group && <span className="muted small"> · {r.group}</span>}{!r.isRequired && " (اختیاری)"}{!r.isActive && " (غیرفعال)"}</span>
            <span className="row-actions">
              <button type="button" className="link-button" aria-label="بالا" disabled={busy || i === 0} onClick={() => move(i, -1)}>▲</button>
              <button type="button" className="link-button" aria-label="پایین" disabled={busy || i === rows.length - 1} onClick={() => move(i, 1)}>▼</button>
              <button type="button" className="link-button" disabled={busy}
                onClick={() => act(() => api(`/api/v1/checklist-templates/items/${r.id}`, { method: "PATCH", body: { isRequired: !r.isRequired } }))}>{r.isRequired ? "اختیاری کن" : "الزامی کن"}</button>
              <button type="button" className="link-button" disabled={busy}
                onClick={() => act(() => api(`/api/v1/checklist-templates/items/${r.id}`, { method: "PATCH", body: { isActive: !r.isActive } }))}>{r.isActive ? "غیرفعال" : "فعال"}</button>
            </span>
          </li>
        ))}
      </ul>
      <form className="stack" onSubmit={(e) => { e.preventDefault(); if (!title.trim()) return;
        act(async () => { await api(`/api/v1/checklist-templates/${motorType}/items`, { body: { title: title.trim() } }); setTitle(""); }, "اضافه شد"); }}>
        <label className="field"><span>ردیف جدید</span><input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={80} /></label>
        <button type="submit" className="primary" disabled={busy || !title.trim()} aria-busy={busy}>افزودن ردیف</button>
      </form>
    </Block>
  );
}

export function WorkshopListsPage({ onBack }: { onBack: () => void }) {
  const problemDefaults = MOTO_PROBLEMS.map((t) => ({ group: "", title: t }));
  const serviceDefaults = MOTO_SERVICE_CATEGORIES.flatMap((c) => c.services.map((t) => ({ group: c.name, title: t })));
  return (
    <SubPage title="فهرست‌ها و چک‌لیست" onBack={onBack}>
      <p className="hint">ایرادهای اعلامی، خدمات و چک‌لیست بازدید موتور را مطابق کار خودتان تنظیم کنید. تغییرها فقط پذیرش‌های بعدی را عوض می‌کنند.</p>
      <OptionsEditor kind="problem" label="ایرادهای اعلامی مشتری" defaults={problemDefaults} />
      <OptionsEditor kind="service" label="خدمات درخواستی" defaults={serviceDefaults} />
      {MOTOR_TYPES.map((t) => <TemplateEditor key={t.key} motorType={t.key} label={t.label} />)}
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
