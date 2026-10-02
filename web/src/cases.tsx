import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from "react";
import { api, ApiError } from "./api";
import { BillingSection, CreditSheet, MoneyBar, toman as tomanFmt, type Billing } from "./billing";
import { useFeedback } from "./feedback";
import { ALERT_ICON, MANUAL_WAIT_REASONS, PARTS_OPTIONS, WAIT_REASONS, statusText, type Alert } from "./labels";
import { PlateInput, PlateView, emptyPlate } from "./plate";
import { BottomSheet, SheetOption } from "./sheet";
import { Field, NumberInput, formatNumber } from "./ui";
import { FUEL_LEVELS, formatPlate, type PlateParts } from "./vehicles";

/* ───────── types ───────── */

type StageRef = { id: string; key: string; name: string; category: string; color: string; isTerminal: boolean };
export type CaseCardData = {
  id: string; number: number; customerName: string | null; customerMobile: string; assetTitle: string | null;
  assetIdentifier: string | null; stage: StageRef; assigneeName: string | null; stageEnteredAt: string;
  promisedAt: string | null; request: string; requestedServices: string[]; waitReason: string | null; alert?: Alert | null;
  reasons?: Alert[]; balanceRials?: number;
};
type TransitionView = { id: string; label: string; isPrimary: boolean; requiresReason: boolean; toStage: { name: string; category: string; color: string } };
type TimelineEntry = { id: number; type: string; occurredAt: string; actor: string | null; data: Record<string, unknown> | null };
type CaseDetailView = {
  id: string; number: number; request: string; requestedServices: string[]; fuelLevel: number | null;
  bodyStatus: string | null; bodyNotes: string | null; diagnosis: string | null; odometerKm: number | null;
  estimatedAmountRials: number | null; promisedAt: string | null; custodyStatus: string; intake: Record<string, string> | null;
  relation: string | null; waitReason: string | null; openedAt: string; stageEnteredAt: string; closedAt: string | null;
  stage: StageRef; customer: { id: string; fullName: string | null; mobile: string };
  asset: { id: string; title: string; identifier: string | null; attributes: Record<string, string> | null } | null;
  assignee: { id: string; name: string } | null; parentCase: { id: string; number: number } | null;
  transitions: TransitionView[]; canEdit: boolean; canManage: boolean; canAssign: boolean; timeline: TimelineEntry[];
  billing: Billing; warrantyUntil: string | null; creditDueAt: string | null;
};
type Assignable = { id: string; name: string; role: string };
export type CaseFilter = { category?: string; mine?: boolean; all?: boolean };

/* ───────── formatting (Jalali calendar everywhere) ───────── */

const faDateTime = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { month: "long", day: "numeric", hour: "2-digit", minute: "2-digit" });
const faWeekdayTime = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { weekday: "long", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" });
const faNumber = new Intl.NumberFormat("fa-IR", { useGrouping: false });

export function ago(iso: string) {
  const minutes = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000));
  if (minutes < 60) return `${formatNumber(minutes)} دقیقه`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${formatNumber(hours)} ساعت`;
  return `${formatNumber(Math.round(hours / 24))} روز`;
}

const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;

/** "امروز ساعت ۱۸:۰۰", "فردا …", otherwise a Jalali date. */
export function promiseText(iso: string) {
  const d = new Date(iso);
  const day = (x: Date) => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const diff = Math.round((day(d) - day(new Date())) / 86400000);
  const time = new Intl.DateTimeFormat("fa-IR", { hour: "2-digit", minute: "2-digit" }).format(d);
  if (diff === 0) return `امروز ساعت ${time}`;
  if (diff === 1) return `فردا ساعت ${time}`;
  if (diff === -1) return `دیروز ساعت ${time}`;
  return faDateTime.format(d);
}

function StageChip({ stage }: { stage: { name: string; color: string } }) {
  return <span className={`chip stage-${stage.color}`}>{stage.name}</span>;
}

function AlertLine({ alert }: { alert: Alert }) {
  return <span className={`alert-line ${alert.severity}`}><span aria-hidden="true">{ALERT_ICON[alert.severity]}</span> {alert.text}</span>;
}

/* ───────── card ───────── */

export function CaseCard({ c, onOpen }: { c: CaseCardData; onOpen: (id: string) => void }) {
  // The status line already says "منتظر تصمیم استاد"; do not repeat it as a warning.
  const alerts = (c.reasons ?? (c.alert ? [c.alert] : [])).filter((a) => a.code !== "owner_decision");
  return (
    <button className="case-card" onClick={() => onOpen(c.id)}>
      <span className="case-row-top">
        <span className="case-row-title">
          {c.assetTitle ?? "بدون وسیله"}
          <span className="muted"> — {c.customerName ?? c.customerMobile}</span>
        </span>
        <span className="font-num muted small">#{faNumber.format(c.number)}</span>
      </span>
      {c.assetIdentifier && <PlateView identifier={c.assetIdentifier} />}
      <span className={`status-line stage-${c.waitReason ? "orange" : c.stage.color}`}>
        {statusText(c.stage, c.waitReason, c.assigneeName)} · {ago(c.stageEnteredAt)}
      </span>
      <span className="case-row-meta muted small">
        {c.assigneeName ? `مسئول: ${c.assigneeName}` : "بدون مسئول"}
        {c.promisedAt && ` · قول تحویل: ${promiseText(c.promisedAt)}`}
      </span>
      {c.stage.category === "done" && !!c.balanceRials && c.balanceRials > 0 && (
        <span className="alert-line warn">مانده: <span className="font-num">{tomanFmt(c.balanceRials)}</span></span>
      )}
      {alerts.slice(0, 2).map((a) => <AlertLine key={a.code} alert={a} />)}
    </button>
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
  const [rows, setRows] = useState<CaseCardData[] | null>(null);

  useEffect(() => {
    const params = new URLSearchParams();
    if (filter.category) params.set("category", filter.category);
    if (filter.mine) params.set("mine", "true");
    if (filter.all) params.set("all", "true");
    if (q.trim()) params.set("q", q.trim());
    const t = setTimeout(() => api<CaseCardData[]>(`/api/v1/cases?${params}`).then(setRows).catch(() => setRows([])), 200);
    return () => clearTimeout(t);
  }, [filter, q]);

  const filtered = q.trim() || !sameFilter(filter, {});

  return (
    <section>
      <div className="toolbar">
        <input type="search" placeholder="شماره پرونده، نام، موبایل، پلاک یا مدل" value={q} onChange={(e) => setQ(e.target.value)} aria-label="جستجوی پرونده" />
        {canCreate && <button className="primary" onClick={onNewCase}>+ پذیرش</button>}
      </div>
      <div className="chips" role="tablist">
        {FILTERS.map((f) => (
          <button key={f.label} role="tab" aria-selected={sameFilter(f.filter, filter)}
            className={`chip-button${sameFilter(f.filter, filter) ? " active" : ""}`} onClick={() => setFilter(f.filter)}>
            {f.label}
          </button>
        ))}
      </div>
      {rows && rows.length === 0 ? (
        filtered ? <p className="empty muted">پرونده‌ای با این شرایط پیدا نشد.</p> : <EmptyCases canCreate={canCreate} onNewCase={onNewCase} />
      ) : (
        <ul className="list">
          {rows?.map((c) => <li key={c.id}><CaseCard c={c} onOpen={onOpen} /></li>)}
        </ul>
      )}
    </section>
  );
}

export function EmptyCases({ canCreate, onNewCase }: { canCreate: boolean; onNewCase: () => void }) {
  return (
    <div className="empty-state">
      <h3>هنوز پرونده‌ای ثبت نشده</h3>
      <p className="muted">
        {canCreate
          ? "اولین وسیله را پذیرش کنید تا مراحل تعمیر، مسئول کار و تحویل آن را یک‌جا مدیریت کنید."
          : "وقتی کاری به شما سپرده شود، این‌جا می‌بینید و مرحله به مرحله جلو می‌برید."}
      </p>
      {canCreate && <button className="primary" onClick={onNewCase}>پذیرش اولین وسیله</button>}
    </div>
  );
}

/* ───────── detail ───────── */

const EVENT_TEXT: Record<string, (d: Record<string, unknown>) => string> = {
  "case.opened": () => "پرونده باز شد",
  "case.stage_changed": (d) =>
    `${d.action ?? "تغییر مرحله"}: از «${d.from}» به «${d.to}»`
    + (d.waitReason ? ` — ${WAIT_REASONS[d.waitReason as string] ?? ""}` : "")
    + (d.reason ? ` (دلیل: ${d.reason})` : ""),
  "case.assigned": (d) => (d.to ? `سپرده شد به ${d.to}` : "مسئول برداشته شد"),
  "case.updated": (d) => `ویرایش: ${Object.keys(d).map((k) => FIELD_NAMES[k] ?? k).join("، ")}`,
  "case.note_added": (d) => `یادداشت: ${d.text}`,
  "case.reopened": (d) => `پرونده دوباره باز شد${d.reason ? ` (دلیل: ${d.reason})` : ""}`,
  "case.delivered": () => "وسیله تحویل مشتری شد",
  "case.cancelled": (d) => `پرونده لغو شد${d.reason ? ` (دلیل: ${d.reason})` : ""}`,
  "case.deleted": () => "پرونده حذف شد",
  "case.restored": () => "پرونده بازگردانی شد",
  "case.custody_changed": (d) => (d.custodyStatus === "in_shop" ? "وسیله در تعمیرگاه است" : "وسیله دست مشتری است"),
  "case.wait_changed": (d) => (d.waitReason ? `کار متوقف شد: ${WAIT_REASONS[d.waitReason as string] ?? ""}` : "کار دوباره جریان گرفت"),
};
const FIELD_NAMES: Record<string, string> = {
  request: "توضیحات", requestedServices: "سرویس‌ها", fuelLevel: "میزان سوخت", bodyStatus: "وضعیت بدنه", bodyNotes: "شرح آسیب",
  diagnosis: "عیب‌یابی", odometerKm: "کیلومتر", estimatedAmountRials: "برآورد هزینه", promisedAt: "قول تحویل", intake: "همراه وسیله",
};

type Sheet = null | "actions" | "assign" | "note" | "wait" | "parts" | "promise" | "credit";

export function CaseDetail({ id, onBack, onDeleted }: { id: string; onBack: () => void; onDeleted: (number: number) => void }) {
  const { notify } = useFeedback();
  const [c, setC] = useState<CaseDetailView | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [sheet, setSheet] = useState<Sheet>(null);
  const [pending, setPending] = useState<TransitionView | null>(null);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [editing, setEditing] = useState(false);
  const [note, setNote] = useState("");
  const [staff, setStaff] = useState<Assignable[]>([]);
  const [needPlate, setNeedPlate] = useState<TransitionView | null>(null);
  const [balanceDue, setBalanceDue] = useState(0);
  const [paySignal, setPaySignal] = useState(0);

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

  const close = useCallback(() => { setSheet(null); setPending(null); }, []);

  async function act<T>(work: () => Promise<T>, ok: string, after?: (r: T) => void) {
    if (busy) return;
    setBusy(true);
    try {
      const r = await work();
      after?.(r);
      notify(ok);
      close();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "خطا", "error");
      if (err instanceof ApiError && err.status === 409) await load();
    } finally {
      setBusy(false);
    }
  }

  async function run(t: TransitionView, extra: { reason?: string; waitReason?: string; allowCredit?: boolean; creditDueAt?: string | null } = {}) {
    if (t.requiresReason && !extra.reason) {
      setPending(t);
      setReason("");
      setSheet("actions");
      return;
    }
    if (busy) return;
    setBusy(true);
    try {
      setC(await api<CaseDetailView>(`/api/v1/cases/${id}/transitions/${t.id}`, { body: extra }));
      notify(`انجام شد: ${t.label}`);
      close();
    } catch (err) {
      if (err instanceof ApiError && err.body?.code === "plate_required") {
        close();
        setNeedPlate(t);
      } else if (err instanceof ApiError && err.body?.code === "balance_due") {
        setPending(t);
        setBalanceDue(Number(err.body.balanceRials ?? 0));
        setSheet("credit");
      } else if (err instanceof ApiError && err.body?.code === "wait_reason_required") {
        setPending(t);
        setSheet("parts");
      } else {
        notify(err instanceof ApiError ? err.message : "خطا", "error");
        if (err instanceof ApiError && err.status === 409) await load();
      }
    } finally {
      setBusy(false);
    }
  }

  if (notFound) return <section><button className="link back" onClick={onBack}>→ پرونده‌ها</button><p className="empty muted">پرونده پیدا نشد.</p></section>;
  if (!c) return <div className="splash" aria-busy="true" />;

  const primary = c.transitions.find((t) => t.isPrimary);
  const others = c.transitions.filter((t) => t !== primary);

  return (
    <section className="case-page">
      <button className="link back" onClick={onBack}>→ پرونده‌ها</button>

      {/* Header: only decision-making facts. */}
      <div className="card case-head">
        <div className="case-row-top">
          <span className="font-num muted">پرونده #{faNumber.format(c.number)}</span>
          <StageChip stage={c.stage} />
        </div>
        {c.asset && (
          <div className="asset-line">
            <strong className="asset-title">{c.asset.title}</strong>
            {c.asset.identifier ? <PlateView identifier={c.asset.identifier} /> : <span className="badge warn">بدون پلاک</span>}
          </div>
        )}
        <div className="customer-line">
          <span>{c.customer.fullName ?? "بدون نام"}</span>
          <a className="call-button" href={`tel:${c.customer.mobile}`} aria-label={`تماس با ${c.customer.mobile}`}>
            <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2z" /></svg>
            <span className="font-num" dir="ltr">{c.customer.mobile}</span>
          </a>
        </div>
        <p className={`status-line stage-${c.waitReason ? "orange" : c.stage.color}`}>
          {statusText(c.stage, c.waitReason, c.assignee?.name)} · {ago(c.stageEnteredAt)}
        </p>
        <div className="head-facts">
          <button className="fact" onClick={() => c.canAssign && setSheet("assign")} disabled={!c.canAssign}>
            <span className="muted small">مسئول</span>
            <span>{c.assignee?.name ?? "تعیین نشده"}</span>
          </button>
          <button className="fact" onClick={() => c.canManage && setSheet("promise")} disabled={!c.canManage}>
            <span className="muted small">قول تحویل</span>
            <span>{c.promisedAt ? promiseText(c.promisedAt) : "تعیین نشده"}</span>
          </button>
        </div>
        <MoneyBar money={c.billing.money} />
        {(c.warrantyUntil || c.creditDueAt) && (
          <p className="muted small">
            {c.warrantyUntil && `ضمانت تا ${new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long", year: "numeric" }).format(new Date(c.warrantyUntil))}`}
            {c.warrantyUntil && c.creditDueAt && " · "}
            {c.creditDueAt && `موعد پرداخت نسیه: ${new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long" }).format(new Date(c.creditDueAt))}`}
          </p>
        )}
        {c.parentCase && <p className="muted small">برگشتی پرونده <span className="font-num">#{faNumber.format(c.parentCase.number)}</span></p>}
      </div>

      {needPlate && c.asset && (
        <PlateFix assetId={c.asset.id} onCancel={() => setNeedPlate(null)}
          onSaved={async () => { const t = needPlate; setNeedPlate(null); await load(); await run(t); }} />
      )}

      {/* Next action: one big button; everything else in a sheet. */}
      {c.transitions.length > 0 && !needPlate && (
        <div className="next-actions">
          {primary && <button className="primary block big" disabled={busy} onClick={() => run(primary)}>{primary.label}</button>}
          <div className="quick-actions">
            {others.length > 0 && <button onClick={() => setSheet("actions")}>اقدام‌های دیگر</button>}
            {c.canEdit && !c.stage.isTerminal && (
              <button onClick={() => setSheet("wait")}>{c.waitReason ? "رفع توقف" : "کار متوقف است"}</button>
            )}
            <button onClick={() => setSheet("note")}>یادداشت</button>
          </div>
        </div>
      )}

      <BillingSection caseId={c.id} billing={c.billing} canAssignLabor={c.canAssign} paySignal={paySignal}
        onChange={(b) => setC({ ...c, billing: b })} />

      {/* Secondary details, collapsible. */}
      <Collapsible title="درخواست مشتری" open>
        {c.requestedServices.length > 0 && <Detail label="سرویس‌ها" value={c.requestedServices.join("، ")} />}
        {c.request && <Detail label="توضیحات" value={c.request} />}
        <Detail label="عیب‌یابی" value={c.diagnosis ?? "—"} />
        {c.estimatedAmountRials != null && <Detail label="برآورد هزینه" value={toman(c.estimatedAmountRials)} />}
        {c.canEdit && !editing && <button onClick={() => setEditing(true)}>ویرایش</button>}
        {editing && <CaseEditForm c={c} onDone={(updated) => { setEditing(false); if (updated) { setC(updated); notify("ذخیره شد"); } }} />}
      </Collapsible>

      <Collapsible title="پذیرش و وضعیت ظاهری">
        <Detail label="کیلومتر" value={c.odometerKm != null ? `${formatNumber(c.odometerKm)} کیلومتر` : "—"} />
        <Detail label="میزان سوخت" value={c.fuelLevel != null ? FUEL_LEVELS[c.fuelLevel] : "بررسی نشده"} />
        <Detail label="وضعیت بدنه" value={c.bodyStatus === "ok" ? "سالم" : c.bodyStatus === "damaged" ? `آسیب: ${c.bodyNotes ?? "—"}` : "بررسی نشده"} />
        {c.intake && <Detail label="همراه وسیله" value={Object.entries(c.intake).map(([k, v]) => (v === "دارد" ? k : `${k}: ${v}`)).join("، ")} />}
        <Detail label="محل وسیله" value={c.custodyStatus === "in_shop" ? "در تعمیرگاه" : "دست مشتری"} />
        {c.asset?.attributes && <VehicleAttributes attributes={c.asset.attributes} />}
        {c.canManage && (
          <button onClick={() => act(
            () => api<CaseDetailView>(`/api/v1/cases/${id}`, { method: "PATCH", body: { custodyStatus: c.custodyStatus === "in_shop" ? "with_customer" : "in_shop" } }),
            "ذخیره شد", setC)}>
            {c.custodyStatus === "in_shop" ? "وسیله دست مشتری است" : "وسیله در تعمیرگاه است"}
          </button>
        )}
      </Collapsible>

      <Collapsible title={`تاریخچه (${formatNumber(c.timeline.length)})`}>
        <ol className="timeline">
          {c.timeline.map((e) => (
            <li key={e.id}>
              <span className="timeline-text">{(EVENT_TEXT[e.type] ?? (() => e.type))(e.data ?? {})}</span>
              <span className="muted small">{faDateTime.format(new Date(e.occurredAt))}{e.actor ? ` · ${e.actor}` : ""}</span>
            </li>
          ))}
        </ol>
      </Collapsible>

      {c.canManage && (
        <button className="link danger delete-case" onClick={() => act(() => api(`/api/v1/cases/${id}`, { method: "DELETE" }), "پرونده حذف شد", () => onDeleted(c.number))}>
          حذف پرونده
        </button>
      )}

      {/* ── sheets ── */}
      <BottomSheet open={sheet === "actions"} title={pending ? `دلیل «${pending.label}»` : "اقدام‌های دیگر"} onClose={close}>
        {pending ? (
          <form onSubmit={(e) => { e.preventDefault(); if (reason.trim()) run(pending, { reason: reason.trim() }); }}>
            <Field label="دلیل">
              <input value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} />
            </Field>
            <button className="primary block" disabled={busy || !reason.trim()}>تأیید</button>
          </form>
        ) : (
          others.map((t) => (
            <SheetOption key={t.id} label={t.label} hint={`مرحله بعد: ${t.toStage.name}`} disabled={busy}
              tone={t.toStage.category === "cancelled" ? "danger" : undefined} onClick={() => run(t)} />
          ))
        )}
      </BottomSheet>

      <CreditSheet open={sheet === "credit"} balanceRials={balanceDue} busy={busy} onClose={close}
        onPay={() => { close(); setPaySignal((n) => n + 1); }}
        onCredit={(dueIso) => pending && run(pending, { allowCredit: true, creditDueAt: dueIso })} />

      <BottomSheet open={sheet === "parts"} title="قطعه را چه کسی تهیه می‌کند؟" onClose={close}>
        {PARTS_OPTIONS.map((o) => (
          <SheetOption key={o.value} label={o.label} hint={o.hint} disabled={busy} onClick={() => pending && run(pending, { waitReason: o.value })} />
        ))}
      </BottomSheet>

      <BottomSheet open={sheet === "assign"} title="مسئول پرونده" onClose={close}>
        {staff.map((s) => (
          <SheetOption key={s.id} label={s.name} hint={s.id === c.assignee?.id ? "مسئول فعلی" : undefined} disabled={busy}
            tone={s.id === c.assignee?.id ? "primary" : undefined}
            onClick={() => act(() => api<CaseDetailView>(`/api/v1/cases/${id}/assign`, { body: { assigneeId: s.id } }), `سپرده شد به ${s.name}`, setC)} />
        ))}
        {c.assignee && (
          <SheetOption label="برداشتن مسئول" disabled={busy}
            onClick={() => act(() => api<CaseDetailView>(`/api/v1/cases/${id}/assign`, { body: { assigneeId: null } }), "مسئول برداشته شد", setC)} />
        )}
      </BottomSheet>

      <BottomSheet open={sheet === "wait"} title={c.waitReason ? "کار دوباره جریان دارد؟" : "چرا کار متوقف است؟"} onClose={close}>
        {c.waitReason ? (
          <SheetOption label="بله، توقف برطرف شد" tone="primary" disabled={busy}
            onClick={() => act(() => api<CaseDetailView>(`/api/v1/cases/${id}/wait`, { body: { waitReason: null } }), "کار دوباره جریان گرفت", setC)} />
        ) : (
          MANUAL_WAIT_REASONS.map((r) => (
            <SheetOption key={r} label={WAIT_REASONS[r]} disabled={busy}
              onClick={() => act(() => api<CaseDetailView>(`/api/v1/cases/${id}/wait`, { body: { waitReason: r } }), "ثبت شد", setC)} />
          ))
        )}
      </BottomSheet>

      <BottomSheet open={sheet === "note"} title="یادداشت" onClose={close}>
        <form onSubmit={(e) => {
          e.preventDefault();
          if (note.trim()) act(() => api(`/api/v1/cases/${id}/notes`, { body: { text: note } }), "یادداشت ثبت شد", async () => { setNote(""); await load(); });
        }}>
          <textarea rows={3} value={note} onChange={(e) => setNote(e.target.value)} maxLength={2000} aria-label="متن یادداشت" placeholder="مثلاً قطعه سفارش داده شد" />
          <button className="primary block" disabled={busy || !note.trim()}>ثبت یادداشت</button>
        </form>
      </BottomSheet>

      <BottomSheet open={sheet === "promise"} title="قول تحویل" onClose={close}>
        <PromisePicker busy={busy} onPick={(iso) => act(() => api<CaseDetailView>(`/api/v1/cases/${id}`, { method: "PATCH", body: { promisedAt: iso } }), "قول تحویل ثبت شد", setC)} />
      </BottomSheet>
    </section>
  );
}

/** Jalali-only promise picker: day chips (today / tomorrow / …) + hour, no Gregorian calendar anywhere. */
function PromisePicker({ onPick, busy }: { onPick: (iso: string) => void; busy: boolean }) {
  const [dayOffset, setDayOffset] = useState(0);
  const [hour, setHour] = useState(18);
  const days = Array.from({ length: 7 }, (_, i) => {
    const d = new Date();
    d.setDate(d.getDate() + i);
    const label = i === 0 ? "امروز" : i === 1 ? "فردا" : new Intl.DateTimeFormat("fa-IR-u-ca-persian", { weekday: "long" }).format(d);
    const date = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long" }).format(d);
    return { i, label, date };
  });
  const picked = new Date();
  picked.setDate(picked.getDate() + dayOffset);
  picked.setHours(hour, 0, 0, 0);

  return (
    <div className="promise">
      <div className="chips">
        {days.map((d) => (
          <button key={d.i} type="button" className={`chip-button${dayOffset === d.i ? " active" : ""}`} onClick={() => setDayOffset(d.i)}>
            {d.label} <span className="small">{d.date}</span>
          </button>
        ))}
      </div>
      <Field label="ساعت">
        <select value={hour} onChange={(e) => setHour(Number(e.target.value))}>
          {Array.from({ length: 15 }, (_, i) => 8 + i).map((h) => (
            <option key={h} value={h}>{new Intl.NumberFormat("fa-IR", { minimumIntegerDigits: 2 }).format(h)}:۰۰</option>
          ))}
        </select>
      </Field>
      <p className="muted">{faWeekdayTime.format(picked)}</p>
      <button className="primary block" disabled={busy} onClick={() => onPick(picked.toISOString())}>ثبت قول تحویل</button>
    </div>
  );
}

function Collapsible({ title, open, children }: { title: string; open?: boolean; children: ReactNode }) {
  return (
    <details className="card collapsible" open={open}>
      <summary>{title}</summary>
      <div className="collapsible-body">{children}</div>
    </details>
  );
}

const ATTRIBUTE_NAMES: Record<string, string> = {
  year: "سال ساخت", color: "رنگ", vin: "شماره شاسی", fuelType: "نوع سوخت", gearbox: "گیربکس",
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
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    const body: Record<string, unknown> = { request, diagnosis };
    if (odometer) body.odometerKm = Number(odometer);
    if (c.canManage && estimate) body.estimatedAmountRials = Number(estimate) * 10;
    setBusy(true);
    try {
      onDone(await api<CaseDetailView>(`/api/v1/cases/${c.id}`, { method: "PATCH", body }));
    } catch (err) {
      if (err instanceof ApiError) {
        const f = Object.fromEntries(Object.entries(err.fields).map(([k, v]) => [k, v[0]]));
        setErrors(Object.keys(f).length ? f : { form: err.message });
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="edit-form" onSubmit={submit} noValidate>
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
        <button className="primary" disabled={busy}>{busy ? "در حال ذخیره…" : "ذخیره"}</button>
        <button type="button" onClick={() => onDone(null)}>انصراف</button>
      </div>
    </form>
  );
}
