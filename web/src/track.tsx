import { useEffect, useRef, useState, type ReactNode } from "react";
import { ApiError, api } from "./api";
import { applyTheme } from "./App";
import { WAIT_REASONS } from "./labels";
import { formatNumber } from "./ui";

type Survey = { state: "open" | "answered" | "expired"; questions: { id: string; text: string }[] | null; score: number | null };

type Track = {
  shop: { name: string; phone: string | null; address: string | null };
  survey: Survey | null;
  photos: { id: string; stageKey: string; caption: string | null; createdAt: string }[];
  case: {
    customer: string | null; openedAt: string; promisedAt: string | null; closedAt: string | null; warrantyUntil: string | null;
    vehicle: { title: string; kind: string; identifier: string | null } | null; reportedProblems: string[]; requestedServices: string[];
  };
  status: { name: string; key: string; category: string; waitReason: string | null; stageEnteredAt: string };
  stages: { name: string; key: string; state: "done" | "current" | "todo"; at: string | null }[] | null;
  items: { kind: string; title: string; quantity: number; status: string; supplier: string; lineTotalRials: number | null }[] | null;
  money: { totalRials: number; paidRials: number; balanceRials: number } | null;
};

type IconName = "calendar" | "camera" | "car" | "moto" | "check" | "clock" | "close" | "document" | "moon" | "phone" | "star" | "sun" | "wrench" | "wallet";

function Icon({ name }: { name: IconName }) {
  const paths: Record<IconName, ReactNode> = {
    calendar: <><rect x="3" y="5" width="18" height="16" rx="3" /><path d="M8 3v4M16 3v4M3 10h18" /></>,
    camera: <><path d="M4 7h3l1.4-2h7.2L17 7h3a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2Z" /><circle cx="12" cy="13" r="4" /></>,
    car: <><path d="m5 11 1.5-4h11l1.5 4M3 11h18v7H3z" /><path d="M5 18v2M19 18v2M6.5 15h.01M17.5 15h.01" /></>,
    moto: <path d="M5 17a3 3 0 1 0 0-.01M19 17a3 3 0 1 0 0-.01M5 17l4-7h5l5 7M12 10l-2-4H7M15 6h3" />,
    check: <path d="m5 12 4 4L19 6" />,
    clock: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>,
    close: <path d="M6 6l12 12M18 6 6 18" />,
    document: <><path d="M6 2h8l4 4v16H6z" /><path d="M14 2v5h5M9 12h6M9 16h6" /></>,
    moon: <path d="M20.5 14.2A8.4 8.4 0 0 1 9.8 3.5a9 9 0 1 0 10.7 10.7Z" />,
    phone: <path d="M5 3h4l2 5-2.5 1.5a15 15 0 0 0 6 6L16 13l5 2v4a2 2 0 0 1-2 2C10.2 20.5 3.5 13.8 3 5a2 2 0 0 1 2-2Z" />,
    star: <path d="m12 3 2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9Z" />,
    sun: <><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></>,
    wrench: <path d="M14.7 6.3a4.5 4.5 0 0 0-5.6 5.6L3 18l3 3 6.1-6.1a4.5 4.5 0 0 0 5.6-5.6L15 12l-3-3 2.7-2.7Z" />,
    wallet: <><rect x="3" y="6" width="18" height="14" rx="3" /><path d="M3 10h18M16 15h2" /></>,
  };
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      {paths[name]}
    </svg>
  );
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

const day = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long", year: "numeric" });
const dayTime = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { weekday: "long", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" });
const time = new Intl.DateTimeFormat("fa-IR", { hour: "2-digit", minute: "2-digit" });
const shortDay = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long" });
const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;
const KIND: Record<string, string> = { part: "کالا", labor: "اجرت", service: "اجرت" };
const STAGE_NAMES: Record<string, string> = {
  received: "پذیرش", diagnosing: "عیب‌یابی", awaiting_approval: "تأیید", awaiting_parts: "انتظار قطعه",
  repairing: "تعمیر", review: "بازبینی", ready: "آماده‌ی تحویل", delivered: "تحویل",
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
  if (s.key === "review") return { title: "کار انجام شده و استاد در حال بازبینی است", note: "پس از تأیید، آماده‌ی تحویل اعلام می‌شود." };
  return { title: "وسیله‌ی شما پذیرش شد و در نوبت کار است", note: "با شروع کار، مراحل در همین صفحه به‌روز می‌شود." };
}

const RATING_WORDS = ["", "خیلی ناراضی", "ناراضی", "متوسط", "راضی", "خیلی راضی"];

/** Five large stars (≥ 44px each) for one question; the chosen word is said below. */
function Stars({ label, value, onChange }: { label: string; value: number; onChange: (v: number) => void }) {
  return (
    <div className="tk-q">
      <p id={`q-${label}`}>{label}</p>
      <div className={`tk-stars${value ? ` r${value}` : ""}`} role="radiogroup" aria-labelledby={`q-${label}`}>
        {[1, 2, 3, 4, 5].map((v) => (
          <button key={v} type="button" role="radio" aria-checked={value === v} aria-label={`${formatNumber(v)} از ۵، ${RATING_WORDS[v]}`}
            className={v <= value ? "on" : ""} onClick={() => onChange(v)}>
            <Icon name="star" />
          </button>
        ))}
      </div>
      <span className="tk-q-word" aria-hidden="true">{value ? RATING_WORDS[value] : " "}</span>
    </div>
  );
}

/** The satisfaction survey after delivery: every question once, an optional comment, then thanks. */
function SurveyCard({ code, survey, customer, focus, onDone }: { code: string; survey: Survey; customer: string | null; focus: boolean; onDone: () => void }) {
  const [ratings, setRatings] = useState<Record<string, number>>({});
  const [comment, setComment] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const ref = useRef<HTMLElement>(null);
  useEffect(() => { if (focus) ref.current?.scrollIntoView({ block: "start" }); }, [focus]);

  if (survey.state === "answered")
    return (
      <section className="tk-card tk-survey done" ref={ref} id="survey">
        <span className="tk-survey-icon"><Icon name="check" /></span>
        <strong>نظر شما ثبت شد</strong>
        <p>از وقتی که گذاشتید سپاسگزاریم؛ نظرتان مستقیم به مدیر تعمیرگاه می‌رسد.</p>
      </section>
    );
  if (survey.state === "expired")
    return <section className="tk-card tk-survey done" ref={ref} id="survey"><p>زمان این نظرسنجی تمام شده است.</p></section>;

  const questions = survey.questions ?? [];
  const missing = questions.filter((q) => !ratings[q.id]).length;
  async function submit() {
    if (missing > 0) { setError("لطفاً به همه‌ی سؤال‌ها جواب دهید."); return; }
    setBusy(true);
    setError(null);
    try {
      await api(`/api/v1/track/${encodeURIComponent(code)}/survey`, {
        method: "POST", body: { answers: questions.map((q) => ({ questionId: q.id, rating: ratings[q.id] })), comment: comment.trim() || undefined },
      });
      onDone();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "ثبت نشد. اتصال را بررسی کنید و دوباره بزنید.");
    } finally {
      setBusy(false);
    }
  }
  return (
    <section className="tk-card tk-survey" ref={ref} id="survey">
      <div className="tk-section-title">
        <span className="tk-section-icon"><Icon name="star" /></span>
        <div><span>نظرسنجی</span><h2>{customer ? `${customer} عزیز، نظرتان چیست؟` : "نظرتان چیست؟"}</h2></div>
      </div>
      <p className="tk-survey-lead">چند ثانیه بیشتر طول نمی‌کشد و به ما کمک می‌کند بهتر شویم.</p>
      {questions.map((q) => (
        <Stars key={q.id} label={q.text} value={ratings[q.id] ?? 0} onChange={(v) => { setRatings((r) => ({ ...r, [q.id]: v })); setError(null); }} />
      ))}
      <label className="tk-q">
        <p>اگر نکته‌ای دارید بنویسید (اختیاری)</p>
        <textarea value={comment} onChange={(e) => setComment(e.target.value)} maxLength={500} rows={3} placeholder="مثلاً از چه چیزی راضی بودید یا چه چیزی بهتر شود" />
      </label>
      {error && <p className="tk-error" role="alert">{error}</p>}
      <button type="button" className="tk-submit" onClick={submit} disabled={busy} aria-busy={busy}>
        {busy ? "در حال ثبت…" : missing > 0 ? `ثبت نظر (${formatNumber(missing)} سؤال مانده)` : "ثبت نظر"}
      </button>
    </section>
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

  const load = () =>
    api<Track>(`/api/v1/track/${encodeURIComponent(code)}`)
      .then((d) => { setT(d); document.title = `پیگیری پرونده · ${d.shop.name}`; })
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
  const problems = t.case.reportedProblems;
  const services = t.case.requestedServices;
  const shownProblems = requestsOpen ? problems : problems.slice(0, 3);
  const hasMore = problems.length > 3;

  return (
    <main className="tk-page">
      <header className="tk-top">
        <div className="tk-top-inner">
          <div className="tk-brand">
            <span className="tk-mark" aria-hidden="true"><Icon name="wrench" /></span>
            <div><strong>{t.shop.name}</strong>{t.shop.address && <span>{t.shop.address}</span>}</div>
          </div>
          <button className="tk-icon-button" type="button" onClick={toggleTheme} aria-label={theme === "light" ? "حالت تاریک" : "حالت روشن"}>
            <Icon name={theme === "light" ? "moon" : "sun"} />
          </button>
        </div>
      </header>

      <div className="tk-content">
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

        {t.survey && <SurveyCard code={code} survey={t.survey} customer={t.case.customer} focus={surveyFirst} onDone={load} />}

        {(problems.length > 0 || services.length > 0) && (
          <Section icon="document" eyebrow="شرح پذیرش" title="درخواست شما">
            <div className="tk-requests">
              {problems.length > 0 && (
                <div>
                  <span className="tk-list-label">ایرادهای گزارش‌شده</span>
                  <ul>{shownProblems.map((p) => <li key={p}>{p}</li>)}</ul>
                </div>
              )}
              {services.length > 0 && (
                <div>
                  <span className="tk-list-label">خدمات درخواستی</span>
                  <ul>{services.map((s) => <li key={s}>{s}</li>)}</ul>
                </div>
              )}
            </div>
            {hasMore && (
              <button className="tk-expand" type="button" onClick={() => setRequestsOpen(!requestsOpen)} aria-expanded={requestsOpen}>
                {requestsOpen ? "نمایش کمتر" : `نمایش همه (${formatNumber(problems.length)} ایراد)`}
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
          <Section icon="camera" eyebrow="گزارش تصویری" title="تصاویر کار" action={<span className="tk-count">{formatNumber(t.photos.length)} تصویر</span>}>
            <div className="tk-photos">
              {t.photos.map((p, i) => (
                <button className="tk-photo" type="button" key={p.id} onClick={() => setPhoto(i)}>
                  <img src={photoUrl(p.id)} alt={p.caption ?? `تصویر مرحله‌ی ${STAGE_NAMES[p.stageKey] ?? ""}`} loading="lazy" />
                  <span className="tk-photo-label">{p.caption ?? `مرحله‌ی ${STAGE_NAMES[p.stageKey] ?? ""}`}</span>
                </button>
              ))}
            </div>
          </Section>
        )}

        {(t.items || t.money) && (
          <Section icon="wallet" eyebrow="صورت‌حساب" title="کالا، کارها و هزینه">
            {t.items && t.items.length > 0 ? (
              <ul className="tk-items">
                {t.items.map((i, k) => (
                  <li key={k}>
                    <span className="tk-kind">{KIND[i.kind] ?? i.kind}</span>
                    <span className="tk-item-title">
                      {i.title}
                      {i.quantity !== 1 && <span className="tk-muted"> × {formatNumber(i.quantity)}</span>}
                      {i.supplier === "customer" && <span className="tk-muted"> (کالای خودتان)</span>}
                      {i.status === "needed" && <span className="tk-muted"> (در انتظار تهیه)</span>}
                    </span>
                    {i.lineTotalRials != null && i.lineTotalRials > 0 && <strong className="font-num">{toman(i.lineTotalRials)}</strong>}
                  </li>
                ))}
              </ul>
            ) : (
              <div className="tk-empty-bill"><strong>هنوز کالا یا کاری ثبت نشده</strong><p>بعد از ثبت کالا و اجرت، این‌جا نشان داده می‌شود.</p></div>
            )}
            {t.money && t.money.totalRials > 0 && (
              <div className="tk-money">
                <p><span>جمع</span><strong className="font-num">{toman(t.money.totalRials)}</strong></p>
                <p><span>پرداخت‌شده</span><strong className="font-num">{toman(t.money.paidRials)}</strong></p>
                <p className={t.money.balanceRials > 0 ? "owe" : ""}><span>مانده</span><strong className="font-num">{toman(t.money.balanceRials)}</strong></p>
              </div>
            )}
          </Section>
        )}

        <section className="tk-help">
          <div className="tk-help-icon"><Icon name="phone" /></div>
          <div>
            <strong>سؤالی دارید؟</strong>
            <span>{t.shop.phone ? "با تعمیرگاه تماس بگیرید." : "شماره‌ی تعمیرگاه هنوز ثبت نشده است."}</span>
          </div>
          {t.shop.phone && <a className="tk-call" href={`tel:${t.shop.phone}`}>تماس</a>}
        </section>
      </div>

      {photo !== null && (
        <div className="tk-lightbox" role="dialog" aria-modal="true" aria-label="نمایش تصویر" onClick={() => setPhoto(null)}>
          <div className="tk-lightbox-content" onClick={(e) => e.stopPropagation()}>
            <button className="tk-lightbox-close" type="button" onClick={() => setPhoto(null)} aria-label="بستن تصویر"><Icon name="close" /></button>
            <img src={photoUrl(t.photos[photo].id)} alt={t.photos[photo].caption ?? ""} />
            <div>
              <span>مرحله‌ی {STAGE_NAMES[t.photos[photo].stageKey] ?? ""}</span>
              <strong>{t.photos[photo].caption ?? when(t.photos[photo].createdAt)}</strong>
            </div>
          </div>
        </div>
      )}
    </main>
  );
}
