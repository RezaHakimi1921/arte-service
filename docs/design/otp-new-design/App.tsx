import { FormEvent, useEffect, useState } from "react";

type IconName =
  | "alert"
  | "arrow"
  | "check"
  | "eye"
  | "eyeOff"
  | "lock"
  | "moon"
  | "phone"
  | "shield"
  | "sun"
  | "wrench";

function Icon({ name }: { name: IconName }) {
  const paths: Record<IconName, React.ReactNode> = {
    alert: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="M12 7v6M12 17h.01" />
      </>
    ),
    arrow: <path d="m9 18 6-6-6-6" />,
    check: <path d="m5 12 4 4L19 6" />,
    eye: (
      <>
        <path d="M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6S2 12 2 12Z" />
        <circle cx="12" cy="12" r="2.5" />
      </>
    ),
    eyeOff: (
      <>
        <path d="m3 3 18 18M10.6 6.2A10 10 0 0 1 12 6c6.5 0 10 6 10 6a17 17 0 0 1-2.1 2.8M6.7 6.7C3.7 8.5 2 12 2 12s3.5 6 10 6a9.8 9.8 0 0 0 3.3-.6M10.2 10.2a2.6 2.6 0 0 0 3.6 3.6" />
      </>
    ),
    lock: (
      <>
        <rect x="4" y="10" width="16" height="11" rx="3" />
        <path d="M8 10V7a4 4 0 0 1 8 0v3" />
      </>
    ),
    moon: <path d="M20.8 14.2A8 8 0 0 1 9.8 3.2 9 9 0 1 0 20.8 14.2Z" />,
    phone: (
      <>
        <rect x="6" y="2" width="12" height="20" rx="3" />
        <path d="M10 18h4" />
      </>
    ),
    shield: <path d="M12 22s8-3.8 8-10V5l-8-3-8 3v7c0 6.2 8 10 8 10Zm-3.5-10 2.2 2.2 4.8-5" />,
    sun: (
      <>
        <circle cx="12" cy="12" r="4" />
        <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
      </>
    ),
    wrench: <path d="M14.7 6.3a4.5 4.5 0 0 0-5.6 5.6L3 18l3 3 6.1-6.1a4.5 4.5 0 0 0 5.6-5.6L15 12l-3-3 2.7-2.7Z" />,
  };

  return (
    <svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      {paths[name]}
    </svg>
  );
}

function Logo() {
  return (
    <div className="logo" aria-hidden="true">
      <svg viewBox="0 0 48 48">
        <path d="M24 8 11.6 38h6.2l2.7-7h7l2.7 7h6.2L24 8Zm-1.7 17.7L24 21l1.7 4.7h-3.4Z" fill="currentColor" />
      </svg>
    </div>
  );
}

function SuccessView({ onReset }: { onReset: () => void }) {
  return (
    <div className="success-view" role="status" aria-live="polite">
      <div className="success-mark">
        <span className="success-ring" />
        <Icon name="check" />
      </div>
      <span className="overline">ورود موفق</span>
      <h2>با موفقیت وارد شدید</h2>
      <p>خوش آمدید؛ تا چند لحظه دیگر به میز کار تعمیرگاه منتقل می‌شوید.</p>
      <div className="success-progress"><span /></div>
      <button className="submit-button success-button" type="button" onClick={onReset}>
        <span>ورود به میز کار</span>
        <Icon name="arrow" />
      </button>
      <button className="text-button another-account" type="button" onClick={onReset}>ورود با حساب دیگر</button>
    </div>
  );
}

export default function App() {
  const [theme, setTheme] = useState<"light" | "dark">(() => {
    const saved = localStorage.getItem("arte-theme");
    if (saved === "light" || saved === "dark") return saved;
    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  });
  const [mode, setMode] = useState<"phone" | "password" | "code" | "success">("phone");
  const [phone, setPhone] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [countdown, setCountdown] = useState(98);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    document.documentElement.dir = "rtl";
    localStorage.setItem("arte-theme", theme);
  }, [theme]);

  useEffect(() => {
    if (mode !== "code" || countdown <= 0) return;
    const timer = window.setInterval(() => setCountdown((current) => Math.max(0, current - 1)), 1000);
    return () => window.clearInterval(timer);
  }, [mode, countdown]);

  const normalizedPhone = phone.replace(/\D/g, "");

  function submit(event: FormEvent) {
    event.preventDefault();
    setError("");
    if (mode !== "code" && mode !== "success" && !/^09\d{9}$/.test(normalizedPhone)) {
      setError("شماره موبایل را به‌صورت صحیح وارد کنید.");
      return;
    }
    if (mode === "password" && password.length < 6) {
      setError("رمز عبور باید حداقل ۶ کاراکتر باشد.");
      return;
    }
    if (mode === "code" && code.length !== 6) {
      setError("کد ۶ رقمی پیامک‌شده را وارد کنید.");
      return;
    }
    setLoading(true);
    window.setTimeout(() => {
      setLoading(false);
      if (mode === "phone") {
        setCountdown(98);
        setMode("code");
        return;
      }
      if (mode === "password") {
        if (password === "123456") {
          setMode("success");
        } else {
          setError("رمز عبور اشتباه است. دوباره تلاش کنید یا از کد پیامکی استفاده کنید.");
        }
        return;
      }
      if (mode === "code") {
        if (code === "123456") {
          setMode("success");
        } else {
          setError("کد واردشده صحیح نیست یا منقضی شده است. لطفاً دوباره بررسی کنید.");
        }
      }
    }, 650);
  }

  function changeMode(nextMode: "phone" | "password") {
    setMode(nextMode);
    setError("");
    setCode("");
  }

  function resetLogin() {
    setMode("phone");
    setPhone("");
    setPassword("");
    setCode("");
    setError("");
  }

  function resendCode() {
    if (countdown > 0) return;
    setCountdown(98);
    setCode("");
    setError("");
  }

  const countdownLabel = `${String(Math.floor(countdown / 60)).padStart(2, "0")}:${String(countdown % 60).padStart(2, "0")}`;

  const formattedPhone = normalizedPhone
    ? normalizedPhone.replace(/^(\d{4})(\d{3})(\d{0,4}).*/, "$1 $2 $3").trim()
    : "شماره شما";

  return (
    <main className="page-shell">
      <div className="ambient ambient-one" />
      <div className="ambient ambient-two" />

      <section className="story-panel" aria-label="معرفی آرته سرویس">
        <div className="story-top">
          <Logo />
          <span>آرته سرویس</span>
        </div>
        <div className="story-copy">
          <span className="eyebrow"><span className="status-dot" />همراه حرفه‌ای تعمیرگاه</span>
          <h1>تعمیرگاه منظم،<br />ذهن <em>آرام‌تر.</em></h1>
          <p>از پذیرش تا تحویل؛ همه‌چیز دقیق، سریع و همیشه در دسترس شماست.</p>
        </div>
        <div className="workflow-card">
          <div className="workflow-head">
            <div><span>وضعیت امروز</span><strong>جریان کار تعمیرگاه</strong></div>
            <span className="live-badge">به‌روز</span>
          </div>
          <div className="workflow-row">
            <div className="workflow-icon"><Icon name="wrench" /></div>
            <div className="workflow-info"><strong>هوندا کلیک ۱۵۰</strong><span>سرویس دوره‌ای و تعویض لنت</span></div>
            <span className="progress">در حال تعمیر</span>
          </div>
          <div className="workflow-track"><span /></div>
          <div className="workflow-meta">
            <span><i className="dot done" /> پذیرش</span>
            <span><i className="dot active" /> تعمیر</span>
            <span><i className="dot" /> تحویل</span>
          </div>
        </div>
        <div className="story-foot">
          <span><Icon name="shield" /> داده‌های شما امن و محرمانه‌اند</span>
          <span className="version">نسخه ۲.۴</span>
        </div>
      </section>

      <section className="auth-panel">
        <header className="mobile-header">
          <div className="mobile-brand"><Logo /><span>آرته سرویس</span></div>
          <button className="theme-button" type="button" onClick={() => setTheme(theme === "light" ? "dark" : "light")} aria-label={theme === "light" ? "فعال‌سازی حالت تاریک" : "فعال‌سازی حالت روشن"}>
            <Icon name={theme === "light" ? "moon" : "sun"} />
          </button>
        </header>

        <button className="desktop-theme-button theme-button" type="button" onClick={() => setTheme(theme === "light" ? "dark" : "light")} aria-label={theme === "light" ? "فعال‌سازی حالت تاریک" : "فعال‌سازی حالت روشن"}>
          <Icon name={theme === "light" ? "moon" : "sun"} />
          <span>{theme === "light" ? "حالت تاریک" : "حالت روشن"}</span>
        </button>

        <div className="auth-content">
          {mode === "success" ? (
            <SuccessView onReset={resetLogin} />
          ) : (
            <>
          <div className="mobile-welcome-icon"><Icon name={mode === "code" ? "shield" : "wrench"} /></div>
          <div className="auth-heading">
            <span className="overline">{mode === "code" ? "تأیید هویت" : "خوش آمدید"}</span>
            <h2>{mode === "code" ? "کد تأیید را وارد کنید" : "وارد حساب خود شوید"}</h2>
            <p>{mode === "code" ? <>کد ارسال‌شده به <bdi dir="ltr">{formattedPhone}</bdi> را وارد کنید.</> : "برای مدیریت سریع‌تر تعمیرگاه، وارد آرته شوید."}</p>
          </div>

          {mode !== "code" && (
            <div className="segment" role="tablist" aria-label="روش ورود">
              <button className={mode === "phone" ? "active" : ""} type="button" role="tab" aria-selected={mode === "phone"} onClick={() => changeMode("phone")}>کد پیامکی</button>
              <button className={mode === "password" ? "active" : ""} type="button" role="tab" aria-selected={mode === "password"} onClick={() => changeMode("password")}>رمز عبور</button>
            </div>
          )}

          <form onSubmit={submit} noValidate>
            {mode !== "code" ? (
              <>
                <label className="field-label" htmlFor="phone">شماره موبایل</label>
                <div className={`input-shell ${error && !normalizedPhone ? "invalid" : ""}`}>
                  <span className="input-icon"><Icon name="phone" /></span>
                  <input id="phone" inputMode="numeric" autoComplete="tel" dir="ltr" value={phone} onChange={(event) => { setPhone(event.target.value.slice(0, 11)); setError(""); }} placeholder="0912 000 0000" aria-describedby={error ? "form-error" : undefined} />
                  {/^09\d{9}$/.test(normalizedPhone) && <span className="valid-icon"><Icon name="check" /></span>}
                </div>
                {mode === "password" && (
                  <>
                    <div className="label-row">
                      <label className="field-label" htmlFor="password">رمز عبور</label>
                      <button type="button" className="text-button">فراموش کرده‌اید؟</button>
                    </div>
                    <div className="input-shell">
                      <span className="input-icon"><Icon name="lock" /></span>
                      <input id="password" type={showPassword ? "text" : "password"} autoComplete="current-password" dir="ltr" value={password} onChange={(event) => { setPassword(event.target.value); setError(""); }} placeholder="رمز عبور" />
                      <button className="reveal-button" type="button" onClick={() => setShowPassword(!showPassword)} aria-label={showPassword ? "پنهان کردن رمز" : "نمایش رمز"}><Icon name={showPassword ? "eyeOff" : "eye"} /></button>
                    </div>
                  </>
                )}
              </>
            ) : (
              <div className="code-area">
                <label className="field-label" htmlFor="code">کد تأیید ۶ رقمی</label>
                <input className="code-input" id="code" inputMode="numeric" autoComplete="one-time-code" dir="ltr" value={code} onChange={(event) => { setCode(event.target.value.replace(/\D/g, "").slice(0, 6)); setError(""); }} placeholder="• • • • • •" autoFocus />
                <div className="code-actions">
                  <button type="button" className="text-button" onClick={() => changeMode("phone")}>تغییر شماره</button>
                  <button type="button" className="text-button resend-button" disabled={countdown > 0} onClick={resendCode}>
                    {countdown > 0 ? <>ارسال دوباره تا <bdi dir="ltr">{countdownLabel}</bdi></> : "ارسال دوباره کد"}
                  </button>
                </div>
              </div>
            )}

            {error && (
              <div className="error-message" id="form-error" role="alert">
                <Icon name="alert" />
                <span>{error}</span>
              </div>
            )}
            <button className="submit-button" type="submit" disabled={loading}>
              <span>{loading ? "لطفاً صبر کنید..." : mode === "phone" ? "دریافت کد ورود" : mode === "password" ? "ورود به حساب" : "تأیید و ورود"}</span>
              {!loading && <Icon name={mode === "code" ? "check" : "arrow"} />}
              {loading && <span className="spinner" />}
            </button>
          </form>

          <div className="support"><span>برای ورود نیاز به راهنمایی دارید؟</span><a href="tel:+982191000000">تماس با پشتیبانی</a></div>
            </>
          )}
        </div>
        <footer className="auth-footer">با ورود به آرته، <a href="#">قوانین و حریم خصوصی</a> را می‌پذیرید.</footer>
      </section>
    </main>
  );
}
