import { useEffect, useState } from "react";
import { ApiError, api } from "./api";
import { WAIT_REASONS } from "./labels";
import { formatNumber } from "./ui";

type Track = {
  shop: { name: string; phone: string | null; address: string | null };
  case: {
    number: number; customer: string | null; openedAt: string; promisedAt: string | null; closedAt: string | null; warrantyUntil: string | null;
    vehicle: { title: string; kind: string; identifier: string | null } | null; reportedProblems: string[]; requestedServices: string[];
  };
  status: { name: string; key: string; category: string; waitReason: string | null; stageEnteredAt: string };
  stages: { name: string; key: string; state: "done" | "current" | "todo"; at: string | null }[] | null;
  items: { kind: string; title: string; quantity: number; status: string; supplier: string; lineTotalRials: number | null }[] | null;
  money: { totalRials: number; paidRials: number; balanceRials: number } | null;
};

const dateTime = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { weekday: "long", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" });
const date = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long", year: "numeric" });
const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;
const KIND: Record<string, string> = { part: "قطعه", labor: "اجرت", service: "خدمت" };

/** One plain sentence for where the vehicle is now. */
function headline(t: Track) {
  const s = t.status;
  if (s.key === "delivered") return "وسیله‌ی شما تحویل شد.";
  if (s.category === "cancelled") return "این پرونده لغو شده است.";
  if (s.key === "ready") return "وسیله‌ی شما آماده‌ی تحویل است.";
  if (s.waitReason) return `کار موقتاً متوقف است: ${WAIT_REASONS[s.waitReason] ?? s.name}`;
  return `وضعیت فعلی: ${s.name}`;
}

/** The customer's page (/t/{code}): no sign-in, read-only, what the shop chose to share. */
export function TrackPage({ code }: { code: string }) {
  const [t, setT] = useState<Track | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    document.title = "پیگیری پرونده";
    api<Track>(`/api/v1/track/${encodeURIComponent(code)}`)
      .then((d) => { setT(d); document.title = `پیگیری پرونده · ${d.shop.name}`; })
      .catch((e) => setError(e instanceof ApiError && e.status === 404 ? "این لینک معتبر نیست یا پرونده دیگر در دسترس نیست." : "ارتباط برقرار نشد. کمی بعد دوباره امتحان کنید."));
  }, [code]);

  if (error) return <main className="track"><div className="card track-error"><h1>پیگیری پرونده</h1><p>{error}</p></div></main>;
  if (!t) return <div className="splash" aria-busy="true" />;
  const done = t.status.key === "delivered" || t.status.key === "ready";
  return (
    <main className="track">
      <header className="track-shop">
        <div>
          <h1>{t.shop.name}</h1>
          {t.shop.address && <p className="muted small">{t.shop.address}</p>}
        </div>
        {t.shop.phone && <a className="track-call" href={`tel:${t.shop.phone}`}>تماس</a>}
      </header>

      <section className={`card track-status${done ? " done" : ""}`} aria-live="polite">
        <span className="muted small">{t.case.customer ? `${t.case.customer} عزیز` : "مشتری گرامی"}</span>
        <h2>{headline(t)}</h2>
        {t.case.vehicle && (
          <p>
            {t.case.vehicle.title}
            {t.case.vehicle.identifier && <> · <bdi dir="ltr" className="font-num">{t.case.vehicle.identifier}</bdi></>}
          </p>
        )}
        <p className="muted small">
          پرونده <bdi dir="ltr">CASE-{t.case.number}</bdi> · پذیرش {date.format(new Date(t.case.openedAt))}
        </p>
        {t.case.promisedAt && t.status.key !== "delivered" && (
          <p className="track-promise">قول تحویل: <strong>{dateTime.format(new Date(t.case.promisedAt))}</strong></p>
        )}
        {t.case.warrantyUntil && <p className="muted small">ضمانت تا {date.format(new Date(t.case.warrantyUntil))}</p>}
      </section>

      {t.stages && (
        <section className="card">
          <h3>مراحل کار</h3>
          <ol className="track-steps">
            {t.stages.map((s) => (
              <li key={s.key} className={`${s.state}${s.key === "delivered" ? " final" : ""}`}>
                <span className="track-dot" aria-hidden="true" />
                <span className="track-step-name">{s.name}{s.state === "current" && <span className="sr-only"> (مرحله‌ی فعلی)</span>}</span>
                {s.at && s.state !== "todo" && <span className="muted small">{dateTime.format(new Date(s.at))}</span>}
              </li>
            ))}
          </ol>
        </section>
      )}

      {(t.case.reportedProblems.length > 0 || t.case.requestedServices.length > 0) && (
        <section className="card">
          <h3>درخواست شما</h3>
          {t.case.reportedProblems.length > 0 && <p><span className="muted">ایراد: </span>{t.case.reportedProblems.join("، ")}</p>}
          {t.case.requestedServices.length > 0 && <p><span className="muted">خدمات: </span>{t.case.requestedServices.join("، ")}</p>}
        </section>
      )}

      {t.items && t.items.length > 0 && (
        <section className="card">
          <h3>قطعات و کارها</h3>
          <ul className="track-items">
            {t.items.map((i, n) => (
              <li key={n}>
                <span>
                  <span className="track-kind">{KIND[i.kind] ?? i.kind}</span> {i.title}
                  {i.quantity !== 1 && <span className="muted small"> × {formatNumber(i.quantity)}</span>}
                  {i.supplier === "customer" && <span className="muted small"> (قطعه‌ی خودتان)</span>}
                  {i.status === "needed" && <span className="muted small"> (در انتظار تهیه)</span>}
                </span>
                {i.lineTotalRials != null && i.lineTotalRials > 0 && <strong className="font-num">{toman(i.lineTotalRials)}</strong>}
              </li>
            ))}
          </ul>
        </section>
      )}

      {t.money && t.money.totalRials > 0 && (
        <section className="card track-money">
          <p><span>جمع</span><strong className="font-num">{toman(t.money.totalRials)}</strong></p>
          <p><span>پرداخت‌شده</span><strong className="font-num">{toman(t.money.paidRials)}</strong></p>
          <p className={t.money.balanceRials > 0 ? "owe" : ""}><span>مانده</span><strong className="font-num">{toman(t.money.balanceRials)}</strong></p>
        </section>
      )}

      <footer className="track-foot muted small">این صفحه با آرته سرویس ساخته شده و هر بار که باز شود به‌روز است.</footer>
    </main>
  );
}
