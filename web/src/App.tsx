import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, ApiError, applySession, refresh, setSignedOutHandler, type Session } from "./api";
import { CaseDetail, CasesView, type CaseFilter } from "./cases";
import { FeedbackProvider } from "./feedback";
import { HomeView } from "./home";
import { NewCaseView } from "./intake";
import { Customers } from "./customers";
import { Field, MobileInput } from "./ui";

type Me = {
  id: string;
  mobile: string;
  displayName: string | null;
  openMode: boolean;
  business: { tenantId: string; name: string; role: string; permissions: string[] } | null;
};
type Tab = "home" | "cases" | "customers" | "staff" | "more";

const ROLE_NAMES: Record<string, string> = { owner: "استاد (مالک)", supervisor: "مدیر داخلی", technician: "شاگرد" };

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
      <Shell me={me} onSignOut={() => signIn(null)} />
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

function Shell({ me, onSignOut }: { me: Me; onSignOut: () => void }) {
  const [tab, setTab] = useState<Tab>("home");
  const [caseId, setCaseId] = useState<string | null>(null);
  const [newCase, setNewCase] = useState(false);
  const [caseFilter, setCaseFilter] = useState<CaseFilter>({});
  const [undoCase, setUndoCase] = useState<{ id: string; number: number } | null>(null);
  const can = (p: string) => me.business!.permissions.includes(p);

  // A new page starts at the top, not at the previous page's scroll position.
  useEffect(() => { window.scrollTo(0, 0); }, [tab, caseId, newCase]);

  function go(next: Tab) {
    setTab(next);
    setCaseId(null);
    setNewCase(false);
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
    page = <NewCaseView canAssign={can("cases.assign")} onCreated={(id) => { setNewCase(false); setCaseId(id); }} onCancel={() => setNewCase(false)}
      onOpenStaff={() => go(can("staff.manage") ? "staff" : "more")} />;
  else if (tab === "cases" && caseId)
    page = <CaseDetail id={caseId} onBack={() => setCaseId(null)} onDeleted={(number) => { setUndoCase({ id: caseId, number }); setCaseId(null); }} />;
  else if (tab === "cases")
    page = <CasesView key={JSON.stringify(caseFilter)} initialFilter={caseFilter} onOpen={setCaseId} onNewCase={startNewCase} canCreate={can("cases.create")} />;
  else if (tab === "customers") page = <Customers canEdit={can("cases.create")} />;
  else if (tab === "staff") page = <StaffList />;
  else if (tab === "more") page = <More me={me} onSignOut={onSignOut} />;
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
            <span>پرونده <span className="font-num">#{undoCase.number}</span> حذف شد.</span>
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
        {can("staff.manage") && (
          <NavButton active={tab === "staff"} onClick={() => go("staff")} label="کارکنان" icon="M20 7h-4V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v2H4a1 1 0 0 0-1 1v11a1 1 0 0 0 1 1h16a1 1 0 0 0 1-1V8a1 1 0 0 0-1-1zM10 5h4v2h-4z" />
        )}
        <NavButton active={tab === "more"} onClick={() => go("more")} label="بیشتر" icon="M5 12h.01M12 12h.01M19 12h.01" />
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


/* ───────── Staff ───────── */

type StaffRow = { id: string; mobile: string; displayName: string | null; role: string; isActive: boolean; payModel: string; commissionPercent: number | null };

function StaffList() {
  const [rows, setRows] = useState<StaffRow[]>([]);
  const [mobile, setMobile] = useState("");
  const [name, setName] = useState("");
  const [role, setRole] = useState("technician");
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => setRows(await api<StaffRow[]>("/api/v1/staff")), []);
  useEffect(() => { load().catch(() => {}); }, [load]);

  async function add(e: FormEvent) {
    e.preventDefault();
    try {
      await api("/api/v1/staff", { body: { mobile, displayName: name, role } });
      setMobile("");
      setName("");
      setError(null);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    }
  }

  async function toggle(s: StaffRow) {
    await api(`/api/v1/staff/${s.id}`, { method: "PATCH", body: { isActive: !s.isActive } }).catch(() => {});
    await load();
  }

  return (
    <section>
      <h2>کارکنان</h2>
      <ul className="list">
        {rows.map((s) => (
          <li key={s.id} className="row-static">
            <span>
              {s.displayName ?? s.mobile}
              <span className="muted small"> · {ROLE_NAMES[s.role] ?? s.role}</span>
            </span>
            {s.role !== "owner" && (
              <button className={s.isActive ? "" : "primary"} onClick={() => toggle(s)}>
                {s.isActive ? "غیرفعال" : "فعال"}
              </button>
            )}
          </li>
        ))}
      </ul>
      <form className="card" onSubmit={add}>
        <h3>افزودن همکار</h3>
        <Field label="شماره موبایل" error={error}>
          <MobileInput value={mobile} onChange={setMobile} />
        </Field>
        <Field label="نام">
          <input value={name} onChange={(e) => setName(e.target.value)} maxLength={80} />
        </Field>
        <Field label="نقش">
          <select value={role} onChange={(e) => setRole(e.target.value)}>
            <option value="technician">شاگرد: فقط پرونده‌های خودش</option>
            <option value="supervisor">مدیر داخلی: ثبت پرونده و تخصیص</option>
          </select>
        </Field>
        <button className="primary">افزودن</button>
      </form>
    </section>
  );
}

/* ───────── More ───────── */

function More({ me, onSignOut }: { me: Me; onSignOut: () => void }) {
  const [theme, setTheme] = useState(() => document.documentElement.dataset.theme ?? "light");

  function toggleTheme() {
    const next = theme === "dark" ? "light" : "dark";
    applyTheme(next);
    setTheme(next);
  }

  async function logout() {
    await api("/api/v1/auth/logout", { method: "POST" }).catch(() => {});
    onSignOut();
  }

  return (
    <section>
      <div className="card">
        <p className="font-num" dir="ltr">{me.mobile}</p>
        <p className="muted">{ROLE_NAMES[me.business!.role] ?? me.business!.role}</p>
      </div>
      <AccountSettings />
      <button className="row-button" onClick={toggleTheme}>
        <span>تم</span>
        <span className="muted">{theme === "dark" ? "تیره" : "روشن"}</span>
      </button>
      {!me.openMode && <button className="row-button danger" onClick={logout}>خروج</button>}
    </section>
  );
}

type Account = { displayName: string | null; username: string | null; hasPassword: boolean };

function AccountSettings() {
  const [account, setAccount] = useState<Account | null>(null);
  const [displayName, setDisplayName] = useState("");
  const [username, setUsername] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [repeat, setRepeat] = useState("");
  const [currentPassword, setCurrentPassword] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api<Account>("/api/v1/account")
      .then((a) => {
        setAccount(a);
        setDisplayName(a.displayName ?? "");
        setUsername(a.username ?? "");
      })
      .catch(() => {});
  }, []);

  async function save(e: FormEvent) {
    e.preventDefault();
    setSaved(false);
    if (newPassword && newPassword !== repeat) {
      setErrors({ repeat: "تکرار رمز یکسان نیست." });
      return;
    }
    setErrors({});
    setBusy(true);
    try {
      const body: Record<string, string> = { displayName };
      if (username && username !== account?.username) body.username = username;
      if (newPassword) body.newPassword = newPassword;
      if (account?.hasPassword && (body.username || body.newPassword)) body.currentPassword = currentPassword;
      setAccount(await api<Account>("/api/v1/account", { method: "PUT", body }));
      setNewPassword("");
      setRepeat("");
      setCurrentPassword("");
      setSaved(true);
    } catch (err) {
      if (err instanceof ApiError) {
        const fields = Object.fromEntries(Object.entries(err.fields).map(([k, v]) => [k, v[0]]));
        setErrors(Object.keys(fields).length ? fields : { form: err.message });
      }
    } finally {
      setBusy(false);
    }
  }

  if (!account) return null;
  return (
    <form className="card" onSubmit={save} noValidate>
      <h2>تنظیمات حساب کاربری</h2>
      <Field label="نام نمایشی" error={errors.displayName}>
        <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} maxLength={80} />
      </Field>
      <Field label="نام کاربری (انگلیسی)" error={errors.username}>
        <input dir="ltr" autoComplete="username" autoCapitalize="none" value={username} onChange={(e) => setUsername(e.target.value)} maxLength={40} />
      </Field>
      <Field label={account.hasPassword ? "رمز جدید (خالی بگذارید تا عوض نشود)" : "رمز عبور (حداقل ۱۰ کاراکتر)"} error={errors.newPassword}>
        <input type="password" dir="ltr" autoComplete="new-password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
      </Field>
      <Field label="تکرار رمز" error={errors.repeat}>
        <input type="password" dir="ltr" autoComplete="new-password" value={repeat} onChange={(e) => setRepeat(e.target.value)} />
      </Field>
      {account.hasPassword && (
        <Field label="رمز فعلی" error={errors.currentPassword}>
          <input type="password" dir="ltr" autoComplete="current-password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} />
        </Field>
      )}
      {errors.form && <span className="error" role="alert">{errors.form}</span>}
      {saved && <span className="success" role="status">ذخیره شد.</span>}
      <button className="primary" disabled={busy}>ذخیره</button>
    </form>
  );
}

export function applyTheme(theme: string) {
  document.documentElement.dataset.theme = theme;
  document.querySelector('meta[name="theme-color"]')?.setAttribute("content", theme === "light" ? "#f3f5f8" : "#0f1115");
  try {
    localStorage.setItem("theme", theme);
  } catch {
    /* storage may be blocked */
  }
}

