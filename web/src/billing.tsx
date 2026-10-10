import { useEffect, useState, type FormEvent } from "react";
import { api, ApiError } from "./api";
import { useFeedback } from "./feedback";
import { BottomSheet, SheetOption } from "./sheet";
import { Field, NumberInput, formatNumber, toLatinDigits } from "./ui";

/* ───────── types ───────── */

export type Item = {
  id: string; kind: string; title: string; quantity: number; unitPriceRials: number; unitCostRials: number | null;
  discountRials: number; supplier: string; status: string; performedBy: string | null; performedByName: string | null;
  warrantyDays: number | null; lineTotalRials: number; profitRials: number | null;
};
export type PaymentRow = { id: string; amountRials: number; method: string; paidAt: string; note: string | null };
export type Billing = {
  items: Item[]; payments: PaymentRow[];
  money: { totalRials: number; paidRials: number; balanceRials: number; partsRials: number; laborRials: number; servicesRials: number; costRials: number | null; profitRials: number | null };
  canEditItems: boolean; canRecordPayments: boolean; canSeeCost: boolean;
};
type CatalogRow = { id: string; kind: string; title: string; defaultPriceRials: number; defaultCostRials: number | null; defaultWarrantyDays: number | null; isActive: boolean };
type Assignable = { id: string; name: string; role: string };

/** Two kinds for the user: goods used, and work (labor and services are one; old «service» rows show as work). */
export const KIND_LABELS: Record<string, string> = { part: "کالای مصرف‌شده", labor: "اجرت و خدمات", service: "اجرت و خدمات" };
const OFFERED_KINDS: [string, string][] = [["part", "کالای مصرف‌شده"], ["labor", "اجرت و خدمات"]];
export const METHOD_LABELS: Record<string, string> = { cash: "نقد", card: "کارت‌خوان", transfer: "کارت به کارت", other: "سایر" };
const WARRANTY_OPTIONS = [0, 30, 90, 180, 365];

/** Amounts: API in rials, people think in toman. Always grouped by three. */
export const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;
const toRials = (tomanDigits: string) => (tomanDigits ? Number(tomanDigits) * 10 : 0);
const toTomanDigits = (rials: number | null | undefined) => (rials ? String(Math.round(rials / 10)) : "");

const faDate = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { month: "long", day: "numeric", hour: "2-digit", minute: "2-digit" });

/* ───────── money bar ───────── */

export function MoneyBar({ money }: { money: Billing["money"] }) {
  if (money.totalRials === 0 && money.paidRials === 0) return null;
  const settled = money.balanceRials <= 0;
  return (
    <div className="money-bar" aria-label="وضعیت مالی">
      {/* Unit in the label keeps each amount on one line on a phone. */}
      <div><span className="muted small">جمع (تومان)</span><strong className="font-num">{formatNumber(Math.round(money.totalRials / 10))}</strong></div>
      <div><span className="muted small">پرداخت‌شده</span><strong className="font-num">{formatNumber(Math.round(money.paidRials / 10))}</strong></div>
      <div className={settled ? "good" : "due"}>
        <span className="muted small">{money.balanceRials < 0 ? "بستانکار" : "مانده"}</span>
        <strong className="font-num">{settled && money.balanceRials === 0 ? "تسویه" : formatNumber(Math.round(Math.abs(money.balanceRials) / 10))}</strong>
      </div>
    </div>
  );
}

/* ───────── items + payments section ───────── */

export function BillingSection({ caseId, billing, onChange, canAssignLabor, paySignal }: {
  caseId: string; billing: Billing; onChange: (b: Billing) => void; canAssignLabor: boolean; paySignal: number;
}) {
  const { notify } = useFeedback();
  const [editing, setEditing] = useState<{ kind: string; item?: Item } | null>(null);
  const [paying, setPaying] = useState(false);
  const [busy, setBusy] = useState(false);

  async function run(work: () => Promise<Billing>, ok: string, undo?: () => Promise<Billing>) {
    if (busy) return;
    setBusy(true);
    try {
      onChange(await work());
      notify(ok);
      if (undo) setLastUndo(() => undo);
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "خطا", "error");
    } finally {
      setBusy(false);
    }
  }
  const [lastUndo, setLastUndo] = useState<(() => Promise<Billing>) | null>(null);
  // Delivery asked for a payment first: open the payment sheet.
  useEffect(() => { if (paySignal > 0 && billing.canRecordPayments) setPaying(true); }, [paySignal, billing.canRecordPayments]);

  const m = billing.money;
  return (
    <>
      <details className="card collapsible" open data-tour="billing">
        <summary>کالا و اجرت {billing.items.length > 0 && <span className="count font-num">{formatNumber(billing.items.length)}</span>}</summary>
        <div className="collapsible-body">
          {billing.items.length === 0 && <p className="muted">هنوز کالا یا اجرتی ثبت نشده. هر کالایی که مصرف شد و هر کاری که انجام شد را این‌جا ثبت کنید تا در صورت‌حساب فراموش نشود.</p>}
          <ul className="items">
            {billing.items.map((i) => (
              <li key={i.id}>
                <button className="item-row" onClick={() => setEditing({ kind: i.kind, item: i })} disabled={!billing.canEditItems}>
                  <span className="item-top">
                    <span><span className={`kind-badge ${i.kind}`}>{KIND_LABELS[i.kind]}</span> {i.title}</span>
                    <strong className="font-num">{i.supplier === "customer" ? "کالای مشتری" : i.status === "needed" ? "—" : toman(i.lineTotalRials)}</strong>
                  </span>
                  <span className="muted small">
                    {formatNumber(i.quantity)} × {toman(i.unitPriceRials)}
                    {i.discountRials > 0 && ` · تخفیف ${toman(i.discountRials)}`}
                    {i.status === "needed" && " · هنوز نصب نشده"}
                    {i.performedByName && ` · ${i.performedByName}`}
                    {!!i.warrantyDays && ` · ضمانت ${formatNumber(i.warrantyDays)} روز`}
                  </span>
                  {billing.canSeeCost && i.unitCostRials != null && (
                    <span className="small cost-line">خرید {toman(i.unitCostRials)} · سود {toman(i.profitRials ?? 0)}</span>
                  )}
                </button>
              </li>
            ))}
          </ul>
          {lastUndo && (
            <button className="link" onClick={() => { const u = lastUndo; setLastUndo(null); run(u, "بازگردانی شد"); }}>بازگردانی ردیف حذف‌شده</button>
          )}
          {billing.canEditItems && (
            <div className="quick-actions">
              <button onClick={() => setEditing({ kind: "part" })}>+ کالای مصرف‌شده</button>
              <button onClick={() => setEditing({ kind: "labor" })}>+ اجرت و خدمات</button>
            </div>
          )}
          {m.totalRials > 0 && (
            <div className="totals">
              {m.partsRials > 0 && <span>کالا: <span className="font-num">{toman(m.partsRials)}</span></span>}
              {m.laborRials + m.servicesRials > 0 && <span>اجرت و خدمات: <span className="font-num">{toman(m.laborRials + m.servicesRials)}</span></span>}
              {billing.canSeeCost && m.profitRials != null && <span className="cost-line">سود پرونده: <span className="font-num">{toman(m.profitRials)}</span></span>}
            </div>
          )}
        </div>
      </details>

      <details className="card collapsible" open={billing.payments.length > 0}>
        <summary>پرداخت‌ها {billing.payments.length > 0 && <span className="count font-num">{formatNumber(billing.payments.length)}</span>}</summary>
        <div className="collapsible-body">
          {billing.payments.length === 0 && <p className="muted">پرداختی ثبت نشده. بیعانه یا پرداخت نهایی را این‌جا ثبت کنید.</p>}
          <ul className="items">
            {billing.payments.map((p) => (
              <li key={p.id} className="row-static">
                <span>
                  <strong className="font-num">{toman(p.amountRials)}</strong> · {METHOD_LABELS[p.method] ?? p.method}
                  <span className="muted small"> · {faDate.format(new Date(p.paidAt))}{p.note ? ` · ${p.note}` : ""}</span>
                </span>
                {billing.canRecordPayments && (
                  <button className="link danger" disabled={busy}
                    onClick={() => run(() => api<Billing>(`/api/v1/cases/${caseId}/payments/${p.id}`, { method: "DELETE" }), "پرداخت باطل شد")}>
                    باطل
                  </button>
                )}
              </li>
            ))}
          </ul>
          {billing.canRecordPayments && <button className="primary block" onClick={() => setPaying(true)}>ثبت پرداخت</button>}
        </div>
      </details>

      <ItemSheet
        open={!!editing} kind={editing?.kind ?? "part"} item={editing?.item} canSeeCost={billing.canSeeCost} canAssignLabor={canAssignLabor}
        busy={busy} onClose={() => setEditing(null)}
        onSave={(body, saveToCatalog) => run(async () => {
          if (saveToCatalog && !editing?.item) {
            await api("/api/v1/catalog", {
              body: { kind: body.kind, title: body.title, defaultPriceRials: body.unitPriceRials, defaultCostRials: body.unitCostRials, defaultWarrantyDays: body.warrantyDays },
            }).catch(() => {});
          }
          const b = editing?.item
            ? await api<Billing>(`/api/v1/cases/${caseId}/items/${editing.item.id}`, { method: "PATCH", body })
            : await api<Billing>(`/api/v1/cases/${caseId}/items`, { body });
          setEditing(null);
          return b;
        }, editing?.item ? "ذخیره شد" : `${KIND_LABELS[body.kind as string]} ثبت شد`)}
        onDelete={editing?.item ? () => {
          const id = editing.item!.id;
          run(async () => { const b = await api<Billing>(`/api/v1/cases/${caseId}/items/${id}`, { method: "DELETE" }); setEditing(null); return b; },
            "ردیف حذف شد", () => api<Billing>(`/api/v1/cases/${caseId}/items/${id}/restore`, { method: "POST" }));
        } : undefined}
      />

      <PaymentSheet open={paying} suggested={Math.max(0, m.balanceRials)} busy={busy} onClose={() => setPaying(false)}
        onSave={(body) => run(async () => { const b = await api<Billing>(`/api/v1/cases/${caseId}/payments`, { body }); setPaying(false); return b; }, "پرداخت ثبت شد")} />
    </>
  );
}

/* ───────── item sheet ───────── */

function ItemSheet({ open, kind: initialKind, item, canSeeCost, canAssignLabor, busy, onClose, onSave, onDelete }: {
  open: boolean; kind: string; item?: Item; canSeeCost: boolean; canAssignLabor: boolean; busy: boolean;
  onClose: () => void; onSave: (body: Record<string, unknown>, saveToCatalog: boolean) => void; onDelete?: () => void;
}) {
  const [kind, setKind] = useState(initialKind);
  const [title, setTitle] = useState("");
  const [qty, setQty] = useState("1");
  const [price, setPrice] = useState("");
  const [cost, setCost] = useState("");
  const [discount, setDiscount] = useState("");
  const [supplier, setSupplier] = useState("shop");
  const [status, setStatus] = useState("used");
  const [warranty, setWarranty] = useState(0);
  const [performedBy, setPerformedBy] = useState("");
  const [catalogId, setCatalogId] = useState<string | null>(null);
  const [saveToCatalog, setSaveToCatalog] = useState(false);
  const [suggestions, setSuggestions] = useState<CatalogRow[]>([]);
  const [staff, setStaff] = useState<Assignable[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setKind(item?.kind ?? initialKind);
    setTitle(item?.title ?? "");
    setQty(item ? String(item.quantity) : "1");
    setPrice(toTomanDigits(item?.unitPriceRials));
    setCost(toTomanDigits(item?.unitCostRials));
    setDiscount(toTomanDigits(item?.discountRials));
    setSupplier(item?.supplier ?? "shop");
    setStatus(item?.status ?? "used");
    setWarranty(item?.warrantyDays ?? 0);
    setPerformedBy(item?.performedBy ?? "");
    setCatalogId(null);
    setSaveToCatalog(false);
    setError(null);
    if (canAssignLabor) api<Assignable[]>("/api/v1/staff/assignable").then(setStaff).catch(() => {});
  }, [open, item, initialKind, canAssignLabor]);

  useEffect(() => {
    if (!open || item || title.trim().length < 2 || catalogId) { setSuggestions([]); return; }
    const t = setTimeout(() => {
      api<CatalogRow[]>(`/api/v1/catalog?kind=${kind}&q=${encodeURIComponent(title.trim())}`).then(setSuggestions).catch(() => {});
    }, 200);
    return () => clearTimeout(t);
  }, [open, item, title, kind, catalogId]);

  function pick(c: CatalogRow) {
    setCatalogId(c.id);
    setTitle(c.title);
    setPrice(toTomanDigits(c.defaultPriceRials));
    if (c.defaultCostRials != null) setCost(toTomanDigits(c.defaultCostRials));
    setWarranty(c.defaultWarrantyDays ?? 0);
    setSuggestions([]);
  }

  const showCost = canSeeCost && supplier === "shop";
  const costError = showCost && cost && price && toRials(cost) > toRials(price) ? "قیمت فروش نباید از قیمت خرید کمتر باشد." : null;

  function submit(e: FormEvent) {
    e.preventDefault();
    if (costError) return;
    if (!title.trim()) { setError("عنوان را بنویسید."); return; }
    const q = Number(toLatinDigits(qty).replace(/[٫،,]/g, ".").replace(/[^\d.]/g, ""));
    if (!(q > 0)) { setError("تعداد نامعتبر است."); return; }
    const body: Record<string, unknown> = {
      kind, title: title.trim(), quantity: q, unitPriceRials: toRials(price), discountRials: toRials(discount),
      status, warrantyDays: warranty,
    };
    if (kind === "part") body.supplier = supplier;
    if (canSeeCost && cost) body.unitCostRials = toRials(cost);
    if (kind !== "part" && performedBy) body.performedBy = performedBy;
    if (catalogId) body.catalogItemId = catalogId;
    onSave(body, saveToCatalog);
  }

  return (
    <BottomSheet open={open} title={item ? `ویرایش ${KIND_LABELS[kind]}` : `افزودن ${KIND_LABELS[kind]}`} onClose={onClose}>
      <form onSubmit={submit} noValidate>
        {!item && (
          <div className="segmented wide" role="radiogroup" aria-label="نوع">
            {OFFERED_KINDS.map(([k, label]) => (
              <button type="button" key={k} role="radio" aria-checked={kind === k} className={kind === k ? "on" : ""} onClick={() => { setKind(k); setCatalogId(null); }}>{label}</button>
            ))}
          </div>
        )}
        <Field label={kind === "part" ? "نام کالا" : "شرح کار یا خدمت"} error={error}>
          <input value={title} onChange={(e) => { setTitle(e.target.value); setCatalogId(null); }} maxLength={120} autoComplete="off" />
        </Field>
        {suggestions.length > 0 && (
          <div className="suggestions">
            {suggestions.map((s) => (
              <button type="button" key={s.id} className="suggestion" onClick={() => pick(s)}>
                <span>{s.title}</span><span className="muted small font-num">{toman(s.defaultPriceRials)}</span>
              </button>
            ))}
          </div>
        )}

        {kind === "part" && (
          <div className="segmented wide" role="radiogroup" aria-label="تأمین کالا">
            <button type="button" role="radio" aria-checked={supplier === "shop"} className={supplier === "shop" ? "on" : ""} onClick={() => setSupplier("shop")}>از تعمیرگاه</button>
            <button type="button" role="radio" aria-checked={supplier === "customer"} className={supplier === "customer" ? "on" : ""} onClick={() => setSupplier("customer")}>مشتری آورده</button>
          </div>
        )}

        {/* Purchase price first, then the sale price it must not undercut. */}
        {showCost && (
          <Field label="قیمت خرید هر عدد (فقط شما می‌بینید)">
            <NumberInput value={cost} onChange={setCost} max={11} suffix="تومان" />
          </Field>
        )}
        <Field label={supplier === "customer" && kind === "part" ? "قیمت (برای سابقه)" : "قیمت فروش هر عدد"} error={costError}>
          <NumberInput value={price} onChange={setPrice} max={11} suffix="تومان" />
        </Field>
        <div className="grid-2">
          <Field label="تعداد">
            <input inputMode="decimal" dir="ltr" className="font-num" value={qty} onChange={(e) => setQty(toLatinDigits(e.target.value).replace(/[٫،,]/g, ".").replace(/[^\d.]/g, "").slice(0, 7))} />
          </Field>
          <Field label="تخفیف این ردیف">
            <NumberInput value={discount} onChange={setDiscount} max={11} suffix="تومان" />
          </Field>
        </div>

        {kind === "part" && (
          <label className="check">
            <input type="checkbox" checked={status === "needed"} onChange={(e) => setStatus(e.target.checked ? "needed" : "used")} />
            <span>هنوز نصب نشده (منتظر قطعه) — در صورت‌حساب حساب نمی‌شود</span>
          </label>
        )}

        {kind !== "part" && canAssignLabor && staff.length > 0 && (
          <Field label="انجام‌دهنده">
            <select value={performedBy} onChange={(e) => setPerformedBy(e.target.value)}>
              <option value="">خودم</option>
              {staff.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          </Field>
        )}

        <div className="field">
          <span className="label">ضمانت</span>
          <div className="chips">
            {WARRANTY_OPTIONS.map((d) => (
              <button type="button" key={d} className={`chip-button${warranty === d ? " active" : ""}`} onClick={() => setWarranty(d)}>
                {d === 0 ? "ندارد" : `${formatNumber(d)} روز`}
              </button>
            ))}
          </div>
        </div>

        {!item && !catalogId && title.trim() && (
          <label className="check">
            <input type="checkbox" checked={saveToCatalog} onChange={(e) => setSaveToCatalog(e.target.checked)} />
            <span>در فهرست قیمت ذخیره شود</span>
          </label>
        )}

        <button className="primary block" disabled={busy} aria-busy={busy}>{busy ? "در حال ذخیره…" : "ذخیره"}</button>
        {onDelete && <SheetOption label="حذف این ردیف" tone="danger" disabled={busy} onClick={onDelete} />}
      </form>
    </BottomSheet>
  );
}

/* ───────── payment sheet ───────── */

function PaymentSheet({ open, suggested, busy, onClose, onSave }: {
  open: boolean; suggested: number; busy: boolean; onClose: () => void; onSave: (body: Record<string, unknown>) => void;
}) {
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState("card");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setAmount(toTomanDigits(suggested));
    setMethod("card");
    setNote("");
    setError(null);
  }, [open, suggested]);

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!amount) { setError("مبلغ را وارد کنید."); return; }
    onSave({ amountRials: toRials(amount), method, note: note.trim() || undefined });
  }

  return (
    <BottomSheet open={open} title="ثبت پرداخت" onClose={onClose}>
      <form onSubmit={submit} noValidate>
        <Field label={suggested > 0 ? `مبلغ (مانده: ${toman(suggested)})` : "مبلغ (بیعانه یا پیش‌پرداخت)"} error={error}>
          <NumberInput value={amount} onChange={setAmount} max={11} suffix="تومان" autoFocus />
        </Field>
        <div className="chips" role="radiogroup" aria-label="روش پرداخت">
          {Object.entries(METHOD_LABELS).map(([k, label]) => (
            <button type="button" key={k} role="radio" aria-checked={method === k} className={`chip-button${method === k ? " active" : ""}`} onClick={() => setMethod(k)}>{label}</button>
          ))}
        </div>
        <Field label="توضیح (اختیاری)">
          <input value={note} onChange={(e) => setNote(e.target.value)} maxLength={300} placeholder="مثلاً بیعانه خرید قطعه" />
        </Field>
        <button className="primary block" disabled={busy} aria-busy={busy}>{busy ? "در حال ثبت…" : "ثبت پرداخت"}</button>
      </form>
    </BottomSheet>
  );
}

/* ───────── delivery with balance ───────── */

export function CreditSheet({ open, balanceRials, busy, onClose, onPay, onCredit }: {
  open: boolean; balanceRials: number; busy: boolean; onClose: () => void; onPay: () => void; onCredit: (dueIso: string | null) => void;
}) {
  const [due, setDue] = useState<number | null>(7);
  const options = [3, 7, 14, 30];
  const dueDate = (days: number) => {
    const d = new Date();
    d.setDate(d.getDate() + days);
    d.setHours(18, 0, 0, 0);
    return d;
  };
  return (
    <BottomSheet open={open} title="این پرونده مانده حساب دارد" onClose={onClose}>
      <p>مانده: <strong className="font-num">{toman(balanceRials)}</strong></p>
      <SheetOption label="اول پرداخت را ثبت کنم" tone="primary" onClick={onPay} disabled={busy} />
      <div className="field">
        <span className="label">یا تحویل به‌صورت نسیه؛ موعد پرداخت:</span>
        <div className="chips">
          {options.map((d) => (
            <button type="button" key={d} className={`chip-button${due === d ? " active" : ""}`} onClick={() => setDue(d)}>
              {formatNumber(d)} روز · {new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long" }).format(dueDate(d))}
            </button>
          ))}
          <button type="button" className={`chip-button${due === null ? " active" : ""}`} onClick={() => setDue(null)}>بدون موعد</button>
        </div>
      </div>
      <SheetOption label="تحویل به‌صورت نسیه" disabled={busy} onClick={() => onCredit(due === null ? null : dueDate(due).toISOString())} />
    </BottomSheet>
  );
}

/* ───────── receivables & catalog pages (under «بیشتر») ───────── */

type Receivable = { id: string; number: number; closedAt: string; creditDueAt: string | null; fullName: string | null; mobile: string; balanceRials: number };

export function ReceivablesView({ onOpenCase, onBack }: { onOpenCase: (id: string) => void; onBack: () => void }) {
  const [data, setData] = useState<{ totalRials: number; cases: Receivable[] } | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { api<{ totalRials: number; cases: Receivable[] }>("/api/v1/receivables").then(setData).catch((e) => setError(e instanceof ApiError ? e.message : "خطا")); }, []);
  const dateFmt = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long" });
  return (
    <section>
      <button className="link back" onClick={onBack}>→ گزارش‌ها</button>
      <h2>نسیه‌ها</h2>
      {error && <p className="error">{error}</p>}
      {data && (
        <>
          <div className="money-bar single"><div className="due"><span className="muted small">جمع طلب</span><strong className="font-num">{toman(data.totalRials)}</strong></div></div>
          {data.cases.length === 0 && <p className="empty muted">نسیه‌ای ندارید.</p>}
          <ul className="list">
            {data.cases.map((r) => {
              const overdue = r.creditDueAt && new Date(r.creditDueAt) < new Date();
              return (
                <li key={r.id}>
                  <button className="case-card" onClick={() => onOpenCase(r.id)}>
                    <span className="case-row-top">
                      <span className="case-row-title">{r.fullName ?? r.mobile}</span>
                      <strong className="font-num">{toman(r.balanceRials)}</strong>
                    </span>
                    <span className="muted small">
                      پرونده #{new Intl.NumberFormat("fa-IR", { useGrouping: false }).format(r.number)} · تحویل {dateFmt.format(new Date(r.closedAt))}
                    </span>
                    {r.creditDueAt && (
                      <span className={`alert-line ${overdue ? "danger" : "warn"}`}>{overdue ? "موعد پرداخت گذشته" : "موعد پرداخت"}: {dateFmt.format(new Date(r.creditDueAt))}</span>
                    )}
                  </button>
                </li>
              );
            })}
          </ul>
        </>
      )}
    </section>
  );
}

export function CatalogView({ onBack, canSeeCost }: { onBack: () => void; canSeeCost: boolean }) {
  const { notify } = useFeedback();
  const [rows, setRows] = useState<CatalogRow[]>([]);
  const [editing, setEditing] = useState<CatalogRow | "new" | null>(null);
  const [kind, setKind] = useState("part");
  const [title, setTitle] = useState("");
  const [price, setPrice] = useState("");
  const [cost, setCost] = useState("");
  const [warranty, setWarranty] = useState(0);

  const load = () => api<CatalogRow[]>("/api/v1/catalog?all=true").then(setRows).catch(() => {});
  useEffect(() => { load(); }, []);

  function open(row: CatalogRow | "new") {
    setEditing(row);
    setKind(row === "new" ? "part" : row.kind === "service" ? "labor" : row.kind);
    setTitle(row === "new" ? "" : row.title);
    setPrice(row === "new" ? "" : toTomanDigits(row.defaultPriceRials));
    setCost(row === "new" ? "" : toTomanDigits(row.defaultCostRials));
    setWarranty(row === "new" ? 0 : row.defaultWarrantyDays ?? 0);
  }

  const catalogCostError = canSeeCost && cost && price && toRials(cost) > toRials(price) ? "قیمت فروش نباید از قیمت خرید کمتر باشد." : null;

  async function save(e: FormEvent) {
    e.preventDefault();
    if (catalogCostError) return;
    const body: Record<string, unknown> = { kind, title, defaultPriceRials: toRials(price), defaultWarrantyDays: warranty };
    if (canSeeCost && cost) body.defaultCostRials = toRials(cost);
    try {
      if (editing === "new") await api("/api/v1/catalog", { body });
      else if (editing) await api(`/api/v1/catalog/${editing.id}`, { method: "PATCH", body });
      notify("ذخیره شد");
      setEditing(null);
      await load();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "خطا", "error");
    }
  }

  async function toggle(row: CatalogRow) {
    await api(`/api/v1/catalog/${row.id}`, { method: "PATCH", body: { isActive: !row.isActive } }).catch(() => {});
    notify(row.isActive ? "غیرفعال شد" : "فعال شد");
    await load();
  }

  return (
    <section>
      <button className="link back" onClick={onBack}>→ تنظیمات</button>
      <div className="toolbar"><h2 style={{ margin: 0, flex: 1 }}>فهرست قیمت</h2><button className="primary" onClick={() => open("new")}>+ افزودن</button></div>
      {rows.length === 0 && <p className="empty muted">کالاها و اجرت‌ها و خدمات پرتکرار را با قیمت این‌جا ثبت کنید تا هنگام ثبت در پرونده با چند حرف پیدا شوند.</p>}
      <ul className="list">
        {rows.map((r) => (
          <li key={r.id} className={`row-static${r.isActive ? "" : " inactive"}`}>
            <button className="link" onClick={() => open(r)} style={{ textAlign: "start" }}>
              <span className={`kind-badge ${r.kind}`}>{KIND_LABELS[r.kind]}</span> {r.title}
              <span className="muted small font-num"> · {toman(r.defaultPriceRials)}</span>
            </button>
            <button onClick={() => toggle(r)}>{r.isActive ? "غیرفعال" : "فعال"}</button>
          </li>
        ))}
      </ul>
      <BottomSheet open={!!editing} title={editing === "new" ? "قلم جدید" : "ویرایش قلم"} onClose={() => setEditing(null)}>
        <form onSubmit={save}>
          <div className="segmented wide" role="radiogroup" aria-label="نوع">
            {OFFERED_KINDS.map(([k, label]) => (
              <button type="button" key={k} role="radio" aria-checked={kind === k} className={kind === k ? "on" : ""} onClick={() => setKind(k)}>{label}</button>
            ))}
          </div>
          <Field label="عنوان"><input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={120} required /></Field>
          {canSeeCost && <Field label="قیمت خرید"><NumberInput value={cost} onChange={setCost} max={11} suffix="تومان" /></Field>}
          <Field label="قیمت فروش" error={catalogCostError}><NumberInput value={price} onChange={setPrice} max={11} suffix="تومان" /></Field>
          <div className="chips">
            {WARRANTY_OPTIONS.map((d) => (
              <button type="button" key={d} className={`chip-button${warranty === d ? " active" : ""}`} onClick={() => setWarranty(d)}>
                {d === 0 ? "بدون ضمانت" : `${formatNumber(d)} روز ضمانت`}
              </button>
            ))}
          </div>
          <button className="primary block">ذخیره</button>
        </form>
      </BottomSheet>
    </section>
  );
}
