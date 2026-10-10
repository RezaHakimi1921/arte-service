import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from "react";
import { api, ApiError } from "./api";
import { BillingSection, CreditSheet, MoneyBar, toman as tomanFmt, type Billing } from "./billing";
import { useFeedback } from "./feedback";
import { ALERT_ICON, MANUAL_WAIT_REASONS, PARTS_OPTIONS, WAIT_REASONS, caseCode, eventText, statusText, type Alert } from "./labels";
import { progressOf, useWorkflow } from "./workflow";
import { PlateView } from "./plate";
import { BottomSheet, SelectSheet, SheetOption } from "./sheet";
import { PhotoSection, type CasePhoto } from "./photos";
import { SurveySection, type CaseSurvey } from "./survey";
import { ROLE_NAMES } from "./labels";
import { Field, NumberInput, formatNumber, toLatinDigits } from "./ui";
import { ServicePicker } from "./services";
import { FUEL_LEVELS, problemsFor } from "./vehicles";

/* ───────── types ───────── */

type StageRef = { id: string; key: string; name: string; category: string; color: string; isTerminal: boolean };
export type CaseCardData = {
  id: string; number: number; customerName: string | null; customerMobile: string; assetTitle: string | null;
  assetIdentifier: string | null; stage: StageRef; assigneeName: string | null; stageEnteredAt: string;
  promisedAt: string | null; request: string; requestedServices: string[]; waitReason: string | null; alert?: Alert | null;
  reasons?: Alert[]; balanceRials?: number; openedAt?: string; reportedProblems?: string[];
  totalRials?: number; paidRials?: number; isCredit?: boolean; creditDueAt?: string | null;
  odometerKm?: number | null; fuelLevel?: number | null; assetKind?: string | null;
  lastEvent?: { type: string; occurredAt: string; data: Record<string, unknown> | null } | null;
};
type TransitionView = { id: string; label: string; isPrimary: boolean; requiresReason: boolean; toStage: { name: string; category: string; color: string } };
type TimelineEntry = { id: number; type: string; occurredAt: string; actor: string | null; data: Record<string, unknown> | null };
type CaseDetailView = {
  id: string; number: number; request: string; requestedServices: string[]; reportedProblems: string[]; fuelLevel: number | null;
  bodyStatus: string | null; bodyNotes: string | null; diagnosis: string | null; odometerKm: number | null;
  estimatedAmountRials: number | null; promisedAt: string | null; custodyStatus: string; intake: Record<string, string> | null;
  relation: string | null; waitReason: string | null; openedAt: string; stageEnteredAt: string; closedAt: string | null;
  stage: StageRef; customer: { id: string; fullName: string | null; mobile: string };
  asset: { id: string; title: string; identifier: string | null; kind: string; attributes: Record<string, string> | null } | null;
  assignee: { id: string; name: string } | null; parentCase: { id: string; number: number } | null;
  photos: CasePhoto[]; trackingCode: string | null; photosVisibleByDefault: boolean;
  transitions: TransitionView[]; canEdit: boolean; canManage: boolean; canAssign: boolean; timeline: TimelineEntry[];
  billing: Billing; warrantyUntil: string | null; creditDueAt: string | null;
  survey: CaseSurvey | null; canFollowUp: boolean;
};
type Assignable = { id: string; name: string; role: string };
export type CaseFilter = { category?: string; mine?: boolean; all?: boolean };

/* ───────── formatting (Jalali calendar everywhere) ───────── */

const faDateTime = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { month: "long", day: "numeric", hour: "2-digit", minute: "2-digit" });
const faWeekdayTime = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { weekday: "long", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" });
const faShort = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" });

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

const maskMobile = (m: string) => (m.length === 11 ? `${m.slice(0, 4)}•••${m.slice(7)}` : m);

/** "۲ ساعت مانده" / "۳ ساعت گذشته" until the promised delivery. */
function slaText(promisedAt: string): { text: string; late: boolean } {
  const ms = new Date(promisedAt).getTime() - Date.now();
  const late = ms < 0;
  const minutes = Math.round(Math.abs(ms) / 60000);
  const span = minutes < 60 ? `${formatNumber(minutes)} دقیقه` : minutes < 1440 ? `${formatNumber(Math.round(minutes / 60))} ساعت` : `${formatNumber(Math.round(minutes / 1440))} روز`;
  return { text: late ? `${span} از قول تحویل گذشته` : `${span} تا قول تحویل`, late };
}

function Progress({ stage }: { stage: StageRef }) {
  const wf = useWorkflow();
  if (!wf || stage.category === "cancelled") return null;
  const { path, index } = progressOf(wf.stages, stage);
  if (path.length < 2 || index < 0) return null;
  return (
    <span className="progress" role="img" aria-label={`مرحله ${formatNumber(index + 1)} از ${formatNumber(path.length)}: ${stage.name}`}>
      <span className="progress-track">
        {path.map((p, i) => (
          <span key={p.id} className={`progress-step${i < index ? " done" : i === index ? ` now stage-bg-${stage.category === "waiting" ? "orange" : stage.color}` : ""}`} />
        ))}
      </span>
      <span className="progress-caption muted small">مرحله {formatNumber(index + 1)} از {formatNumber(path.length)}</span>
    </span>
  );
}

/**
 * One look: what is this, where is it now, whose is it, and what happened last.
 * Everything else lives in the case page.
 */
export function CaseCard({ c, onOpen }: { c: CaseCardData; onOpen: (id: string) => void }) {
  // The status line already says "منتظر تصمیم استاد"; do not repeat it as a warning.
  const alerts = (c.reasons ?? (c.alert ? [c.alert] : [])).filter((a) => a.code !== "owner_decision");
  const status = statusText(c.stage, c.waitReason, null);
  const color = c.waitReason ? "orange" : c.stage.color;
  const sla = c.promisedAt && !c.stage.isTerminal ? slaText(c.promisedAt) : null;
  const total = c.totalRials ?? 0;
  const balance = c.balanceRials ?? 0;
  return (
    <button className="case-card" onClick={() => onOpen(c.id)}>
      <span className="case-row-top">
        <span className="case-code" dir="ltr">{caseCode(c.number)}</span>
        <span className="case-badges">
          {c.isCredit && <span className="badge credit">نسیه</span>}
          <span className={`status-badge stage-${color}`}><span className="dot" aria-hidden="true" />{status}</span>
        </span>
      </span>
      <span className="case-title">
        <strong>{c.assetTitle ?? "بدون وسیله"}</strong>
        {c.assetIdentifier && <PlateView identifier={c.assetIdentifier} />}
      </span>
      <span className="muted">{c.customerName ?? <span dir="ltr" className="font-num">{maskMobile(c.customerMobile)}</span>}</span>
      <Progress stage={c.stage} />

      {/* Three short panels: the vehicle, the work, the money. */}
      <span className="case-panels">
        <span className="case-panel">
          <span className="panel-label">وسیله (کیلومتر)</span>
          <span className="panel-value">{c.odometerKm != null ? <span className="font-num">{formatNumber(c.odometerKm)}</span> : "—"}</span>
          {c.fuelLevel == null && <span className="panel-sub">{c.odometerKm != null ? "سوخت ثبت نشده" : "ثبت نشده"}</span>}
          {c.fuelLevel != null && <FuelMini level={c.fuelLevel} />}
        </span>
        <span className="case-panel">
          <span className="panel-label">تعمیر</span>
          <span className="panel-value">{c.assigneeName ?? "بدون مسئول"}</span>
          <span className="panel-sub">{c.lastEvent ? `${ago(c.lastEvent.occurredAt)} پیش` : `${ago(c.stageEnteredAt)} پیش`}</span>
        </span>
        <span className={`case-panel${balance > 0 && c.stage.category === "done" ? " owe" : ""}`}>
          <span className="panel-label">مالی (تومان)</span>
          {total > 0 ? (
            <>
              <span className="panel-value font-num">{formatNumber(Math.round(total / 10))}</span>
              <span className="panel-sub">
                {balance > 0 ? <>مانده <span className="font-num">{formatNumber(Math.round(balance / 10))}</span></> : balance === 0 ? "تسویه" : "بستانکار"}
              </span>
            </>
          ) : <><span className="panel-value">—</span><span className="panel-sub">بدون ردیف</span></>}
        </span>
      </span>

      {c.isCredit && c.creditDueAt && (
        <span className="alert-line warn">موعد پرداخت نسیه: {faShort.format(new Date(c.creditDueAt))}</span>
      )}
      {sla && <span className={`alert-line ${sla.late ? "danger" : "warn"}`}>⏱ {sla.text}</span>}
      {c.lastEvent && <span className="last-activity muted small">آخرین فعالیت: {eventText(c.lastEvent.type, c.lastEvent.data)}</span>}
      {alerts.filter((a) => !a.code.startsWith("due") && a.code !== "overdue").slice(0, 1).map((a) => <AlertLine key={a.code} alert={a} />)}
    </button>
  );
}

/** Fuel at intake as a small five-step bar (red empty … green full). */
function FuelMini({ level }: { level: number }) {
  return (
    <span className="fuel-mini" role="img" aria-label={`سوخت: ${FUEL_LEVELS[level] ?? ""}`}>
      {[0, 1, 2, 3, 4].map((i) => <i key={i} className={i <= level ? `on f${level}` : ""} />)}
    </span>
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
    if (q.trim()) params.set("q", toLatinDigits(q.trim()));
    const t = setTimeout(() => api<CaseCardData[]>(`/api/v1/cases?${params}`).then(setRows).catch(() => setRows([])), 200);
    return () => clearTimeout(t);
  }, [filter, q]);

  const filtered = q.trim() || !sameFilter(filter, {});

  return (
    <section>
      <div className="toolbar">
        <input type="search" placeholder="CASE-12، نام، موبایل، پلاک یا مدل" value={q} onChange={(e) => setQ(e.target.value)} aria-label="جستجوی پرونده" />
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
        filtered ? <p className="empty muted">پرونده‌ای با این شرایط پیدا نشد.</p> : <EmptyCases canCreate={canCreate} />
      ) : (
        <ul className="list">
          {rows?.map((c) => <li key={c.id}><CaseCard c={c} onOpen={onOpen} /></li>)}
        </ul>
      )}
    </section>
  );
}

export function EmptyCases({ canCreate }: { canCreate: boolean }) {
  return (
    <div className="empty-state">
      <h3>پرونده بازی ندارید</h3>
      <p className="muted">
        {canCreate
          ? "با دکمه «پذیرش» وسیله تازه را ثبت کنید. پرونده‌های تحویل‌شده در فیلتر «همه» هستند."
          : "وقتی کاری به شما سپرده شود، این‌جا می‌بینید و مرحله به مرحله جلو می‌برید."}
      </p>
    </div>
  );
}

/* ───────── detail ───────── */

type Sheet = null | "actions" | "assign" | "note" | "wait" | "parts" | "promise" | "credit" | "link";

export function CaseDetail({ id, onBack, onDeleted, focusSurvey = false }: {
  id: string; onBack: () => void; onDeleted: (number: number) => void; focusSurvey?: boolean;
}) {
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
      if (err instanceof ApiError && err.body?.code === "balance_due") {
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

  const photoSection = <PhotoSection caseId={c.id} photos={c.photos ?? []} stageKey={c.stage.key} canAdd={c.canEdit && c.stage.key !== "cancelled"} onChange={load} visibleByDefault={c.photosVisibleByDefault} />;
  const primary = c.transitions.find((t) => t.isPrimary);
  const others = c.transitions.filter((t) => t !== primary);

  return (
    <section className="case-page">
      <button className="link back" onClick={onBack}>→ پرونده‌ها</button>

      {/* Header: only decision-making facts. */}
      <div className="card case-head" data-tour="case-head">
        <div className="case-row-top">
          <span className="case-code" dir="ltr">{caseCode(c.number)}</span>
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
        <Progress stage={c.stage} />
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
        {c.stage.key === "delivered" && c.billing.money.balanceRials > 0 && (
          <div className="credit-bar" role="status">
            <span>
              <strong>نسیه</strong> · مانده <span className="font-num">{tomanFmt(c.billing.money.balanceRials)}</span>
              {c.creditDueAt && <> · موعد {new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long" }).format(new Date(c.creditDueAt))}</>}
            </span>
            {c.billing.canRecordPayments && <button className="primary" onClick={() => setPaySignal((n) => n + 1)}>ثبت پرداخت</button>}
          </div>
        )}
        {(c.warrantyUntil || c.creditDueAt) && (
          <p className="muted small">
            {c.warrantyUntil && `ضمانت تا ${new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long", year: "numeric" }).format(new Date(c.warrantyUntil))}`}
            {c.warrantyUntil && c.creditDueAt && " · "}
            {c.creditDueAt && `موعد پرداخت نسیه: ${new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long" }).format(new Date(c.creditDueAt))}`}
          </p>
        )}
        {c.parentCase && <p className="muted small">برگشتی پرونده <span dir="ltr">{caseCode(c.parentCase.number)}</span></p>}
      </div>

      {/* Next action: one big button; everything else in a sheet. */}
      {c.transitions.length > 0 && (
        <div className="next-actions" data-tour="next-action">
          {primary && <button className="primary block big" disabled={busy} aria-busy={busy} onClick={() => run(primary)}>{primary.label}</button>}
          <div className="quick-actions">
            {others.length > 0 && <button onClick={() => setSheet("actions")}>اقدام‌های دیگر</button>}
            {c.canEdit && !c.stage.isTerminal && (
              <button onClick={() => setSheet("wait")}>{c.waitReason ? "رفع توقف" : "کار متوقف است"}</button>
            )}
            <button onClick={() => setSheet("note")}>یادداشت</button>
            {c.trackingCode && <button onClick={() => setSheet("link")}>لینک مشتری</button>}
          </div>
        </div>
      )}

      {c.survey && <SurveySection caseId={c.id} survey={c.survey} canFollowUp={c.canFollowUp} focus={focusSurvey} onChange={load} />}

      {/* In review the master looks at the work photos first; otherwise they sit below the bill. */}
      {c.stage.key === "review" && photoSection}
      <BillingSection caseId={c.id} billing={c.billing} canAssignLabor={c.canAssign} paySignal={paySignal} vehicleKind={c.asset?.kind ?? null}
        onChange={(b) => { setC({ ...c, billing: b }); load(); }} />

      {c.stage.key !== "review" && photoSection}

      {/* Secondary details, collapsible. */}
      <Collapsible title="درخواست مشتری" open>
        <Detail label="ایراد اعلامی" value={c.reportedProblems.length > 0 ? c.reportedProblems.join("، ") : "—"} />
        {c.request && <Detail label="شرح مشتری" value={c.request} />}
        <Detail label="خدمات درخواستی" value={c.requestedServices.length > 0 ? c.requestedServices.join("، ") : "—"} />
        <Detail label="عیب‌یابی" value={c.diagnosis ?? "—"} />
        {c.estimatedAmountRials != null && <Detail label="برآورد هزینه" value={toman(c.estimatedAmountRials)} />}
        {c.canEdit && <button onClick={() => setEditing(true)}>ویرایش درخواست</button>}
      </Collapsible>
      <BottomSheet open={editing} title="ویرایش درخواست مشتری" onClose={() => setEditing(false)}>
        {editing && <CaseEditForm c={c} onDone={(updated) => { setEditing(false); if (updated) { setC(updated); notify("ذخیره شد"); } }} />}
      </BottomSheet>

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

      <Collapsible title={`تاریخچه (${formatNumber(c.timeline.length)})`} tour="timeline">
        <ol className="timeline">
          {c.timeline.map((e) => (
            <li key={e.id}>
              <span className="timeline-text">{eventText(e.type, e.data)}</span>
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
            <button className="primary block" disabled={busy || !reason.trim()} aria-busy={busy}>تأیید</button>
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

      {c.trackingCode && (
        <BottomSheet open={sheet === "link"} title="لینک پیگیری مشتری" onClose={close}>
          <p className="muted small">مشتری با این لینک، بدون ورود، وضعیت کار، قطعات و هزینه را می‌بیند.</p>
          <SheetOption label="باز کردن صفحه‌ی مشتری" hint="همان چیزی که مشتری می‌بیند"
            onClick={() => { window.open(`/t/${c.trackingCode}`, "_blank", "noopener"); close(); }} />
          <SheetOption label={busy ? "در حال ارسال…" : "ارسال دوباره با پیامک"} hint="به شماره‌ی مشتری · هزینه‌ی یک پیامک از اعتبار کم می‌شود" disabled={busy}
            onClick={() => act(() => api(`/api/v1/cases/${id}/send-link`, { method: "POST" }), "لینک برای مشتری پیامک شد", () => { close(); load(); })} />
          {"share" in navigator && (
            <SheetOption label="اشتراک‌گذاری" hint="واتساپ، تلگرام، پیامک گوشی…"
              onClick={() => { navigator.share({ title: "پیگیری کار شما", url: `${location.origin}/t/${c.trackingCode}` }).catch(() => {}); close(); }} />
          )}
          <SheetOption label="کپی لینک"
            onClick={() => { navigator.clipboard?.writeText(`${location.origin}/t/${c.trackingCode}`).then(() => notify("لینک کپی شد"), () => {}); close(); }} />
        </BottomSheet>
      )}

      <BottomSheet open={sheet === "parts"} title="قطعه را چه کسی تهیه می‌کند؟" onClose={close}>
        {PARTS_OPTIONS.map((o) => (
          <SheetOption key={o.value} label={o.label} hint={o.hint} disabled={busy} onClick={() => pending && run(pending, { waitReason: o.value })} />
        ))}
      </BottomSheet>

      <SelectSheet open={sheet === "assign"} title="مسئول پرونده" onClose={close} busy={busy} value={c.assignee?.id}
        items={staff.map((s) => ({ value: s.id, label: s.name, group: ROLE_NAMES[s.role] ?? s.role }))} noneLabel="برداشتن مسئول"
        onSelect={(v) => act(() => api<CaseDetailView>(`/api/v1/cases/${id}/assign`, { body: { assigneeId: v } }),
          v ? `سپرده شد به ${staff.find((s) => s.id === v)?.name ?? ""}` : "مسئول برداشته شد", setC)} />

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
          <button className="primary block" disabled={busy || !note.trim()} aria-busy={busy}>ثبت یادداشت</button>
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
      <button className="primary block" disabled={busy} aria-busy={busy} onClick={() => onPick(picked.toISOString())}>ثبت قول تحویل</button>
    </div>
  );
}

function Collapsible({ title, open, children, tour }: { title: string; open?: boolean; children: ReactNode; tour?: string }) {
  return (
    <details className="card collapsible" open={open} data-tour={tour}>
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

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div className="detail">
      <span className="label">{label}</span>
      <span>{value}</span>
    </div>
  );
}

function CaseEditForm({ c, onDone }: { c: CaseDetailView; onDone: (updated: CaseDetailView | null) => void }) {
  const [problems, setProblems] = useState<string[]>(c.reportedProblems);
  const [services, setServices] = useState<string[]>(c.requestedServices);
  const [request, setRequest] = useState(c.request);
  const [diagnosis, setDiagnosis] = useState(c.diagnosis ?? "");
  const [odometer, setOdometer] = useState(c.odometerKm?.toString() ?? "");
  const [estimate, setEstimate] = useState(c.estimatedAmountRials != null ? String(c.estimatedAmountRials / 10) : "");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    const body: Record<string, unknown> = { request, diagnosis, reportedProblems: problems, requestedServices: services };
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
      <div className="field">
        <span className="label">ایراد اعلامی</span>
        <div className="chips">
          {[...new Set([...problemsFor(c.asset?.kind), ...problems])].map((p) => (
            <button type="button" key={p} aria-pressed={problems.includes(p)} className={`chip-button${problems.includes(p) ? " active" : ""}`}
              onClick={() => setProblems(problems.includes(p) ? problems.filter((x) => x !== p) : [...problems, p])}>{p}</button>
          ))}
        </div>
      </div>
      <div className="field">
        <span className="label">خدمات درخواستی</span>
        <ServicePicker value={services} onChange={setServices} kind={c.asset?.kind} />
      </div>
      <Field label="شرح مشتری" error={errors.request}>
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
        <button className="primary" disabled={busy} aria-busy={busy}>{busy ? "در حال ذخیره…" : "ذخیره"}</button>
        <button type="button" onClick={() => onDone(null)}>انصراف</button>
      </div>
    </form>
  );
}
