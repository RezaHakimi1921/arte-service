import { FormEvent, useEffect, useState } from "react";
import { ApiError, api } from "./api";
import { ROLE_NAMES, caseCode } from "./labels";
import { useFeedback } from "./feedback";
import { BottomSheet } from "./sheet";
import { Field, NumberInput, formatNumber } from "./ui";

export type StaffPayRow = {
  membershipId: string; name: string; role: string;
  employmentStartedOn: string; employmentEndedOn: string | null;
  cases: number; baseRials: number; commissionRials: number;
  commissionType: string; commissionPercent: number | null; commissionFixedRials: number | null; commissionBase: string;
  fixedSalaryRials: number; bonusRials: number; deductionRials: number;
  incomeRials: number; afterDeductionsRials: number;
  shopLossCount: number; shopLossRials: number;
  bonuses: { id: string; title: string; amountRials: number; jalaliYear: number; jalaliMonth: number; voidedAt: string | null }[];
  deductions: { id: string; category: string; amountRials: number; reason: string; jalaliYear: number; jalaliMonth: number; voidedAt: string | null }[];
  commissionCases: { caseId: string; caseNumber: number; grantedAt: string; baseRials: number; commissionRials: number }[];
  settlementsInRange: { id: string; jalaliYear: number; jalaliMonth: number; afterDeductionsRials: number; confirmedAt: string }[];
};

type PayReport = { staff: StaffPayRow[] };

const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;
const monthLabel = (y: number, m: number) => `${formatNumber(m)} / ${formatNumber(y)}`;

const DEDUCTION_LABELS: Record<string, string> = {
  advance: "علی‌الحساب",
  financial_adjustment: "اصلاح مالی",
  loss_linked: "مرتبط با زیان",
};

const jalaliParts = new Intl.DateTimeFormat("en-u-ca-persian-nu-latn", { year: "numeric", month: "numeric", day: "numeric" });
function partsOf(d: Date) {
  const bits = Object.fromEntries(jalaliParts.formatToParts(d).filter((p) => p.type !== "literal").map((p) => [p.type, Number(p.value)]));
  return { year: bits.year, month: bits.month, day: bits.day };
}

/** Staff pay block inside Reports: loads /reports/staff-pay, expandable rows, optional manage actions. */
export function StaffPayBlock({ from, to, canManage, onOpenCase }: {
  from: Date; to: Date; canManage: boolean; onOpenCase: (id: string) => void;
}) {
  const [data, setData] = useState<PayReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    let alive = true;
    setData(null);
    setError(null);
    api<PayReport>(`/api/v1/reports/staff-pay?from=${encodeURIComponent(from.toISOString())}&to=${encodeURIComponent(to.toISOString())}`)
      .then((d) => alive && setData(d))
      .catch((err) => alive && setError(err instanceof ApiError ? err.message : "خطا"));
    return () => { alive = false; };
  }, [from.getTime(), to.getTime(), reload]);

  if (error) {
    return (
      <>
        <h3>دستمزد کارکنان</h3>
        <p className="error" role="alert">{error}</p>
        <button type="button" onClick={() => setReload((n) => n + 1)}>تلاش دوباره</button>
      </>
    );
  }
  if (!data) return <div className="splash" aria-busy="true" />;

  const rows = data.staff.filter((s) =>
    s.cases > 0 || s.fixedSalaryRials > 0 || s.bonusRials > 0 || s.deductionRials > 0
    || s.shopLossCount > 0 || s.commissionType !== "none" || s.settlementsInRange.length > 0);

  return (
    <>
      <h3>دستمزد کارکنان</h3>
      {rows.length === 0 ? (
        <p className="muted small">در این بازه دستمزد، پورسانت، پاداش یا زیانی برای کارکنان ثبت نشده است.</p>
      ) : (
        <div className="settings-list">
          {rows.map((s) => {
            const open = openId === s.membershipId;
            return (
              <div key={s.membershipId}>
                <button type="button" className="settings-row report-pay" aria-expanded={open}
                  onClick={() => setOpenId(open ? null : s.membershipId)}>
                  <span className="settings-row-text">
                    <span>{s.name}</span>
                    <span className="muted small">{ROLE_NAMES[s.role] ?? s.role} · {formatNumber(s.cases)} پرونده</span>
                    <span className="muted small">
                      حقوق <span className="font-num">{toman(s.fixedSalaryRials)}</span>
                      {" · "}پورسانت <span className="font-num">{toman(s.commissionRials)}</span>
                      {s.bonusRials > 0 && <> · پاداش <span className="font-num">{toman(s.bonusRials)}</span></>}
                      {s.deductionRials > 0 && <> · کسر <span className="font-num">{toman(s.deductionRials)}</span></>}
                    </span>
                    {s.shopLossRials > 0 && (
                      <span className="muted small">زیان داخلی ثبت‌شده: <span className="font-num">{toman(s.shopLossRials)}</span> ({formatNumber(s.shopLossCount)} مورد) — از درآمد کسر نشده</span>
                    )}
                    {s.settlementsInRange.length > 0 && (
                      <span className="muted small">پرداخت تأییدشده: {s.settlementsInRange.map((x) => monthLabel(x.jalaliYear, x.jalaliMonth)).join("، ")}</span>
                    )}
                  </span>
                  <strong className="font-num">{toman(s.afterDeductionsRials)}</strong>
                </button>
                {open && (
                  <StaffPayDetail row={s} canManage={canManage} onOpenCase={onOpenCase}
                    onChanged={() => setReload((n) => n + 1)} />
                )}
              </div>
            );
          })}
        </div>
      )}
      <p className="hint">جمع نمایش‌داده‌شده = درآمد محاسبه‌شده (حقوق + پورسانت + پاداش) منهای کسورات دستی. زیان داخلی جداست و کسر نشده. تا تأیید پرداخت، «پرداخت‌شده» نیست.</p>
    </>
  );
}

function StaffPayDetail({ row, canManage, onOpenCase, onChanged }: {
  row: StaffPayRow; canManage: boolean; onOpenCase: (id: string) => void; onChanged: () => void;
}) {
  const { notify } = useFeedback();
  const [sheet, setSheet] = useState<"bonus" | "deduction" | "settle" | "void" | null>(null);
  const [voidTarget, setVoidTarget] = useState<{ kind: "bonus" | "deduction"; id: string } | null>(null);

  return (
    <div className="staff-pay-detail">
      <div className="report-grid">
        <Stat label="حقوق ثابت (سهم بازه)" value={toman(row.fixedSalaryRials)} />
        <Stat label="پورسانت" value={toman(row.commissionRials)} />
        <Stat label="پاداش" value={toman(row.bonusRials)} />
        <Stat label="کسورات دستی" value={toman(row.deductionRials)} />
        <Stat label="درآمد محاسبه‌شده" value={toman(row.incomeRials)} wide />
        <Stat label="مبلغ پس از کسورات" value={toman(row.afterDeductionsRials)} wide />
        {row.shopLossRials > 0 && <Stat label="زیان داخلی (جدا)" value={toman(row.shopLossRials)} wide />}
      </div>

      {row.commissionCases.length > 0 && (
        <>
          <h4>پرونده‌های پورسانت</h4>
          <div className="settings-list">
            {row.commissionCases.map((c) => (
              <button type="button" key={c.caseId} className="settings-row" onClick={() => onOpenCase(c.caseId)}>
                <span className="settings-row-text">
                  <span className="case-code" dir="ltr">{caseCode(c.caseNumber)}</span>
                  <span className="muted small">{new Intl.DateTimeFormat("fa-IR-u-ca-persian", { dateStyle: "medium" }).format(new Date(c.grantedAt))}</span>
                </span>
                <strong className="font-num">{toman(c.commissionRials)}</strong>
              </button>
            ))}
          </div>
        </>
      )}

      {row.bonuses.length > 0 && (
        <>
          <h4>پاداش‌ها</h4>
          <div className="settings-list">
            {row.bonuses.map((b) => (
              <div key={b.id} className="settings-row static">
                <span className="settings-row-text">
                  <span>{b.title}{b.voidedAt ? " (باطل)" : ""}</span>
                  <span className="muted small">{monthLabel(b.jalaliYear, b.jalaliMonth)}</span>
                </span>
                <span className="font-num">{toman(b.amountRials)}</span>
                {canManage && !b.voidedAt && (
                  <button type="button" className="link" onClick={() => { setVoidTarget({ kind: "bonus", id: b.id }); setSheet("void"); }}>ابطال</button>
                )}
              </div>
            ))}
          </div>
        </>
      )}

      {row.deductions.length > 0 && (
        <>
          <h4>کسورات</h4>
          <div className="settings-list">
            {row.deductions.map((d) => (
              <div key={d.id} className="settings-row static">
                <span className="settings-row-text">
                  <span>{DEDUCTION_LABELS[d.category] ?? d.category}{d.voidedAt ? " (باطل)" : ""}</span>
                  <span className="muted small">{d.reason} · {monthLabel(d.jalaliYear, d.jalaliMonth)}</span>
                </span>
                <span className="font-num">{toman(d.amountRials)}</span>
                {canManage && !d.voidedAt && (
                  <button type="button" className="link" onClick={() => { setVoidTarget({ kind: "deduction", id: d.id }); setSheet("void"); }}>ابطال</button>
                )}
              </div>
            ))}
          </div>
        </>
      )}

      {canManage && (
        <div className="row-actions">
          <button type="button" onClick={() => setSheet("bonus")}>ثبت پاداش</button>
          <button type="button" onClick={() => setSheet("deduction")}>ثبت کسر</button>
          <button type="button" className="primary" onClick={() => setSheet("settle")}>تأیید پرداخت ماه</button>
        </div>
      )}

      <BonusSheet open={sheet === "bonus"} membershipId={row.membershipId} onClose={() => setSheet(null)}
        onSaved={() => { setSheet(null); notify("پاداش ثبت شد"); onChanged(); }} />
      <DeductionSheet open={sheet === "deduction"} membershipId={row.membershipId} onClose={() => setSheet(null)}
        onSaved={() => { setSheet(null); notify("کسر ثبت شد"); onChanged(); }} />
      <SettleSheet open={sheet === "settle"} membershipId={row.membershipId} onClose={() => setSheet(null)}
        onSaved={() => { setSheet(null); notify("پرداخت تأیید و قفل شد"); onChanged(); }} />
      <VoidSheet open={sheet === "void" && !!voidTarget} target={voidTarget} onClose={() => { setSheet(null); setVoidTarget(null); }}
        onSaved={() => { setSheet(null); setVoidTarget(null); notify("باطل شد"); onChanged(); }} />
    </div>
  );
}

function Stat({ label, value, wide }: { label: string; value: string; wide?: boolean }) {
  return (
    <div className={`report-stat${wide ? " wide" : ""}`}>
      <span className="muted small">{label}</span>
      <strong className="font-num">{value}</strong>
    </div>
  );
}

function MonthFields({ year, month, setYear, setMonth }: {
  year: string; month: string; setYear: (v: string) => void; setMonth: (v: string) => void;
}) {
  const now = partsOf(new Date());
  return (
    <div className="row-2">
      <Field label="سال شمسی">
        <input inputMode="numeric" dir="ltr" className="font-num" value={year} maxLength={4}
          onChange={(e) => setYear(e.target.value.replace(/\D/g, "").slice(0, 4))} placeholder={String(now.year)} />
      </Field>
      <Field label="ماه شمسی">
        <input inputMode="numeric" dir="ltr" className="font-num" value={month} maxLength={2}
          onChange={(e) => setMonth(e.target.value.replace(/\D/g, "").slice(0, 2))} placeholder={String(now.month)} />
      </Field>
    </div>
  );
}

function BonusSheet({ open, membershipId, onClose, onSaved }: {
  open: boolean; membershipId: string; onClose: () => void; onSaved: () => void;
}) {
  const now = partsOf(new Date());
  const [title, setTitle] = useState("");
  const [amount, setAmount] = useState("");
  const [year, setYear] = useState(String(now.year));
  const [month, setMonth] = useState(String(now.month));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!open) return;
    setTitle(""); setAmount(""); setYear(String(now.year)); setMonth(String(now.month)); setError(null);
  }, [open]);

  async function save(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    setBusy(true); setError(null);
    try {
      await api("/api/v1/reports/staff-pay/bonuses", {
        method: "POST",
        body: {
          membershipId, title: title.trim(), amountRials: Number(amount || 0) * 10,
          jalaliYear: Number(year), jalaliMonth: Number(month),
        },
      });
      onSaved();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    } finally {
      setBusy(false);
    }
  }

  return (
    <BottomSheet open={open} title="ثبت پاداش" onClose={onClose}>
      <form onSubmit={save} noValidate>
        <Field label="عنوان / علت"><input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={200} required /></Field>
        <Field label="مبلغ"><NumberInput value={amount} onChange={setAmount} max={11} suffix="تومان" /></Field>
        <MonthFields year={year} month={month} setYear={setYear} setMonth={setMonth} />
        {error && <p className="error" role="alert">{error}</p>}
        <button className="primary block" disabled={busy} aria-busy={busy}>{busy ? "در حال ثبت…" : "ثبت پاداش"}</button>
      </form>
    </BottomSheet>
  );
}

function DeductionSheet({ open, membershipId, onClose, onSaved }: {
  open: boolean; membershipId: string; onClose: () => void; onSaved: () => void;
}) {
  const now = partsOf(new Date());
  const [category, setCategory] = useState("advance");
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const [year, setYear] = useState(String(now.year));
  const [month, setMonth] = useState(String(now.month));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!open) return;
    setCategory("advance"); setAmount(""); setReason(""); setYear(String(now.year)); setMonth(String(now.month)); setError(null);
  }, [open]);

  async function save(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    setBusy(true); setError(null);
    try {
      await api("/api/v1/reports/staff-pay/deductions", {
        method: "POST",
        body: {
          membershipId, category, reason: reason.trim(), amountRials: Number(amount || 0) * 10,
          jalaliYear: Number(year), jalaliMonth: Number(month),
        },
      });
      onSaved();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    } finally {
      setBusy(false);
    }
  }

  return (
    <BottomSheet open={open} title="ثبت کسر دستی" onClose={onClose}>
      <form onSubmit={save} noValidate>
        <span className="label">دسته</span>
        <div className="segmented wide" role="radiogroup" aria-label="دسته کسر">
          {Object.entries(DEDUCTION_LABELS).map(([v, l]) => (
            <button type="button" key={v} role="radio" aria-checked={category === v} className={category === v ? "on" : ""} onClick={() => setCategory(v)}>{l}</button>
          ))}
        </div>
        <Field label="مبلغ"><NumberInput value={amount} onChange={setAmount} max={11} suffix="تومان" /></Field>
        <Field label="شرح / علت"><input value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} required /></Field>
        <MonthFields year={year} month={month} setYear={setYear} setMonth={setMonth} />
        <p className="hint">کسر نمی‌تواند از درآمد همان ماه بیشتر شود. زیان ثبت‌شده به‌تنهایی کسر نمی‌سازد.</p>
        {error && <p className="error" role="alert">{error}</p>}
        <button className="primary block" disabled={busy} aria-busy={busy}>{busy ? "در حال ثبت…" : "ثبت کسر"}</button>
      </form>
    </BottomSheet>
  );
}

function SettleSheet({ open, membershipId, onClose, onSaved }: {
  open: boolean; membershipId: string; onClose: () => void; onSaved: () => void;
}) {
  const now = partsOf(new Date());
  const [year, setYear] = useState(String(now.year));
  const [month, setMonth] = useState(String(now.month));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!open) return;
    setYear(String(now.year)); setMonth(String(now.month)); setError(null);
  }, [open]);

  async function save(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    setBusy(true); setError(null);
    try {
      await api("/api/v1/reports/staff-pay/settlements", {
        method: "POST",
        body: { membershipId, jalaliYear: Number(year), jalaliMonth: Number(month) },
      });
      onSaved();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    } finally {
      setBusy(false);
    }
  }

  return (
    <BottomSheet open={open} title="تأیید پرداخت ماه" onClose={onClose}>
      <form onSubmit={save} noValidate>
        <p className="hint">پرداخت فقط کامل و یک‌مرحله‌ای است. بعد از تأیید، مبلغ و اجزای همان ماه قفل می‌شوند.</p>
        <MonthFields year={year} month={month} setYear={setYear} setMonth={setMonth} />
        {error && <p className="error" role="alert">{error}</p>}
        <button className="primary block" disabled={busy} aria-busy={busy}>{busy ? "در حال تأیید…" : "تأیید و قفل پرداخت"}</button>
      </form>
    </BottomSheet>
  );
}

function VoidSheet({ open, target, onClose, onSaved }: {
  open: boolean; target: { kind: "bonus" | "deduction"; id: string } | null; onClose: () => void; onSaved: () => void;
}) {
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => { if (open) { setReason(""); setError(null); } }, [open]);

  async function save(e: FormEvent) {
    e.preventDefault();
    if (!target || busy) return;
    setBusy(true); setError(null);
    try {
      const path = target.kind === "bonus"
        ? `/api/v1/reports/staff-pay/bonuses/${target.id}/void`
        : `/api/v1/reports/staff-pay/deductions/${target.id}/void`;
      await api(path, { method: "POST", body: { reason: reason.trim() } });
      onSaved();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    } finally {
      setBusy(false);
    }
  }

  return (
    <BottomSheet open={open} title="ابطال" onClose={onClose}>
      <form onSubmit={save} noValidate>
        <p className="muted small">حذف فیزیکی نیست؛ با علت در سابقه می‌ماند و از جمع خارج می‌شود.</p>
        <Field label="علت ابطال"><input value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} required /></Field>
        {error && <p className="error" role="alert">{error}</p>}
        <button className="primary block" disabled={busy} aria-busy={busy}>{busy ? "در حال ابطال…" : "ابطال"}</button>
      </form>
    </BottomSheet>
  );
}
