import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, ApiError, applySession, refresh, setSignedOutHandler, type Session } from "./api";
import { CaseDetail, CasesView, type CaseFilter } from "./cases";
import { FeedbackProvider } from "./feedback";
import { HomeView } from "./home";
import { NewCaseView } from "./intake";
import { ReportsPage } from "./reports";
import { AccountPage, AppearancePage, BusinessPage, IntakeRulesPage, ROLE_NAMES, SettingsHome, StaffPage, type SettingsPage } from "./settings";
import { CatalogView, ReceivablesView } from "./billing";
import { Customers } from "./customers";
import { Field, MobileInput } from "./ui";

type Me = {
  id: string;
  mobile: string;
  displayName: string | null;
  openMode: boolean;
  business: { tenantId: string; name: string; role: string; permissions: string[]; requireAssigneeOnIntake: boolean } | null;
};
type Tab = "home" | "cases" | "customers" | "settings";


export default function App() {
  const [booting, setBooting] = useState(true);
  const [bootError, setBootError] = useState<string | null>(null);
  const [session, setSession] = useState<Session | null>(null);
  const [me, setMe] = useState<Me | null>(null);

  const signIn = useCallback(async (s: Session | null) => {
    applySession(s);
    setSession(s);
    setMe(s ? await api<Me>("/api/v1/me") : null);
  }, []);

  useEffect(() => {
    setSignedOutHandler(() => {
      setSession(null);
      setMe(null);
    });
    boot();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [signIn]);

  function boot() {
    setBooting(true);
    setBootError(null);
    refresh()
      .then(async (s) => s ?? (await openModeSession()))
      .then((s) => signIn(s))
      .catch((err) => setBootError(err instanceof ApiError ? err.message : "خطا در ارتباط با سرور"))
      .finally(() => setBooting(false));
  }

  if (booting) return <div className="splash" aria-busy="true" />;
  if (bootError)
    return (
      <main className="auth">
        <div className="card">
          <p>{bootError}</p>
          <button className="primary" onClick={boot}>تلاش دوباره</button>
        </div>
      </main>
    );
  if (!session || !me) return <Login onDone={signIn} />;
  if (!me.business) return <ChooseBusiness session={session} onDone={signIn} />;
  return (
    <FeedbackProvider>
      <Shell me={me} onSignOut={() => signIn(null)} onSettingsChanged={() => { api<Me>("/api/v1/me").then(setMe).catch(() => {}); }} />
    </FeedbackProvider>
  );
}

/** While sign-in is switched off on the server, everyone enters as the owner. Null when it is on. */
async function openModeSession(): Promise<Session | null> {
  try {
    return await api<Session>("/api/v1/auth/open", { method: "POST" });
  } catch {
    return null;
  }
}

/* ───────── Login ───────── */

function Login({ onDone }: { onDone: (s: Session) => void }) {
  const [mode, setMode] = useState<"otp" | "password">("otp");
  if (mode === "password") return <PasswordLogin onDone={onDone} onBack={() => setMode("otp")} />;
  return <OtpLogin onDone={onDone} onPassword={() => setMode("password")} />;
}

function PasswordLogin({ onDone, onBack }: { onDone: (s: Session) => void; onBack: () => void }) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      onDone(await api<Session>("/api/v1/auth/password", { body: { username, password } }));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا در ارتباط با سرور");
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="auth">
      <div className="brand">
        <img src="/icon.svg" alt="" width={56} height={56} />
        <h1>آرته سرویس</h1>
      </div>
      <form className="card" onSubmit={submit} noValidate>
        <Field label="نام کاربری">
          <input dir="ltr" autoComplete="username" autoCapitalize="none" value={username} onChange={(e) => setUsername(e.target.value)} required autoFocus />
        </Field>
        <Field label="رمز عبور" error={error}>
          <input type="password" dir="ltr" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </Field>
        <button className="primary" disabled={busy}>ورود</button>
        <button type="button" className="link" onClick={onBack}>ورود با کد پیامکی</button>
      </form>
    </main>
  );
}

function OtpLogin({ onDone, onPassword }: { onDone: (s: Session) => void; onPassword: () => void }) {
  const [mobile, setMobile] = useState("");
  const [code, setCode] = useState("");
  const [step, setStep] = useState<"mobile" | "code">("mobile");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      if (step === "mobile") {
        await api("/api/v1/auth/otp/request", { body: { mobile } });
        setStep("code");
      } else {
        onDone(await api<Session>("/api/v1/auth/otp/verify", { body: { mobile, code } }));
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا در ارتباط با سرور");
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="auth">
      <div className="brand">
        <img src="/icon.svg" alt="" width={56} height={56} />
        <h1>آرته سرویس</h1>
        <p className="muted">مدیریت پذیرش و پرونده‌های تعمیرگاه</p>
      </div>
      <form className="card" onSubmit={submit} noValidate>
        {step === "mobile" ? (
          <Field label="شماره موبایل" error={error}>
            <MobileInput value={mobile} onChange={setMobile} autoFocus />
          </Field>
        ) : (
          <Field label={`کد ارسال‌شده به ${mobile}`} error={error}>
            <input
              inputMode="numeric" autoComplete="one-time-code" dir="ltr" className="font-num code"
              maxLength={6} value={code} onChange={(e) => setCode(e.target.value)} required autoFocus
            />
          </Field>
        )}
        <button className="primary" disabled={busy}>{step === "mobile" ? "دریافت کد" : "ورود"}</button>
        {step === "code" && (
          <button type="button" className="link" onClick={() => { setStep("mobile"); setCode(""); setError(null); }}>
            تغییر شماره
          </button>
        )}
        {step === "mobile" && (
          <button type="button" className="link" onClick={onPassword}>ورود با نام کاربری</button>
        )}
      </form>
      {/* Public description: anyone opening the site (including an SMS provider's reviewer) sees what it is before logging in. */}
      <section className="card about" aria-labelledby="about-title">
        <h2 id="about-title">آرته سرویس چیست؟</h2>
        <p>
          سامانه‌ی آنلاین مدیریت تعمیرگاه برای تعمیرگاه‌های موتورسیکلت و خودرو: پذیرش وسیله، پرونده‌ی تعمیر هر مشتری،
          سپردن کار به همکاران، ثبت قطعه و اجرت، صورت‌حساب، پرداخت و تحویل وسیله؛ همه روی گوشی.
        </p>
        <ul>
          <li>کاربران: صاحب تعمیرگاه و کارکنانی که خودش اضافه می‌کند.</li>
          <li>ورود با شماره موبایل؛ برای هر ورود یک کد یک‌بار مصرف پیامک می‌شود.</li>
          <li>اطلاعات هر تعمیرگاه جدا و فقط برای کارکنان همان تعمیرگاه قابل دیدن است.</li>
        </ul>
        <p className="muted small">آرته سرویس محصولی از <a href="https://artepersia.com" rel="noopener">آرته</a> است.</p>
      </section>
    </main>
  );
}

/* ───────── Business ───────── */

function ChooseBusiness({ session, onDone }: { session: Session; onDone: (s: Session) => void }) {
  const [name, setName] = useState("");
  const [inviteCode, setInviteCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function select(tenantId: string) {
    onDone(await api<Session>("/api/v1/auth/select-tenant", { body: { tenantId } }));
  }

  async function create(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const t = await api<{ id: string }>("/api/v1/tenants", { body: { name, inviteCode } });
      await select(t.id);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="auth">
      {session.memberships.length > 0 && (
        <section className="card">
          <h2>کسب‌وکار را انتخاب کنید</h2>
          {session.memberships.map((m) => (
            <button key={m.tenantId} className="row-button" onClick={() => select(m.tenantId)}>
              <span>{m.tenantName}</span>
              <span className="muted">{ROLE_NAMES[m.role] ?? m.role}</span>
            </button>
          ))}
        </section>
      )}
      <form className="card" onSubmit={create}>
        <h2>ثبت تعمیرگاه جدید</h2>
        <Field label="نام مغازه">
          <input value={name} onChange={(e) => setName(e.target.value)} maxLength={120} required />
        </Field>
        <Field label="کد دعوت" error={error}>
          <input value={inviteCode} onChange={(e) => setInviteCode(e.target.value)} dir="ltr" autoComplete="off" required />
        </Field>
        <button className="primary" disabled={busy}>ساخت و شروع</button>
      </form>
    </main>
  );
}

/* ───────── Shell ───────── */

function Shell({ me, onSignOut, onSettingsChanged }: { me: Me; onSignOut: () => void; onSettingsChanged: () => void }) {
  const [tab, setTab] = useState<Tab>("home");
  const [caseId, setCaseId] = useState<string | null>(null);
  const [newCase, setNewCase] = useState(false);
  const [caseFilter, setCaseFilter] = useState<CaseFilter>({});
  const [undoCase, setUndoCase] = useState<{ id: string; number: number } | null>(null);
  const [morePage, setMorePage] = useState<SettingsPage | null>(null);
  const can = (p: string) => me.business!.permissions.includes(p);

  // A new page starts at the top, not at the previous page's scroll position.
  useEffect(() => { window.scrollTo(0, 0); }, [tab, caseId, newCase, morePage]);

  function go(next: Tab) {
    setTab(next);
    setCaseId(null);
    setNewCase(false);
    setMorePage(null);
  }
  function openCases(filter: CaseFilter) {
    setCaseFilter(filter);
    go("cases");
  }
  function startNewCase() {
    setTab("cases");
    setCaseId(null);
    setNewCase(true);
  }
  async function restoreCase() {
    if (!undoCase) return;
    await api(`/api/v1/cases/${undoCase.id}/restore`, { method: "POST" }).catch(() => {});
    setCaseId(undoCase.id);
    setUndoCase(null);
  }

  let page;
  if (tab === "cases" && newCase)
    page = <NewCaseView canAssign={can("cases.assign")} requireAssignee={me.business!.requireAssigneeOnIntake} onCreated={(id) => { setNewCase(false); setCaseId(id); }} onCancel={() => setNewCase(false)}
      onOpenStaff={() => { go("settings"); if (can("staff.manage")) setMorePage("staff"); }} />;
  else if (tab === "cases" && caseId)
    page = <CaseDetail id={caseId} onBack={() => setCaseId(null)} onDeleted={(number) => { setUndoCase({ id: caseId, number }); setCaseId(null); }} />;
  else if (tab === "cases")
    page = <CasesView key={JSON.stringify(caseFilter)} initialFilter={caseFilter} onOpen={setCaseId} onNewCase={startNewCase} canCreate={can("cases.create")} />;
  else if (tab === "customers") page = <Customers canEdit={can("cases.create")} />;
  else if (tab === "settings") {
    const back = () => setMorePage(null);
    if (morePage === "receivables") page = <ReceivablesView onBack={back} onOpenCase={(id) => { setTab("cases"); setCaseId(id); }} />;
    else if (morePage === "catalog") page = <CatalogView onBack={back} canSeeCost={can("reports.view")} />;
    else if (morePage === "account") page = <AccountPage onBack={back} />;
    else if (morePage === "business") page = <BusinessPage onBack={back} onSaved={onSettingsChanged} />;
    else if (morePage === "intake") page = <IntakeRulesPage onBack={back} onSaved={onSettingsChanged} />;
    else if (morePage === "staff") page = <StaffPage onBack={back} />;
    else if (morePage === "reports") page = <ReportsPage onBack={back} />;
    else if (morePage === "appearance") page = <AppearancePage onBack={back} applyTheme={applyTheme} />;
    else page = <SettingsHome name={me.displayName ?? me.mobile} mobile={me.mobile} role={me.business!.role} can={can}
      onOpen={setMorePage} onSignOut={onSignOut} openMode={me.openMode} />;
  }
  else page = <HomeView key={String(caseId)} onOpen={(id) => { setTab("cases"); setCaseId(id); }} onOpenCases={openCases} onNewCase={startNewCase} canCreate={can("cases.create")} />;

  return (
    <div className="shell">
      {me.openMode && (
        <div className="open-banner" role="status">
          ورود فعلاً خاموش است و هر کسی با این آدرس وارد می‌شود. اطلاعات واقعی وارد نکنید.
        </div>
      )}
      <header className="topbar">
        <strong>{me.business!.name}</strong>
        <span className="muted small">{me.displayName ?? me.mobile}</span>
      </header>
      <main className="content">
        {undoCase && tab === "cases" && !caseId && (
          <div className="toast" role="status">
            <span>پرونده <span dir="ltr">CASE-{undoCase.number}</span> حذف شد.</span>
            <button className="link" onClick={restoreCase}>بازگردانی</button>
          </div>
        )}
        {page}
      </main>
      <nav className="bottom-nav" aria-label="بخش‌ها">
        <NavButton active={tab === "home"} onClick={() => go("home")} label="خانه" icon="M3 11l9-8 9 8v10a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1z" />
        <NavButton active={tab === "cases"} onClick={() => { setCaseFilter({}); go("cases"); }} label="پرونده‌ها" icon="M9 3h6l1 2h3a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1h3zM8 11h8M8 15h5" />
        {(can("cases.create") || can("cases.view_all")) && (
          <NavButton active={tab === "customers"} onClick={() => go("customers")} label="مشتریان" icon="M16 19v-1a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v1M9 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8zm13 9v-1a4 4 0 0 0-3-3.9M16 2.1a4 4 0 0 1 0 7.8" />
        )}
        <NavButton active={tab === "settings"} onClick={() => go("settings")} label="تنظیمات" icon="M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z" />
      </nav>
    </div>
  );
}

function NavButton({ active, onClick, label, icon }: { active: boolean; onClick: () => void; label: string; icon: string }) {
  return (
    <button className={`nav-item${active ? " active" : ""}`} onClick={onClick} aria-current={active ? "page" : undefined}>
      <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true"><path d={icon} /></svg>
      <span>{label}</span>
    </button>
  );
}


/** Applies a theme; persists it only when the user chose it (startup must not turn the default into a choice). */
export function applyTheme(theme: string, persist = true) {
  document.documentElement.dataset.theme = theme;
  document.querySelector('meta[name="theme-color"]')?.setAttribute("content", theme === "light" ? "#f3f5f8" : "#0f1115");
  if (!persist) return;
  try {
    localStorage.setItem("arte-theme", theme);
  } catch {
    /* storage may be blocked */
  }
}

