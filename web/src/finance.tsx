import { useEffect, useState } from "react";
import { api, apiBlob, ApiError } from "./api";
import { useFeedback } from "./feedback";
import { BottomSheet } from "./sheet";
import { SubPage } from "./settings";
import { Field, NumberInput, formatNumber } from "./ui";

export type Terminal = { id: string; name: string; bank: string | null; isActive: boolean };
export type BankCard = { id: string; cardNumber: string; holderName: string; bank: string | null; isActive: boolean; showOnTracking: boolean };
export type CustomerReceipt = { id: string; attachmentId: string; claimedRials: number | null; status: string; createdAt: string; reviewNote: string | null; paidRials?: number | null };

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


/** A settings row as a card: details on one side, edit on the other; the sheet also holds «حذف» (kept in old payments, hidden from new ones). */
function RowCard({ off, children, onEdit, onRestore, busy, label }: { off: boolean; children: React.ReactNode; onEdit: () => void; onRestore: () => void; busy: boolean; label: string }) {
  return (
    <li className={`opt-row${off ? " off" : ""}`}>
      <div className="opt-main">{children}</div>
      <div className="opt-actions">
        {off
          ? <button type="button" className="secondary" disabled={busy} onClick={onRestore}>بازگردانی</button>
          : <button type="button" className="icon-btn" aria-label={`ویرایش ${label}`} disabled={busy} onClick={onEdit}>
              <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="M4 20h4l10.500-10.500a2.100 2.100 0 0 0-3-3L5 17v3Zm9.500-13.500 3 3" fill="none" stroke="currentColor" strokeWidth="1.800" strokeLinecap="round" strokeLinejoin="round" /></svg>
            </button>}
      </div>
    </li>
  );
}

function DeleteZone({ busy, what, onDelete }: { busy: boolean; what: string; onDelete: () => void }) {
  const [sure, setSure] = useState(false);
  return sure ? (
    <div className="stack">
      <p className="hint">«{what}» از فهرست‌های جدید برداشته می‌شود؛ پرداخت‌های قبلی که با آن ثبت شده‌اند می‌مانند. هر وقت خواستید بازگردانی می‌کنید.</p>
      <button type="button" className="danger" disabled={busy} aria-busy={busy} onClick={onDelete}>{busy ? "در حال حذف…" : "بله، حذف شود"}</button>
      <button type="button" className="secondary" onClick={() => setSure(false)}>انصراف</button>
    </div>
  ) : <button type="button" className="danger-outline" disabled={busy} onClick={() => setSure(true)}>حذف</button>;
}

function TerminalsBlock() {
  const [rows, setRows] = useState<Terminal[] | null>(null);
  const [name, setName] = useState("");
  const [bank, setBank] = useState("");
  const [edit, setEdit] = useState<Terminal | null>(null);
  const [eName, setEName] = useState("");
  const [eBank, setEBank] = useState("");
  const load = () => api<Terminal[]>("/api/v1/payment-terminals?includeInactive=true").then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); }, []);
  const { busy, act } = useAct(load);
  if (!rows) return <div className="splash" aria-busy="true" />;
  return (
    <section className="stack">
      <h3>کارتخوان‌ها</h3>
      {rows.length === 0 && <p className="hint">هر کارتخوان را اینجا ثبت کنید تا هنگام ثبت پرداخت «کارت‌خوان» بتوانید یکی را انتخاب کنید.</p>}
      <ul className="opt-list">
        {rows.map((r) => (
          <RowCard key={r.id} off={!r.isActive} busy={busy} label={r.name} onEdit={() => { setEdit(r); setEName(r.name); setEBank(r.bank ?? ""); }}
            onRestore={() => act(() => api(`/api/v1/payment-terminals/${r.id}`, { method: "PATCH", body: { isActive: true } }), "بازگردانی شد")}>
            <strong>{r.name}</strong>
            <span className="opt-badges">{r.bank && <span className="badge subtle">{r.bank}</span>}{!r.isActive && <span className="badge subtle">حذف‌شده</span>}</span>
          </RowCard>
        ))}
      </ul>
      <BottomSheet open={edit !== null} title="ویرایش کارتخوان" onClose={() => setEdit(null)}>
        <form className="stack" onSubmit={(e) => { e.preventDefault(); if (!eName.trim() || !edit) return;
          act(async () => { await api(`/api/v1/payment-terminals/${edit.id}`, { method: "PATCH", body: { name: eName.trim(), bank: eBank.trim() } }); setEdit(null); }, "ذخیره شد"); }}>
          <Field label="نام کارتخوان"><input value={eName} onChange={(e) => setEName(e.target.value)} maxLength={60} /></Field>
          <Field label="بانک (اختیاری)"><input value={eBank} onChange={(e) => setEBank(e.target.value)} maxLength={60} /></Field>
          <button type="submit" className="primary" disabled={busy || !eName.trim()} aria-busy={busy}>{busy ? "در حال ذخیره…" : "ذخیره"}</button>
        </form>
        {edit && <DeleteZone busy={busy} what={edit.name} onDelete={() => act(async () => { await api(`/api/v1/payment-terminals/${edit.id}`, { method: "PATCH", body: { isActive: false } }); setEdit(null); }, "حذف شد")} />}
      </BottomSheet>
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
  const [edit, setEdit] = useState<BankCard | null>(null);
  const [eNumber, setENumber] = useState("");
  const [eHolder, setEHolder] = useState("");
  const [eBank, setEBank] = useState("");
  const load = () => api<BankCard[]>("/api/v1/bank-cards?includeInactive=true").then(setRows).catch(() => setRows([]));
  useEffect(() => { load(); }, []);
  const { busy, act } = useAct(load);
  if (!rows) return <div className="splash" aria-busy="true" />;
  return (
    <section className="stack">
      <h3>کارت‌های بانکی (کارت به کارت)</h3>
      {rows.length === 0 && <p className="hint">کارتی که مشتری می‌تواند به آن واریز کند را ثبت کنید. یک کارت را می‌توانید در لینک پیگیری مشتری، با دکمه‌ی کپی، نمایش دهید.</p>}
      <ul className="opt-list">
        {rows.map((r) => (
          <RowCard key={r.id} off={!r.isActive} busy={busy} label={`کارت ${r.holderName}`}
            onEdit={() => { setEdit(r); setENumber(r.cardNumber); setEHolder(r.holderName); setEBank(r.bank ?? ""); }}
            onRestore={() => act(() => api(`/api/v1/bank-cards/${r.id}`, { method: "PATCH", body: { isActive: true } }), "بازگردانی شد")}>
            <strong dir="ltr" className="font-num">{cardText(r.cardNumber)}</strong>
            <span className="opt-badges">
              <span className="badge subtle">{r.holderName}</span>
              {r.bank && <span className="badge subtle">{r.bank}</span>}
              {r.showOnTracking && <span className="badge good">نمایش در لینک مشتری</span>}
              {!r.isActive && <span className="badge subtle">حذف‌شده</span>}
            </span>
          </RowCard>
        ))}
      </ul>
      <BottomSheet open={edit !== null} title="ویرایش کارت" onClose={() => setEdit(null)}>
        <form className="stack" onSubmit={(e) => { e.preventDefault(); if (eNumber.length !== 16 || !eHolder.trim() || !edit) return;
          act(async () => { await api(`/api/v1/bank-cards/${edit.id}`, { method: "PATCH", body: { cardNumber: eNumber, holderName: eHolder.trim(), bank: eBank.trim() } }); setEdit(null); }, "ذخیره شد"); }}>
          <Field label="شماره کارت (۱۶ رقم)"><input inputMode="numeric" dir="ltr" value={eNumber} maxLength={16} onChange={(e) => setENumber(e.target.value.replace(/\D/g, "").slice(0, 16))} /></Field>
          <Field label="نام صاحب کارت"><input value={eHolder} onChange={(e) => setEHolder(e.target.value)} maxLength={80} /></Field>
          <Field label="بانک (اختیاری)"><input value={eBank} onChange={(e) => setEBank(e.target.value)} maxLength={60} /></Field>
          {edit && <label className="setting-row">
            <span><strong>نمایش در لینک مشتری</strong><span className="muted small">فقط یک کارت در لینک نشان داده می‌شود.</span></span>
            <input type="checkbox" role="switch" className="switch" checked={edit.showOnTracking} disabled={busy}
              onChange={() => act(async () => { await api(`/api/v1/bank-cards/${edit.id}`, { method: "PATCH", body: { showOnTracking: !edit.showOnTracking } }); setEdit(null); })} />
          </label>}
          <button type="submit" className="primary" disabled={busy || eNumber.length !== 16 || !eHolder.trim()} aria-busy={busy}>{busy ? "در حال ذخیره…" : "ذخیره"}</button>
        </form>
        {edit && <DeleteZone busy={busy} what={cardText(edit.cardNumber)} onDelete={() => act(async () => { await api(`/api/v1/bank-cards/${edit.id}`, { method: "PATCH", body: { isActive: false } }); setEdit(null); }, "حذف شد")} />}
      </BottomSheet>
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
              <button type="button" className="link-button" onClick={() => show(r)}>{open === r.id ? "بستن" : r.status === "pending" ? "بررسی" : "دیدن رسید"}</button>
            </div>
            {open === r.id && (
              <div className="stack">
                {img ? <img className="receipt-image" src={img} alt="رسید مشتری" /> : <div className="splash" aria-busy="true" />}
                {r.status === "pending" && (
                  <>
                    <Field label="مبلغ واریزی طبق رسید (تومان)"><NumberInput value={amount} onChange={setAmount} max={11} suffix="تومان" /></Field>
                    <p className="muted small">اگر مشتری فقط بخشی از مبلغ را واریز کرده، همان مقدار را بنویسید؛ باقی‌مانده همچنان روی پرونده می‌ماند.</p>
                    <Field label="دلیل رد (فقط اگر رد می‌کنید)"><input value={note} onChange={(e) => setNote(e.target.value)} maxLength={200} /></Field>
                  </>
                )}
                {r.status === "rejected" && r.reviewNote && <p className="muted small">دلیل رد: {r.reviewNote}</p>}
                {r.status === "pending" && <div className="row-actions">
                  <button type="button" className="primary" disabled={busy || !amount} aria-busy={busy}
                    onClick={() => act(() => api(`/api/v1/customer-receipts/${r.id}/approve`, { body: { amountRials: Number(amount) * 10 } }), "پرداخت ثبت شد")}>تأیید و ثبت پرداخت</button>
                  <button type="button" disabled={busy}
                    onClick={() => act(() => api(`/api/v1/customer-receipts/${r.id}/reject`, { body: { note: note.trim() || undefined } }), "رد شد")}>رد رسید</button>
                </div>}
              </div>
            )}
          </li>
        ))}
      </ul>
    </details>
  );
}
