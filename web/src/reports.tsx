import { useEffect, useMemo, useState } from "react";
import { ApiError, api } from "./api";
import { ROLE_NAMES, caseCode } from "./labels";
import { SubPage } from "./settings";
import { StaffPayBlock } from "./staffPay";
import { StarRow, scoreText } from "./survey";
import { formatNumber } from "./ui";

type SurveyReport = {
  sent: number; responses: number; responseRatePercent: number | null; average: number | null; satisfiedPercent: number | null;
  questions: { question: string; average: number; responses: number }[];
  staff: { membershipId: string; name: string; responses: number; average: number | null; satisfiedPercent: number | null }[];
  low: { id: string; number: number; customer: string | null; score: number; comment: string | null; answeredAt: string; followed: boolean }[];
};

type Summary = {
  opened: number; delivered: number; salesRials: number; partsRials: number; workRials: number; partsProfitRials: number | null;
  receivedRials: number; receivablesRials: number; survey: SurveyReport | null;
  topItems: { kind: string; title: string; cases: number; quantity: number; salesRials: number; profitRials: number | null }[];
};

const jalaliDay = new Intl.DateTimeFormat("en-u-ca-persian-nu-latn", { day: "numeric" });
const midnight = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate());
const addDays = (d: Date, n: number) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + n);
/** Walks back to the 1st of the Jalali month that contains d. */
function jalaliMonthStart(d: Date) {
  let x = midnight(d);
  while (Number(jalaliDay.format(x)) !== 1) x = addDays(x, -1);
  return x;
}

const PERIODS = [
  { key: "today", label: "امروز", range: (now: Date) => [midnight(now), addDays(midnight(now), 1)] as [Date, Date] },
  // The Iranian week starts on Saturday (getDay() === 6).
  { key: "week", label: "این هفته", range: (now: Date) => [addDays(midnight(now), -((now.getDay() + 1) % 7)), addDays(midnight(now), 1)] as [Date, Date] },
  { key: "month", label: "این ماه", range: (now: Date) => [jalaliMonthStart(now), addDays(midnight(now), 1)] as [Date, Date] },
  { key: "last", label: "ماه قبل", range: (now: Date) => { const start = jalaliMonthStart(now); return [jalaliMonthStart(addDays(start, -1)), start] as [Date, Date]; } },
  { key: "custom", label: "سفارشی", range: null },
] as const;

const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;
const monthName = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { month: "long", year: "numeric" });
const dayChipLabel = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { weekday: "short", day: "numeric", month: "short" });

/** A few fixed numbers for the owner; no builder, no charts. Staff pay has its own endpoint. */
export function ReportsPage({ onBack, onReceivables, onOpenCase, canManageStaff }: {
  onBack: () => void; onReceivables: () => void; onOpenCase: (id: string) => void; canManageStaff: boolean;
}) {
  const now = new Date();
  const [period, setPeriod] = useState<(typeof PERIODS)[number]["key"]>("month");
  const [customFrom, setCustomFrom] = useState(() => jalaliMonthStart(now));
  const [customTo, setCustomTo] = useState(() => addDays(midnight(now), 1));
  const [data, setData] = useState<Summary | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [from, to] = useMemo(() => {
    if (period === "custom") {
      const a = midnight(customFrom);
      const b = midnight(customTo);
      return a < b ? [a, b] as [Date, Date] : [b, addDays(a, 1)] as [Date, Date];
    }
    return PERIODS.find((p) => p.key === period)!.range!(now);
  }, [period, customFrom.getTime(), customTo.getTime()]);

  useEffect(() => {
    let alive = true;
    setData(null);
    setError(null);
    api<Summary>(`/api/v1/reports/summary?from=${encodeURIComponent(from.toISOString())}&to=${encodeURIComponent(to.toISOString())}`)
      .then((d) => alive && setData(d))
      .catch((err) => alive && setError(err instanceof ApiError ? err.message : "خطا"));
    return () => { alive = false; };
  }, [from.getTime(), to.getTime()]);

  const subtitle = period === "custom"
    ? `${monthName.format(from)} ← ${monthName.format(addDays(to, -1))}`
    : period === "month" || period === "last" ? monthName.format(from) : null;

  const customDays = useMemo(() => Array.from({ length: 60 }, (_, i) => addDays(midnight(now), -45 + i)), [now.toDateString()]);

  return (
    <SubPage title="گزارش‌ها" onBack={onBack}>
      <div className="segmented wide" role="radiogroup" aria-label="بازه">
        {PERIODS.map((p) => (
          <button type="button" key={p.key} role="radio" aria-checked={period === p.key} className={period === p.key ? "on" : ""} onClick={() => setPeriod(p.key)}>{p.label}</button>
        ))}
      </div>
      {period === "custom" && (
        <div className="custom-range">
          <span className="label">از روز</span>
          <div className="day-chips" role="listbox" aria-label="شروع بازه">
            {customDays.map((d) => (
              <button type="button" key={`f-${d.toISOString()}`} role="option" aria-selected={midnight(customFrom).getTime() === d.getTime()}
                className={midnight(customFrom).getTime() === d.getTime() ? "on" : ""} onClick={() => setCustomFrom(d)}>
                {d.toDateString() === midnight(now).toDateString() ? "امروز" : dayChipLabel.format(d)}
              </button>
            ))}
          </div>
          <span className="label">تا روز (پایان؛ خودش داخل بازه نیست)</span>
          <div className="day-chips" role="listbox" aria-label="پایان بازه">
            {customDays.map((d) => (
              <button type="button" key={`t-${d.toISOString()}`} role="option" aria-selected={midnight(customTo).getTime() === d.getTime()}
                className={midnight(customTo).getTime() === d.getTime() ? "on" : ""} onClick={() => setCustomTo(d)}>
                {d.toDateString() === midnight(now).toDateString() ? "امروز" : dayChipLabel.format(d)}
              </button>
            ))}
          </div>
        </div>
      )}
      {subtitle && <p className="muted small">{subtitle}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      {!data && !error && <div className="splash" aria-busy="true" />}
      {data && (
        <>
          <div className="report-grid">
            <Stat label="پذیرش" value={`${formatNumber(data.opened)} پرونده`} />
            <Stat label="تحویل" value={`${formatNumber(data.delivered)} پرونده`} />
            <Stat label="فروش (پرونده‌های تحویل‌شده)" value={toman(data.salesRials)} wide />
            <Stat label="دریافتی" value={toman(data.receivedRials)} />
            {data.partsProfitRials != null && <Stat label="سود کالا" value={toman(data.partsProfitRials)} />}
            <Stat label="اجرت و خدمات" value={toman(data.workRials)} />
            <Stat label="فروش کالا" value={toman(data.partsRials)} />
            <button type="button" className={`report-stat wide report-link${data.receivablesRials > 0 ? " warn" : ""}`} onClick={onReceivables}>
              <span className="muted small">نسیه‌ها · طلب از مشتریان (الان)</span>
              <strong className="font-num">{toman(data.receivablesRials)}</strong>
              <span className="muted small">فهرست نسیه‌ها و موعد پرداخت ‹</span>
            </button>
          </div>

          <h3>پرفروش‌ها</h3>
          {data.topItems.length === 0 ? (
            <p className="muted small">در این بازه پرونده‌ای با کالا یا اجرت تحویل نشده است.</p>
          ) : (
            <div className="settings-list">
              {data.topItems.map((i) => (
                <div key={`${i.kind}-${i.title}`} className="settings-row static">
                  <span className="settings-row-text">
                    <span>{i.title}</span>
                    <span className="muted small">
                      {i.kind === "part" ? "کالا" : "اجرت و خدمات"}، {formatNumber(i.cases)} پرونده
                      {i.kind === "part" && <>، {formatNumber(i.quantity)} عدد</>}
                      {i.profitRials != null && <>، سود <span className="font-num">{toman(i.profitRials)}</span></>}
                    </span>
                  </span>
                  <strong className="font-num">{toman(i.salesRials)}</strong>
                </div>
              ))}
            </div>
          )}

          <StaffPayBlock from={from} to={to} canManage={canManageStaff} onOpenCase={onOpenCase} />

          <WorkshopBlock from={from} to={to} />

          {data.survey && <SurveyBlock r={data.survey} onOpenCase={onOpenCase} />}
        </>
      )}
    </SubPage>
  );
}

type WorkshopReport = {
  today: { opened: number; delivered: number; byStatus: { key: string; name: string; count: number }[] };
  people: { membershipId: string; name: string; role: string; finished: number; surveys: number; averageScore: number | null }[];
  reportedProblems: { title: string; count: number }[]; foundProblems: { title: string; count: number }[];
  motorTypes: { type: string; count: number }[];
};

/** The workshop at a glance (today), each person's finished jobs and satisfaction, and the most frequent problems of the period. */
function WorkshopBlock({ from, to }: { from: Date; to: Date }) {
  const [r, setR] = useState<WorkshopReport | null>(null);
  useEffect(() => {
    let alive = true;
    const t0 = midnight(new Date());
    const q = new URLSearchParams({ from: from.toISOString(), to: to.toISOString(), dayFrom: t0.toISOString(), dayTo: addDays(t0, 1).toISOString() });
    api<WorkshopReport>(`/api/v1/reports/workshop?${q}`).then((d) => alive && setR(d)).catch(() => alive && setR(null));
    return () => { alive = false; };
  }, [from.getTime(), to.getTime()]);
  if (!r) return null;
  const TYPE_NAMES: Record<string, string> = { geared: "دنده‌ای", automatic: "اتوماتیک" };
  const list = (rows: { title: string; count: number }[], empty: string) => rows.length === 0
    ? <p className="muted small">{empty}</p>
    : <div className="settings-list">{rows.map((x) => <div key={x.title} className="setting-row"><span><strong>{x.title}</strong></span><span className="font-num">{formatNumber(x.count)} بار</span></div>)}</div>;
  return (
    <>
      <h3>کارگاه امروز</h3>
      <div className="report-grid">
        <Stat label="پذیرش امروز" value={`${formatNumber(r.today.opened)} پرونده`} />
        <Stat label="تحویل امروز" value={`${formatNumber(r.today.delivered)} پرونده`} />
        {r.today.byStatus.map((s) => <Stat key={s.key} label={s.name} value={`${formatNumber(s.count)} پرونده`} />)}
      </div>

      <h3>عملکرد همکاران در این بازه</h3>
      {r.people.length === 0 ? <p className="muted small">در این بازه کاری تمام‌شده‌ای ثبت نشده است.</p> : (
        <div className="settings-list">
          {r.people.map((p) => (
            <div key={p.membershipId} className="setting-row">
              <span><strong>{p.name}</strong><span className="muted small">{ROLE_NAMES[p.role] ?? p.role}</span></span>
              <span className="font-num">{formatNumber(p.finished)} کار تمام‌شده{p.averageScore != null ? ` · رضایت ${new Intl.NumberFormat("fa-IR").format(p.averageScore)} از ۵ (${formatNumber(p.surveys)} نظر)` : ""}</span>
            </div>
          ))}
        </div>
      )}
      <p className="hint">«کار تمام‌شده» یعنی همکار پرونده را از «در حال تعمیر» برای بررسی یا تحویل فرستاده است.</p>

      <h3>پرتکرارترین ایرادها</h3>
      <h4>اعلام‌شده توسط مشتری</h4>
      {list(r.reportedProblems, "در این بازه ایرادی اعلام نشده است.")}
      <h4>پیدا‌شده در کارگاه</h4>
      {list(r.foundProblems, "هنوز ایرادی در پرونده‌ها ثبت نشده است.")}
      {r.motorTypes.length > 0 && <p className="hint">نوع موتور: {r.motorTypes.map((m) => `${TYPE_NAMES[m.type] ?? m.type} ${formatNumber(m.count)}`).join(" · ")}</p>}
    </>
  );
}

function Stat({ label, value, wide, tone }: { label: string; value: string; wide?: boolean; tone?: "warn" }) {
  return (
    <div className={`report-stat${wide ? " wide" : ""}${tone ? ` ${tone}` : ""}`}>
      <span className="muted small">{label}</span>
      <strong className="font-num">{value}</strong>
    </div>
  );
}

/** Customer satisfaction for the period: CSAT (share of 4–5 answers), response rate, each question, each person. */
function SurveyBlock({ r, onOpenCase }: { r: SurveyReport; onOpenCase: (id: string) => void }) {
  return (
    <>
      <h3>رضایت مشتری</h3>
      {r.responses === 0 ? (
        <p className="muted small">
          {r.sent > 0 ? `${formatNumber(r.sent)} نظرسنجی فرستاده شد؛ هنوز پاسخی نیامده است.` : "در این بازه نظری ثبت نشده است."}
        </p>
      ) : (
        <>
          <div className="report-grid">
            <Stat label="مشتریان راضی (۴ و ۵ ستاره)" value={`${formatNumber(r.satisfiedPercent ?? 0)}٪`} tone={(r.satisfiedPercent ?? 0) < 70 ? "warn" : undefined} />
            <Stat label="میانگین امتیاز" value={scoreText(r.average ?? 0)} />
            <Stat label="تعداد نظر" value={formatNumber(r.responses)} />
            <Stat label="نرخ پاسخ" value={r.responseRatePercent != null ? `${formatNumber(r.responseRatePercent)}٪ از ${formatNumber(r.sent)} پیامک` : "—"} />
          </div>
          <div className="settings-list">
            {r.questions.map((q) => (
              <div key={q.question} className="settings-row static">
                <span className="settings-row-text"><span>{q.question}</span><span className="muted small">{formatNumber(q.responses)} پاسخ</span></span>
                <span className="report-score"><StarRow rating={q.average} /><strong className="font-num">{scoreText(q.average)}</strong></span>
              </div>
            ))}
          </div>
          <h3>رضایت به تفکیک کارکنان</h3>
          <div className="settings-list">
            {r.staff.map((s) => (
              <div key={s.membershipId} className="settings-row static">
                <span className="settings-row-text">
                  <span>{s.name}</span>
                  <span className="muted small">{formatNumber(s.responses)} نظر، {formatNumber(s.satisfiedPercent ?? 0)}٪ راضی</span>
                </span>
                {s.average != null && <strong className="font-num">{scoreText(s.average)}</strong>}
              </div>
            ))}
          </div>
          <p className="hint">هر نظر برای مسئول پرونده و کسی که اجرت را انجام داده حساب می‌شود.</p>
          {r.low.length > 0 && (
            <>
              <h3>نظرهای پایین</h3>
              <div className="settings-list">
                {r.low.map((l) => (
                  <button type="button" key={l.id} className="settings-row" onClick={() => onOpenCase(l.id)}>
                    <span className="settings-row-text">
                      <span>{l.customer ?? "مشتری"}، {scoreText(l.score)}</span>
                      {l.comment && <span className="muted small">«{l.comment}»</span>}
                      <span className="muted small">{l.followed ? "پیگیری شد" : "پیگیری نشده"} · <span className="case-code" dir="ltr">{caseCode(l.number)}</span></span>
                    </span>
                    <span className="muted" aria-hidden="true">‹</span>
                  </button>
                ))}
              </div>
            </>
          )}
        </>
      )}
    </>
  );
}
