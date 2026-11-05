import { useEffect, useState } from "react";
import { api, apiBlob, ApiError } from "./api";
import { useFeedback } from "./feedback";
import { SubPage } from "./settings";
import { Field, NumberInput, formatNumber } from "./ui";

export type Terminal = { id: string; name: string; bank: string | null; isActive: boolean };
export type BankCard = { id: string; cardNumber: string; holderName: string; bank: string | null; isActive: boolean; showOnTracking: boolean };
export type CustomerReceipt = { id: string; attachmentId: string; claimedRials: number | null; status: string; createdAt: string; reviewNote: string | null };

/** 6037 9971 1234 5678, always left-to-right so the groups do not flip in an RTL page. */
export function cardText(n: string) { return n.replace(/(\d{4})(?=\d)/g, "$1 "); }

function useAct(reload: () => Promise<unknown>) {
  const { notify } = useFeedback();
  const [busy, setBusy] = useState(false);
  async function act(fn: () => Promise<unknown>, ok?: string) {
    if (busy) return;
    setBusy(true);
    try { await fn(); if (ok) notify(ok); await reload(); }
    catch (e) { notify(e instanceof ApiError ? e.message : "خطا در ارتباط با سرور", "error"); }
    finally { setBusy(false); }
  }
  return { busy, act };
}

function TerminalsBlock() {
  const [rows, setRows] = useState<Terminal[] | null>(null);
  const [name, setName] = useState("");
  const [bank, setBank] = useState("");
  const load = () => api<Terminal[]>("/api/v1/payment-terminals?includeInactive=true").then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); }, []);
  const { busy, act } = useAct(load);
  if (!rows) return <div className="splash" aria-busy="true" />;
  return (
    <section className="stack">
      <h3>کارتخوان‌ها</h3>
      {rows.length === 0 && <p className="hint">هر کارتخوان را اینجا ثبت کنید تا هنگام ثبت پرداخت «کارت‌خوان» بتوانید یکی را انتخاب کنید.</p>}
      <ul className="plain-list">
        {rows.map((r) => (
          <li key={r.id} className="row-between">
            <span className={r.isActive ? "" : "muted"}>{r.name}{r.bank && <span className="muted small"> · {r.bank}</span>}{!r.isActive && " (غیرفعال)"}</span>
            <button type="button" className="link-button" disabled={busy} onClick={() => act(() => api(`/api/v1/payment-terminals/${r.id}`, { method: "PATCH", body: { isActive: !r.isActive } }))}>{r.isActive ? "غیرفعال" : "فعال"}</button>
          </li>
        ))}
      </ul>
      <form className="stack" onSubmit={(e) => { e.preventDefault(); if (!name.trim()) return;
        act(async () => { await api("/api/v1/payment-terminals", { body: { name: name.trim(), bank: bank.trim() || undefined } }); setName(""); setBank(""); }, "اضافه شد"); }}>
        <Field label="نام کارتخوان"><input value={name} onChange={(e) => setName(e.target.value)} maxLength={60} /></Field>
        <Field label="بانک (اختیاری)"><input value={bank} onChange={(e) => setBank(e.target.value)} maxLength={60} /></Field>
        <button type="submit" className="primary" disabled={busy || !name.trim()} aria-busy={busy}>افزودن کارتخوان</button>
      </form>
    </section>
  );
}

function CardsBlock() {
  const [rows, setRows] = useState<BankCard[] | null>(null);
  const [number, setNumber] = useState("");
  const [holder, setHolder] = useState("");
  const [bank, setBank] = useState("");
  const load = () => api<BankCard[]>("/api/v1/bank-cards?includeInactive=true").then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); }, []);
  const { busy, act } = useAct(load);
  if (!rows) return <div className="splash" aria-busy="true" />;
  return (
    <section className="stack">
      <h3>کارت‌های بانکی (کارت به کارت)</h3>
      {rows.length === 0 && <p className="hint">کارتی که مشتری می‌تواند به آن واریز کند را ثبت کنید. یک کارت را می‌توانید در لینک پیگیری مشتری، با دکمه‌ی کپی، نمایش دهید.</p>}
      <ul className="plain-list">
        {rows.map((r) => (
          <li key={r.id} className="row-between">
            <span className={r.isActive ? "" : "muted"}>
              <bdi dir="ltr" className="font-num">{cardText(r.cardNumber)}</bdi> · {r.holderName}{r.bank && <span className="muted small"> · {r.bank}</span>}
              {r.showOnTracking && <span className="badge good"> نمایش در لینک مشتری</span>}{!r.isActive && " (غیرفعال)"}
            </span>
            <span className="row-actions">
              {r.isActive && <button type="button" className="link-button" disabled={busy}
                onClick={() => act(() => api(`/api/v1/bank-cards/${r.id}`, { method: "PATCH", body: { showOnTracking: !r.showOnTracking } }))}>{r.showOnTracking ? "مخفی در لینک" : "نمایش در لینک"}</button>}
              <button type="button" className="link-button" disabled={busy}
                onClick={() => act(() => api(`/api/v1/bank-cards/${r.id}`, { method: "PATCH", body: { isActive: !r.isActive } }))}>{r.isActive ? "غیرفعال" : "فعال"}</button>
            </span>
          </li>
        ))}
      </ul>
      <form className="stack" onSubmit={(e) => { e.preventDefault(); if (number.length !== 16 || !holder.trim()) return;
        act(async () => { await api("/api/v1/bank-cards", { body: { cardNumber: number, holderName: holder.trim(), bank: bank.trim() || undefined, showOnTracking: rows.every((r) => !r.showOnTracking) } }); setNumber(""); setHolder(""); setBank(""); }, "کارت اضافه شد"); }}>
        <Field label="شماره کارت (۱۶ رقم)">
          <input inputMode="numeric" dir="ltr" value={number} maxLength={16} onChange={(e) => setNumber(e.target.value.replace(/\D/g, "").slice(0, 16))} />
        </Field>
        <Field label="نام صاحب کارت"><input value={holder} onChange={(e) => setHolder(e.target.value)} maxLength={80} /></Field>
        <Field label="بانک (اختیاری)"><input value={bank} onChange={(e) => setBank(e.target.value)} maxLength={60} /></Field>
        <button type="submit" className="primary" disabled={busy || number.length !== 16 || !holder.trim()} aria-busy={busy}>افزودن کارت</button>
      </form>
    </section>
  );
}

export function FinancePage({ onBack }: { onBack: () => void }) {
  return (
    <SubPage title="مالی: کارتخوان و کارت‌ها" onBack={onBack}>
      <TerminalsBlock />
      <CardsBlock />
    </SubPage>
  );
}

/** On the case: receipts the customer sent from their link. Approving records the payment. */
export function CustomerReceipts({ caseId, onChange }: { caseId: string; onChange: () => void }) {
  const [rows, setRows] = useState<CustomerReceipt[] | null>(null);
  const [open, setOpen] = useState<string | null>(null);
  const [img, setImg] = useState<string | null>(null);
  const [amount, setAmount] = useState("");
  const [note, setNote] = useState("");
  const load = () => api<CustomerReceipt[]>(`/api/v1/cases/${caseId}/customer-receipts`).then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); }, [caseId]);
  const { busy, act } = useAct(async () => { await load(); onChange(); });

  async function show(r: CustomerReceipt) {
    if (open === r.id) { setOpen(null); return; }
    setOpen(r.id); setImg(null); setNote(""); setAmount(r.claimedRials ? String(Math.round(r.claimedRials / 10)) : "");
    try { setImg(URL.createObjectURL(await apiBlob(`/api/v1/attachments/${r.attachmentId}`))); } catch { /* the buttons still work */ }
  }

  const pending = (rows ?? []).filter((r) => r.status === "pending");
  if (!rows || rows.length === 0) return null;
  const date = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" });
  return (
    <details className="case-section" open={pending.length > 0}>
      <summary>رسیدهای مشتری {pending.length > 0 && <span className="badge warn font-num">{formatNumber(pending.length)} در انتظار</span>}</summary>
      <ul className="plain-list">
        {rows.map((r) => (
          <li key={r.id} className="stack">
            <div className="row-between">
              <span>{date.format(new Date(r.createdAt))}{r.claimedRials ? ` · ${formatNumber(Math.round(r.claimedRials / 10))} تومان` : ""}
                <span className="muted small"> · {r.status === "pending" ? "در انتظار بررسی" : r.status === "approved" ? "تأیید شد" : "رد شد"}</span></span>
              {r.status === "pending" && <button type="button" className="link-button" onClick={() => show(r)}>{open === r.id ? "بستن" : "بررسی"}</button>}
            </div>
            {open === r.id && (
              <div className="stack">
                {img ? <img className="receipt-image" src={img} alt="رسید مشتری" /> : <div className="splash" aria-busy="true" />}
                <Field label="مبلغ واریزی (تومان)"><NumberInput value={amount} onChange={setAmount} max={11} suffix="تومان" /></Field>
                <Field label="دلیل رد (فقط اگر رد می‌کنید)"><input value={note} onChange={(e) => setNote(e.target.value)} maxLength={200} /></Field>
                <div className="row-actions">
                  <button type="button" className="primary" disabled={busy || !amount} aria-busy={busy}
                    onClick={() => act(() => api(`/api/v1/customer-receipts/${r.id}/approve`, { body: { amountRials: Number(amount) * 10 } }), "پرداخت ثبت شد")}>تأیید و ثبت پرداخت</button>
                  <button type="button" disabled={busy}
                    onClick={() => act(() => api(`/api/v1/customer-receipts/${r.id}/reject`, { body: { note: note.trim() || undefined } }), "رد شد")}>رد رسید</button>
                </div>
              </div>
            )}
          </li>
        ))}
      </ul>
    </details>
  );
}
