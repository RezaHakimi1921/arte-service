import { useEffect, useState } from "react";
import { api, ApiError, type Session } from "./api";
import { ROLE_NAMES } from "./labels";
import type { LicenseStatus } from "./license";
import { formatNumber } from "./ui";

type Branch = {
  id: string; name: string; brand: string | null; role: string; isActive: boolean; license: LicenseStatus;
  open: number; active: number; waiting: number; ready: number; unassigned: number | null;
  openedToday: number; deliveredToday: number; receivedTodayRials: number | null; creditRials: number | null; unread: number;
};

const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;

/**
 * «شعب شما»: for anyone in more than one branch, the first screen after sign-in (and reachable from the top bar).
 * Each branch shows its own numbers as this person sees them there; a tap enters that branch.
 */
export function BranchesHome({ name, currentId, onPick, onSignOut, onClose }: {
  name: string; currentId?: string | null; onPick: (s: Session) => void; onSignOut: () => void; onClose?: () => void;
}) {
  const [rows, setRows] = useState<Branch[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  useEffect(() => {
    api<Branch[]>("/api/v1/branches").then(setRows).catch((e) => setError(e instanceof ApiError ? e.message : "فهرست شعب بارگذاری نشد."));
  }, []);

  async function pick(b: Branch) {
    if (busy) return;
    if (b.id === currentId && onClose) { onClose(); return; }
    setBusy(b.id);
    try {
      onPick(await api<Session>("/api/v1/auth/select-tenant", { body: { tenantId: b.id } }));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "ورود به شعبه انجام نشد.");
      setBusy(null);
    }
  }

  const groups = rows ? [...new Set(rows.map((r) => r.brand ?? ""))] : [];
  return (
    <main className="branches">
      <header className="branches-head">
        <div>
          <span className="muted small">{name}</span>
          <h1>{rows && rows.some((r) => r.brand) ? "داشبورد برند" : "شعب شما"}</h1>
        </div>
        {onClose ? <button type="button" className="link" onClick={onClose}>بازگشت ‹</button>
          : <button type="button" className="link" onClick={onSignOut}>خروج</button>}
      </header>
      {error && <p className="error" role="alert">{error}</p>}
      {!rows && !error && <div className="splash" aria-busy="true" />}
      {groups.map((g) => (
        <section key={g} className="branch-group">
          {g && <h2 className="branch-brand">{g}</h2>}
          <BrandTotals rows={rows!.filter((r) => (r.brand ?? "") === g)} />
          {rows!.filter((r) => (r.brand ?? "") === g).map((b) => {
            const expired = b.license.state === "expired";
            return (
              <button key={b.id} type="button" className={`card branch-card${b.isActive ? "" : " inactive"}${b.id === currentId ? " current" : ""}`}
                onClick={() => pick(b)} disabled={!!busy} aria-busy={busy === b.id}>
                <span className="branch-top">
                  <strong>{b.name}</strong>
                  <span className="branch-badges">
                    {b.unread > 0 && <span className="badge danger">{formatNumber(b.unread)} اعلان</span>}
                    {!b.isActive ? <span className="badge danger">غیرفعال</span>
                      : expired ? <span className="badge danger">اشتراک تمام شده</span>
                      : b.license.state === "expiring" ? <span className="badge warn">{formatNumber(b.license.daysLeft)} روز مانده</span>
                      : b.license.kind === "trial" ? <span className="badge">رایگان · {formatNumber(b.license.daysLeft)} روز</span> : null}
                  </span>
                </span>
                <span className="muted small">{ROLE_NAMES[b.role] ?? b.role}{b.id === currentId ? " · شعبه‌ی فعلی" : ""}</span>
                <span className="branch-stats">
                  <Stat label={b.role === "technician" ? "کارهای من" : "باز"} value={b.open} />
                  <Stat label="در حال کار" value={b.active} />
                  <Stat label="منتظر" value={b.waiting} tone={b.waiting > 0 ? "warn" : undefined} />
                  <Stat label="آماده تحویل" value={b.ready} tone={b.ready > 0 ? "good" : undefined} />
                </span>
                <span className="branch-foot small">
                  <span>امروز: {formatNumber(b.openedToday)} پذیرش، {formatNumber(b.deliveredToday)} تحویل</span>
                  {b.unassigned != null && b.unassigned > 0 && <span className="warn-text">{formatNumber(b.unassigned)} بدون مسئول</span>}
                  {b.receivedTodayRials != null && <span>دریافتی امروز: <span className="font-num">{toman(b.receivedTodayRials)}</span></span>}
                  {b.creditRials != null && b.creditRials > 0 && <span className="warn-text">نسیه: <span className="font-num">{toman(b.creditRials)}</span></span>}
                </span>
                <span className="open-link small">{busy === b.id ? "در حال ورود…" : "ورود به این شعبه ‹"}</span>
              </button>
            );
          })}
        </section>
      ))}
      <p className="hint">برای افزودن شعبه‌ی جدید با پشتیبانی آرته تماس بگیرید.</p>
    </main>
  );
}

function Stat({ label, value, tone }: { label: string; value: number; tone?: "warn" | "good" }) {
  return (
    <span className={`branch-stat${tone ? ` ${tone}` : ""}`}>
      <strong className="font-num">{formatNumber(value)}</strong>
      <span>{label}</span>
    </span>
  );
}

/** The brand at a glance: all its branches added up (money only from branches where this person sees money). */
function BrandTotals({ rows }: { rows: Branch[] }) {
  if (rows.length < 2) return null;
  const sum = (f: (b: Branch) => number) => rows.reduce((n, b) => n + f(b), 0);
  const money = rows.filter((b) => b.receivedTodayRials != null);
  return (
    <div className="card brand-totals">
      <span className="muted small">جمع {formatNumber(rows.length)} شعبه</span>
      <span className="branch-stats">
        <Stat label="باز" value={sum((b) => b.open)} />
        <Stat label="در حال کار" value={sum((b) => b.active)} />
        <Stat label="منتظر" value={sum((b) => b.waiting)} tone={sum((b) => b.waiting) > 0 ? "warn" : undefined} />
        <Stat label="آماده تحویل" value={sum((b) => b.ready)} tone={sum((b) => b.ready) > 0 ? "good" : undefined} />
      </span>
      <span className="branch-foot small">
        <span>امروز: {formatNumber(sum((b) => b.openedToday))} پذیرش، {formatNumber(sum((b) => b.deliveredToday))} تحویل</span>
        {money.length > 0 && <span>دریافتی امروز: <span className="font-num">{toman(money.reduce((n, b) => n + (b.receivedTodayRials ?? 0), 0))}</span></span>}
        {money.some((b) => (b.creditRials ?? 0) > 0) && <span className="warn-text">نسیه: <span className="font-num">{toman(money.reduce((n, b) => n + (b.creditRials ?? 0), 0))}</span></span>}
      </span>
    </div>
  );
}
