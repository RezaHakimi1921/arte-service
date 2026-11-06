import { useEffect, useState, type ReactNode } from "react";
import { ApiError, api } from "./api";
import { applyTheme } from "./App";
import { WAIT_REASONS } from "./labels";
import { formatNumber } from "./ui";
import { compress } from "./photos";
import { BottomSheet } from "./sheet";

type Change = { title: string; state: "kept" | "removed" | "added" };

type Survey = { state: "open" | "answered" | "expired"; questions: { id: string; text: string }[] | null; score: number | null };

type Track = {
  shop: { name: string; phone: string | null; address: string | null; instagram?: string | null; maps?: { key: string; name: string; url: string }[] };
  pay?: { card: { cardNumber: string; holderName: string; bank: string | null } | null; receipts: { status: string; claimedRials: number | null; createdAt: string; note: string | null }[] };
  survey: Survey | null;
  photos: { id: string; stageKey: string; caption: string | null; createdAt: string; isVideo?: boolean }[];
  case: {
    customer: string | null; openedAt: string; promisedAt: string | null; closedAt: string | null; warrantyUntil: string | null;
    vehicle: { title: string; kind: string; identifier: string | null } | null; reportedProblems: string[]; requestedServices: string[];
    /** Services at intake against now: kept, taken off ("removed") or put on after intake ("added"). */
    serviceChanges?: Change[];
    /** The same for the reported problems. */
    problemChanges?: Change[];
  };
  status: { name: string; key: string; category: string; waitReason: string | null; stageEnteredAt: string };
  stages: { name: string; key: string; state: "done" | "current" | "todo"; at: string | null }[] | null;
  items: { kind: string; title: string; quantity: number; status: string; supplier: string; lineTotalRials: number | null; discountRials?: number | null; image?: string | null }[] | null;
  money: { totalRials: number; paidRials: number; balanceRials: number; subtotalRials?: number; discountRials?: number; payments?: { amountRials: number; method: string; paidAt: string }[] } | null;
};

type IconName = "calendar" | "camera" | "car" | "moto" | "check" | "plus" | "clock" | "close" | "document" | "moon" | "phone" | "sparkle" | "star" | "sun" | "wrench" | "wallet" | "box" | "chevron" | "back" | "copy" | "pin" | "play";

function Icon({ name }: { name: IconName }) {
  const paths: Record<IconName, ReactNode> = {
    calendar: <><rect x="3" y="5" width="18" height="16" rx="3" /><path d="M8 3v4M16 3v4M3 10h18" /></>,
    camera: <><path d="M4 7h3l1.4-2h7.2L17 7h3a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2Z" /><circle cx="12" cy="13" r="4" /></>,
    car: <><path d="m5 11 1.5-4h11l1.5 4M3 11h18v7H3z" /><path d="M5 18v2M19 18v2M6.5 15h.01M17.5 15h.01" /></>,
    moto: <path d="M5 17a3 3 0 1 0 0-.01M19 17a3 3 0 1 0 0-.01M5 17l4-7h5l5 7M12 10l-2-4H7M15 6h3" />,
    check: <path d="m5 12 4 4L19 6" />,
    plus: <path d="M12 5v14M5 12h14" />,
    clock: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>,
    close: <path d="M6 6l12 12M18 6 6 18" />,
    document: <><path d="M6 2h8l4 4v16H6z" /><path d="M14 2v5h5M9 12h6M9 16h6" /></>,
    moon: <path d="M20.5 14.2A8.4 8.4 0 0 1 9.8 3.5a9 9 0 1 0 10.7 10.7Z" />,
    phone: <path d="M5 3h4l2 5-2.5 1.5a15 15 0 0 0 6 6L16 13l5 2v4a2 2 0 0 1-2 2C10.2 20.5 3.5 13.8 3 5a2 2 0 0 1 2-2Z" />,
    sparkle: <path d="m12 3 1.2 3.8L17 8l-3.8 1.2L12 13l-1.2-3.8L7 8l3.8-1.2L12 3Zm7 11 .7 2.3L22 17l-2.3.7L19 20l-.7-2.3L16 17l2.3-.7L19 14ZM5 13l.7 2.3L8 16l-2.3.7L5 19l-.7-2.3L2 16l2.3-.7L5 13Z" />,
    star: <path d="m12 3 2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9Z" />,
    sun: <><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></>,
    wrench: <path d="M14.7 6.3a4.5 4.5 0 0 0-5.6 5.6L3 18l3 3 6.1-6.1a4.5 4.5 0 0 0 5.6-5.6L15 12l-3-3 2.7-2.7Z" />,
    box: <><path d="M21 8 12 3 3 8v8l9 5 9-5Z" /><path d="m3 8 9 5 9-5M12 13v8" /></>,
    chevron: <path d="m6 9 6 6 6-6" />,
    back: <path d="m15 6-6 6 6 6" />,
    copy: <><rect x="9" y="9" width="11" height="11" rx="2" /><path d="M5 15V6a2 2 0 0 1 2-2h8" /></>,
    pin: <><path d="M12 21s7-6.100 7-11a7 7 0 0 0-14 0c0 4.900 7 11 7 11Z" /><circle cx="12" cy="10" r="2.500" /></>,
    play: <path d="m8 5 11 7-11 7Z" />,
    wallet: <><rect x="3" y="6" width="18" height="14" rx="3" /><path d="M3 10h18M16 15h2" /></>,
  };
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      {paths[name]}
    </svg>
  );
}

const cardGroups = (n: string) => n.replace(/(\d{4})(?=\d)/g, "$1 ");

/**
 * Pay by card, in a sheet: the shop's card to copy, then the bank receipt as a photo. The customer never types an
 * amount: the shop reads it off the receipt and records what was really paid (possibly only part of the invoice).
 */
function PaySheet({ open, onClose, code, pay, owe, onSent }: { open: boolean; onClose: () => void; code: string; pay: NonNullable<Track["pay"]>; owe: boolean; onSent: () => void }) {
  const [copied, setCopied] = useState(false);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<{ text: string; ok: boolean } | null>(null);

  async function copy() {
    try { await navigator.clipboard.writeText(pay.card!.cardNumber); setCopied(true); setTimeout(() => setCopied(false), 2000); }
    catch { setMsg({ text: "کپی نشد؛ شماره را به‌صورت دستی بنویسید.", ok: false }); }
  }

  async function send(files: FileList | null) {
    const file = files?.[0];
    if (!file) return;
    setBusy(true); setMsg(null);
    try {
      const blob = await compress(file);
      const form = new FormData();
      form.append("file", blob, "receipt.jpg");
      await api(`/api/v1/track/${code}/receipts`, { body: form });
      setMsg({ text: "رسید ارسال شد؛ بعد از بررسی تعمیرگاه، پرداخت شما ثبت می‌شود.", ok: true });
      onSent();
    } catch (err) {
      setMsg({ text: err instanceof ApiError ? err.message : "ارسال رسید انجام نشد.", ok: false });
    } finally { setBusy(false); }
  }

  const label: Record<string, string> = { pending: "در انتظار بررسی", approved: "تأیید شد", rejected: "رد شد" };
  return (
    <BottomSheet open={open} title="کارت به کارت" onClose={onClose}>
      <div className="tk-sheet-body">
        {pay.card && (
          <>
            <p className="tk-sheet-note">مبلغ را به این کارت واریز کنید، سپس عکس رسید را بفرستید.</p>
            <div className="tk-bankcard">
              <span className="tk-bankcard-bank">{pay.card.bank ?? "کارت بانکی"}</span>
              <strong dir="ltr" className="font-num">{cardGroups(pay.card.cardNumber)}</strong>
              <span className="tk-bankcard-holder">به نام {pay.card.holderName}</span>
            </div>
            <button type="button" className={`tk-copy${copied ? " done" : ""}`} onClick={copy}>
              <Icon name={copied ? "check" : "copy"} />{copied ? "شماره کارت کپی شد" : "کپی شماره کارت"}
            </button>
          </>
        )}
        {owe && (
          <label className={`tk-upload${busy ? " busy" : ""}`} aria-busy={busy}>
            <input type="file" accept="image/*" hidden disabled={busy} onChange={(e) => { void send(e.target.files); e.target.value = ""; }} />
            <Icon name="camera" />{busy ? "در حال ارسال رسید…" : "ارسال عکس رسید واریز"}
          </label>
        )}
        {msg && <p className={msg.ok ? "tk-sheet-ok" : "tk-error"} role="status">{msg.text}</p>}
        {pay.receipts.length > 0 && (
          <ul className="tk-receipts">
            {pay.receipts.map((r, i) => (
              <li key={i}><span>{when(r.createdAt)}</span><b className={`st-${r.status}`}>{label[r.status] ?? r.status}</b>{r.status === "rejected" && r.note && <small>{r.note}</small>}</li>
            ))}
          </ul>
        )}
      </div>
    </BottomSheet>
  );
}

/** Instagram's camera-in-a-rounded-square mark, so the handle is recognised at a glance. */
function InstagramIcon({ size = 24 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} aria-hidden="true">
      <defs>
        <linearGradient id="ig-grad" x1="0" y1="1" x2="1" y2="0">
          <stop offset="0" stopColor="#feda75" /><stop offset="0.35" stopColor="#fa7e1e" /><stop offset="0.6" stopColor="#d62976" /><stop offset="1" stopColor="#4f5bd5" />
        </linearGradient>
      </defs>
      <rect x="2" y="2" width="20" height="20" rx="6" fill="url(#ig-grad)" />
      <rect x="6.500" y="6.500" width="11" height="11" rx="3.500" fill="none" stroke="#fff" strokeWidth="1.600" />
      <circle cx="12" cy="12" r="2.700" fill="none" stroke="#fff" strokeWidth="1.600" />
      <circle cx="16" cy="8" r="0.900" fill="#fff" />
    </svg>
  );
}

/** The mark of each navigation app, so customers recognise it without reading. */
function MapIcon({ name }: { name: string }) {
  const common = { viewBox: "0 0 48 48", width: 36, height: 36, "aria-hidden": true } as const;
  switch (name) {
    case "google":
      return (
        <svg {...common}>
          <path d="M24 4a14 14 0 0 0-14 14c0 10 14 26 14 26s14-16 14-26A14 14 0 0 0 24 4Z" fill="#34a853" />
          <path d="M24 4a14 14 0 0 0-11.6 6.2L24 24Z" fill="#ea4335" />
          <path d="M12.4 10.2A14 14 0 0 0 10 18c0 3.2 1.4 7 3.4 10.6L24 24Z" fill="#fbbc04" />
          <path d="M24 4a14 14 0 0 1 14 14c0 3.2-1.4 7-3.4 10.6L24 24Z" fill="#4285f4" />
          <circle cx="24" cy="18" r="5.5" fill="#fff" />
        </svg>
      );
    case "waze":
      return (
        <svg {...common}>
          <path d="M24 6c-9 0-16 6.5-16 14.5 0 3 1 5.8 2.700 8.100-.4 2.600-1.800 4.600-3.200 5.700 3.200.300 6-.800 7.900-2.400 2.400 1.200 5 1.800 8.600 1.800 9 0 16-6.500 16-14.500S33 6 24 6Z" fill="#33ccff" stroke="#0a2540" strokeWidth="2.200" strokeLinejoin="round" />
          <circle cx="18.500" cy="19" r="2.200" fill="#0a2540" /><circle cx="29.500" cy="19" r="2.200" fill="#0a2540" />
          <path d="M18 25c2 2.500 10 2.500 12 0" fill="none" stroke="#0a2540" strokeWidth="2.200" strokeLinecap="round" />
          <circle cx="16" cy="38" r="3.500" fill="#0a2540" /><circle cx="32" cy="38" r="3.500" fill="#0a2540" />
        </svg>
      );
    case "neshan":
      return (
        <svg {...common}>
          <rect x="4" y="4" width="40" height="40" rx="11" fill="#1f6feb" />
          <path d="M24 10 33 36l-9-4.500L15 36Z" fill="#fff" />
        </svg>
      );
    case "balad":
      return (
        <svg {...common}>
          <rect x="4" y="4" width="40" height="40" rx="11" fill="#1aa86c" />
          <path d="M24 11a9 9 0 0 0-9 9c0 7 9 17 9 17s9-10 9-17a9 9 0 0 0-9-9Z" fill="#fff" />
          <circle cx="24" cy="20" r="3.500" fill="#1aa86c" />
        </svg>
      );
    default:
      return <svg {...common}><circle cx="24" cy="24" r="16" fill="var(--accent)" /></svg>;
  }
}

function Section({ icon, eyebrow, title, action, children }: { icon: IconName; eyebrow: string; title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <section className="tk-card">
      <div className="tk-section-title">
        <span className="tk-section-icon"><Icon name={icon} /></span>
        <div><span>{eyebrow}</span><h2>{title}</h2></div>
        {action}
      </div>
      {children}
    </section>
  );
}

/** One request list: count badges, then pill tags (problems) or full-width rows (services), changed ones first. */
function ChangeList({ label, items, open, rows }: { label: string; items: Change[]; open: boolean; rows?: boolean }) {
  const count = (state: Change["state"]) => items.filter((x) => x.state === state).length;
  const changed = items.some((x) => x.state !== "kept");
  const rank = { added: 0, removed: 1, kept: 2 } as const;
  const sorted = changed ? [...items].sort((a, b) => rank[a.state] - rank[b.state]) : items;
  let keptSeen = 0;
  const visible = open ? sorted : sorted.filter((x) => x.state !== "kept" || ++keptSeen <= 3);
  const badges: { state: Change["state"]; text: string }[] = [
    { state: "kept", text: "فعلی" }, { state: "added", text: "اضافه" }, { state: "removed", text: "حذف" },
  ];
  const mark = (state: Change["state"]) => (
    <span className="svc-mark" aria-hidden="true">{state === "added" ? "+" : state === "removed" ? "×" : "•"}</span>
  );
  return (
    <div className="svc-block">
      <div className="svc-head">
        <span className="tk-list-label">{label}</span>
        {changed && (
          <span className="svc-pills">
            {badges.map(({ state, text }) => count(state) > 0 && (
              <span key={state} className={`svc-pill svc-pill-${state}`}>
                {state === "added" ? "+ " : state === "removed" ? "× " : ""}{text} {formatNumber(count(state))}
              </span>
            ))}
          </span>
        )}
      </div>
      <ul className={rows ? "svc-rows" : "svc-tags"}>
        {visible.map((x) => (
          <li key={x.title} className={`svc-${x.state}`}>
            {mark(x.state)}
            {x.state === "removed" ? <del>{x.title}</del> : <span>{x.title}</span>}
            {x.state !== "kept" && <span className="tk-sr">{x.state === "added" ? "، اضافه شد" : "، حذف شد"}</span>}
          </li>
        ))}
      </ul>
      {visible.length < items.length && <span className="svc-more">و {formatNumber(items.length - visible.length)} مورد دیگر</span>}
    </div>
  );
}

const day = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long", year: "numeric" });
const dayTime = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { weekday: "long", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" });
const time = new Intl.DateTimeFormat("fa-IR", { hour: "2-digit", minute: "2-digit" });
const shortDay = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long" });
const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;
const PAY_METHOD: Record<string, string> = { cash: "نقد", card: "کارت‌خوان", transfer: "کارت به کارت", other: "سایر" };
const KIND: Record<string, string> = { part: "کالا", labor: "اجرت", service: "اجرت" };
const STAGE_NAMES: Record<string, string> = {
  received: "پذیرش", diagnosing: "عیب‌یابی", awaiting_approval: "تأیید", awaiting_parts: "انتظار قطعه",
  repairing: "تعمیر", ready: "آماده‌ی تحویل", delivered: "تحویل",
};

function when(iso: string) {
  const d = new Date(iso);
  return new Date().toDateString() === d.toDateString() ? `امروز، ${time.format(d)}` : `${shortDay.format(d)}، ${time.format(d)}`;
}

/** One plain sentence for where the vehicle is now, and what happens next. */
function now(t: Track): { title: string; note: string } {
  const s = t.status;
  if (s.key === "delivered") return { title: "وسیله‌ی شما تحویل داده شد", note: "از اعتمادتان سپاسگزاریم. سابقه‌ی کار و ضمانت همین‌جا می‌ماند." };
  if (s.category === "cancelled") return { title: "این پرونده لغو شده است", note: "برای جزئیات با تعمیرگاه تماس بگیرید." };
  if (s.key === "ready") return { title: "وسیله‌ی شما آماده‌ی تحویل است", note: "می‌توانید برای تحویل مراجعه کنید." };
  if (s.waitReason) return { title: WAIT_REASONS[s.waitReason] ?? "کار موقتاً متوقف است", note: "به‌محض رفع، کار ادامه پیدا می‌کند و این صفحه به‌روز می‌شود." };
  if (s.key === "diagnosing") return { title: "کارشناس در حال بررسی علت ایراد است", note: "پس از عیب‌یابی، کار و هزینه‌ها در همین صفحه نشان داده می‌شود." };
  if (s.key === "repairing") return { title: "وسیله‌ی شما در حال تعمیر است", note: "قطعات و کارهای انجام‌شده را پایین‌تر می‌بینید." };
  return { title: "وسیله‌ی شما پذیرش شد و در نوبت کار است", note: "با شروع کار، مراحل در همین صفحه به‌روز می‌شود." };
}

/**
 * The master's final review is an inside step. The server already hides it; this keeps an older server's answer
 * from showing it: the step is dropped from the list and, while it runs, the stage before it is the current one.
 */
function withoutInternalSteps(t: Track): Track {
  if (!t.stages?.some((s) => s.key === "review") && t.status.key !== "review") return t;
  const list = t.stages ?? [];
  const at = list.findIndex((s) => s.key === "review");
  const wasCurrent = at >= 0 && list[at].state === "current";
  const stages = list.filter((s) => s.key !== "review").map((s) => ({ ...s }));
  if (wasCurrent) {
    for (let i = stages.length - 1; i >= 0; i--) if (stages[i].state === "done") { stages[i].state = "current"; break; }
  }
  const status = t.status.key === "review" ? { ...t.status, key: "repairing", name: "در حال تعمیر", category: "active" } : t.status;
  return { ...t, stages: t.stages ? stages : null, status };
}

const RATING_WORDS = ["", "خیلی بد", "بد", "معمولی", "خوب", "عالی"];

/**
 * The survey tab: a navy header with the customer's name, a progress line, numbered questions, an optional note,
 * then a thank-you page with the overall score. One answer per case.
 */
function SurveyView({ code, survey, customer, vehicle, shop, instagram, onDone, onBack }: {
  code: string; survey: Survey; customer: string | null; vehicle: string | null; shop: string; instagram?: string | null; onDone: () => void; onBack: () => void;
}) {
  const [ratings, setRatings] = useState<Record<string, number>>({});
  const [comment, setComment] = useState("");
  const [step, setStep] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (survey.state !== "open") {
    const score = survey.score ?? 0;
    const r = Math.min(5, Math.max(1, Math.round(score)));
    return (
      <div className="tk-content">
        <section className="tk-card tk-thanks">
          <span className="tk-thanks-icon"><Icon name={survey.state === "answered" ? "check" : "clock"} /></span>
          {survey.state === "answered" ? (
            <>
              <p className="tk-thanks-eyebrow">نظر شما ثبت شد</p>
              <h1>ممنون که کمک می‌کنید بهتر شویم</h1>
              <p className="tk-thanks-note">پاسخ شما مستقیم به مدیریت {shop} رسید.</p>
              {instagram && (
                <a className="tk-follow" href={`https://instagram.com/${encodeURIComponent(instagram)}`} target="_blank" rel="noopener noreferrer">
                  <InstagramIcon size={32} />
                  <span><strong>ما را در اینستاگرام دنبال کنید</strong><span dir="ltr" className="font-num">@{instagram}</span></span>
                </a>
              )}
              {score > 0 && (
                <div className="tk-thanks-score">
                  <span>امتیاز کلی شما</span>
                  <div className={`tk-thanks-stars r${r}`} aria-label={`${formatNumber(r)} از ۵`}>
                    {[1, 2, 3, 4, 5].map((v) => <span key={v} className={v <= r ? "on" : ""}><Icon name="star" /></span>)}
                  </div>
                </div>
              )}
            </>
          ) : (
            <>
              <h1>زمان این نظرسنجی تمام شده است</h1>
              <p className="tk-thanks-note">برای گفتن نظرتان می‌توانید با تعمیرگاه تماس بگیرید.</p>
            </>
          )}
          <button type="button" className="tk-submit" onClick={onBack}>بازگشت به صفحه‌ی پیگیری</button>
        </section>
      </div>
    );
  }

  const questions = survey.questions ?? [];
  // One question per step, then a last step for the optional note.
  const total = questions.length + 1;
  const last = step >= questions.length;
  const answered = questions.filter((q) => ratings[q.id]).length;
  const percent = Math.round((Math.min(step, questions.length) / total) * 100 + (last ? 100 / total : 0));
  const q = questions[step];
  const pick = (v: number) => {
    setRatings((r) => ({ ...r, [q.id]: v }));
    setError(null);
    // Move on by itself shortly after the choice, so the answer is seen first.
    window.setTimeout(() => setStep((x) => Math.min(x + 1, questions.length)), 260);
  };
  async function submit() {
    if (answered < questions.length) { setError("لطفاً به همه‌ی سؤال‌ها جواب دهید."); return; }
    setBusy(true);
    setError(null);
    try {
      await api(`/api/v1/track/${encodeURIComponent(code)}/survey`, {
        method: "POST", body: { answers: questions.map((x) => ({ questionId: x.id, rating: ratings[x.id] })), comment: comment.trim() || undefined },
      });
      window.scrollTo(0, 0);
      onDone();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "ثبت نشد. اتصال را بررسی کنید و دوباره بزنید.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <section className="tk-survey-hero">
        <span className="tk-chip"><Icon name="sparkle" />کمتر از یک دقیقه</span>
        <h1>{customer ? `${customer} عزیز، نظرتان برای ما مهم است` : "نظرتان برای ما مهم است"}</h1>
        <p>{vehicle ? `تجربه‌تان از تعمیر «${vehicle}» را با ما در میان بگذارید.` : "تجربه‌تان را با ما در میان بگذارید."}</p>
      </section>
      <div className="tk-content tk-survey-wrap">
        <section className="tk-survey">
          <div className="tk-progress">
            <div className="tk-progress-top">
              <div>
                <span>{last ? "آخرین مرحله" : `سؤال ${formatNumber(step + 1)} از ${formatNumber(questions.length)}`}</span>
                <strong>{last ? "نکته‌ای برای گفتن دارید؟" : "یک گزینه را انتخاب کنید"}</strong>
              </div>
              <span className="tk-progress-ring font-num">{formatNumber(percent)}٪</span>
            </div>
            <div className="tk-progress-bar" aria-hidden="true"><i style={{ width: `${percent}%` }} /></div>
          </div>

          {!last && q && (
            <div className="tk-q" key={q.id}>
              <div className="tk-q-head">
                <span className="tk-q-num font-num">{formatNumber(step + 1)}</span>
                <p id={`q-${q.id}`}>{q.text}</p>
              </div>
              <div className="tk-scale" role="radiogroup" aria-labelledby={`q-${q.id}`}>
                {[1, 2, 3, 4, 5].map((v) => (
                  <button key={v} type="button" role="radio" aria-checked={ratings[q.id] === v} className={`r${v}${ratings[q.id] === v ? " on" : ""}`} onClick={() => pick(v)}>
                    <b className="font-num">{formatNumber(v)}</b><span>{RATING_WORDS[v]}</span>
                  </button>
                ))}
              </div>
            </div>
          )}

          {last && (
            <div className="tk-q">
              <label className="tk-q-title" htmlFor="tk-comment">نکته‌ای هست که دوست دارید بدانیم؟</label>
              <p className="tk-q-hint">این بخش اختیاری است و مستقیم برای مدیر تعمیرگاه فرستاده می‌شود.</p>
              <textarea id="tk-comment" value={comment} onChange={(e) => setComment(e.target.value)} maxLength={500} rows={4}
                placeholder="مثلاً چه چیزی خوب بود یا چه چیزی می‌تواند بهتر شود…" />
              <span className="tk-counter font-num" dir="ltr">{formatNumber(comment.length)} / ۵۰۰</span>
            </div>
          )}

          <div className="tk-survey-foot">
            {error && <p className="tk-error" role="alert">{error}</p>}
            {last ? (
              <button type="button" className="tk-submit" onClick={submit} disabled={busy || answered < questions.length} aria-busy={busy}>
                {busy ? "در حال ثبت نظر…" : "ثبت نظر"}
              </button>
            ) : (
              <button type="button" className="tk-submit" onClick={() => setStep(step + 1)} disabled={!ratings[q?.id]}>بعدی</button>
            )}
            {step > 0 && <button type="button" className="tk-back" onClick={() => setStep(step - 1)} disabled={busy}>بازگشت</button>}
            <p className="tk-privacy">پاسخ‌های شما فقط برای بهتر شدن خدمات این تعمیرگاه استفاده می‌شود.</p>
          </div>
        </section>
      </div>
    </>
  );
}

/** «پیگیری تعمیر» / «نظرسنجی»: in the top bar on wide screens, floating at the bottom on phones. */
function Tabs({ view, onChange, className }: { view: "tracking" | "survey"; onChange: (v: "tracking" | "survey") => void; className: string }) {
  return (
    <div className={className} role="tablist" aria-label="بخش‌های صفحه">
      <button type="button" role="tab" aria-selected={view === "tracking"} className={view === "tracking" ? "on" : ""} onClick={() => onChange("tracking")}>پیگیری تعمیر</button>
      <button type="button" role="tab" aria-selected={view === "survey"} className={view === "survey" ? "on" : ""} onClick={() => onChange("survey")}>نظرسنجی</button>
    </div>
  );
}

const instagramUrl = (h: string) => `https://instagram.com/${encodeURIComponent(h)}`;

/** «تعمیرگاه در دسترس شما»: pay, call, Instagram and directions as one list; pay and directions open sheets. */
function ConnectCard({ t, code, onSent }: { t: Track; code: string; onSent: () => void }) {
  const [sheet, setSheet] = useState<"pay" | "maps" | null>(null);
  const owe = (t.money?.balanceRials ?? 0) > 0;
  const showPay = !!t.pay && (owe || t.pay.receipts.length > 0);
  const maps = t.shop.maps ?? [];
  const mask = t.pay?.card ? cardGroups(t.pay.card.cardNumber) : null;
  const rows: { key: string; icon: ReactNode; tone: string; title: string; sub?: string; ltr?: boolean; onClick?: () => void; href?: string }[] = [];
  if (showPay) rows.push({ key: "pay", icon: <Icon name="wallet" />, tone: "blue", title: owe ? "پرداخت کارت‌به‌کارت" : "رسیدهای پرداخت", sub: owe && mask ? mask : undefined, ltr: true, onClick: () => setSheet("pay") });
  if (t.shop.phone) rows.push({ key: "call", icon: <Icon name="phone" />, tone: "green", title: "تماس با تعمیرگاه", sub: t.shop.phone, ltr: true, href: `tel:${t.shop.phone}` });
  if (t.shop.instagram) rows.push({ key: "ig", icon: <InstagramIcon size={22} />, tone: "pink", title: "اینستاگرام", sub: `@${t.shop.instagram}`, ltr: true, href: instagramUrl(t.shop.instagram) });
  if (maps.length > 0) rows.push({ key: "maps", icon: <Icon name="pin" />, tone: "orange", title: "مسیریابی تعمیرگاه", sub: "باز کردن در برنامه‌ی نقشه", onClick: () => setSheet("maps") });
  if (rows.length === 0) return null;
  return (
    <>
      <section className="tk-card tk-connect" aria-label="ارتباط با تعمیرگاه">
        <div className="tk-section-title">
          <span className="tk-section-icon"><Icon name="phone" /></span>
          <div><span>ارتباط سریع</span><h2>تعمیرگاه در دسترس شما</h2></div>
        </div>
        <ul className="tk-connect-list">
          {rows.map((r) => {
            const body = (
              <>
                <span className={`tk-connect-icon ${r.tone}`}>{r.icon}</span>
                <span className="tk-connect-text"><strong>{r.title}</strong>{r.sub && <span dir={r.ltr ? "ltr" : undefined} className={r.ltr ? "font-num" : undefined}>{r.sub}</span>}</span>
                <span className="tk-connect-go" aria-hidden="true"><Icon name="back" /></span>
              </>
            );
            return (
              <li key={r.key}>
                {r.href
                  ? <a className="tk-connect-row" href={r.href} {...(r.href.startsWith("http") ? { target: "_blank", rel: "noopener noreferrer" } : {})}>{body}</a>
                  : <button type="button" className="tk-connect-row" onClick={r.onClick}>{body}</button>}
              </li>
            );
          })}
        </ul>
      </section>
      {t.pay && <PaySheet open={sheet === "pay"} onClose={() => setSheet(null)} code={code} pay={owe ? t.pay : { ...t.pay, card: null }} owe={owe} onSent={onSent} />}
      <BottomSheet open={sheet === "maps"} title="مسیریابی" onClose={() => setSheet(null)}>
        <div className="tk-sheet-body">
          <p className="tk-sheet-note">با کدام برنامه مسیر را باز کنیم؟</p>
          <ul className="tk-connect-list">
            {maps.map((m) => (
              <li key={m.key}>
                <a className="tk-connect-row" href={m.url} target="_blank" rel="noopener noreferrer" onClick={() => setSheet(null)}>
                  <span className="tk-connect-icon plain"><MapIcon name={m.key} /></span>
                  <span className="tk-connect-text"><strong>{m.name}</strong><span>باز کردن مسیر</span></span>
                  <span className="tk-connect-go" aria-hidden="true"><Icon name="back" /></span>
                </a>
              </li>
            ))}
          </ul>
        </div>
      </BottomSheet>
    </>
  );
}

/**
 * The customer's page (/t/{code}): no sign-in, read-only, only what the shop chose to share. After delivery it
 * carries the satisfaction survey; the survey SMS links to /s/{code}, which opens the same page at the survey.
 */
export function TrackPage({ code, survey: surveyFirst = false }: { code: string; survey?: boolean }) {
  const [t, setT] = useState<Track | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [requestsOpen, setRequestsOpen] = useState(false);
  const [photo, setPhoto] = useState<number | null>(null);
  const [theme, setTheme] = useState(() => (document.documentElement.dataset.theme === "dark" ? "dark" : "light"));
  // The survey SMS (/s/) opens the survey tab; the tracking link (/t/) opens the tracking tab.
  const [view, setView] = useState<"tracking" | "survey">(surveyFirst ? "survey" : "tracking");
  const go = (v: "tracking" | "survey") => { setView(v); window.scrollTo(0, 0); };

  const load = () =>
    api<Track>(`/api/v1/track/${encodeURIComponent(code)}`)
      .then((d) => { setT(withoutInternalSteps(d)); document.title = `پیگیری پرونده · ${d.shop.name}`; })
      .catch((e) => setError(e instanceof ApiError && e.status === 404
        ? "این لینک معتبر نیست یا پرونده دیگر در دسترس نیست."
        : "ارتباط برقرار نشد. کمی بعد دوباره امتحان کنید."));
  useEffect(() => {
    document.title = "پیگیری پرونده";
    load();
  }, [code]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (photo === null) return;
    const close = (e: KeyboardEvent) => { if (e.key === "Escape") setPhoto(null); };
    window.addEventListener("keydown", close);
    return () => window.removeEventListener("keydown", close);
  }, [photo]);

  const toggleTheme = () => { const next = theme === "light" ? "dark" : "light"; applyTheme(next); setTheme(next); };

  if (error)
    return (
      <main className="tk-page"><div className="tk-content"><section className="tk-card tk-empty"><h1>پیگیری پرونده</h1><p>{error}</p></section></div></main>
    );
  if (!t) return <div className="splash" aria-busy="true" />;

  const n = now(t);
  const vehicleIcon: IconName = t.case.vehicle?.kind === "motorcycle" ? "moto" : "car";
  const photoUrl = (id: string) => `/api/v1/track/${code}/photos/${id}`;
  const asList = (changes: Change[] | undefined, current: string[]): Change[] => changes ?? current.map((title) => ({ title, state: "kept" as const }));
  const problems = asList(t.case.problemChanges, t.case.reportedProblems);
  const services = asList(t.case.serviceChanges, t.case.requestedServices);
  // Collapsed, only the first three unchanged problems show; anything removed or added always shows.
  const tooLong = [problems, services].some((l) => l.filter((x) => x.state === "kept").length > 3);

  return (
    <main className="tk-page">
      <header className="tk-top">
        <div className="tk-top-inner">
          <div className="tk-brand">
            <span className="tk-mark" aria-hidden="true"><Icon name="wrench" /></span>
            <div><strong>{t.shop.name}</strong>{t.shop.address && <span>{t.shop.address}</span>}</div>
          </div>
          {t.survey && <Tabs view={view} onChange={go} className="tk-tabs top" />}
          <button className="tk-icon-button" type="button" onClick={toggleTheme} aria-label={theme === "light" ? "حالت تاریک" : "حالت روشن"}>
            <Icon name={theme === "light" ? "moon" : "sun"} />
          </button>
        </div>
      </header>

      {t.survey && view === "survey" && (
        <SurveyView code={code} survey={t.survey} customer={t.case.customer} vehicle={t.case.vehicle?.title ?? null} shop={t.shop.name} instagram={t.shop.instagram}
          onDone={load} onBack={() => go("tracking")} />
      )}

      <div className="tk-content tk-wide" hidden={!!t.survey && view === "survey"}>
        <section className="tk-hero">
          <div className="tk-hero-main">
            <div className="tk-vehicle-icon"><Icon name={vehicleIcon} /></div>
            <div className="tk-hero-copy">
              <div className="tk-greeting">
                <span>{t.case.customer ? `${t.case.customer} عزیز` : "مشتری گرامی"}</span>
              </div>
              <h1>{t.case.vehicle?.title ?? "پرونده‌ی شما"}</h1>
              {t.case.vehicle?.identifier && <bdi className="tk-plate font-num" dir="ltr">{t.case.vehicle.identifier}</bdi>}
              <div className={`tk-pill${t.status.key === "delivered" || t.status.key === "ready" ? " done" : ""}`}><i />{t.status.name}</div>
            </div>
          </div>
          <div className="tk-hero-meta">
            <div><Icon name="calendar" /><span>پذیرش</span><strong>{day.format(new Date(t.case.openedAt))}</strong></div>
            <div><Icon name="clock" /><span>آخرین به‌روزرسانی</span><strong>{when(t.status.stageEnteredAt)}</strong></div>
            {t.case.promisedAt && t.status.key !== "delivered" && (
              <div><Icon name="check" /><span>قول تحویل</span><strong>{dayTime.format(new Date(t.case.promisedAt))}</strong></div>
            )}
            {t.case.warrantyUntil && (
              <div><Icon name="check" /><span>ضمانت تا</span><strong>{day.format(new Date(t.case.warrantyUntil))}</strong></div>
            )}
          </div>
        </section>

        {t.survey?.state === "open" && (
          <button type="button" className="tk-card tk-survey-invite" onClick={() => go("survey")}>
            <span className="tk-section-icon"><Icon name="star" /></span>
            <span><strong>نظرتان درباره‌ی این تعمیر چیست؟</strong><span>کمتر از یک دقیقه طول می‌کشد.</span></span>
          </button>
        )}

        <div className="tk-grid">
        {(problems.length > 0 || services.length > 0) && (
          <Section icon="document" eyebrow="شرح پذیرش" title="درخواست شما">
            <div className="tk-requests">
              {problems.length > 0 && <ChangeList label="ایرادهای گزارش‌شده" items={problems} open={requestsOpen} />}
              {services.length > 0 && <ChangeList label="خدمات درخواستی" items={services} open={requestsOpen} rows />}
            </div>
            {tooLong && (
              <button className="tk-expand" type="button" onClick={() => setRequestsOpen(!requestsOpen)} aria-expanded={requestsOpen}>
                {requestsOpen ? "نمایش کمتر" : "نمایش همه"}
              </button>
            
            )}
          </Section>
        )}

        <Section icon="wrench" eyebrow="وضعیت لحظه‌ای" title="مسیر کار">
          <div className="tk-note">
            <span className="tk-dot" aria-hidden="true" />
            <div><strong>{n.title}</strong><p>{n.note}</p></div>
          </div>
          {t.stages && (
            <ol className="tk-timeline">
              {t.stages.map((s, i) => (
                <li className={`${s.state}${s.key === "delivered" && s.state === "current" ? " final" : ""}`} key={s.key}>
                  <div className="tk-rail">
                    <span className="tk-node">{(s.state === "done" || (s.key === "delivered" && s.state === "current")) && <Icon name="check" />}</span>
                    {i < t.stages!.length - 1 && <span className="tk-line" />}
                  </div>
                  <div className="tk-step">
                    <strong>{s.name}</strong>
                    {s.state === "current" && s.key !== "delivered" && <span className="tk-badge">مرحله‌ی فعلی</span>}
                  </div>
                  <time>{s.at && s.state !== "todo" ? when(s.at) : "—"}</time>
                </li>
              ))}
            </ol>
          )}
        </Section>

        {t.photos.length > 0 && (
          <Section icon="camera" eyebrow="گزارش تصویری" title="تصاویر و فیلم کار" action={<span className="tk-count">{formatNumber(t.photos.length)} مورد</span>}>
            <div className="tk-photos">
              {t.photos.map((p, i) => (
                <button className="tk-photo" type="button" key={p.id} onClick={() => setPhoto(i)}>
                  {p.isVideo
                    ? <><video src={photoUrl(p.id)} muted playsInline preload="metadata" /><span className="tk-play" aria-hidden="true"><Icon name="play" /></span></>
                    : <img src={photoUrl(p.id)} alt={p.caption ?? `تصویر مرحله‌ی ${STAGE_NAMES[p.stageKey] ?? ""}`} loading="lazy" />}
                  <span className="tk-photo-label">{p.caption ?? `مرحله‌ی ${STAGE_NAMES[p.stageKey] ?? ""}`}</span>
                </button>
              ))}
            </div>
          </Section>
        )}

        {(t.items || t.money) && (
          <Section icon="wallet" eyebrow="صورت‌حساب" title="کالا، کارها و هزینه">
            {t.items && t.items.length > 0 ? (
              <ul className="inv-list">
                {t.items.map((i, k) => (
                  <li className="inv-row" key={k}>
                    <span className={`inv-thumb ${i.kind === "part" ? "part" : "labor"}`}>
                      {i.image ? <img src={i.image} alt="" loading="lazy" /> : <Icon name={i.kind === "part" ? "box" : "wrench"} />}
                    </span>
                    <span className="inv-body">
                      <span className="inv-title">{i.title}</span>
                      <span className="inv-sub">
                        {KIND[i.kind] ?? i.kind}
                        {i.supplier === "customer" && " · کالای خودتان"}
                        {i.status === "needed" && " · در انتظار تهیه"}
                      </span>
                      {i.discountRials != null && i.discountRials > 0 && <span className="inv-discount">تخفیف <span dir="ltr" className="neg font-num">−{formatNumber(Math.round(i.discountRials / 10))}</span></span>}
                    </span>
                    <span className="inv-qty font-num">× {formatNumber(i.quantity)}</span>
                    <span className="inv-amount font-num">{i.lineTotalRials != null && i.lineTotalRials > 0 ? formatNumber(Math.round(i.lineTotalRials / 10)) : "—"}</span>
                  </li>
                ))}
              </ul>
            ) : (
              <div className="tk-empty-bill"><strong>هنوز کالا یا کاری ثبت نشده</strong><p>بعد از ثبت کالا و اجرت، این‌جا نشان داده می‌شود.</p></div>
            )}
            {t.money && t.money.totalRials > 0 && (
              <div className="inv-sum">
                {(t.money.discountRials ?? 0) > 0 && (
                  <>
                    <p><span>جمع قبل از تخفیف</span><strong className="font-num">{toman(t.money.subtotalRials ?? t.money.totalRials)}</strong></p>
                    <p className="discount"><span>تخفیف</span><strong className="font-num" dir="ltr">−{toman(t.money.discountRials ?? 0)}</strong></p>
                  </>
                )}
                <p className="total"><span>{(t.money.discountRials ?? 0) > 0 ? "جمع پس از تخفیف" : "جمع کل"}</span><strong className="font-num">{toman(t.money.totalRials)}</strong></p>
                {(t.money.payments?.length ?? 0) > 0 ? (
                  <>
                    {t.money.payments!.map((p, k) => (
                      <p className="paid" key={k}><span>پرداخت {k + 1}: {PAY_METHOD[p.method] ?? p.method} · {shortDay.format(new Date(p.paidAt))}</span><strong className="font-num">{toman(p.amountRials)}</strong></p>
                    ))}
                  </>
                ) : (
                  t.money.paidRials > 0 && <p className="paid"><span>پرداخت‌شده</span><strong className="font-num">{toman(t.money.paidRials)}</strong></p>
                )}
                <div className={`inv-due${t.money.balanceRials > 0 ? " owe" : " clear"}`}>
                  <span>{t.money.balanceRials > 0 ? "مبلغ قابل پرداخت" : "تسویه شده"}</span>
                  <strong className="font-num">{toman(t.money.balanceRials)}</strong>
                </div>
              </div>
            )}
          </Section>
        )}

        <ConnectCard t={t} code={code} onSent={() => api<Track>(`/api/v1/track/${code}`).then((d) => setT(withoutInternalSteps(d))).catch(() => {})} />
        </div>
      </div>

      {photo !== null && (
        <div className="tk-lightbox" role="dialog" aria-modal="true" aria-label="نمایش تصویر" onClick={() => setPhoto(null)}>
          <div className="tk-lightbox-content" onClick={(e) => e.stopPropagation()}>
            <button className="tk-lightbox-close" type="button" onClick={() => setPhoto(null)} aria-label="بستن تصویر"><Icon name="close" /></button>
            {t.photos[photo].isVideo
              ? <video src={photoUrl(t.photos[photo].id)} controls autoPlay playsInline />
              : <img src={photoUrl(t.photos[photo].id)} alt={t.photos[photo].caption ?? ""} />}
            <div>
              <span>مرحله‌ی {STAGE_NAMES[t.photos[photo].stageKey] ?? ""}</span>
              <strong>{t.photos[photo].caption ?? when(t.photos[photo].createdAt)}</strong>
            </div>
          </div>
        </div>
      )}
      {t.survey && <Tabs view={view} onChange={go} className="tk-tabs bottom" />}
    </main>
  );
}
