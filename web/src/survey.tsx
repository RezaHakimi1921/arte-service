import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError } from "./api";
import { useFeedback } from "./feedback";
import { BottomSheet } from "./sheet";
import { Field } from "./ui";

export type CaseSurvey = {
  state: "scheduled" | "sent" | "answered" | "cancelled" | "failed";
  dueAt: string; sentAt: string | null; answeredAt: string | null; expiresAt: string;
  score: number | null; isLow: boolean; comment: string | null; customerName: string | null;
  answers: { question: string; rating: number }[]; responsible: string[];
  followedUpAt: string | null; followedUpBy: string | null; followUpNote: string | null;
};

const faDateTime = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" });
const faScore = new Intl.NumberFormat("fa-IR", { maximumFractionDigits: 1 });

/** «۴٫۳ از ۵» */
export const scoreText = (score: number) => `${faScore.format(score)} از ۵`;

/** Read-only stars: filled up to the rating, with the number spoken for screen readers. */
export function StarRow({ rating }: { rating: number }) {
  return (
    <span className={`star-row r${Math.min(5, Math.max(1, Math.round(rating)))}`} role="img" aria-label={scoreText(rating)}>
      {[1, 2, 3, 4, 5].map((v) => (
        <svg key={v} viewBox="0 0 24 24" className={v <= Math.round(rating) ? "on" : ""} aria-hidden="true">
          <path d="m12 3 2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9Z" />
        </svg>
      ))}
    </span>
  );
}

/**
 * «نظر مشتری» on the case page: what the customer said, who did the work, and the manager's follow-up.
 * Before an answer it says where the survey is (scheduled / sent / not sent).
 */
export function SurveySection({ caseId, survey, canFollowUp, focus, onChange }: {
  caseId: string; survey: CaseSurvey; canFollowUp: boolean; focus: boolean; onChange: () => void;
}) {
  const { notify } = useFeedback();
  const [open, setOpen] = useState(false);
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const ref = useRef<HTMLElement>(null);
  useEffect(() => { if (focus) ref.current?.scrollIntoView({ block: "start" }); }, [focus]);

  async function followUp() {
    setBusy(true);
    try {
      await api(`/api/v1/cases/${caseId}/survey/follow-up`, { body: { note: note.trim() || undefined } });
      notify("پیگیری ثبت شد");
      setOpen(false);
      onChange();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "ثبت نشد", "error");
    } finally {
      setBusy(false);
    }
  }

  const s = survey;
  return (
    <section className={`card survey-card${s.isLow && !s.followedUpAt ? " low" : ""}`} ref={ref} id="case-survey">
      <header className="survey-head">
        <h3>نظر مشتری</h3>
        {s.state === "answered" && s.isLow && <span className="badge danger">رضایت پایین</span>}
      </header>
      {s.state === "answered" && s.score != null ? (
        <>
          <p className="survey-who">
            <strong>{s.customerName ?? "مشتری"}</strong>
            <span className="muted small">، {faDateTime.format(new Date(s.answeredAt!))}</span>
          </p>
          <div className="survey-total"><StarRow rating={s.score} /><strong className="font-num">{scoreText(s.score)}</strong></div>
          <ul className="survey-answers">
            {s.answers.map((a) => (
              <li key={a.question}><span>{a.question}</span><StarRow rating={a.rating} /></li>
            ))}
          </ul>
          {s.comment && <blockquote className="survey-comment">{s.comment}</blockquote>}
          {s.responsible.length > 0 && <p className="muted small">کار این پرونده: {s.responsible.join("، ")}</p>}
          {s.followedUpAt ? (
            <p className="survey-followed small">
              پیگیری شد، {s.followedUpBy ?? ""}، {faDateTime.format(new Date(s.followedUpAt))}{s.followUpNote ? ` — ${s.followUpNote}` : ""}
            </p>
          ) : canFollowUp && s.isLow ? (
            <button className="primary block" onClick={() => { setNote(""); setOpen(true); }}>با مشتری تماس گرفتم؛ پیگیری شد</button>
          ) : null}
        </>
      ) : (
        <p className="muted">
          {s.state === "scheduled" && `پیامک نظرسنجی ${faDateTime.format(new Date(s.dueAt))} برای مشتری فرستاده می‌شود.`}
          {s.state === "sent" && "نظرسنجی برای مشتری فرستاده شد؛ هنوز پاسخ نداده است."}
          {s.state === "failed" && "پیامک نظرسنجی فرستاده نشد؛ مشتری از لینک پیگیری هم می‌تواند نظر بدهد."}
          {s.state === "cancelled" && "نظرسنجی این پرونده لغو شد (پرونده دوباره باز شد)."}
        </p>
      )}

      <BottomSheet open={open} title="پیگیری نظر مشتری" onClose={() => setOpen(false)}>
        <form onSubmit={(e) => { e.preventDefault(); void followUp(); }}>
          <Field label="چه شد؟ (اختیاری)">
            <input value={note} onChange={(e) => setNote(e.target.value)} maxLength={300} placeholder="مثلاً قرار شد فردا دوباره بیاید" autoFocus />
          </Field>
          <button className="primary block" disabled={busy} aria-busy={busy}>{busy ? "در حال ثبت…" : "ثبت پیگیری"}</button>
        </form>
      </BottomSheet>
    </section>
  );
}

type Notice = { id: string; type: string; caseId: string | null; title: string; body: string | null; isAlert: boolean; createdAt: string; read: boolean };

function ago(iso: string) {
  const min = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
  if (min < 1) return "همین حالا";
  if (min < 60) return `${new Intl.NumberFormat("fa-IR").format(min)} دقیقه پیش`;
  if (min < 24 * 60) return `${new Intl.NumberFormat("fa-IR").format(Math.round(min / 60))} ساعت پیش`;
  return faDateTime.format(new Date(iso));
}

/**
 * The bell at the end of the top bar (left in RTL): the unread count, checked every minute and whenever the app
 * comes back to the front (no open connection, which is steadier on poor networks). Tapping a row opens its case.
 */
export function NotificationBell({ onOpenCase }: { onOpenCase: (caseId: string) => void }) {
  const [data, setData] = useState<{ unread: number; items: Notice[] } | null>(null);
  const [open, setOpen] = useState(false);
  const load = useCallback(() => { api<{ unread: number; items: Notice[] }>("/api/v1/notifications").then(setData).catch(() => {}); }, []);

  useEffect(() => {
    load();
    const timer = window.setInterval(() => { if (document.visibilityState === "visible") load(); }, 60_000);
    const onShow = () => { if (document.visibilityState === "visible") load(); };
    document.addEventListener("visibilitychange", onShow);
    return () => { window.clearInterval(timer); document.removeEventListener("visibilitychange", onShow); };
  }, [load]);

  function read(ids?: string[]) {
    setData((d) => d && { unread: ids ? Math.max(0, d.unread - d.items.filter((n) => !n.read && ids.includes(n.id)).length) : 0,
      items: d.items.map((n) => (!ids || ids.includes(n.id) ? { ...n, read: true } : n)) });
    api("/api/v1/notifications/read", { body: ids ? { ids } : {} }).catch(() => {});
  }

  const unread = data?.unread ?? 0;
  return (
    <>
      <button type="button" className="icon-button bell" onClick={() => { setOpen(true); load(); }}
        aria-label={unread > 0 ? `اعلان‌ها، ${new Intl.NumberFormat("fa-IR").format(unread)} خوانده‌نشده` : "اعلان‌ها"}>
        <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
          <path d="M6 9a6 6 0 1 1 12 0c0 5 2 6.5 2 6.5H4S6 14 6 9M10 19a2 2 0 0 0 4 0" />
        </svg>
        {unread > 0 && <span className="bell-count font-num" aria-hidden="true">{unread > 99 ? "۹۹+" : new Intl.NumberFormat("fa-IR").format(unread)}</span>}
      </button>
      <BottomSheet open={open} title="اعلان‌ها" onClose={() => setOpen(false)}>
        {!data ? <div className="splash small" aria-busy="true" /> : data.items.length === 0 ? (
          <p className="muted">اعلانی ندارید. وقتی مشتری‌ها در نظرسنجی نظر بدهند، این‌جا می‌بینید.</p>
        ) : (
          <>
            {unread > 0 && <button type="button" className="link" onClick={() => read()}>همه را خوانده‌شده کن</button>}
            <ul className="notice-list">
              {data.items.map((n) => (
                <li key={n.id}>
                  <button type="button" className={`notice${n.read ? "" : " unread"}`}
                    onClick={() => { if (!n.read) read([n.id]); setOpen(false); if (n.caseId) onOpenCase(n.caseId); }}>
                    <span className="notice-title">{n.title}</span>
                    {n.body && <span className="notice-body">{n.body}</span>}
                    <span className="notice-meta muted small">
                      {n.isAlert && <span className="badge danger">رضایت پایین</span>}
                      {ago(n.createdAt)}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </>
        )}
      </BottomSheet>
    </>
  );
}
