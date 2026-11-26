import { FormEvent, useEffect, useState } from "react";
import { api, ApiError } from "./api";
import { useFeedback } from "./feedback";
import { BottomSheet } from "./sheet";
import { NumberInput, formatNumber, onlyDigits, toLatinDigits } from "./ui";

export type DamageMedia = { id: string; isVideo: boolean };
export type CustomerDamage = {
  id: string; caseId: string; amountRials: number; remainingRials: number;
  internalNote: string | null; showReportToCustomer: boolean; showMediaToCustomer: boolean;
  createdAt: string; media?: DamageMedia[];
};

const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;

export function CustomerDamageSection({ caseId, canManage, onChanged }: {
  caseId: string; canManage: boolean; onChanged?: () => void;
}) {
  const { notify } = useFeedback();
  const [rows, setRows] = useState<CustomerDamage[] | null>(null);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [amount, setAmount] = useState("");
  const [note, setNote] = useState("");
  const [showReport, setShowReport] = useState(false);
  const [showMedia, setShowMedia] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const load = () => api<CustomerDamage[]>(`/api/v1/cases/${caseId}/customer-damages`)
    .then(setRows).catch((e) => notify(e instanceof ApiError ? e.message : "بارگذاری خسارت‌ها نشد", "error"));

  useEffect(() => { void load(); }, [caseId]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setErr(null);
    const rials = Number(onlyDigits(toLatinDigits(amount))) * 10;
    if (!Number.isFinite(rials) || rials <= 0) { setErr("مبلغ لازم است."); return; }
    setBusy(true);
    try {
      await api(`/api/v1/cases/${caseId}/customer-damages`, {
        method: "POST",
        body: {
          amountRials: rials,
          internalNote: note.trim() || null,
          showReportToCustomer: showReport,
          showMediaToCustomer: showReport && showMedia,
        },
      });
      notify("خسارت ثبت شد");
      setOpen(false);
      setAmount(""); setNote(""); setShowReport(false); setShowMedia(false);
      await load();
      onChanged?.();
    } catch (ex) {
      setErr(ex instanceof ApiError ? ex.message : "ثبت نشد.");
    } finally { setBusy(false); }
  }

  async function voidRow(id: string) {
    const reason = window.prompt("علت ابطال خسارت؟");
    if (!reason?.trim()) return;
    setBusy(true);
    try {
      await api(`/api/v1/cases/${caseId}/customer-damages/${id}/void`, {
        method: "POST", body: { reason: reason.trim() },
      });
      notify("گزارش ابطال شد");
      await load();
      onChanged?.();
    } catch (ex) { notify(ex instanceof ApiError ? ex.message : "ابطال نشد", "error"); }
    finally { setBusy(false); }
  }

  async function upload(reportId: string, file: File) {
    const fd = new FormData();
    fd.append("file", file);
    setBusy(true);
    try {
      await api(`/api/v1/cases/${caseId}/customer-damages/${reportId}/photos`, { method: "POST", body: fd });
      notify("رسانه افزوده شد");
      await load();
    } catch (ex) { notify(ex instanceof ApiError ? ex.message : "آپلود نشد", "error"); }
    finally { setBusy(false); }
  }

  if (rows === null) return <p className="muted splash" aria-busy="true">در حال بارگذاری خسارت‌ها…</p>;

  return (
    <section className="card damage-section">
      <header className="row between">
        <h2 className="h3">خسارت به مشتری</h2>
        {canManage && <button type="button" onClick={() => setOpen(true)}>ثبت خسارت</button>}
      </header>
      {rows.length === 0 ? (
        <p className="muted empty-hint">هنوز خسارتی ثبت نشده. ثبت فقط برای مدیر و مکانیک ارشدِ دارای مجوز است.</p>
      ) : (
        <ul className="damage-list">
          {rows.map((r) => (
            <li key={r.id} className="damage-card">
              <div className="row between">
                <strong>بستانکاری: {toman(r.amountRials)}</strong>
                <span className="muted">مانده: {toman(r.remainingRials)}</span>
              </div>
              {r.internalNote && <p className="small">{r.internalNote}</p>}
              <p className="muted small">
                نمایش لینک: {r.showReportToCustomer ? "روشن" : "خاموش"}
                {" · "}رسانه: {r.showMediaToCustomer ? "روشن" : "خاموش"}
                {" · "}فایل: {formatNumber(r.media?.length ?? 0)}
              </p>
              {canManage && (
                <div className="row gap">
                  <label className="btn-like">
                    افزودن عکس/فیلم
                    <input type="file" accept="image/*,video/*" hidden disabled={busy}
                      onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(r.id, f); e.target.value = ""; }} />
                  </label>
                  <button type="button" className="danger" disabled={busy} onClick={() => void voidRow(r.id)}>ابطال</button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}

      <BottomSheet open={open} title="ثبت خسارت به مشتری" onClose={() => !busy && setOpen(false)}>
        <form className="stack" onSubmit={submit}>
          <label>
            <span>مبلغ بستانکاری مشتری (تومان)</span>
            <NumberInput value={amount} onChange={setAmount} inputMode="numeric" />
          </label>
          <label>
            <span>یادداشت داخلی (به مشتری نشان داده نمی‌شود)</span>
            <textarea value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} rows={3} />
          </label>
          <label className="check">
            <input type="checkbox" checked={showReport} onChange={(e) => {
              setShowReport(e.target.checked);
              if (!e.target.checked) setShowMedia(false);
            }} />
            <span>نمایش گزارش در لینک مشتری (فقط وجود و مبلغ)</span>
          </label>
          <label className="check">
            <input type="checkbox" checked={showMedia} disabled={!showReport}
              onChange={(e) => setShowMedia(e.target.checked)} />
            <span>نمایش عکس/فیلم در لینک مشتری</span>
          </label>
          {err && <p className="error" role="alert">{err}</p>}
          <button type="submit" disabled={busy} aria-busy={busy}>
            {busy ? "در حال ثبت…" : "ثبت خسارت"}
          </button>
        </form>
      </BottomSheet>
    </section>
  );
}

export function MarkReturnedButton({ caseId, already, onDone }: {
  caseId: string; already: boolean; onDone: () => void;
}) {
  const { notify } = useFeedback();
  const [busy, setBusy] = useState(false);
  if (already) return <p className="notice warn" role="status">این پرونده برگشتی علامت خورده است.</p>;

  return (
    <button type="button" disabled={busy} aria-busy={busy} onClick={async () => {
      const reason = window.prompt("دلیل برگشتی؟ (کار را خودکار باز نمی‌کند)");
      if (!reason?.trim()) return;
      setBusy(true);
      try {
        await api(`/api/v1/cases/${caseId}/mark-returned`, { method: "POST", body: { reason: reason.trim() } });
        notify("برگشتی ثبت شد");
        onDone();
      } catch (e) { notify(e instanceof ApiError ? e.message : "ثبت نشد", "error"); }
      finally { setBusy(false); }
    }}>
      {busy ? "در حال ثبت…" : "ثبت برگشتی"}
    </button>
  );
}
