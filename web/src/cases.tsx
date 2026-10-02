import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, ApiError } from "./api";
import { Field, MobileInput } from "./ui";

/* ───────── types ───────── */

type StageRef = { id: string; key: string; name: string; category: string; color: string; isTerminal: boolean };
type CaseRow = {
  id: string; number: number; customerName: string | null; customerMobile: string; assetTitle: string | null;
  stage: StageRef; assigneeName: string | null; stageEnteredAt: string; promisedAt: string | null; request: string;
};
type TransitionView = { id: string; label: string; isPrimary: boolean; requiresReason: boolean; toStage: { name: string; category: string; color: string } };
type TimelineEntry = { id: number; type: string; occurredAt: string; actor: string | null; data: Record<string, unknown> | null };
type CaseDetailView = {
  id: string; number: number; request: string; diagnosis: string | null; odometerKm: number | null;
  estimatedAmountRials: number | null; promisedAt: string | null; custodyStatus: string; intake: Record<string, string> | null;
  relation: string | null; openedAt: string; stageEnteredAt: string; closedAt: string | null;
  stage: StageRef; customer: { id: string; fullName: string | null; mobile: string };
  asset: { id: string; title: string; identifier: string | null } | null;
  assignee: { id: string; name: string } | null; parentCase: { id: string; number: number } | null;
  transitions: TransitionView[]; canEdit: boolean; canManage: boolean; canAssign: boolean; timeline: TimelineEntry[];
};
type Dashboard = {
  open: number; byCategory: Record<string, number>; byStage: { id: string; name: string; color: string; count: number }[];
  stuck: number; stuckAfterDays: number; unassigned: number; mine: number; openedToday: number; deliveredToday: number;
};
type Assignable = { id: string; name: string; role: string };
export type CaseFilter = { category?: string; mine?: boolean; all?: boolean };

/* ───────── formatting ───────── */

const faDateTime = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { month: "long", day: "numeric", hour: "2-digit", minute: "2-digit" });
const faNumber = new Intl.NumberFormat("fa-IR");

export function ago(iso: string) {
  const minutes = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000));
  if (minutes < 60) return `${faNumber.format(minutes)} دقیقه`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${faNumber.format(hours)} ساعت`;
  return `${faNumber.format(Math.round(hours / 24))} روز`;
}

const toman = (rials: number) => `${faNumber.format(Math.round(rials / 10))} تومان`;

function StageChip({ stage }: { stage: { name: string; color: string } }) {
  return <span className={`chip stage-${stage.color}`}>{stage.name}</span>;
}

/* ───────── dashboard ───────── */

export function DashboardView({ onOpenCases, onNewCase, canCreate }: {
  onOpenCases: (f: CaseFilter) => void; onNewCase: () => void; canCreate: boolean;
}) {
  const [d, setD] = useState<Dashboard | null>(null);
  useEffect(() => { api<Dashboard>("/api/v1/dashboard").then(setD).catch(() => {}); }, []);
  if (!d) return <div className="splash" aria-busy="true" />;

  const tiles: { label: string; value: number; filter: CaseFilter; tone?: string }[] = [
    { label: "پرونده‌های باز", value: d.open, filter: {} },
    { label: "آماده تحویل", value: d.byCategory.done ?? 0, filter: { category: "done" }, tone: "good" },
    { label: "منتظر (تأیید / قطعه)", value: d.byCategory.waiting ?? 0, filter: { category: "waiting" }, tone: "warn" },
    { label: "در حال کار", value: d.byCategory.active ?? 0, filter: { category: "active" } },
  ];

  return (
    <section>
      {canCreate && <button className="primary block big" onClick={onNewCase}>+ پذیرش موتور جدید</button>}
      <div className="tiles">
        {tiles.map((t) => (
          <button key={t.label} className={`tile ${t.tone ?? ""}`} onClick={() => onOpenCases(t.filter)}>
            <span className="tile-value font-num">{faNumber.format(t.value)}</span>
            <span className="tile-label">{t.label}</span>
          </button>
        ))}
      </div>

      {(d.stuck > 0 || d.unassigned > 0) && (
        <div className="card attention">
          <h3>نیاز به توجه</h3>
          {d.stuck > 0 && <p>{faNumber.format(d.stuck)} پرونده بیش از {faNumber.format(d.stuckAfterDays)} روز در یک مرحله مانده است.</p>}
          {d.unassigned > 0 && <p>{faNumber.format(d.unassigned)} پرونده هنوز مسئول ندارد.</p>}
        </div>
      )}

      <div className="card">
        <p>امروز: <strong className="font-num">{faNumber.format(d.openedToday)}</strong> پذیرش · <strong className="font-num">{faNumber.format(d.deliveredToday)}</strong> تحویل</p>
        {d.mine > 0 && (
          <button className="link" onClick={() => onOpenCases({ mine: true })}>{faNumber.format(d.mine)} پرونده به من سپرده شده</button>
        )}
      </div>

      {d.byStage.length > 0 && (
        <ul className="list">
          {d.byStage.map((s) => (
            <li key={s.id} className="row-static">
              <StageChip stage={s} />
              <span className="font-num">{faNumber.format(s.count)}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/* ───────── list ───────── */

const FILTERS: { label: string; filter: CaseFilter }[] = [
  { label: "باز", filter: {} },
  { label: "در حال کار", filter: { category: "active" } },
  { label: "منتظر", filter: { category: "waiting" } },
  { label: "آماده تحویل", filter: { category: "done" } },
  { label: "مال من", filter: { mine: true } },
  { label: "همه", filter: { all: true } },
];

const sameFilter = (a: CaseFilter, b: CaseFilter) => a.category === b.category && !!a.mine === !!b.mine && !!a.all === !!b.all;

export function CasesView({ initialFilter, onOpen, onNewCase, canCreate }: {
  initialFilter: CaseFilter; onOpen: (id: string) => void; onNewCase: () => void; canCreate: boolean;
}) {
  const [filter, setFilter] = useState<CaseFilter>(initialFilter);
  const [q, setQ] = useState("");
  const [rows, setRows] = useState<CaseRow[] | null>(null);

  useEffect(() => {
    const params = new URLSearchParams();
    if (filter.category) params.set("category", filter.category);
    if (filter.mine) params.set("mine", "true");
    if (filter.all) params.set("all", "true");
    if (q.trim()) params.set("q", q.trim());
    const t = setTimeout(() => api<CaseRow[]>(`/api/v1/cases?${params}`).then(setRows).catch(() => setRows([])), 200);
    return () => clearTimeout(t);
  }, [filter, q]);

  return (
    <section>
      <div className="toolbar">
        <input type="search" placeholder="شماره پرونده، نام، موبایل یا موتور" value={q} onChange={(e) => setQ(e.target.value)} aria-label="جستجوی پرونده" />
        {canCreate && <button className="primary" onClick={onNewCase}>+ پرونده</button>}
      </div>
      <div className="chips" role="tablist">
        {FILTERS.map((f) => (
          <button key={f.label} role="tab" aria-selected={sameFilter(f.filter, filter)}
            className={`chip-button${sameFilter(f.filter, filter) ? " active" : ""}`} onClick={() => setFilter(f.filter)}>
            {f.label}
          </button>
        ))}
      </div>
      <ul className="list">
        {rows?.map((c) => (
          <li key={c.id}>
            <button className="case-row" onClick={() => onOpen(c.id)}>
              <span className="case-row-top">
                <span className="font-num muted">#{faNumber.format(c.number)}</span>
                <StageChip stage={c.stage} />
              </span>
              <span className="case-row-title">{c.customerName ?? c.customerMobile}{c.assetTitle && <span className="muted"> · {c.assetTitle}</span>}</span>
              <span className="case-row-sub muted">
                {c.request}
              </span>
              <span className="case-row-meta muted small">
                {c.assigneeName ? `مسئول: ${c.assigneeName}` : "بدون مسئول"} · {ago(c.stageEnteredAt)} در این مرحله
              </span>
            </button>
          </li>
        ))}
        {rows?.length === 0 && <li className="empty muted">پرونده‌ای نیست.</li>}
      </ul>
    </section>
  );
}

/* ───────── new case ───────── */

type CustomerHit = { id: string; mobile: string; fullName: string | null };
type CustomerFull = { id: string; fullName: string | null; assets: { id: string; title: string; identifier: string | null }[] };
type ParentSuggestion = { id: string; number: number; closedAt: string; request: string } | null;

const INTAKE_ITEMS = ["کلاه کاسکت", "سوئیچ", "مدارک", "قفل", "باک پر"];

export function NewCaseView({ canAssign, onCreated, onCancel }: { canAssign: boolean; onCreated: (id: string) => void; onCancel: () => void }) {
  const [mobile, setMobile] = useState("");
  const [customer, setCustomer] = useState<CustomerFull | null>(null);
  const [customerName, setCustomerName] = useState("");
  const [assetId, setAssetId] = useState<string | "new">("new");
  const [assetTitle, setAssetTitle] = useState("");
  const [plate, setPlate] = useState("");
  const [request, setRequest] = useState("");
  const [odometer, setOdometer] = useState("");
  const [assigneeId, setAssigneeId] = useState("");
  const [staff, setStaff] = useState<Assignable[]>([]);
  const [parent, setParent] = useState<ParentSuggestion>(null);
  const [linkParent, setLinkParent] = useState(true);
  const [intake, setIntake] = useState<Record<string, boolean>>({});
  const [condition, setCondition] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (canAssign) api<Assignable[]>("/api/v1/staff/assignable").then(setStaff).catch(() => {});
  }, [canAssign]);

  // Once the number is complete, find the customer and their motorcycles.
  useEffect(() => {
    setCustomer(null);
    setAssetId("new");
    if (mobile.length !== 11) return;
    api<CustomerHit[]>(`/api/v1/customers?q=${mobile}`)
      .then(async (hits) => {
        const hit = hits.find((h) => h.mobile === mobile);
        if (!hit) return;
        const full = await api<CustomerFull>(`/api/v1/customers/${hit.id}`);
        setCustomer(full);
        if (full.assets.length > 0) setAssetId(full.assets[full.assets.length - 1].id);
      })
      .catch(() => {});
  }, [mobile]);

  useEffect(() => {
    setParent(null);
    if (assetId === "new") return;
    api<ParentSuggestion>(`/api/v1/cases/suggest-parent?assetId=${assetId}`).then(setParent).catch(() => {});
  }, [assetId]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setErrors({});
    const intakeData: Record<string, string> = {};
    for (const item of INTAKE_ITEMS) if (intake[item]) intakeData[item] = "دارد";
    if (condition.trim()) intakeData["وضعیت ظاهری"] = condition.trim();
    try {
      const created = await api<{ id: string }>("/api/v1/cases", {
        body: {
          mobile,
          customerName: customer?.fullName ? undefined : customerName || undefined,
          assetId: assetId === "new" ? undefined : assetId,
          newAsset: assetId === "new" && assetTitle.trim() ? { title: assetTitle, identifier: plate || null } : undefined,
          request,
          odometerKm: odometer ? Number(odometer) : undefined,
          assigneeId: assigneeId || undefined,
          parentCaseId: parent && linkParent ? parent.id : undefined,
          intake: Object.keys(intakeData).length ? intakeData : undefined,
        },
      });
      onCreated(created.id);
    } catch (err) {
      if (err instanceof ApiError) {
        const f = Object.fromEntries(Object.entries(err.fields).map(([k, v]) => [k, v[0]]));
        setErrors(Object.keys(f).length ? f : { form: err.message });
      } else setErrors({ form: "خطا در ارتباط با سرور" });
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="card" onSubmit={submit} noValidate>
      <h2>پذیرش موتور</h2>
      <Field label="موبایل مشتری" error={errors.mobile}>
        <MobileInput value={mobile} onChange={setMobile} autoFocus />
      </Field>

      {customer ? (
        <p className="muted">مشتری: <strong>{customer.fullName ?? "بدون نام"}</strong></p>
      ) : (
        mobile.length === 11 && (
          <Field label="نام مشتری (مشتری جدید)" error={errors.customerName}>
            <input value={customerName} onChange={(e) => setCustomerName(e.target.value)} maxLength={120} />
          </Field>
        )
      )}

      {customer && customer.assets.length > 0 && (
        <div className="field">
          <span className="label">موتور</span>
          <div className="chips">
            {customer.assets.map((a) => (
              <button type="button" key={a.id} className={`chip-button${assetId === a.id ? " active" : ""}`} onClick={() => setAssetId(a.id)}>
                {a.title}{a.identifier ? ` · ${a.identifier}` : ""}
              </button>
            ))}
            <button type="button" className={`chip-button${assetId === "new" ? " active" : ""}`} onClick={() => setAssetId("new")}>+ موتور دیگر</button>
          </div>
        </div>
      )}

      {assetId === "new" && (
        <>
          <Field label="مدل موتور" error={errors.newAsset}>
            <input placeholder="مثلاً هوندا CG 125" value={assetTitle} onChange={(e) => setAssetTitle(e.target.value)} maxLength={120} />
          </Field>
          <Field label="پلاک یا شماره موتور">
            <input value={plate} onChange={(e) => setPlate(e.target.value)} maxLength={60} />
          </Field>
        </>
      )}

      {parent && (
        <label className="check">
          <input type="checkbox" checked={linkParent} onChange={(e) => setLinkParent(e.target.checked)} />
          <span>برگشتی پرونده <span className="font-num">#{faNumber.format(parent.number)}</span> ({ago(parent.closedAt)} پیش تحویل شد)</span>
        </label>
      )}

      <Field label="شرح مشکل" error={errors.request}>
        <textarea value={request} onChange={(e) => setRequest(e.target.value)} maxLength={2000} rows={3} placeholder="مثلاً هنگام گرم شدن خاموش می‌شود" />
      </Field>
      <Field label="کیلومتر" error={errors.odometerKm}>
        <input inputMode="numeric" dir="ltr" className="font-num" value={odometer} onChange={(e) => setOdometer(e.target.value.replace(/\D/g, "").slice(0, 7))} />
      </Field>

      <div className="field">
        <span className="label">همراه موتور</span>
        <div className="chips">
          {INTAKE_ITEMS.map((item) => (
            <button type="button" key={item} className={`chip-button${intake[item] ? " active" : ""}`} aria-pressed={!!intake[item]}
              onClick={() => setIntake({ ...intake, [item]: !intake[item] })}>
              {item}
            </button>
          ))}
        </div>
      </div>
      <Field label="وضعیت ظاهری (خط و خش، شکستگی…)" error={errors.intake}>
        <input value={condition} onChange={(e) => setCondition(e.target.value)} maxLength={200} />
      </Field>

      {canAssign && (
        <Field label="مسئول" error={errors.assigneeId}>
          <select value={assigneeId} onChange={(e) => setAssigneeId(e.target.value)}>
            <option value="">فعلاً بدون مسئول</option>
            {staff.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        </Field>
      )}

      {errors.form && <span className="error" role="alert">{errors.form}</span>}
      <div className="actions">
        <button className="primary" disabled={busy}>ثبت پرونده</button>
        <button type="button" onClick={onCancel}>انصراف</button>
      </div>
    </form>
  );
}

/* ───────── detail ───────── */

const EVENT_TEXT: Record<string, (d: Record<string, unknown>) => string> = {
  "case.opened": () => "پرونده باز شد",
  "case.stage_changed": (d) => `${d.action ?? "تغییر مرحله"}: از «${d.from}» به «${d.to}»` + (d.reason ? ` (دلیل: ${d.reason})` : ""),
  "case.assigned": (d) => (d.to ? `سپرده شد به ${d.to}` : "مسئول برداشته شد"),
  "case.updated": (d) => `ویرایش: ${Object.keys(d).map((k) => FIELD_NAMES[k] ?? k).join("، ")}`,
  "case.note_added": (d) => `یادداشت: ${d.text}`,
  "case.reopened": (d) => `پرونده دوباره باز شد${d.reason ? ` (دلیل: ${d.reason})` : ""}`,
  "case.delivered": () => "موتور تحویل مشتری شد",
  "case.cancelled": (d) => `پرونده لغو شد${d.reason ? ` (دلیل: ${d.reason})` : ""}`,
  "case.deleted": () => "پرونده حذف شد",
  "case.restored": () => "پرونده بازگردانی شد",
  "case.custody_changed": (d) => (d.custodyStatus === "in_shop" ? "موتور در مغازه است" : "موتور دست مشتری است"),
};
const FIELD_NAMES: Record<string, string> = {
  request: "شرح مشکل", diagnosis: "عیب‌یابی", odometerKm: "کیلومتر", estimatedAmountRials: "برآورد هزینه", promisedAt: "زمان تحویل", intake: "همراه موتور",
};

export function CaseDetail({ id, onBack, onDeleted }: { id: string; onBack: () => void; onDeleted: (number: number) => void }) {
  const [c, setC] = useState<CaseDetailView | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [pending, setPending] = useState<TransitionView | null>(null);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [editing, setEditing] = useState(false);
  const [note, setNote] = useState("");
  const [staff, setStaff] = useState<Assignable[]>([]);

  const load = useCallback(async () => {
    try {
      setC(await api<CaseDetailView>(`/api/v1/cases/${id}`));
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) setNotFound(true);
    }
  }, [id]);
  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    if (c?.canAssign && staff.length === 0) api<Assignable[]>("/api/v1/staff/assignable").then(setStaff).catch(() => {});
  }, [c?.canAssign, staff.length]);

  async function run(t: TransitionView, withReason?: string) {
    if (t.requiresReason && !withReason) {
      setPending(t);
      setReason("");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      setC(await api<CaseDetailView>(`/api/v1/cases/${id}/transitions/${t.id}`, { body: { reason: withReason } }));
      setPending(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
      if (err instanceof ApiError && err.status === 409) await load();
    } finally {
      setBusy(false);
    }
  }

  async function assign(assigneeId: string) {
    try {
      setC(await api<CaseDetailView>(`/api/v1/cases/${id}/assign`, { body: { assigneeId: assigneeId || null } }));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    }
  }

  async function addNote(e: FormEvent) {
    e.preventDefault();
    if (!note.trim()) return;
    try {
      await api(`/api/v1/cases/${id}/notes`, { body: { text: note } });
      setNote("");
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    }
  }

  async function custody(status: string) {
    try {
      setC(await api<CaseDetailView>(`/api/v1/cases/${id}`, { method: "PATCH", body: { custodyStatus: status } }));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    }
  }

  async function remove() {
    await api(`/api/v1/cases/${id}`, { method: "DELETE" });
    onDeleted(c!.number);
  }

  if (notFound) return <section><button className="link back" onClick={onBack}>→ پرونده‌ها</button><p className="empty muted">پرونده پیدا نشد.</p></section>;
  if (!c) return <div className="splash" aria-busy="true" />;

  const primary = c.transitions.find((t) => t.isPrimary);
  const others = c.transitions.filter((t) => t !== primary);

  return (
    <section>
      <button className="link back" onClick={onBack}>→ پرونده‌ها</button>

      <div className="card case-head">
        <div className="case-row-top">
          <h2 className="font-num">پرونده #{faNumber.format(c.number)}</h2>
          <StageChip stage={c.stage} />
        </div>
        <p>
          <strong>{c.customer.fullName ?? "بدون نام"}</strong>{" "}
          <a className="font-num" dir="ltr" href={`tel:${c.customer.mobile}`}>{c.customer.mobile}</a>
        </p>
        {c.asset && <p>{c.asset.title}{c.asset.identifier && <span className="muted font-num"> · {c.asset.identifier}</span>}</p>}
        {c.parentCase && <p className="muted">برگشتی پرونده <span className="font-num">#{faNumber.format(c.parentCase.number)}</span></p>}
        <p className="muted small">{ago(c.stageEnteredAt)} در این مرحله · {c.custodyStatus === "in_shop" ? "موتور در مغازه" : "موتور دست مشتری"}</p>
      </div>

      {error && <p className="error" role="alert">{error}</p>}

      {pending ? (
        <div className="card">
          <Field label={`دلیل «${pending.label}»`}>
            <input value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} autoFocus />
          </Field>
          <div className="actions">
            <button className="primary" disabled={busy || !reason.trim()} onClick={() => run(pending, reason.trim())}>تأیید</button>
            <button onClick={() => setPending(null)}>انصراف</button>
          </div>
        </div>
      ) : (
        c.transitions.length > 0 && (
          <div className="next-actions">
            {primary && <button className="primary block big" disabled={busy} onClick={() => run(primary)}>{primary.label}</button>}
            {others.length > 0 && (
              <div className="chips">
                {others.map((t) => (
                  <button key={t.id} className="chip-button" disabled={busy} onClick={() => run(t)}>{t.label}</button>
                ))}
              </div>
            )}
          </div>
        )
      )}

      {editing ? (
        <CaseEditForm c={c} onDone={(updated) => { setEditing(false); if (updated) setC(updated); }} />
      ) : (
        <div className="card">
          <Detail label="شرح مشکل" value={c.request} />
          <Detail label="عیب‌یابی" value={c.diagnosis ?? "—"} />
          <Detail label="کیلومتر" value={c.odometerKm != null ? faNumber.format(c.odometerKm) : "—"} />
          {c.estimatedAmountRials != null && <Detail label="برآورد هزینه" value={toman(c.estimatedAmountRials)} />}
          {c.promisedAt && <Detail label="قول تحویل" value={faDateTime.format(new Date(c.promisedAt))} />}
          {c.intake && <Detail label="همراه موتور" value={Object.entries(c.intake).map(([k, v]) => (v === "دارد" ? k : `${k}: ${v}`)).join("، ")} />}
          {c.canEdit && <button onClick={() => setEditing(true)}>ویرایش</button>}
        </div>
      )}

      {c.canAssign && (
        <Field label="مسئول پرونده">
          <select value={c.assignee?.id ?? ""} onChange={(e) => assign(e.target.value)}>
            <option value="">بدون مسئول</option>
            {staff.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        </Field>
      )}
      {!c.canAssign && c.assignee && <p className="muted">مسئول: {c.assignee.name}</p>}

      {c.canManage && (
        <div className="actions wrap">
          {c.custodyStatus === "in_shop"
            ? <button onClick={() => custody("with_customer")}>موتور دست مشتری است</button>
            : <button onClick={() => custody("in_shop")}>موتور در مغازه است</button>}
          <button className="danger" onClick={remove}>حذف پرونده</button>
        </div>
      )}

      <h3>تاریخچه</h3>
      <form className="note-form" onSubmit={addNote}>
        <input placeholder="یادداشت…" value={note} onChange={(e) => setNote(e.target.value)} maxLength={2000} aria-label="یادداشت" />
        <button disabled={!note.trim()}>ثبت</button>
      </form>
      <ol className="timeline">
        {c.timeline.map((e) => (
          <li key={e.id}>
            <span className="timeline-text">{(EVENT_TEXT[e.type] ?? (() => e.type))(e.data ?? {})}</span>
            <span className="muted small">{faDateTime.format(new Date(e.occurredAt))}{e.actor ? ` · ${e.actor}` : ""}</span>
          </li>
        ))}
      </ol>
    </section>
  );
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div className="detail">
      <span className="label">{label}</span>
      <span>{value}</span>
    </div>
  );
}

function CaseEditForm({ c, onDone }: { c: CaseDetailView; onDone: (updated: CaseDetailView | null) => void }) {
  const [request, setRequest] = useState(c.request);
  const [diagnosis, setDiagnosis] = useState(c.diagnosis ?? "");
  const [odometer, setOdometer] = useState(c.odometerKm?.toString() ?? "");
  const [estimate, setEstimate] = useState(c.estimatedAmountRials != null ? String(c.estimatedAmountRials / 10) : "");
  const [errors, setErrors] = useState<Record<string, string>>({});

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body: Record<string, unknown> = { request, diagnosis };
    if (odometer) body.odometerKm = Number(odometer);
    if (c.canManage && estimate) body.estimatedAmountRials = Number(estimate) * 10;
    try {
      onDone(await api<CaseDetailView>(`/api/v1/cases/${c.id}`, { method: "PATCH", body }));
    } catch (err) {
      if (err instanceof ApiError) {
        const f = Object.fromEntries(Object.entries(err.fields).map(([k, v]) => [k, v[0]]));
        setErrors(Object.keys(f).length ? f : { form: err.message });
      }
    }
  }

  return (
    <form className="card" onSubmit={submit} noValidate>
      <Field label="شرح مشکل" error={errors.request}>
        <textarea value={request} onChange={(e) => setRequest(e.target.value)} maxLength={2000} rows={3} />
      </Field>
      <Field label="عیب‌یابی" error={errors.diagnosis}>
        <textarea value={diagnosis} onChange={(e) => setDiagnosis(e.target.value)} maxLength={4000} rows={3} />
      </Field>
      <Field label="کیلومتر" error={errors.odometerKm}>
        <input inputMode="numeric" dir="ltr" className="font-num" value={odometer} onChange={(e) => setOdometer(e.target.value.replace(/\D/g, "").slice(0, 7))} />
      </Field>
      {c.canManage && (
        <Field label="برآورد هزینه (تومان)" error={errors.estimatedAmountRials}>
          <input inputMode="numeric" dir="ltr" className="font-num" value={estimate} onChange={(e) => setEstimate(e.target.value.replace(/\D/g, "").slice(0, 12))} />
        </Field>
      )}
      {errors.form && <span className="error" role="alert">{errors.form}</span>}
      <div className="actions">
        <button className="primary">ذخیره</button>
        <button type="button" onClick={() => onDone(null)}>انصراف</button>
      </div>
    </form>
  );
}
