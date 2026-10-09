import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { ApiError, api, type Session } from "./api";
import { applyTheme } from "./App";
import { toLatinDigits } from "./ui";

type IconName = "alert" | "arrow" | "check" | "eye" | "eyeOff" | "lock" | "moon" | "phone" | "shield" | "sun" | "wrench" | "user" | "shop" | "gift";

function Icon({ name }: { name: IconName }) {
  const paths: Record<IconName, ReactNode> = {
    alert: <><circle cx="12" cy="12" r="9" /><path d="M12 7v6M12 17h.01" /></>,
    arrow: <path d="m9 18 6-6-6-6" />,
    check: <path d="m5 12 4 4L19 6" />,
    eye: <><path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6S2 12 2 12Z" /><circle cx="12" cy="12" r="2.5" /></>,
    eyeOff: <path d="m3 3 18 18M10.6 6.2A10 10 0 0 1 12 6c6.5 0 10 6 10 6a17 17 0 0 1-2.1 2.8M6.7 6.7C3.7 8.5 2 12 2 12s3.5 6 10 6a9.8 9.8 0 0 0 3.3-.6M10.2 10.2a2.6 2.6 0 0 0 3.6 3.6" />,
    lock: <><rect x="4" y="10" width="16" height="11" rx="3" /><path d="M8 10V7a4 4 0 0 1 8 0v3" /></>,
    moon: <path d="M20.8 14.2A8 8 0 0 1 9.8 3.2 9 9 0 1 0 20.8 14.2Z" />,
    phone: <><rect x="6" y="2" width="12" height="20" rx="3" /><path d="M10 18h4" /></>,
    shield: <path d="M12 22s8-3.8 8-10V5l-8-3-8 3v7c0 6.2 8 10 8 10Zm-3.5-10 2.2 2.2 4.8-5" />,
    sun: <><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></>,
    wrench: <path d="M14.7 6.3a4.5 4.5 0 0 0-5.6 5.6L3 18l3 3 6.1-6.1a4.5 4.5 0 0 0 5.6-5.6L15 12l-3-3 2.7-2.7Z" />,
    user: <><circle cx="12" cy="8" r="4" /><path d="M4 21v-1a7 7 0 0 1 16 0v1" /></>,
    shop: <path d="M3 9l1.5-5h15L21 9M3 9h18M3 9v11h18V9M9 20v-6h6v6" />,
    gift: <><rect x="3" y="8" width="18" height="13" rx="2" /><path d="M12 8v13M3 12h18M12 8S10 3 7.5 4.5 9 8 12 8Zm0 0s2-5 4.5-3.5S15 8 12 8Z" /></>,
  };
  return (
    <svg data-icon={name} viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      {paths[name]}
    </svg>
  );
}

function Logo() {
  return (
    <div className="lg-logo" aria-hidden="true">
      <svg viewBox="0 0 48 48"><path d="M24 8 11.6 38h6.2l2.7-7h7l2.7 7h6.2L24 8Zm-1.7 17.7L24 21l1.7 4.7h-3.4Z" fill="currentColor" /></svg>
    </div>
  );
}

function ThemeButton({ wide }: { wide?: boolean }) {
  const [theme, setTheme] = useState(() => document.documentElement.dataset.theme === "dark" ? "dark" : "light");
  const next = theme === "light" ? "dark" : "light";
  return (
    <button type="button" className={`lg-theme${wide ? " wide" : ""}`} aria-label={next === "dark" ? "حالت تاریک" : "حالت روشن"}
      onClick={() => { applyTheme(next); setTheme(next); }}>
      <Icon name={theme === "light" ? "moon" : "sun"} />
      {wide && <span>{theme === "light" ? "حالت تاریک" : "حالت روشن"}</span>}
    </button>
  );
}

/** Two-panel sign-in layout: the brand story on desktop, a compact header on phones. */
export function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <main className="lg-page">
      <section className="lg-story" aria-label="معرفی آرته سرویس">
        <div className="lg-story-top"><Logo /><span>آرته سرویس</span></div>
        <div className="lg-story-copy">
          <span className="lg-eyebrow"><span className="lg-status-dot" />همراه حرفه‌ای تعمیرگاه</span>
          <h1>تعمیرگاه منظم،<br />ذهن <em>آرام‌تر.</em></h1>
          <p>از پذیرش تا تحویل؛ همه‌چیز دقیق، سریع و همیشه در دسترس شماست.</p>
        </div>
        <div className="lg-workflow" aria-hidden="true">
          <div className="lg-workflow-head">
            <div><span>وضعیت امروز</span><strong>جریان کار تعمیرگاه</strong></div>
            <span className="lg-live">به‌روز</span>
          </div>
          <div className="lg-workflow-row">
            <div className="lg-workflow-icon"><Icon name="wrench" /></div>
            <div className="lg-workflow-info"><strong>هوندا کلیک ۱۵۰</strong><span>سرویس دوره‌ای و تعویض لنت</span></div>
            <span className="lg-workflow-state">در حال تعمیر</span>
          </div>
          <div className="lg-track"><span /></div>
          <div className="lg-workflow-meta">
            <span><i className="lg-dot done" /> پذیرش</span>
            <span><i className="lg-dot active" /> تعمیر</span>
            <span><i className="lg-dot" /> تحویل</span>
          </div>
        </div>
        <div className="lg-story-foot"><span><Icon name="shield" /> اطلاعات هر تعمیرگاه جدا و محرمانه است</span></div>
      </section>

      <section className="lg-auth">
        <header className="lg-mobile-header">
          <div className="lg-mobile-brand"><Logo /><span>آرته سرویس</span></div>
          <ThemeButton />
        </header>
        <div className="lg-desktop-theme"><ThemeButton wide /></div>
        <div className="lg-content">{children}</div>
      </section>
    </main>
  );
}

function Heading({ icon, over, title, children }: { icon: IconName; over: string; title: string; children?: ReactNode }) {
  return (
    <>
      <div className="lg-welcome-icon"><Icon name={icon} /></div>
      <div className="lg-heading">
        <span className="lg-overline">{over}</span>
        <h2>{title}</h2>
        {children && <p>{children}</p>}
      </div>
    </>
  );
}

function ErrorBox({ text, id }: { text: string | null; id?: string }) {
  if (!text) return null;
  return <div className="lg-error" id={id} role="alert"><Icon name="alert" /><span>{text}</span></div>;
}

function Submit({ busy, label, icon = "arrow", disabled }: { busy: boolean; label: string; icon?: IconName; disabled?: boolean }) {
  return (
    <button className="lg-submit" type="submit" disabled={busy || disabled} aria-busy={busy}>
      <span>{busy ? "لطفاً صبر کنید…" : label}</span>
      {busy ? <span className="lg-spinner" aria-hidden="true" /> : <Icon name={icon} />}
    </button>
  );
}

const message = (e: unknown) => (e instanceof ApiError ? e.message : "ارتباط با سرور برقرار نشد. دوباره تلاش کنید.");
const isMobile = (v: string) => /^09\d{9}$/.test(v);
const spaced = (m: string) => m.replace(/^(\d{4})(\d{3})(\d{0,4}).*/, "$1 $2 $3").trim();

type OtpSent = { codeLength: number; resendInSeconds: number };

/** Resend countdown measured against the clock, so a background tab or a slow device never runs it slow. */
function useCountdown(seconds: number, key: number) {
  const [left, setLeft] = useState(seconds);
  useEffect(() => {
    const until = Date.now() + seconds * 1000;
    setLeft(seconds);
    const t = window.setInterval(() => setLeft(Math.max(0, Math.ceil((until - Date.now()) / 1000))), 500);
    return () => window.clearInterval(t);
  }, [seconds, key]);
  return left;
}

/**
 * Android Chrome can read the code straight from the SMS (WebOTP) when the message ends with
 * "@service.artepersia.com #code". iOS fills it from the keyboard through autocomplete="one-time-code".
 */
function useWebOtp(active: boolean, onCode: (code: string) => void) {
  const latest = useRef(onCode);
  latest.current = onCode;
  useEffect(() => {
    if (!active || !("OTPCredential" in window)) return;
    const abort = new AbortController();
    (navigator.credentials as CredentialsContainer)
      .get({ otp: { transport: ["sms"] }, signal: abort.signal } as CredentialRequestOptions)
      .then((c) => { const code = (c as unknown as { code?: string } | null)?.code; if (code) latest.current(code); })
      .catch(() => {});
    return () => abort.abort();
  }, [active]);
}

/** The code field: digits only, the phone's number pad, filled from the SMS where the phone allows it. */
function CodeField({ value, onChange, length, id }: { value: string; onChange: (v: string) => void; length: number; id: string }) {
  return (
    <input className="lg-code" id={id} type="text" inputMode="numeric" pattern="[0-9]*" autoComplete="one-time-code"
      dir="ltr" maxLength={length} value={value} autoFocus aria-label={`کد ${length} رقمی`}
      placeholder={Array.from({ length }, () => "•").join(" ")}
      onChange={(e) => onChange(toLatinDigits(e.target.value).replace(/\D/g, "").slice(0, length))} />
  );
}

export function Login({ onDone }: { onDone: (s: Session) => void }) {
  const [mode, setMode] = useState<"phone" | "password" | "code" | "reset" | "reset-code">("phone");
  const [signup, setSignup] = useState(false);
  const [phone, setPhone] = useState("");
  const [password, setPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [code, setCode] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState<OtpSent>({ codeLength: 5, resendInSeconds: 60 });
  const [sendKey, setSendKey] = useState(0);
  const left = useCountdown(sent.resendInSeconds, sendKey);
  const verifying = useRef(false);

  const codeStep = mode === "code" || mode === "reset-code";

  function go(next: typeof mode) {
    setMode(next);
    setError(null);
    setCode("");
  }

  async function requestCode(nextMode: "code" | "reset-code") {
    if (!isMobile(phone)) { setError("شماره موبایل را درست وارد کنید (۱۱ رقم، با ۰۹)."); return; }
    setBusy(true);
    setError(null);
    try {
      const r = await api<OtpSent>("/api/v1/auth/otp/request", { body: { mobile: phone } });
      setSent({ codeLength: r.codeLength ?? 5, resendInSeconds: r.resendInSeconds ?? 60 });
      setSendKey((k) => k + 1);
      setCode("");
      setMode(nextMode);
    } catch (e) {
      setError(message(e));
    } finally {
      setBusy(false);
    }
  }

  // Entering the last digit signs in at once: no extra tap.
  async function verify(value: string) {
    if (verifying.current || value.length !== sent.codeLength) return;
    verifying.current = true;
    setBusy(true);
    setError(null);
    try {
      onDone(await api<Session>("/api/v1/auth/otp/verify", { body: { mobile: phone, code: value } }));
    } catch (e) {
      setError(message(e));
      setCode("");
    } finally {
      verifying.current = false;
      setBusy(false);
    }
  }

  useWebOtp(mode === "code", (c) => { setCode(c); void verify(c); });

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    if (mode === "phone") return requestCode("code");
    if (mode === "reset") return requestCode("reset-code");
    if (mode === "code") {
      if (code.length !== sent.codeLength) { setError(`کد ${toFa(sent.codeLength)} رقمی پیامک‌شده را وارد کنید.`); return; }
      return verify(code);
    }
    setBusy(true);
    setError(null);
    try {
      if (mode === "password") {
        if (!isMobile(phone) || !password) { setError("شماره موبایل و رمز را وارد کنید."); return; }
        onDone(await api<Session>("/api/v1/auth/password", { body: { username: phone.trim(), password } }));
      } else {
        if (code.length !== sent.codeLength) { setError(`کد ${toFa(sent.codeLength)} رقمی پیامک‌شده را وارد کنید.`); return; }
        if (newPassword.length < 10) { setError("رمز تازه باید دست‌کم ۱۰ حرف باشد."); return; }
        onDone(await api<Session>("/api/v1/auth/password/reset", { body: { mobile: phone, code, newPassword } }));
      }
    } catch (err) {
      setError(message(err));
    } finally {
      setBusy(false);
    }
  }

  const resend = (
    <div className="lg-code-actions">
      <button type="button" className="lg-text" onClick={() => go(mode === "code" ? "phone" : "reset")}>تغییر شماره</button>
      <button type="button" className="lg-text lg-resend" disabled={left > 0 || busy}
        onClick={() => requestCode(mode === "code" ? "code" : "reset-code")}>
        {left > 0 ? <>ارسال دوباره تا <bdi dir="ltr">{`${Math.floor(left / 60)}:${String(left % 60).padStart(2, "0")}`}</bdi></> : "ارسال دوباره‌ی کد"}
      </button>
    </div>
  );

  return (
    <AuthLayout>
      {mode === "code" ? (
        <Heading icon="shield" over="تأیید شماره" title="کد تأیید را وارد کنید">
          کد {toFa(sent.codeLength)} رقمی ارسال‌شده به <bdi dir="ltr">{spaced(phone)}</bdi> را وارد کنید؛ با رقم آخر وارد می‌شوید.
        </Heading>
      ) : mode === "reset" || mode === "reset-code" ? (
        <Heading icon="lock" over="بازیابی رمز" title="رمز عبور تازه">
          {mode === "reset" ? "کد تأیید به شماره‌ی موبایل حساب پیامک می‌شود." : <>کد ارسال‌شده به <bdi dir="ltr">{spaced(phone)}</bdi> و رمز تازه را وارد کنید.</>}
        </Heading>
      ) : (
        signup && mode === "phone" ? (
          <Heading icon="user" over="ثبت‌نام رایگان" title="ثبت‌نام در آرته سرویس">
            شماره‌ی موبایلتان را بزنید؛ با کد تأیید پیامکی حسابتان ساخته می‌شود و ۱۴ روز رایگان از همه‌ی امکانات استفاده می‌کنید.
          </Heading>
        ) : (
          <Heading icon="wrench" over="خوش آمدید" title="وارد حساب خود شوید">
            با شماره‌ی موبایلی که در آرته ثبت کرده‌اید وارد شوید.
          </Heading>
        )
      )}

      {(mode === "phone" || mode === "password") && !signup && (
        <div className="lg-segment" role="tablist" aria-label="روش ورود">
          <button className={mode === "phone" ? "active" : ""} type="button" role="tab" aria-selected={mode === "phone"} onClick={() => go("phone")}>کد پیامکی</button>
          <button className={mode === "password" ? "active" : ""} type="button" role="tab" aria-selected={mode === "password"} onClick={() => go("password")}>رمز عبور</button>
        </div>
      )}

      <form onSubmit={submit} noValidate>
        {(mode === "phone" || mode === "reset" || mode === "password") && (
          <>
            <label className="lg-label" htmlFor="lg-phone">شماره موبایل</label>
            <div className={`lg-input${error && mode !== "password" && !isMobile(phone) ? " invalid" : ""}`}>
              <span className="lg-input-icon"><Icon name="phone" /></span>
              <input id="lg-phone" type="tel" inputMode="numeric" pattern="[0-9]*" autoComplete={mode === "password" ? "username" : "tel"}
                dir="ltr" maxLength={11} value={phone}
                onChange={(e) => { setPhone(toLatinDigits(e.target.value).replace(/\D/g, "").slice(0, 11)); setError(null); }}
                placeholder="0912 000 0000" aria-describedby={error ? "lg-error" : undefined} autoFocus />
              {isMobile(phone) && <span className="lg-valid"><Icon name="check" /></span>}
            </div>
          </>
        )}

        {mode === "password" && (
          <>
            <div className="lg-label-row">
              <label className="lg-label" htmlFor="lg-password">رمز عبور</label>
              <button type="button" className="lg-text" onClick={() => go("reset")}>فراموش کرده‌اید؟</button>
            </div>
            <div className="lg-input">
              <span className="lg-input-icon"><Icon name="lock" /></span>
              <input id="lg-password" type={showPassword ? "text" : "password"} autoComplete="current-password" dir="ltr" value={password}
                onChange={(e) => { setPassword(e.target.value); setError(null); }} placeholder="رمز عبور" />
              <button className="lg-reveal" type="button" onClick={() => setShowPassword(!showPassword)} aria-label={showPassword ? "پنهان کردن رمز" : "نمایش رمز"}>
                <Icon name={showPassword ? "eyeOff" : "eye"} />
              </button>
            </div>
          </>
        )}

        {codeStep && (
          <div className="lg-code-area">
            <label className="lg-label" htmlFor="lg-code">کد تأیید {toFa(sent.codeLength)} رقمی</label>
            <CodeField id="lg-code" value={code} length={sent.codeLength}
              onChange={(v) => { setCode(v); setError(null); if (mode === "code" && v.length === sent.codeLength) void verify(v); }} />
            {resend}
            {mode === "reset-code" && (
              <>
                <label className="lg-label lg-gap" htmlFor="lg-new">رمز عبور تازه (دست‌کم ۱۰ حرف)</label>
                <div className="lg-input">
                  <span className="lg-input-icon"><Icon name="lock" /></span>
                  <input id="lg-new" type={showPassword ? "text" : "password"} autoComplete="new-password" dir="ltr" value={newPassword}
                    onChange={(e) => { setNewPassword(e.target.value); setError(null); }} />
                  <button className="lg-reveal" type="button" onClick={() => setShowPassword(!showPassword)} aria-label={showPassword ? "پنهان کردن رمز" : "نمایش رمز"}>
                    <Icon name={showPassword ? "eyeOff" : "eye"} />
                  </button>
                </div>
              </>
            )}
          </div>
        )}

        <ErrorBox text={error} id="lg-error" />
        <Submit busy={busy}
          label={mode === "phone" ? (signup ? "دریافت کد ثبت‌نام" : "دریافت کد ورود") : mode === "reset" ? "دریافت کد" : mode === "password" ? "ورود به حساب" : mode === "code" ? "تأیید و ورود" : "ذخیره‌ی رمز و ورود"}
          icon={codeStep ? "check" : "arrow"} />
      </form>

      {(mode === "reset" || mode === "reset-code") && (
        <div className="lg-support"><button type="button" className="lg-text" onClick={() => go("password")}>بازگشت به ورود با رمز</button></div>
      )}
      {(mode === "phone" || mode === "password") && (
        signup ? (
          <div className="lg-support"><span>حساب دارید؟</span><button type="button" className="lg-text" onClick={() => setSignup(false)}>ورود</button></div>
        ) : (
          <div className="lg-signup">
            <div>
              <strong>هنوز در آرته ثبت‌نام نکرده‌اید؟</strong>
              <span>شماره‌تان را بزنید؛ اگر حسابی نداشته باشید، بعد از تأیید کد ثبت‌نام همین‌جا انجام می‌شود.</span>
            </div>
            <button type="button" className="lg-signup-button" onClick={() => { setSignup(true); go("phone"); }}>ثبت‌نام رایگان</button>
          </div>
        )
      )}
    </AuthLayout>
  );
}

const toFa = (n: number) => new Intl.NumberFormat("fa-IR").format(n);

/** First sign-in of a new number: a short sign-up (name; shop name optional), then the trial welcome. */
export function SignupView({ onCreate }: { onCreate: (ownerName: string, shopName: string) => Promise<number> }) {
  const [ownerName, setOwnerName] = useState("");
  const [shopName, setShopName] = useState("");
  const [error, setError] = useState<{ field?: string; text: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const family = ownerName.trim().split(/\s+/).pop();

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (ownerName.trim().length < 2) { setError({ field: "ownerName", text: "نام و نام خانوادگی را بنویسید." }); return; }
    setBusy(true);
    setError(null);
    try {
      await onCreate(ownerName.trim(), shopName.trim());
    } catch (err) {
      const fields = err instanceof ApiError ? err.fields : {};
      const [field, texts] = Object.entries(fields)[0] ?? [];
      setError({ field, text: texts?.[0] ?? message(err) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <AuthLayout>
      <Heading icon="user" over="ثبت‌نام" title="به آرته سرویس خوش آمدید">
        شماره‌ی شما تأیید شد. فقط نامتان را بنویسید تا وارد شوید؛ ۱۴ روز استفاده‌ی رایگان دارید.
      </Heading>
      <form onSubmit={submit} noValidate>
        <label className="lg-label" htmlFor="lg-owner">نام و نام خانوادگی</label>
        <div className={`lg-input${error?.field === "ownerName" ? " invalid" : ""}`}>
          <span className="lg-input-icon"><Icon name="user" /></span>
          <input id="lg-owner" className="lg-text-input" value={ownerName} onChange={(e) => { setOwnerName(e.target.value); setError(null); }}
            maxLength={80} autoComplete="name" autoFocus />
        </div>
        <label className="lg-label lg-gap" htmlFor="lg-shop">نام تعمیرگاه (اختیاری)</label>
        <div className={`lg-input${error?.field === "name" ? " invalid" : ""}`}>
          <span className="lg-input-icon"><Icon name="shop" /></span>
          <input id="lg-shop" className="lg-text-input" value={shopName} onChange={(e) => setShopName(e.target.value)} maxLength={120}
            placeholder={family ? `تعمیرگاه ${family}` : "بعداً هم می‌توانید بنویسید"} />
        </div>
        <p className="lg-hint">اگر خالی بماند، نام خانوادگی شما گذاشته می‌شود؛ در تنظیمات ← اطلاعات کسب‌وکار قابل تغییر است.</p>
        <ErrorBox text={error?.text ?? null} />
        <Submit busy={busy} label="ثبت‌نام و ورود" />
      </form>
    </AuthLayout>
  );
}

/** Shown once after sign-up: the free period is on. Continues by itself after a few seconds. */
export function TrialWelcome({ days, onContinue }: { days: number; onContinue: () => void }) {
  const done = useRef(onContinue);
  done.current = onContinue;
  useEffect(() => {
    const t = window.setTimeout(() => done.current(), 6000);
    return () => window.clearTimeout(t);
  }, []);
  return (
    <AuthLayout>
      <div className="lg-success" role="status" aria-live="polite">
        <div className="lg-success-mark"><span className="lg-success-ring" /><Icon name="gift" /></div>
        <span className="lg-overline">ثبت‌نام انجام شد</span>
        <h2>{toFa(days)} روز استفاده‌ی رایگان برای شما فعال شد</h2>
        <p>همه‌ی امکانات آرته در این مدت باز است. یک پرونده‌ی نمونه و راهنمای کوتاه هم برایتان آماده کرده‌ایم.</p>
        <div className="lg-success-progress"><span /></div>
        <button className="lg-submit" type="button" onClick={onContinue}><span>ورود به میز کار</span><Icon name="arrow" /></button>
      </div>
    </AuthLayout>
  );
}
