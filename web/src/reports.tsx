import { useEffect, useState } from "react";
import { ApiError, api } from "./api";
import { ROLE_NAMES } from "./labels";
import { SubPage } from "./settings";
import { formatNumber } from "./ui";

type StaffPay = {
  membershipId: string; name: string; role: string; cases: number; baseRials: number; commissionRials: number;
  commissionType: string; fixedMonthlyRials: number;
};
type Summary = {
  opened: number; delivered: number; salesRials: number; partsRials: number; workRials: number; partsProfitRials: number;
  receivedRials: number; receivablesRials: number; staff: StaffPay[];
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
  { key: "today", label: "امروز", range: (now: Date) => [midnight(now), addDays(midnight(now), 1)] },
  // The Iranian week starts on Saturday (getDay() === 6).
  { key: "week", label: "این هفته", range: (now: Date) => [addDays(midnight(now), -((now.getDay() + 1) % 7)), addDays(midnight(now), 1)] },
  { key: "month", label: "این ماه", range: (now: Date) => [jalaliMonthStart(now), addDays(midnight(now), 1)] },
  { key: "last", label: "ماه قبل", range: (now: Date) => { const start = jalaliMonthStart(now); return [jalaliMonthStart(addDays(start, -1)), start]; } },
] as const;

const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;
const monthName = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { month: "long", year: "numeric" });

/** A few fixed numbers for the owner; no builder, no charts. Delivered cases drive sales and staff pay. */
export function ReportsPage({ onBack }: { onBack: () => void }) {
  const [period, setPeriod] = useState<(typeof PERIODS)[number]["key"]>("month");
  const [data, setData] = useState<Summary | null>(null);
  const [error, setError] = useState<string | null>(null);
  const now = new Date();
  const [from, to] = PERIODS.find((p) => p.key === period)!.range(now);

  useEffect(() => {
    let alive = true;
    setData(null);
    setError(null);
    api<Summary>(`/api/v1/reports/summary?from=${encodeURIComponent(from.toISOString())}&to=${encodeURIComponent(to.toISOString())}`)
      .then((d) => alive && setData(d))
      .catch((err) => alive && setError(err instanceof ApiError ? err.message : "خطا"));
    return () => { alive = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [period]);

  const subtitle = period === "month" || period === "last" ? monthName.format(from) : null;
  const payable = data?.staff.filter((s) => s.cases > 0 || s.fixedMonthlyRials > 0 || s.commissionType !== "none") ?? [];

  return (
    <SubPage title="گزارش‌ها" onBack={onBack}>
      <div className="segmented wide" role="radiogroup" aria-label="بازه">
        {PERIODS.map((p) => (
          <button type="button" key={p.key} role="radio" aria-checked={period === p.key} className={period === p.key ? "on" : ""} onClick={() => setPeriod(p.key)}>{p.label}</button>
        ))}
      </div>
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
            <Stat label="سود قطعه" value={toman(data.partsProfitRials)} />
            <Stat label="اجرت و خدمات" value={toman(data.workRials)} />
            <Stat label="فروش قطعه" value={toman(data.partsRials)} />
            <Stat label="طلب از مشتریان (الان)" value={toman(data.receivablesRials)} wide tone={data.receivablesRials > 0 ? "warn" : undefined} />
          </div>

          <h3>دستمزد کارکنان</h3>
          {payable.length === 0 ? (
            <p className="muted small">برای کارکنان حقوق یا پورسانت تعریف نشده است. از «کارکنان و دسترسی‌ها» تنظیم کنید.</p>
          ) : (
            <div className="settings-list">
              {payable.map((s) => (
                <div key={s.membershipId} className="settings-row static report-pay">
                  <span className="settings-row-text">
                    <span>{s.name}</span>
                    <span className="muted small">{ROLE_NAMES[s.role] ?? s.role} · {formatNumber(s.cases)} پرونده تحویل‌شده</span>
                    <span className="muted small">
                      پورسانت <span className="font-num">{toman(s.commissionRials)}</span>
                      {s.fixedMonthlyRials > 0 && <> · حقوق ماهانه <span className="font-num">{toman(s.fixedMonthlyRials)}</span></>}
                    </span>
                  </span>
                  <strong className="font-num">{toman(s.commissionRials + (period === "today" || period === "week" ? 0 : s.fixedMonthlyRials))}</strong>
                </div>
              ))}
            </div>
          )}
          {payable.some((s) => s.fixedMonthlyRials > 0) && (
            <p className="hint">{period === "today" || period === "week" ? "در بازه روزانه و هفتگی فقط پورسانت در جمع آمده است." : "جمع = پورسانت این بازه + حقوق ثابت یک ماه."}</p>
          )}
        </>
      )}
    </SubPage>
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
