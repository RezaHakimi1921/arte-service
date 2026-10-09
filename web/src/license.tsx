import { useEffect, useState } from "react";
import { ApiError, api } from "./api";
import { SubPage } from "./settings";
import { formatNumber } from "./ui";

export type LicenseStatus = { state: "active" | "expiring" | "expired"; daysLeft: number; endsAt: string | null; kind: string | null };
type Plan = { id: string; name: string; months: number; priceRials: number };
type LicenseView = {
  status: LicenseStatus; plans: Plan[]; supportPhone: string | null;
  history: { id: string; kind: string; startsAt: string; endsAt: string; plan: string | null }[];
};

export const jalaliDate = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long", year: "numeric" });
export const KIND_NAMES: Record<string, string> = { trial: "دوره‌ی رایگان", paid: "اشتراک خریداری‌شده", gift: "هدیه‌ی آرته" };
const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;

/** Plain words for the state; colour only backs them up. */
export function licenseHeadline(s: LicenseStatus) {
  if (s.state === "expired") return "اشتراک تمام شده است";
  if (s.kind === "trial") return s.state === "expiring" ? "دوره‌ی رایگان رو به پایان است" : "دوره‌ی رایگان";
  return s.state === "expiring" ? "اشتراک رو به پایان است" : "اشتراک فعال";
}

/** Settings → اشتراک: where the branch stands, the price list, and how to renew. */
export function LicensePage({ onBack }: { onBack: () => void }) {
  const [data, setData] = useState<LicenseView | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    api<LicenseView>("/api/v1/license").then(setData).catch((e) => setError(e instanceof ApiError ? e.message : "خطا"));
  }, []);

  const monthly = data?.plans.find((p) => p.months === 1)?.priceRials;
  return (
    <SubPage title="اشتراک" onBack={onBack}>
      {error && <p className="error" role="alert">{error}</p>}
      {!data && !error && <div className="splash" aria-busy="true" />}
      {data && (
        <>
          <div className={`card license-status ${data.status.state}`}>
            <span className="license-badge">{licenseHeadline(data.status)}</span>
            {data.status.state === "expired" ? (
              <p>
                پرونده‌های فعلی را می‌توانید ادامه دهید، تحویل بدهید و پرداخت ثبت کنید؛
                اما پذیرش جدید، مشتری جدید و همکار جدید تا تمدید بسته است.
              </p>
            ) : (
              <p className="license-days">
                <strong className="font-num">{formatNumber(data.status.daysLeft)}</strong> روز مانده
                {data.status.endsAt && <span className="muted"> · تا {jalaliDate.format(new Date(data.status.endsAt))}</span>}
              </p>
            )}
          </div>

          <h3>تمدید اشتراک</h3>
          <div className="settings-list">
            {data.plans.map((p) => {
              const perMonth = p.priceRials / p.months;
              const saving = monthly && p.months > 1 ? Math.round((1 - perMonth / monthly) * 100) : 0;
              return (
                <div key={p.id} className="settings-row static plan-row">
                  <span className="settings-row-text">
                    <span>{p.name}</span>
                    <span className="muted small">
                      ماهی {toman(perMonth)}{saving > 0 && <> · <span className="plan-saving">{formatNumber(saving)}٪ ارزان‌تر</span></>}
                    </span>
                  </span>
                  <strong className="font-num">{toman(p.priceRials)}</strong>
                </div>
              );
            })}
          </div>
          <div className="card renew-how">
            <p>
              پرداخت فعلاً دستی است: پلن را انتخاب کنید و با پشتیبانی آرته تماس بگیرید؛ بعد از پرداخت، اشتراک از پایان
              اشتراک فعلی تمدید می‌شود و روزی از دست نمی‌رود.
            </p>
            {data.supportPhone && (
              <a className="button primary block" href={`tel:${data.supportPhone}`}>
                تماس با پشتیبانی · <span dir="ltr" className="font-num">{data.supportPhone}</span>
              </a>
            )}
          </div>

          {data.history.length > 0 && (
            <>
              <h3>سابقه</h3>
              <div className="settings-list">
                {data.history.map((h) => (
                  <div key={h.id} className="settings-row static">
                    <span className="settings-row-text">
                      <span>{h.plan ?? KIND_NAMES[h.kind] ?? h.kind}</span>
                      <span className="muted small">{jalaliDate.format(new Date(h.startsAt))} تا {jalaliDate.format(new Date(h.endsAt))}</span>
                    </span>
                  </div>
                ))}
              </div>
            </>
          )}
        </>
      )}
    </SubPage>
  );
}

/** Thin bar under the top bar: only when the subscription ends within 3 days or has ended. */
export function LicenseBanner({ status, onOpen }: { status: LicenseStatus; onOpen?: () => void }) {
  if (status.state === "active") return null;
  const text = status.state === "expired"
    ? "اشتراک تمام شده؛ پرونده‌های فعلی باز است ولی پذیرش جدید تا تمدید بسته است."
    : `${formatNumber(status.daysLeft)} روز تا پایان ${status.kind === "trial" ? "دوره‌ی رایگان" : "اشتراک"} مانده است.`;
  return (
    <div className={`license-banner ${status.state}`} role="status">
      <span>{text}</span>
      {onOpen && <button className="link" onClick={onOpen}>تمدید</button>}
    </div>
  );
}

/** A branch the platform admin switched off: nothing works, so say so plainly and offer the way out. */
export function BranchBlocked({ name, onSignOut }: { name: string; onSignOut: () => void }) {
  async function signOut() {
    await api("/api/v1/auth/logout", { method: "POST" }).catch(() => {});
    onSignOut();
  }
  return (
    <main className="auth">
      <div className="card blocked">
        <h2>{name} غیرفعال است</h2>
        <p>دسترسی به این شعبه فعلاً بسته شده و اطلاعات آن محفوظ است. برای فعال‌سازی دوباره با پشتیبانی آرته تماس بگیرید.</p>
        <button onClick={signOut}>خروج از حساب</button>
      </div>
    </main>
  );
}
