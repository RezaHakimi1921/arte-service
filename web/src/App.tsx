import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from "react";
import { api, ApiError, applySession, refresh, setSignedOutHandler, type Session } from "./api";

type Me = {
  id: string;
  mobile: string;
  displayName: string | null;
  business: { tenantId: string; name: string; role: string; permissions: string[] } | null;
};
type Tab = "home" | "customers" | "staff" | "more";

const ROLE_NAMES: Record<string, string> = { owner: "استاد (مالک)", supervisor: "مدیر داخلی", technician: "شاگرد" };

export default function App() {
  const [booting, setBooting] = useState(true);
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
    refresh()
      .then((s) => signIn(s))
      .catch(() => signIn(null))
      .finally(() => setBooting(false));
  }, [signIn]);

  if (booting) return <div className="splash" aria-busy="true" />;
  if (!session || !me) return <Login onDone={signIn} />;
  if (!me.business) return <ChooseBusiness session={session} onDone={signIn} />;
  return <Shell me={me} onSignOut={() => signIn(null)} />;
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
        <p className="muted">مدیریت پرونده‌های تعمیرگاه</p>
      </div>
      <form className="card" onSubmit={submit} noValidate>
        {step === "mobile" ? (
          <Field label="شماره موبایل" error={error}>
            <input
              type="tel" inputMode="numeric" autoComplete="tel" dir="ltr" className="font-num"
              placeholder="09xxxxxxxxx" value={mobile} onChange={(e) => setMobile(e.target.value)} required autoFocus
            />
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
        <h2>ثبت موتورسازی جدید</h2>
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
  const can = (p: string) => me.business!.permissions.includes(p);

  return (
    <div className="shell">
      <header className="topbar">
        <strong>{me.business!.name}</strong>
        <span className="muted small">{me.displayName ?? me.mobile}</span>
      </header>
      <main className="content">
        {tab === "home" && <Home me={me} />}
        {tab === "customers" && <Customers canEdit={can("cases.create")} />}
        {tab === "staff" && <StaffList />}
        {tab === "more" && <More me={me} onSignOut={onSignOut} />}
      </main>
      <nav className="bottom-nav" aria-label="بخش‌ها">
        <NavButton active={tab === "home"} onClick={() => setTab("home")} label="خانه" icon="M3 11l9-8 9 8v10a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1z" />
        {(can("cases.create") || can("cases.view_all")) && (
          <NavButton active={tab === "customers"} onClick={() => setTab("customers")} label="مشتریان" icon="M16 19v-1a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v1M9 10a4 4 0 1 0 0-8 4 4 0 0 0 0 8zm13 9v-1a4 4 0 0 0-3-3.9M16 2.1a4 4 0 0 1 0 7.8" />
        )}
        {can("staff.manage") && (
          <NavButton active={tab === "staff"} onClick={() => setTab("staff")} label="کارکنان" icon="M20 7h-4V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v2H4a1 1 0 0 0-1 1v11a1 1 0 0 0 1 1h16a1 1 0 0 0 1-1V8a1 1 0 0 0-1-1zM10 5h4v2h-4z" />
        )}
        <NavButton active={tab === "more"} onClick={() => setTab("more")} label="بیشتر" icon="M5 12h.01M12 12h.01M19 12h.01" />
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

function Home({ me }: { me: Me }) {
  return (
    <section>
      <h2>سلام {me.displayName ?? ""}</h2>
      <div className="card">
        <p>نقش شما: <strong>{ROLE_NAMES[me.business!.role] ?? me.business!.role}</strong></p>
        <p className="muted">ثبت پرونده و تخصیص به شاگرد در نسخه بعدی (اسپرینت ۲) اضافه می‌شود. فعلاً مشتریان، موتورها و کارکنان را ثبت کنید.</p>
      </div>
    </section>
  );
}

/* ───────── Customers ───────── */

type CustomerRow = { id: string; mobile: string; fullName: string | null; assetCount: number };
type AssetView = { id: string; title: string; identifier: string | null; attributes: Record<string, string> | null };
type CustomerView = { id: string; mobile: string; fullName: string | null; notes: string | null; assets: AssetView[] };

function Customers({ canEdit }: { canEdit: boolean }) {
  const [q, setQ] = useState("");
  const [rows, setRows] = useState<CustomerRow[]>([]);
  const [openId, setOpenId] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);

  const load = useCallback(async (term: string) => {
    setRows(await api<CustomerRow[]>(`/api/v1/customers?q=${encodeURIComponent(term)}`));
  }, []);

  useEffect(() => {
    const t = setTimeout(() => load(q).catch(() => {}), 250);
    return () => clearTimeout(t);
  }, [q, load]);

  if (openId) return <CustomerDetail id={openId} canEdit={canEdit} onBack={() => { setOpenId(null); load(q); }} />;

  return (
    <section>
      <div className="toolbar">
        <input type="search" placeholder="جستجو با نام یا شماره" value={q} onChange={(e) => setQ(e.target.value)} aria-label="جستجوی مشتری" />
        {canEdit && <button className="primary" onClick={() => setAdding(true)}>+ مشتری</button>}
      </div>
      {adding && <AddCustomer onDone={(id) => { setAdding(false); if (id) setOpenId(id); }} />}
      <ul className="list">
        {rows.map((c) => (
          <li key={c.id}>
            <button className="row-button" onClick={() => setOpenId(c.id)}>
              <span>{c.fullName ?? "بدون نام"}</span>
              <span className="font-num muted" dir="ltr">{c.mobile}</span>
            </button>
          </li>
        ))}
        {rows.length === 0 && <li className="empty muted">مشتری‌ای پیدا نشد.</li>}
      </ul>
    </section>
  );
}

function AddCustomer({ onDone }: { onDone: (id: string | null) => void }) {
  const [mobile, setMobile] = useState("");
  const [fullName, setFullName] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    try {
      const c = await api<{ id: string }>("/api/v1/customers", { body: { mobile, fullName } });
      onDone(c.id);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    }
  }

  return (
    <form className="card" onSubmit={submit}>
      <Field label="شماره موبایل" error={error}>
        <input type="tel" inputMode="numeric" dir="ltr" className="font-num" value={mobile} onChange={(e) => setMobile(e.target.value)} required autoFocus />
      </Field>
      <Field label="نام و نام خانوادگی">
        <input value={fullName} onChange={(e) => setFullName(e.target.value)} maxLength={120} />
      </Field>
      <div className="actions">
        <button className="primary">ثبت</button>
        <button type="button" onClick={() => onDone(null)}>انصراف</button>
      </div>
    </form>
  );
}

function CustomerDetail({ id, canEdit, onBack }: { id: string; canEdit: boolean; onBack: () => void }) {
  const [c, setC] = useState<CustomerView | null>(null);
  const [title, setTitle] = useState("");
  const [plate, setPlate] = useState("");
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => setC(await api<CustomerView>(`/api/v1/customers/${id}`)), [id]);
  useEffect(() => { load().catch(() => {}); }, [load]);

  async function addAsset(e: FormEvent) {
    e.preventDefault();
    try {
      await api(`/api/v1/customers/${id}/assets`, { body: { title, identifier: plate || null } });
      setTitle("");
      setPlate("");
      setError(null);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    }
  }

  if (!c) return <div className="splash" aria-busy="true" />;
  return (
    <section>
      <button className="link back" onClick={onBack}>← مشتریان</button>
      <div className="card">
        <h2>{c.fullName ?? "بدون نام"}</h2>
        <p className="font-num" dir="ltr">{c.mobile}</p>
      </div>
      <h3>موتورها</h3>
      <ul className="list">
        {c.assets.map((a) => (
          <li key={a.id} className="row-static">
            <span>{a.title}</span>
            <span className="muted font-num">{a.identifier ?? ""}</span>
          </li>
        ))}
        {c.assets.length === 0 && <li className="empty muted">هنوز موتوری ثبت نشده.</li>}
      </ul>
      {canEdit && (
        <form className="card" onSubmit={addAsset}>
          <Field label="مدل موتور" error={error}>
            <input placeholder="مثلاً هوندا CG 125" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={120} required />
          </Field>
          <Field label="پلاک یا شماره موتور">
            <input value={plate} onChange={(e) => setPlate(e.target.value)} maxLength={60} />
          </Field>
          <button className="primary">افزودن موتور</button>
        </form>
      )}
    </section>
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
          <input type="tel" inputMode="numeric" dir="ltr" className="font-num" value={mobile} onChange={(e) => setMobile(e.target.value)} required />
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
  const [theme, setTheme] = useState(() => document.documentElement.dataset.theme ?? "dark");

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
      <button className="row-button" onClick={toggleTheme}>
        <span>تم</span>
        <span className="muted">{theme === "dark" ? "تیره" : "روشن"}</span>
      </button>
      <button className="row-button danger" onClick={logout}>خروج</button>
    </section>
  );
}

export function applyTheme(theme: string) {
  document.documentElement.dataset.theme = theme;
  document.querySelector('meta[name="theme-color"]')?.setAttribute("content", theme === "light" ? "#f6f7f9" : "#0f1115");
  try {
    localStorage.setItem("theme", theme);
  } catch {
    /* storage may be blocked */
  }
}

function Field({ label, error, children }: { label: string; error?: string | null; children: ReactNode }) {
  return (
    <label className="field">
      <span className="label">{label}</span>
      {children}
      {error && <span className="error" role="alert">{error}</span>}
    </label>
  );
}
