import { useEffect, useState, type ReactNode } from "react";
import { api } from "./api";
import { CaseCard, type CaseCardData, type CaseFilter } from "./cases";
import { WAIT_REASONS, caseCode } from "./labels";
import type { LicenseStatus } from "./license";
import { StarRow, scoreText } from "./survey";
import { formatNumber } from "./ui";

type LowCase = { id: string; number: number; customerName: string | null; assetTitle: string | null; score: number; comment: string | null; answeredAt: string };
type MyScore = { enabled: boolean; responses?: number; average?: number | null; satisfiedPercent?: number | null; days?: number };
type Csat = { responses: number; average: number | null; satisfiedPercent: number | null } | null;

type Inbox = {
  role: string;
  needsAction: CaseCardData[];
  mine: CaseCardData[];
  ready: CaseCardData[];
  blocked: { reason: string; cases: CaseCardData[] }[];
  dueToday: CaseCardData[];
  lowSatisfaction: LowCase[];
  stats: {
    open: number; active: number; waiting: number; ready: number; openedToday: number; deliveredToday: number;
    unassigned: number; blocked: number; readyBalanceRials: number;
  };
};

const today = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { weekday: "long", day: "numeric", month: "long" });
const shortDate = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long", year: "numeric" });
const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;

function greeting() {
  const h = new Date().getHours();
  return h < 11 ? "صبح بخیر" : h < 16 ? "روز بخیر" : "عصر بخیر";
}

type IconName = "case" | "tool" | "check" | "clock" | "plus" | "calendar" | "chevron" | "alert" | "phone" | "star" | "building";
function Icon({ name }: { name: IconName }) {
  const d: Record<IconName, ReactNode> = {
    case: <><rect x="4" y="5" width="16" height="16" rx="3" /><path d="M9 3h6v4H9zM8 12h8M8 16h5" /></>,
    tool: <path d="M14.7 6.3a4 4 0 0 0-5-5L12 3.6 9.6 6 7.3 3.7a4 4 0 0 0 5 5l-7.8 7.8a2.1 2.1 0 0 0 3 3l7.8-7.8a4 4 0 0 0 5-5L18 9l-2.4-2.4 2.3-2.3a4 4 0 0 0-3.2 2Z" />,
    check: <path d="m5 12 4 4L19 6" />,
    clock: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>,
    plus: <path d="M12 5v14M5 12h14" />,
    calendar: <><rect x="3" y="5" width="18" height="16" rx="3" /><path d="M16 3v4M8 3v4M3 10h18" /></>,
    chevron: <path d="m15 18-6-6 6-6" />,
    alert: <><path d="M12 9v4M12 17h.01" /><path d="M10.3 3.6 2.5 17.1A2 2 0 0 0 4.2 20h15.6a2 2 0 0 0 1.7-2.9L13.7 3.6a2 2 0 0 0-3.4 0Z" /></>,
    phone: <path d="M5 3h4l2 5-2.5 1.5a15 15 0 0 0 6 6L16 13l5 2v4a2 2 0 0 1-2 2C10.2 20.5 3.5 13.8 3 5a2 2 0 0 1 2-2Z" />,
    star: <path d="m12 3 2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9Z" />,
    building: <path d="M3 21h18M5 21V8l7-5 7 5v13M9 21v-6h6v6M9 11h.01M15 11h.01" />,
  };
  return <svg className="h-icon" viewBox="0 0 24 24" aria-hidden="true">{d[name]}</svg>;
}

/**
 * Home: who and when, the one main action, the shop in four numbers, then what needs attention and what is ready.
 * Technicians see their own numbers and jobs. Wide screens put the lists side by side.
 */
export function HomeView({ name, license, canReports, canCreate, onOpen, onOpenSurvey, onNewCase, onOpenCases, surveyEnabled, brand }: {
  name: string | null;
  license: { status: LicenseStatus; open: () => void } | null;
  canReports: boolean;
  canCreate: boolean; onOpen: (id: string) => void; onOpenSurvey: (id: string) => void; onNewCase: () => void;
  onOpenCases: (f: CaseFilter) => void; surveyEnabled: boolean;
  /** In more than one branch: the way to the brand dashboard. */
  brand: { name: string | null; count: number; open: () => void } | null;
}) {
  const [inbox, setInbox] = useState<Inbox | null>(null);
  const [score, setScore] = useState<MyScore | null>(null);
  const [csat, setCsat] = useState<Csat>(null);
  useEffect(() => { api<Inbox>("/api/v1/inbox").then(setInbox).catch(() => {}); }, []);
  useEffect(() => { if (surveyEnabled) api<MyScore>("/api/v1/surveys/mine").then(setScore).catch(() => {}); }, [surveyEnabled]);
  // The shop's satisfaction over the last 30 days (managers with reports).
  useEffect(() => {
    if (!surveyEnabled || !canReports) return;
    const to = new Date(), from = new Date(Date.now() - 30 * 864e5);
    api<{ survey: Csat }>(`/api/v1/reports/summary?from=${encodeURIComponent(from.toISOString())}&to=${encodeURIComponent(to.toISOString())}`)
      .then((r) => setCsat(r.survey)).catch(() => {});
  }, [surveyEnabled, canReports]);
  if (!inbox) return <div className="splash" aria-busy="true" />;

  const manager = inbox.role !== "technician";
  const st = inbox.stats;
  const first = name?.trim().split(/\s+/)[0];
  const attention = manager ? inbox.needsAction.length + inbox.lowSatisfaction.length : inbox.mine.length;

  return (
    <section className="home2">
      <div className="page-intro">
        <div>
          <span>{today.format(new Date())}</span>
          <h1>{first ? `سلام ${first}، ${greeting()}` : greeting()}</h1>
          <p>
            {attention > 0
              ? manager ? `امروز ${formatNumber(attention)} پرونده نیاز به توجه شما دارد.` : `${formatNumber(attention)} کار به شما سپرده شده است.`
              : manager ? "همه‌چیز روبه‌راه است؛ کاری منتظر شما نیست." : "فعلاً کاری به شما سپرده نشده است."}
          </p>
        </div>
        {canCreate && (
          <button type="button" className="primary-action" onClick={onNewCase} data-tour="new-case"><Icon name="plus" />پذیرش جدید</button>
        )}
      </div>

      {license && license.status.state === "active" && (
        <button type="button" className="trial-banner" onClick={license.open}>
          <span className="trial-icon"><Icon name="calendar" /></span>
          <span>
            <strong>{formatNumber(license.status.daysLeft)} روز از {license.status.kind === "trial" ? "دوره‌ی رایگان" : "اشتراک"} باقی مانده</strong>
            {license.status.endsAt && <span>تا {shortDate.format(new Date(license.status.endsAt))}</span>}
          </span>
          <span className="banner-chevron"><Icon name="chevron" /></span>
        </button>
      )}

      {brand && (
        <button type="button" className="trial-banner brand-banner" onClick={brand.open}>
          <span className="trial-icon brand"><Icon name="building" /></span>
          <span>
            <strong>داشبورد برند{brand.name ? ` ${brand.name}` : ""}</strong>
            <span>{formatNumber(brand.count)} شعبه · وضعیت همه‌ی شعب و رفتن به هر کدام</span>
          </span>
          <span className="banner-chevron"><Icon name="chevron" /></span>
        </button>
      )}

      <div className="stats-grid" data-tour="queue">
        <Stat icon="case" tone="primary" value={st.open} label={manager ? "پرونده‌های باز" : "کارهای من"}
          sub={`${formatNumber(st.openedToday)} پذیرش امروز`} onClick={() => onOpenCases({})} />
        <Stat icon="tool" tone="blue" value={st.active} label="در حال کار"
          sub={manager && st.unassigned > 0 ? `${formatNumber(st.unassigned)} بدون مسئول` : "در دست تعمیرکاران"} onClick={() => onOpenCases({ category: "active" })} />
        <Stat icon="check" tone="green" value={st.ready} label="آماده تحویل"
          sub={st.readyBalanceRials > 0 ? `${toman(st.readyBalanceRials)} مانده` : `${formatNumber(st.deliveredToday)} تحویل امروز`} onClick={() => onOpenCases({ category: "done" })} />
        <Stat icon="clock" tone="orange" value={st.waiting} label="منتظر یا متوقف"
          sub={st.blocked > 0 ? `${formatNumber(st.blocked)} مورد متوقف` : "کاری متوقف نیست"} onClick={() => onOpenCases({ category: "waiting" })} />
      </div>

      {st.open === 0 && (
        <p className="muted empty-inline">
          {canCreate ? "پرونده بازی ندارید. با «پذیرش جدید» اولین وسیله را ثبت کنید." : "فعلاً کاری به شما سپرده نشده است."}
        </p>
      )}

      <div className="dashboard-grid">
        <div className="dash-col">
          {manager && inbox.lowSatisfaction.length > 0 && (
            <section>
              <SectionTitle title="رضایت پایین مشتری" count={inbox.lowSatisfaction.length} tone="danger" />
              <div className="card-stack">
                {inbox.lowSatisfaction.map((l) => (
                  <button key={l.id} type="button" className="card low-card" onClick={() => onOpenSurvey(l.id)}>
                    <span className="low-card-top">
                      <strong>{l.customerName ?? "مشتری"}</strong>
                      <span className="case-code" dir="ltr">{caseCode(l.number)}</span>
                    </span>
                    <span className="muted small">{l.assetTitle ?? ""}</span>
                    <span className="low-card-score"><StarRow rating={l.score} /><span>رضایت پایین مشتری ({scoreText(l.score)})</span></span>
                    {l.comment && <span className="low-card-comment">«{l.comment}»</span>}
                    <span className="small link-text">تماس بگیرید و «پیگیری شد» را بزنید ‹</span>
                  </button>
                ))}
              </div>
            </section>
          )}

          {!manager && inbox.mine.length > 0 && (
            <section>
              <SectionTitle title="کارهای من" count={inbox.mine.length} tone="primary" action={{ label: "همه‌ی کارها", onClick: () => onOpenCases({ mine: true }) }} />
              <div className="card-stack">{inbox.mine.map((c) => <CaseCard key={c.id} c={c} onOpen={onOpen} />)}</div>
            </section>
          )}

          {manager && inbox.needsAction.length > 0 && (
            <section>
              <SectionTitle title="نیازمند اقدام" count={inbox.needsAction.length} tone="warning" action={{ label: "مشاهده همه", onClick: () => onOpenCases({}) }} />
              <div className="card-stack">{inbox.needsAction.slice(0, 6).map((c) => <CaseCard key={c.id} c={c} onOpen={onOpen} compact />)}</div>
            </section>
          )}

          {inbox.blocked.length > 0 && (
            <section>
              <SectionTitle title="چه چیزی کار را متوقف کرده؟" tone="warning" />
              <div className="card-stack">
                {inbox.blocked.map((g) => (
                  <details key={g.reason} className="card blocked-group">
                    <summary>
                      <span>{WAIT_REASONS[g.reason] ?? g.reason}</span>
                      <span className="count font-num">{formatNumber(g.cases.length)}</span>
                    </summary>
                    <div className="card-stack">{g.cases.map((c) => <CaseCard key={c.id} c={c} onOpen={onOpen} compact />)}</div>
                  </details>
                ))}
              </div>
            </section>
          )}
        </div>

        <div className="dash-col">
          {!manager && score?.enabled && (score.responses ?? 0) > 0 && score.average != null && (
            <section className="feedback-card">
              <span className="feedback-score font-num">{new Intl.NumberFormat("fa-IR", { maximumFractionDigits: 1 }).format(score.average)}</span>
              <span>
                <span>رضایت مشتری از کارهای شما ({formatNumber(score.days ?? 90)} روز اخیر)</span>
                <strong><StarRow rating={score.average} /></strong>
                <span>{formatNumber(score.responses ?? 0)} نظر، {formatNumber(score.satisfiedPercent ?? 0)}٪ راضی</span>
              </span>
            </section>
          )}

          {manager && inbox.ready.length > 0 && (
            <section>
              <SectionTitle title="آماده تحویل" count={inbox.ready.length} tone="success" action={{ label: "مشاهده پرونده‌ها", onClick: () => onOpenCases({ category: "done" }) }} />
              <div className="card-stack">
                {inbox.ready.slice(0, 6).map((c) => (
                  <div key={c.id} className="ready-card">
                    <span className="ready-icon"><Icon name="check" /></span>
                    <button type="button" className="ready-text" onClick={() => onOpen(c.id)}>
                      <strong>{c.assetTitle ?? "بدون وسیله"}</strong>
                      <span>{c.customerName ?? "مشتری"} · <span dir="ltr">{caseCode(c.number)}</span>
                        {c.balanceRials ? <> · مانده <span className="font-num">{toman(c.balanceRials)}</span></> : null}</span>
                    </button>
                    <a className="ready-call" href={`tel:${c.customerMobile}`}><Icon name="phone" />تماس</a>
                  </div>
                ))}
              </div>
            </section>
          )}

          {inbox.dueToday.length > 0 && (
            <section>
              <SectionTitle title="قول تحویل امروز" count={inbox.dueToday.length} tone="warning" />
              <div className="card-stack">{inbox.dueToday.map((c) => <CaseCard key={c.id} c={c} onOpen={onOpen} compact />)}</div>
            </section>
          )}

          {manager && csat && csat.responses > 0 && csat.average != null && (
            <section className="feedback-card">
              <span className="feedback-score font-num">{new Intl.NumberFormat("fa-IR", { maximumFractionDigits: 1 }).format(csat.average)}</span>
              <span>
                <span>رضایت مشتریان در ۳۰ روز اخیر</span>
                <strong>{(csat.satisfiedPercent ?? 0) >= 80 ? "عملکرد تعمیرگاه خوب است" : (csat.satisfiedPercent ?? 0) >= 60 ? "جای بهتر شدن دارد" : "نیاز به توجه دارد"}</strong>
                <span>از مجموع {formatNumber(csat.responses)} نظر ثبت‌شده، {formatNumber(csat.satisfiedPercent ?? 0)}٪ راضی</span>
              </span>
            </section>
          )}
        </div>
      </div>
    </section>
  );
}

function Stat({ icon, tone, value, label, sub, onClick }: {
  icon: IconName; tone: "primary" | "blue" | "green" | "orange"; value: number; label: string; sub: string; onClick: () => void;
}) {
  return (
    <button type="button" className={`stat-card ${tone}`} onClick={onClick}>
      <span className="stat-icon"><Icon name={icon} /></span>
      <strong className="font-num">{formatNumber(value)}</strong>
      <span className="stat-label">{label}</span>
      <small>{sub}</small>
    </button>
  );
}

function SectionTitle({ title, count, tone, action }: {
  title: string; count?: number; tone?: "danger" | "warning" | "success" | "primary"; action?: { label: string; onClick: () => void };
}) {
  return (
    <div className="section-heading">
      <div>
        <span className={`section-dot ${tone ?? ""}`} />
        <h2>{title}</h2>
        {count !== undefined && <small className="font-num">{formatNumber(count)}</small>}
      </div>
      {action && <button type="button" onClick={action.onClick}>{action.label}</button>}
    </div>
  );
}
