import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError, api } from "./api";
import { useFeedback } from "./feedback";
import { jalaliDate } from "./license";
import { BottomSheet, SheetOption } from "./sheet";
import { Field, NumberInput, formatNumber } from "./ui";

const errorText = (e: unknown) => (e instanceof ApiError ? e.message : "خطا در ارتباط با سرور");
const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;

type Status = {
  centralParts: number; committedParts: number; headroomParts: number;
  purchasePricePerPartRial: number; salePricePerPartRial: number; profitPerPartRial: number; lowCentralParts: number;
  warnings: string[];
};
type LedgerRow = { id: string; kind: string; parts: number; centralAfter: number; amountRial: number | null; note: string | null; createdAt: string; tenantId: string | null };
type Report = {
  days: number; purchasePricePerPartRial: number; salePricePerPartRial: number; profitPerPartRial: number;
  branchParts: number; arteParts: number; branchMessages: number; arteMessages: number;
  byKey: { kind: string; messageKey: string | null; messages: number; parts: number; saleRial: number; costRial: number }[];
};
type TemplateRequest = {
  id: string; tenantId: string; tenantName: string; messageKey: string; title: string;
  proposedText: string; note: string | null; status: string; createdAt: string; reviewNote: string | null;
};
type Overview = {
  status: Status; branches: { id: string; name: string; balanceParts: number }[]; ledger: LedgerRow[];
  report?: Report; pendingTemplateRequests?: number;
};

const KIND_TITLES: Record<string, string> = {
  central_topup: "خرید شارژ از اسمارت", central_sync: "هم‌سان‌سازی با پنل اسمارت", credit: "شارژ شعبه",
  spend: "ارسال پیام", spend_arte: "ارسال پیام (هزینه‌ی آرته)", refund: "بازگشت پارت",
};
const WARNINGS: Record<string, string> = {
  central_exceeded: "مجموع شارژ شعبه‌ها از موجودی دفتر مرکزی بیشتر است. پنل اسمارت را شارژ کنید و موجودی را ثبت کنید.",
  central_low: "موجودی دفتر مرکزی کم است. پنل اسمارت را شارژ کنید.",
};

function Warnings({ status }: { status: Status }) {
  if (status.warnings.length === 0) return null;
  return (
    <div className="card" role="alert">
      {status.warnings.map((w) => <p key={w}><strong>⚠ هشدار:</strong> {WARNINGS[w] ?? w}</p>)}
    </div>
  );
}

/** One number or an amount: parts win when both are filled. */
function AmountFields({ parts, amount, onParts, onAmount, priceRial, partsLabel }: {
  parts: string; amount: string; onParts: (v: string) => void; onAmount: (v: string) => void; priceRial: number; partsLabel: string;
}) {
  const fromAmount = amount ? Math.floor((Number(amount) * 10) / priceRial) : 0;
  return (
    <>
      <Field label={partsLabel}><NumberInput value={parts} onChange={onParts} max={9} /></Field>
      <Field label="یا مبلغ پرداخت‌شده (تومان)"><NumberInput value={amount} onChange={onAmount} max={12} suffix="تومان" /></Field>
      {!parts && amount && <p className="muted small">معادل <strong className="font-num">{formatNumber(fromAmount)}</strong> پارت با قیمت {toman(priceRial)}.</p>}
    </>
  );
}

/** Central balance, prices, and every branch's credit. */
export function AdminSmsPage({ onBack, onOpen }: { onBack: () => void; onOpen: (id: string) => void }) {
  const { notify } = useFeedback();
  const [data, setData] = useState<Overview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [sheet, setSheet] = useState<null | "topup" | "sync" | "prices" | "requests">(null);
  const [parts, setParts] = useState(""); const [amount, setAmount] = useState(""); const [note, setNote] = useState("");
  const [buy, setBuy] = useState(""); const [sell, setSell] = useState(""); const [low, setLow] = useState("");
  const [requests, setRequests] = useState<TemplateRequest[] | null>(null);
  const [review, setReview] = useState<TemplateRequest | null>(null);

  const load = useCallback(() => api<Overview>("/api/v1/admin/sms").then(setData).catch((e) => setError(errorText(e))), []);
  useEffect(() => { load(); }, [load]);

  async function openRequests() {
    setSheet("requests");
    setRequests(null);
    try { setRequests(await api<TemplateRequest[]>("/api/v1/admin/sms/template-requests")); }
    catch (e) { notify(errorText(e), "error"); setSheet(null); }
  }

  async function run(work: () => Promise<unknown>, done: string) {
    setBusy(true);
    try { await work(); notify(done); setSheet(null); setParts(""); setAmount(""); setNote(""); setReview(null); await load(); }
    catch (e) { notify(errorText(e), "error"); }
    finally { setBusy(false); }
  }

  if (error) return <section className="admin"><button className="link back" onClick={onBack}>→ فهرست</button><p className="error" role="alert">{error}</p></section>;
  if (!data) return <div className="splash" aria-busy="true" />;
  const s = data.status;
  const r = data.report;
  const body = { parts: parts ? Number(parts) : undefined, amountRial: amount ? Number(amount) * 10 : undefined, note: note || undefined };

  return (
    <section className="admin">
      <button className="link back" onClick={onBack}>→ فهرست</button>
      <h2>پیامک و شارژ</h2>
      <Warnings status={s} />

      {(data.pendingTemplateRequests ?? 0) > 0 && (
        <button type="button" className="card admin-facts" onClick={() => void openRequests()} style={{ textAlign: "right", width: "100%" }}>
          <p><strong>⚠ {formatNumber(data.pendingTemplateRequests!)} درخواست تغییر متن پیامک</strong> در انتظار تأیید است.</p>
          <span className="muted small">برای بررسی و تأیید/رد بزنید</span>
        </button>
      )}
      <button type="button" className="block" onClick={() => void openRequests()}>درخواست‌های تغییر متن</button>

      <div className="card admin-facts">
        <h3>شارژ دفتر مرکزی (پنل اسمارت)</h3>
        <p><span className="muted">موجودی:</span> <strong className="font-num">{formatNumber(s.centralParts)}</strong> پارت</p>
        <p><span className="muted">تعهد شعبه‌ها:</span> <span className="font-num">{formatNumber(s.committedParts)}</span> پارت · <span className="muted">آزاد برای تخصیص:</span> <strong className="font-num">{formatNumber(s.headroomParts)}</strong> پارت</p>
        <p className="muted small">خرید {toman(s.purchasePricePerPartRial)} · فروش {toman(s.salePricePerPartRial)} · سود هر پارت {toman(s.profitPerPartRial)}</p>
        <button className="primary block" onClick={() => setSheet("topup")}>ثبت خرید شارژ از اسمارت</button>
        <button className="block" onClick={() => setSheet("sync")}>ثبت موجودی پنل اسمارت</button>
        <button className="block" onClick={() => { setBuy(String(s.purchasePricePerPartRial / 10)); setSell(String(s.salePricePerPartRial / 10)); setLow(String(s.lowCentralParts)); setSheet("prices"); }}>قیمت‌ها</button>
      </div>

      {r && (
        <div className="card admin-facts">
          <h3>گزارش ۳۰ روز اخیر</h3>
          <p><span className="muted">پیام شعبه:</span> <strong className="font-num">{formatNumber(r.branchMessages)}</strong> · <span className="font-num">{formatNumber(r.branchParts)}</span> پارت · فروش {toman(r.branchParts * r.salePricePerPartRial)}</p>
          <p><span className="muted">پیام آرته (OTP و …):</span> <strong className="font-num">{formatNumber(r.arteMessages)}</strong> · <span className="font-num">{formatNumber(r.arteParts)}</span> پارت · هزینه {toman(r.arteParts * r.purchasePricePerPartRial)}</p>
          <p className="muted small">نرخ خرید {toman(r.purchasePricePerPartRial)} · فروش {toman(r.salePricePerPartRial)} · سود هر پارت {toman(r.profitPerPartRial)}</p>
          {r.byKey.length > 0 && (
            <div className="settings-list">
              {r.byKey.map((row) => (
                <div key={`${row.kind}-${row.messageKey}`} className="settings-row static">
                  <span className="settings-row-text">
                    <span>{row.messageKey ?? "—"} {row.kind === "spend_arte" ? "(آرته)" : ""}</span>
                    <span className="muted small">{formatNumber(row.messages)} پیام · {formatNumber(row.parts)} پارت</span>
                  </span>
                  <span className="font-num muted small">{toman(row.costRial)}</span>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      <h3>شارژ شعبه‌ها</h3>
      {data.branches.length === 0 && <p className="muted">هنوز به هیچ شعبه‌ای شارژ داده نشده. از صفحه‌ی هر کسب‌وکار، بخش «پیامک» را باز کنید.</p>}
      <div className="settings-list">
        {data.branches.map((b) => (
          <button key={b.id} type="button" className="settings-row admin-row" onClick={() => onOpen(b.id)}>
            <span className="settings-row-text"><strong>{b.name}</strong></span>
            <span className="font-num">{formatNumber(b.balanceParts)} پارت</span>
            <span className="muted" aria-hidden="true">‹</span>
          </button>
        ))}
      </div>

      <h3>آخرین تغییرات</h3>
      <div className="settings-list">
        {data.ledger.map((l) => (
          <div key={l.id} className="settings-row static">
            <span className="settings-row-text">
              <span>{KIND_TITLES[l.kind] ?? l.kind} <span className="muted small">· {jalaliDate.format(new Date(l.createdAt))}</span></span>
              {l.note && <span className="muted small">{l.note}</span>}
            </span>
            <span className="font-num" dir="ltr">{l.parts > 0 ? "+" : ""}{formatNumber(l.parts)}</span>
          </div>
        ))}
      </div>

      <BottomSheet open={sheet === "topup"} title="خرید شارژ از اسمارت" onClose={() => setSheet(null)}>
        <p className="muted small">بعد از شارژ پنل اسمارت، همان مقدار را اینجا ثبت کنید تا به موجودی مرکزی اضافه شود.</p>
        <AmountFields parts={parts} amount={amount} onParts={setParts} onAmount={setAmount} priceRial={s.purchasePricePerPartRial} partsLabel="تعداد پارت" />
        <Field label="یادداشت"><input value={note} onChange={(e) => setNote(e.target.value)} maxLength={300} /></Field>
        <SheetOption label={busy ? "در حال ثبت…" : "ثبت"} disabled={busy || (!parts && !amount)}
          onClick={() => run(() => api("/api/v1/admin/sms/central/topup", { body }), "شارژ مرکزی ثبت شد")} />
      </BottomSheet>

      <BottomSheet open={sheet === "sync"} title="موجودی پنل اسمارت" onClose={() => setSheet(null)}>
        <p className="muted small">موجودی مرکزی با عددی که در پنل اسمارت می‌بینید برابر می‌شود (تا وقتی اسمارت API موجودی ندهد، دستی).</p>
        <Field label="موجودی پنل (پارت)"><NumberInput value={parts} onChange={setParts} max={9} /></Field>
        <Field label="یادداشت"><input value={note} onChange={(e) => setNote(e.target.value)} maxLength={300} /></Field>
        <SheetOption label={busy ? "در حال ثبت…" : "ثبت موجودی"} disabled={busy || !parts}
          onClick={() => run(() => api("/api/v1/admin/sms/central/sync", { body: { parts: Number(parts), note: note || undefined } }), "موجودی مرکزی به‌روز شد")} />
      </BottomSheet>

      <BottomSheet open={sheet === "prices"} title="قیمت هر پارت" onClose={() => setSheet(null)}>
        <Field label="قیمت خرید از اسمارت"><NumberInput value={buy} onChange={setBuy} max={9} suffix="تومان" /></Field>
        <Field label="قیمت فروش به شعبه"><NumberInput value={sell} onChange={setSell} max={9} suffix="تومان" /></Field>
        <Field label="هشدار وقتی موجودی مرکزی کمتر از (پارت)"><NumberInput value={low} onChange={setLow} max={9} /></Field>
        <SheetOption label={busy ? "در حال ذخیره…" : "ذخیره"} disabled={busy || !buy || !sell}
          onClick={() => run(() => api("/api/v1/admin/sms/prices", { method: "PUT", body: { purchasePricePerPartRial: Number(buy) * 10, salePricePerPartRial: Number(sell) * 10, lowCentralParts: low ? Number(low) : undefined } }), "قیمت‌ها ذخیره شد")} />
      </BottomSheet>

      <BottomSheet open={sheet === "requests" && !review} title="درخواست‌های تغییر متن" onClose={() => setSheet(null)}>
        {!requests && <div className="splash" aria-busy="true" />}
        {requests && requests.length === 0 && <p className="muted">درخواست معلقی نیست.</p>}
        {requests?.map((req) => (
          <button key={req.id} type="button" className="settings-row" onClick={() => setReview(req)}>
            <span className="settings-row-text">
              <strong>{req.tenantName}</strong>
              <span className="muted small">{req.title} · {jalaliDate.format(new Date(req.createdAt))}</span>
            </span>
            <span className="muted" aria-hidden="true">‹</span>
          </button>
        ))}
      </BottomSheet>

      <BottomSheet open={!!review} title={review?.title ?? "بررسی متن"} onClose={() => setReview(null)}>
        {review && (
          <>
            <p className="muted small">{review.tenantName}</p>
            <p className="sms-preview">{review.proposedText}</p>
            {review.note && <p className="hint">توضیح شعبه: {review.note}</p>}
            <SheetOption label={busy ? "در حال تأیید…" : "تأیید و اعمال متن"} disabled={busy}
              onClick={() => run(() => api(`/api/v1/admin/sms/template-requests/${review.id}/approve`, { method: "POST" }), "متن تأیید شد",)} />
            <SheetOption label="رد درخواست" tone="danger" disabled={busy}
              onClick={() => run(() => api(`/api/v1/admin/sms/template-requests/${review.id}/reject`, { method: "POST", body: {} }), "درخواست رد شد")} />
          </>
        )}
      </BottomSheet>
    </section>
  );
}

type Template = {
  key: string; title: string; defaultText: string; draftText: string | null; approvedText: string | null;
  status: "default" | "draft" | "approved"; approvedAt: string | null; needsLink: boolean;
};
type Preview = { text: string; parts: number; length: number; error: string | null };
const STATUS_TEXT = { default: "متن سیستم", draft: "پیش‌نویس (تأیید نشده)", approved: "تأییدشده" } as const;

/** A branch's SMS credit and message texts, inside the business page. */
export function BranchSms({ id }: { id: string }) {
  const { notify } = useFeedback();
  const [wallet, setWallet] = useState<{ parts: number; status: Status; ledger: LedgerRow[] } | null>(null);
  const [tpl, setTpl] = useState<{ templates: Template[]; tags: { tag: string; title: string }[]; maxLength: number } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [credit, setCredit] = useState(false);
  const [parts, setParts] = useState(""); const [amount, setAmount] = useState(""); const [note, setNote] = useState("");
  const [edit, setEdit] = useState<Template | null>(null);
  const [text, setText] = useState("");
  const [preview, setPreview] = useState<Preview | null>(null);
  const area = useRef<HTMLTextAreaElement>(null);

  const load = useCallback(async () => {
    try {
      const [w, t] = await Promise.all([api<typeof wallet>(`/api/v1/admin/sms/businesses/${id}`), api<typeof tpl>(`/api/v1/admin/sms/businesses/${id}/templates`)]);
      setWallet(w); setTpl(t);
    } catch (e) { setError(errorText(e)); }
  }, [id]);
  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    if (!edit) return;
    const h = window.setTimeout(() => {
      api<Preview>(`/api/v1/admin/sms/preview/${edit.key}`, { body: { text } }).then(setPreview).catch(() => setPreview(null));
    }, 300);
    return () => window.clearTimeout(h);
  }, [edit, text]);

  async function run(work: () => Promise<unknown>, done: string, after?: () => void) {
    setBusy(true);
    try { await work(); notify(done); after?.(); await load(); }
    catch (e) { notify(errorText(e), "error"); }
    finally { setBusy(false); }
  }

  const addTag = (tag: string) => {
    const el = area.current;
    const at = el?.selectionStart ?? text.length;
    setText(text.slice(0, at) + tag + text.slice(el?.selectionEnd ?? at));
  };

  if (error) return <p className="error" role="alert">{error}</p>;
  if (!wallet || !tpl) return <div className="splash" aria-busy="true" />;
  const st = wallet.status;
  const give = (sign: 1 | -1) => run(
    () => api(`/api/v1/admin/sms/businesses/${id}/credit`, {
      body: { parts: parts ? sign * Number(parts) : undefined, amountRial: amount ? sign * Number(amount) * 10 : undefined, note: note || undefined },
    }), sign > 0 ? "شارژ شعبه ثبت شد" : "از شارژ شعبه کم شد", () => { setCredit(false); setParts(""); setAmount(""); setNote(""); });

  return (
    <>
      <h3>پیامک</h3>
      <div className="card admin-facts">
        <p><span className="muted">شارژ پیامک شعبه:</span> <strong className="font-num">{formatNumber(wallet.parts)}</strong> پارت</p>
        <p className="muted small">آزاد برای تخصیص از دفتر مرکزی: <span className="font-num">{formatNumber(st.headroomParts)}</span> پارت · قیمت فروش {toman(st.salePricePerPartRial)}</p>
        <Warnings status={st} />
        <button className="primary block" onClick={() => setCredit(true)}>شارژ دستی شعبه</button>
      </div>

      <h3>متن پیامک‌ها</h3>
      <p className="muted small">پیامک فقط با متن تأییدشده‌ی همین شعبه فرستاده می‌شود؛ بدون آن، متن سیستم استفاده می‌شود.</p>
      <div className="settings-list">
        {tpl.templates.map((t) => (
          <button key={t.key} type="button" className="settings-row admin-row" onClick={() => { setEdit(t); setText(t.draftText ?? t.approvedText ?? t.defaultText); setPreview(null); }}>
            <span className="settings-row-text">
              <strong>{t.title}</strong>
              <span className="muted small">{STATUS_TEXT[t.status]}{t.approvedAt && ` · ${jalaliDate.format(new Date(t.approvedAt))}`}</span>
            </span>
            <span className="muted" aria-hidden="true">‹</span>
          </button>
        ))}
      </div>

      <BottomSheet open={credit} title="شارژ دستی شعبه" onClose={() => setCredit(false)}>
        <AmountFields parts={parts} amount={amount} onParts={setParts} onAmount={setAmount} priceRial={st.salePricePerPartRial} partsLabel="تعداد پارت" />
        <Field label="یادداشت"><input value={note} onChange={(e) => setNote(e.target.value)} maxLength={300} /></Field>
        <SheetOption label={busy ? "در حال ثبت…" : "افزودن به شارژ شعبه"} disabled={busy || (!parts && !amount)} onClick={() => give(1)} />
        <SheetOption label="کم کردن از شارژ شعبه" tone="danger" disabled={busy || (!parts && !amount)} onClick={() => give(-1)} />
      </BottomSheet>

      <BottomSheet open={edit !== null} title={edit?.title ?? ""} onClose={() => setEdit(null)}>
        {edit && (
          <>
            <Field label="متن پیامک">
              <textarea ref={area} className="sms-text" value={text} maxLength={tpl.maxLength} onChange={(e) => setText(e.target.value)} />
            </Field>
            <div className="chips" role="group" aria-label="تگ‌ها">
              {tpl.tags.map((t) => <button type="button" key={t.tag} className="chip-button" title={t.title} onClick={() => addTag(t.tag)}>{t.tag}</button>)}
            </div>
            {preview && (
              <div className="card">
                <p className="muted small">پیش‌نمایش با نمونه — حدود <strong className="font-num">{formatNumber(preview.parts)}</strong> پارت · {formatNumber(preview.length)} نویسه</p>
                <p className="sms-preview">{preview.text}</p>
                {preview.error && <p className="error" role="alert">{preview.error}</p>}
              </div>
            )}
            <SheetOption label={busy ? "در حال ذخیره…" : "ذخیره‌ی پیش‌نویس"} disabled={busy || !!preview?.error}
              onClick={() => run(() => api(`/api/v1/admin/sms/businesses/${id}/templates/${edit.key}`, { method: "PUT", body: { text } }), "پیش‌نویس ذخیره شد")} />
            <SheetOption label={busy ? "در حال تأیید…" : "ذخیره و تأیید برای ارسال"} disabled={busy || !!preview?.error}
              onClick={() => run(async () => {
                await api(`/api/v1/admin/sms/businesses/${id}/templates/${edit.key}`, { method: "PUT", body: { text } });
                await api(`/api/v1/admin/sms/businesses/${id}/templates/${edit.key}/approve`, { method: "POST" });
              }, "متن تأیید شد", () => setEdit(null))} />
            <SheetOption label="بازگشت به متن سیستم" tone="danger" disabled={busy}
              onClick={() => run(() => api(`/api/v1/admin/sms/businesses/${id}/templates/${edit.key}/reset`, { method: "POST" }), "به متن سیستم برگشت", () => setEdit(null))} />
          </>
        )}
      </BottomSheet>
    </>
  );
}
