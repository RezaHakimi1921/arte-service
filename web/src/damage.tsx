import { FormEvent, useEffect, useState } from "react";
import { api, ApiError } from "./api";
import { useFeedback } from "./feedback";
import { BottomSheet, SelectField, SelectSheet, SheetOption } from "./sheet";
import { Field, NumberInput, formatNumber, onlyDigits, toLatinDigits } from "./ui";

export type DamageMedia = { id: string; isVideo: boolean };
export type CustomerDamage = {
  id: string; caseId: string; amountRials: number; remainingRials: number;
  internalNote: string | null; showReportToCustomer: boolean; showMediaToCustomer: boolean;
  createdAt: string; media?: DamageMedia[];
};
export type ShopLoss = {
  id: string; caseId: string | null; amountRials: number; internalNote: string | null;
  responsibleUserId: string; responsibleName?: string | null; createdAt: string;
};
type Assignable = { id: string; name: string; role: string };

const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;
const faShort = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });

/** ALERT بالای صفحه: برگشتی فعال روی تحویل، یا پیام بازگشایی پس از reopen (سابقه پاک نمی‌شود). */
export function ReturnBanner({ caseId, canManage, stageKey, stageName, returnMarkedAt, returnReason, onDone }: {
  caseId: string; canManage: boolean; stageKey: string; stageName: string;
  returnMarkedAt?: string | null; returnReason?: string | null;
  onDone: () => void;
}) {
  const { notify } = useFeedback();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const delivered = stageKey === "delivered";

  if (returnMarkedAt && delivered) {
    return (
      <div className="notice warn return-alert" role="alert">
        <strong className="notice-title">در دست بررسی مجدد</strong>
        <span className="notice-body">
          نشانگر برگشتی ثبت شده؛ کار خودکار باز نمی‌شود — از «بازگشایی در ضمانت» استفاده کنید.
          {returnReason ? <> دلیل داخلی: {returnReason}</> : null}
          {" · "}{faShort.format(new Date(returnMarkedAt))}
        </span>
      </div>
    );
  }

  if (returnMarkedAt && !delivered) {
    return (
      <div className="notice good return-alert" role="status">
        <strong className="notice-title">پرونده بازگشایی شد</strong>
        <span className="notice-body">
          مرحلهٔ فعلی: {stageName}. سابقهٔ برگشتی در تاریخچه مانده است
          {returnReason ? <> (دلیل برگشتی: {returnReason})</> : null}.
        </span>
      </div>
    );
  }

  if (!canManage || !delivered) return null;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setErr(null);
    if (!reason.trim()) { setErr("دلیل برگشتی لازم است."); return; }
    setBusy(true);
    try {
      await api(`/api/v1/cases/${caseId}/mark-returned`, { method: "POST", body: { reason: reason.trim() } });
      notify("برگشتی ثبت شد");
      setOpen(false);
      setReason("");
      onDone();
    } catch (ex) {
      setErr(ex instanceof ApiError ? ex.message : "ثبت نشد.");
    } finally { setBusy(false); }
  }

  return (
    <>
      <div className="credit-bar return-alert" role="alert">
        <span>
          <strong>برگشتی؟</strong>
          {" "}مشتری بعد از تحویل برگشته. فقط نشانگر ثبت می‌شود؛ کار را جدا باز کنید.
        </span>
        <button type="button" className="primary" onClick={() => { setErr(null); setOpen(true); }}>
          ثبت برگشتی
        </button>
      </div>
      <BottomSheet open={open} title="ثبت برگشتی" onClose={() => !busy && setOpen(false)}>
        <form className="stack" onSubmit={(e) => { void submit(e); }}>
          <p className="muted small">مرحلهٔ پرونده عوض نمی‌شود و کار خودکار باز نمی‌شود. برای ادامهٔ کار بعداً از بازگشایی در ضمانت استفاده کنید.</p>
          <Field label="دلیل برگشتی" error={err}>
            <textarea value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} rows={3}
              placeholder="مثلاً صدای غیرعادی بعد از تحویل" />
          </Field>
          <button type="submit" className="primary block" disabled={busy || !reason.trim()} aria-busy={busy}>
            {busy ? "در حال ثبت…" : "ثبت برگشتی"}
          </button>
        </form>
      </BottomSheet>
    </>
  );
}

/** یک ماژول خسارت روی پرونده: انتخاب نوع (مشتری / داخلی) سپس فرم همان نوع. */
export function CustomerDamageSection({ caseId, canManage, caseAssigneeId, onChanged }: {
  caseId: string; canManage: boolean; caseAssigneeId?: string | null; onChanged?: () => void;
}) {
  const { notify } = useFeedback();
  const [customers, setCustomers] = useState<CustomerDamage[] | null>(null);
  const [losses, setLosses] = useState<ShopLoss[] | null>(null);
  const [staff, setStaff] = useState<Assignable[]>([]);
  const [pickOpen, setPickOpen] = useState(false);
  const [customerOpen, setCustomerOpen] = useState(false);
  const [lossOpen, setLossOpen] = useState(false);
  const [voidId, setVoidId] = useState<string | null>(null);
  const [voidReason, setVoidReason] = useState("");
  const [deleteLossId, setDeleteLossId] = useState<string | null>(null);
  const [pickStaff, setPickStaff] = useState(false);
  const [busy, setBusy] = useState(false);
  const [amount, setAmount] = useState("");
  const [note, setNote] = useState("");
  const [showReport, setShowReport] = useState(false);
  const [showMedia, setShowMedia] = useState(false);
  const [responsibleId, setResponsibleId] = useState<string | null>(caseAssigneeId ?? null);
  const [err, setErr] = useState<string | null>(null);
  const [voidErr, setVoidErr] = useState<string | null>(null);

  const load = async () => {
    try {
      const [cRows, lRows] = await Promise.all([
        api<CustomerDamage[]>(`/api/v1/cases/${caseId}/customer-damages`),
        api<ShopLoss[]>(`/api/v1/cases/${caseId}/shop-losses`),
      ]);
      setCustomers(cRows);
      setLosses(lRows);
    } catch (e) {
      notify(e instanceof ApiError ? e.message : "بارگذاری خسارت‌ها نشد", "error");
    }
  };

  useEffect(() => { void load(); }, [caseId]);
  useEffect(() => {
    if (!canManage) return;
    api<Assignable[]>("/api/v1/staff/assignable").then(setStaff).catch(() => {});
  }, [canManage]);
  useEffect(() => {
    if (caseAssigneeId) setResponsibleId((prev) => prev ?? caseAssigneeId);
  }, [caseAssigneeId]);

  function resetCustomerForm() {
    setAmount(""); setNote(""); setShowReport(false); setShowMedia(false); setErr(null);
  }
  function resetLossForm() {
    setAmount(""); setNote(""); setErr(null);
    setResponsibleId(caseAssigneeId ?? null);
  }

  async function submitCustomer(e: FormEvent) {
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
      notify(showReport
        ? "خسارت مشتری ثبت شد و در لینک (وجود و مانده) دیده می‌شود"
        : "خسارت مشتری ثبت شد — تا «نمایش در لینک» روشن نشود مشتری نمی‌بیند");
      setCustomerOpen(false);
      resetCustomerForm();
      await load();
      onChanged?.();
    } catch (ex) {
      setErr(ex instanceof ApiError ? ex.message : "ثبت نشد.");
    } finally { setBusy(false); }
  }

  async function submitLoss(e: FormEvent) {
    e.preventDefault();
    setErr(null);
    const rials = Number(onlyDigits(toLatinDigits(amount))) * 10;
    if (!Number.isFinite(rials) || rials <= 0) { setErr("مبلغ لازم است."); return; }
    if (!responsibleId) { setErr("مسئول لازم است."); return; }
    setBusy(true);
    try {
      await api("/api/v1/shop-losses", {
        method: "POST",
        body: {
          amountRials: rials,
          internalNote: note.trim() || null,
          responsibleUserId: responsibleId,
          caseId,
        },
      });
      notify("زیان داخلی ثبت شد");
      setLossOpen(false);
      resetLossForm();
      await load();
      onChanged?.();
    } catch (ex) {
      setErr(ex instanceof ApiError ? ex.message : "ثبت نشد.");
    } finally { setBusy(false); }
  }

  async function confirmVoid(e: FormEvent) {
    e.preventDefault();
    if (!voidId) return;
    setVoidErr(null);
    if (!voidReason.trim()) { setVoidErr("علت ابطال لازم است."); return; }
    setBusy(true);
    try {
      await api(`/api/v1/cases/${caseId}/customer-damages/${voidId}/void`, {
        method: "POST", body: { reason: voidReason.trim() },
      });
      notify("گزارش خسارت مشتری ابطال شد");
      setVoidId(null);
      setVoidReason("");
      await load();
      onChanged?.();
    } catch (ex) {
      setVoidErr(ex instanceof ApiError ? ex.message : "ابطال نشد.");
    } finally { setBusy(false); }
  }

  async function confirmDeleteLoss() {
    if (!deleteLossId) return;
    setBusy(true);
    try {
      await api(`/api/v1/shop-losses/${deleteLossId}`, { method: "DELETE" });
      notify("زیان داخلی حذف شد");
      setDeleteLossId(null);
      await load();
      onChanged?.();
    } catch (ex) {
      notify(ex instanceof ApiError ? ex.message : "حذف نشد", "error");
    } finally { setBusy(false); }
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

  if (customers === null || losses === null) {
    return <p className="muted splash" aria-busy="true">در حال بارگذاری خسارت‌ها…</p>;
  }

  const empty = customers.length === 0 && losses.length === 0;
  const staffItems = staff.map((s) => ({ value: s.id, label: s.name, hint: s.role }));

  return (
    <section className="card damage-section">
      <header className="row between">
        <div>
          <h2 className="h3">خسارت</h2>
          <p className="muted small">بستانکاری مشتری یا زیان داخلی مجموعه — هر دو از همین‌جا ثبت می‌شوند.</p>
        </div>
        {canManage && (
          <button type="button" className="primary" onClick={() => setPickOpen(true)}>
            ثبت خسارت
          </button>
        )}
      </header>

      {empty ? (
        <p className="muted empty-hint">هنوز خسارتی ثبت نشده. نوع را انتخاب کنید: به مشتری، یا زیان داخلی مجموعه.</p>
      ) : (
        <ul className="damage-list">
          {customers.map((r) => (
            <li key={r.id} className="damage-card">
              <div className="row between">
                <span className="chip stage-blue">به مشتری</span>
                <span className={`chip ${r.remainingRials > 0 ? "stage-orange" : "stage-green"}`}>
                  مانده {toman(r.remainingRials)}
                </span>
              </div>
              <strong>بستانکاری {toman(r.amountRials)}</strong>
              {r.internalNote && <p className="small">{r.internalNote}</p>}
              <p className="muted small">
                لینک: {r.showReportToCustomer ? "وجود و مانده" : "مخفی"}
                {" · "}رسانه: {r.showMediaToCustomer ? "روشن" : "خاموش"}
                {" · "}فایل: {formatNumber(r.media?.length ?? 0)}
              </p>
              {!r.showReportToCustomer && (
                <p className="hint">یادداشت داخلی هرگز عمومی نمی‌شود؛ برای دیدن مشتری در لینک، هنگام ثبت «نمایش در لینک» را روشن کنید.</p>
              )}
              {canManage && (
                <div className="quick-actions">
                  <label className="btn-file">
                    افزودن عکس یا فیلم
                    <input type="file" accept="image/*,video/*" hidden disabled={busy}
                      onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(r.id, f); e.target.value = ""; }} />
                  </label>
                  <button type="button" className="danger" disabled={busy}
                    onClick={() => { setVoidId(r.id); setVoidReason(""); setVoidErr(null); }}>
                    ابطال
                  </button>
                </div>
              )}
            </li>
          ))}
          {losses.map((r) => (
            <li key={r.id} className="damage-card loss">
              <div className="row between">
                <span className="chip stage-orange">زیان داخلی</span>
                <strong>{toman(r.amountRials)}</strong>
              </div>
              {r.responsibleName && <p className="small">مسئول: {r.responsibleName}</p>}
              {r.internalNote && <p className="small">{r.internalNote}</p>}
              <p className="muted small">هرگز در لینک مشتری دیده نمی‌شود · {faShort.format(new Date(r.createdAt))}</p>
              {canManage && (
                <div className="quick-actions">
                  <button type="button" className="danger" disabled={busy}
                    onClick={() => setDeleteLossId(r.id)}>
                    حذف
                  </button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}

      <BottomSheet open={pickOpen} title="ثبت خسارت" onClose={() => setPickOpen(false)}>
        <div className="stack">
          <p className="muted small">کدام نوع را ثبت می‌کنید؟</p>
          <SheetOption
            label="خسارت به مشتری"
            hint="بستانکاری مشتری روی پرونده؛ در صورت نیاز در لینک دیده می‌شود"
            tone="primary"
            onClick={() => { setPickOpen(false); resetCustomerForm(); setCustomerOpen(true); }}
          />
          <SheetOption
            label="خسارت داخلی (زیان مجموعه)"
            hint="زیان مغازه با مسئول مشخص؛ هرگز به مشتری نشان داده نمی‌شود"
            onClick={() => { setPickOpen(false); resetLossForm(); setLossOpen(true); }}
          />
        </div>
      </BottomSheet>

      <BottomSheet open={customerOpen} title="ثبت خسارت به مشتری" onClose={() => !busy && setCustomerOpen(false)}>
        <form className="stack" onSubmit={(e) => { void submitCustomer(e); }}>
          <Field label="مبلغ بستانکاری مشتری (تومان)" error={err}>
            <NumberInput value={amount} onChange={(v) => { setAmount(v); if (err) setErr(null); }} inputMode="numeric" />
          </Field>
          <label>
            <span>یادداشت داخلی (به مشتری نشان داده نمی‌شود)</span>
            <textarea value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} rows={3} />
          </label>
          <label className="check">
            <input type="checkbox" checked={showReport} onChange={(e) => {
              setShowReport(e.target.checked);
              if (!e.target.checked) setShowMedia(false);
            }} />
            <span>نمایش در لینک مشتری (فقط وجود و مانده — بدون شرح)</span>
          </label>
          {!showReport && (
            <p className="hint">پیش‌فرض خاموش است؛ تا وقتی روشن نکنید مشتری در لینک چیزی از خسارت نمی‌بیند.</p>
          )}
          <label className="check">
            <input type="checkbox" checked={showMedia} disabled={!showReport}
              onChange={(e) => setShowMedia(e.target.checked)} />
            <span>نمایش عکس/فیلم در لینک مشتری</span>
          </label>
          <button type="submit" className="primary block"
            disabled={busy || !onlyDigits(toLatinDigits(amount))} aria-busy={busy}>
            {busy ? "در حال ثبت…" : "ثبت خسارت به مشتری"}
          </button>
        </form>
      </BottomSheet>

      <BottomSheet open={lossOpen} title="ثبت زیان داخلی" onClose={() => !busy && setLossOpen(false)}>
        <form className="stack" onSubmit={(e) => { void submitLoss(e); }}>
          <p className="muted small">این مبلغ بستانکاری مشتری نیست و در لینک مشتری دیده نمی‌شود.</p>
          <Field label="مبلغ زیان (تومان)" error={err && err.includes("مسئول") ? undefined : err ?? undefined}>
            <NumberInput value={amount} onChange={(v) => { setAmount(v); if (err) setErr(null); }} inputMode="numeric" />
          </Field>
          <div>
            <span className="muted small">مسئول</span>
            <SelectField
              label={staff.find((s) => s.id === responsibleId)?.name}
              placeholder={staff.length === 0 ? "در حال بارگذاری…" : "انتخاب همکار"}
              invalid={!!err && !responsibleId}
              onOpen={() => setPickStaff(true)}
            />
            <SelectSheet
              open={pickStaff}
              title="مسئول زیان"
              value={responsibleId}
              items={staffItems}
              onClose={() => setPickStaff(false)}
              onSelect={(v) => { setResponsibleId(v); setPickStaff(false); if (err) setErr(null); }}
            />
            {err && !responsibleId && <span className="error">{err}</span>}
          </div>
          <label>
            <span>یادداشت داخلی</span>
            <textarea value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} rows={3}
              placeholder="مثلاً شکستگی ابزار · اشتباه سفارش" />
          </label>
          <button type="submit" className="primary block"
            disabled={busy || !onlyDigits(toLatinDigits(amount)) || !responsibleId} aria-busy={busy}>
            {busy ? "در حال ثبت…" : "ثبت زیان داخلی"}
          </button>
        </form>
      </BottomSheet>

      <BottomSheet open={!!voidId} title="ابطال گزارش خسارت مشتری" onClose={() => !busy && setVoidId(null)}>
        <form className="stack" onSubmit={(e) => { void confirmVoid(e); }}>
          <p className="muted small">گزارش از لیست فعال خارج می‌شود؛ علت برای سابقه لازم است.</p>
          <Field label="علت ابطال" error={voidErr}>
            <textarea value={voidReason} onChange={(e) => setVoidReason(e.target.value)} maxLength={500} rows={3} />
          </Field>
          <button type="submit" className="danger block" disabled={busy || !voidReason.trim()} aria-busy={busy}>
            {busy ? "در حال ابطال…" : "ابطال گزارش"}
          </button>
        </form>
      </BottomSheet>

      <BottomSheet open={!!deleteLossId} title="حذف زیان داخلی" onClose={() => !busy && setDeleteLossId(null)}>
        <div className="stack">
          <p>این زیان از فهرست فعال برداشته شود؟ در تاریخچه پرونده می‌ماند.</p>
          <button type="button" className="danger block" disabled={busy} aria-busy={busy}
            onClick={() => { void confirmDeleteLoss(); }}>
            {busy ? "در حال حذف…" : "بله، حذف شود"}
          </button>
          <button type="button" className="block" disabled={busy} onClick={() => setDeleteLossId(null)}>انصراف</button>
        </div>
      </BottomSheet>
    </section>
  );
}
