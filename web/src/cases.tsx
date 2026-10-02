import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, ApiError } from "./api";
import { PlateInput, PlateView, emptyPlate } from "./plate";
import { Field, NumberInput, formatNumber } from "./ui";
import { FUEL_LEVELS, formatPlate, type PlateParts } from "./vehicles";

/* ───────── types ───────── */

type StageRef = { id: string; key: string; name: string; category: string; color: string; isTerminal: boolean };
type CaseRow = {
  id: string; number: number; customerName: string | null; customerMobile: string; assetTitle: string | null;
  assetIdentifier: string | null; stage: StageRef; assigneeName: string | null; stageEnteredAt: string; promisedAt: string | null;
  request: string; requestedServices: string[];
};
type TransitionView = { id: string; label: string; isPrimary: boolean; requiresReason: boolean; toStage: { name: string; category: string; color: string } };
type TimelineEntry = { id: number; type: string; occurredAt: string; actor: string | null; data: Record<string, unknown> | null };
type CaseDetailView = {
  id: string; number: number; request: string; requestedServices: string[]; fuelLevel: number | null;
  bodyStatus: string | null; bodyNotes: string | null; diagnosis: string | null; odometerKm: number | null;
  estimatedAmountRials: number | null; promisedAt: string | null; custodyStatus: string; intake: Record<string, string> | null;
  relation: string | null; openedAt: string; stageEnteredAt: string; closedAt: string | null;
  stage: StageRef; customer: { id: string; fullName: string | null; mobile: string };
  asset: { id: string; title: string; identifier: string | null; attributes: Record<string, string> | null } | null;
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
      {canCreate && <button className="primary block big" onClick={onNewCase}>+ پذیرش جدید</button>}
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
        <input type="search" placeholder="شماره پرونده، نام، موبایل، پلاک یا مدل" value={q} onChange={(e) => setQ(e.target.value)} aria-label="جستجوی پرونده" />
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
              {c.assetIdentifier && <PlateView identifier={c.assetIdentifier} />}
              <span className="case-row-sub muted">
                {[...c.requestedServices, c.request].filter(Boolean).join("، ")}
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

/* ───────── detail ───────── */

const EVENT_TEXT: Record<string, (d: Record<string, unknown>) => string> = {
  "case.opened": () => "پرونده باز شد",
  "case.stage_changed": (d) => `${d.action ?? "تغییر مرحله"}: از «${d.from}» به «${d.to}»` + (d.reason ? ` (دلیل: ${d.reason})` : ""),
  "case.assigned": (d) => (d.to ? `سپرده شد به ${d.to}` : "مسئول برداشته شد"),
  "case.updated": (d) => `ویرایش: ${Object.keys(d).map((k) => FIELD_NAMES[k] ?? k).join("، ")}`,
  "case.note_added": (d) => `یادداشت: ${d.text}`,
  "case.reopened": (d) => `پرونده دوباره باز شد${d.reason ? ` (دلیل: ${d.reason})` : ""}`,
  "case.delivered": () => "وسیله تحویل مشتری شد",
  "case.cancelled": (d) => `پرونده لغو شد${d.reason ? ` (دلیل: ${d.reason})` : ""}`,
  "case.deleted": () => "پرونده حذف شد",
  "case.restored": () => "پرونده بازگردانی شد",
  "case.custody_changed": (d) => (d.custodyStatus === "in_shop" ? "وسیله در تعمیرگاه است" : "وسیله دست مشتری است"),
};
const FIELD_NAMES: Record<string, string> = {
  request: "توضیحات", requestedServices: "سرویس‌ها", fuelLevel: "میزان سوخت", bodyStatus: "وضعیت بدنه", bodyNotes: "شرح آسیب",
  diagnosis: "عیب‌یابی", odometerKm: "کیلومتر", estimatedAmountRials: "برآورد هزینه", promisedAt: "زمان تحویل", intake: "همراه وسیله",
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
  const [needPlate, setNeedPlate] = useState<TransitionView | null>(null);

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
      if (err instanceof ApiError && err.body?.code === "plate_required") {
        setNeedPlate(t);
        return;
      }
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
        {c.asset && (
          <div className="asset-line">
            <span>{c.asset.title}{c.asset.attributes?.year && <span className="muted"> · مدل {c.asset.attributes.year}</span>}</span>
            {c.asset.identifier ? <PlateView identifier={c.asset.identifier} /> : <span className="badge warn">بدون پلاک</span>}
          </div>
        )}
        {c.parentCase && <p className="muted">برگشتی پرونده <span className="font-num">#{faNumber.format(c.parentCase.number)}</span></p>}
        <p className="muted small">{ago(c.stageEnteredAt)} در این مرحله · {c.custodyStatus === "in_shop" ? "وسیله در تعمیرگاه" : "وسیله دست مشتری"}</p>
      </div>

      {error && <p className="error" role="alert">{error}</p>}

      {needPlate && c.asset ? (
        <PlateFix assetId={c.asset.id} onCancel={() => setNeedPlate(null)}
          onSaved={async () => { const t = needPlate; setNeedPlate(null); await load(); await run(t); }} />
      ) : pending ? (
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
          {c.requestedServices.length > 0 && <Detail label="سرویس‌های درخواستی" value={c.requestedServices.join("، ")} />}
          {c.request && <Detail label="توضیحات" value={c.request} />}
          {c.fuelLevel != null && <Detail label="میزان سوخت" value={FUEL_LEVELS[c.fuelLevel]} />}
          {c.bodyStatus && <Detail label="وضعیت بدنه" value={c.bodyStatus === "ok" ? "سالم" : `آسیب: ${c.bodyNotes ?? "—"}`} />}
          {c.asset?.attributes && <VehicleAttributes attributes={c.asset.attributes} />}
          <Detail label="عیب‌یابی" value={c.diagnosis ?? "—"} />
          <Detail label="کیلومتر" value={formatNumber(c.odometerKm)} />
          {c.estimatedAmountRials != null && <Detail label="برآورد هزینه" value={toman(c.estimatedAmountRials)} />}
          {c.promisedAt && <Detail label="قول تحویل" value={faDateTime.format(new Date(c.promisedAt))} />}
          {c.intake && <Detail label="همراه وسیله" value={Object.entries(c.intake).map(([k, v]) => (v === "دارد" ? k : `${k}: ${v}`)).join("، ")} />}
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
            ? <button onClick={() => custody("with_customer")}>وسیله دست مشتری است</button>
            : <button onClick={() => custody("in_shop")}>وسیله در تعمیرگاه است</button>}
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

const ATTRIBUTE_NAMES: Record<string, string> = {
  color: "رنگ", vin: "شماره شاسی", fuelType: "نوع سوخت", gearbox: "گیربکس",
};

function VehicleAttributes({ attributes }: { attributes: Record<string, string> }) {
  const rows = Object.entries(ATTRIBUTE_NAMES).filter(([k]) => attributes[k]);
  if (rows.length === 0) return null;
  return <Detail label="مشخصات وسیله" value={rows.map(([k, name]) => `${name}: ${attributes[k]}`).join(" · ")} />;
}

function PlateFix({ assetId, onSaved, onCancel }: { assetId: string; onSaved: () => void; onCancel: () => void }) {
  const [plate, setPlate] = useState<PlateParts>(emptyPlate());
  const [error, setError] = useState<string | null>(null);

  async function save() {
    const identifier = formatPlate(plate);
    if (!identifier) {
      setError("پلاک کامل نیست.");
      return;
    }
    try {
      await api(`/api/v1/assets/${assetId}`, { method: "PATCH", body: { identifier } });
      onSaved();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    }
  }

  return (
    <div className="card attention">
      <h3>قبل از شروع کار، پلاک را وارد کنید</h3>
      <PlateInput value={plate} onChange={setPlate} invalid={!!error} />
      {error && <span className="error">{error}</span>}
      <div className="actions">
        <button className="primary" onClick={save}>ثبت پلاک و ادامه</button>
        <button onClick={onCancel}>انصراف</button>
      </div>
    </div>
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
      <Field label="توضیحات" error={errors.request}>
        <textarea value={request} onChange={(e) => setRequest(e.target.value)} maxLength={2000} rows={3} />
      </Field>
      <Field label="عیب‌یابی" error={errors.diagnosis}>
        <textarea value={diagnosis} onChange={(e) => setDiagnosis(e.target.value)} maxLength={4000} rows={3} />
      </Field>
      <Field label="کیلومتر" error={errors.odometerKm}>
        <NumberInput value={odometer} onChange={setOdometer} max={7} />
      </Field>
      {c.canManage && (
        <Field label="برآورد هزینه" error={errors.estimatedAmountRials}>
          <NumberInput value={estimate} onChange={setEstimate} max={12} suffix="تومان" />
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
