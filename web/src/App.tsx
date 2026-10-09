import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError, applySession, refresh, setSignedOutHandler, type Session } from "./api";
import { CaseDetail, CasesView, type CaseFilter } from "./cases";
import { FeedbackProvider } from "./feedback";
import { HomeView } from "./home";
import { NewCaseView } from "./intake";
import { ReportsPage } from "./reports";
import { Tour, type TourStep } from "./tour";
import { AuthLayout, Login, SignupView, TrialWelcome } from "./login";
import { BranchBlocked, LicenseBanner, LicensePage, LicenseStrip, type LicenseStatus } from "./license";
import { AdminPanel } from "./admin";
import { useFeedback } from "./feedback";
import { AccountPage, AppearancePage, BusinessPage, IntakeRulesPage, ROLE_NAMES, SettingsHome, StaffPage, type SettingsPage } from "./settings";
import { CatalogView, ReceivablesView } from "./billing";
import { Customers } from "./customers";

type Me = {
  id: string;
  mobile: string;
  displayName: string | null;
  openMode: boolean;
  isPlatformAdmin: boolean;
  business: {
    tenantId: string; name: string; role: string; permissions: string[]; requireAssigneeOnIntake: boolean;
    tourDone: boolean; sampleCaseId: string | null; isActive: boolean; license: LicenseStatus;
  } | null;
};
type Tab = "home" | "cases" | "customers" | "settings";


/** adminservice.artepersia.com (or ?admin in development) serves the platform admin panel instead of the shop app. */
const ADMIN_HOST = location.hostname.startsWith("adminservice.") || (import.meta.env.DEV && new URLSearchParams(location.search).has("admin"));

export default function App() {
  return ADMIN_HOST ? <AdminRoot /> : <ShopApp />;
}

/**
 * The admin site: its own sign-in (same accounts), no demo/open mode, and only platform admins get past the door.
 * Desktop gets a wide layout; on a phone it is the same panel as inside the app.
 */
function AdminRoot() {
  const [state, setState] = useState<"boot" | "login" | "denied" | "ok">("boot");
  const [me, setMe] = useState<Me | null>(null);

  const signIn = useCallback(async (s: Session | null) => {
    applySession(s);
    if (!s) { setMe(null); setState("login"); return; }
    try {
      const m = await api<Me>("/api/v1/me");
      setMe(m);
      setState(m.isPlatformAdmin ? "ok" : "denied");
    } catch {
      setState("login");
    }
  }, []);

  useEffect(() => {
    document.title = "مدیریت آرته";
    setSignedOutHandler(() => { setMe(null); setState("login"); });
    refresh().then(signIn).catch(() => setState("login"));
  }, [signIn]);

  async function logout() {
    await api("/api/v1/auth/logout", { method: "POST" }).catch(() => {});
    signIn(null);
  }

  if (state === "boot") return <div className="splash" aria-busy="true" />;
  if (state === "login") return <Login onDone={signIn} />;
  if (state === "denied")
    return (
      <main className="auth">
        <div className="card blocked">
          <h2>دسترسی ندارید</h2>
          <p>این بخش فقط برای مدیریت آرته است. با حساب مدیر وارد شوید.</p>
          <button onClick={logout}>خروج و ورود با حساب دیگر</button>
        </div>
      </main>
    );
  return (
    <FeedbackProvider>
      <div className="admin-app">
        <header className="admin-top">
          <strong>مدیریت آرته</strong>
          <span className="muted small">{me?.displayName ?? me?.mobile}</span>
          <a className="link small" href="https://service.artepersia.com">برنامه‌ی تعمیرگاه</a>
          <button className="link small" onClick={logout}>خروج</button>
        </header>
        <main className="admin-main">
          <AdminPanel />
        </main>
      </div>
    </FeedbackProvider>
  );
}

function ShopApp() {
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
  if (!me.business.isActive) return <BranchBlocked name={me.business.name} onSignOut={() => signIn(null)} />;
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

/* ───────── Business ───────── */

/**
 * After the code is verified: a new number signs up (then sees the trial welcome); someone in several
 * businesses picks one, or registers another.
 */
function ChooseBusiness({ session, onDone }: { session: Session; onDone: (s: Session) => void }) {
  const [welcome, setWelcome] = useState<{ session: Session; days: number } | null>(null);
  const [adding, setAdding] = useState(session.memberships.length === 0);

  const select = (tenantId: string) => api<Session>("/api/v1/auth/select-tenant", { body: { tenantId } });

  async function create(ownerName: string, shopName: string) {
    const t = await api<{ id: string; trialDays: number }>("/api/v1/tenants", { body: { ownerName, name: shopName || undefined } });
    setWelcome({ session: await select(t.id), days: t.trialDays });
    return t.trialDays;
  }

  if (welcome) return <TrialWelcome days={welcome.days} onContinue={() => onDone(welcome.session)} />;
  if (adding) return <SignupView onCreate={create} />;
  return (
    <AuthLayout>
      <div className="lg-heading">
        <span className="lg-overline">انتخاب کسب‌وکار</span>
        <h2>با کدام تعمیرگاه وارد می‌شوید؟</h2>
      </div>
      <div className="settings-list">
        {session.memberships.map((m) => (
          <button key={m.tenantId} className="settings-row" onClick={async () => onDone(await select(m.tenantId))}>
            <span className="settings-row-text"><span>{m.tenantName}</span><span className="muted small">{ROLE_NAMES[m.role] ?? m.role}</span></span>
            <span className="muted" aria-hidden="true">‹</span>
          </button>
        ))}
      </div>
      <div className="lg-support"><button type="button" className="lg-text" onClick={() => setAdding(true)}>ثبت تعمیرگاه جدید</button></div>
    </AuthLayout>
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
  const canIntake = can("cases.create") && (me.openMode || me.business!.license.state !== "expired");
  const { notify } = useFeedback();
  // Businesses from before the tour have no sample yet: it is created first, then the tour opens.
  const needsSample = !me.business!.tourDone && can("cases.create") && !me.business!.sampleCaseId;
  const [touring, setTouring] = useState(!me.business!.tourDone && !needsSample);
  const [sampleId, setSampleId] = useState<string | null>(me.business!.sampleCaseId);
  const [homeKey, setHomeKey] = useState(0);

  async function startTour() {
    if (can("cases.create") && !sampleId) {
      try {
        setSampleId((await api<{ caseId: string }>("/api/v1/onboarding/sample", { method: "POST" })).caseId);
      } catch (err) {
        notify(err instanceof ApiError ? err.message : "پرونده‌ی نمونه ساخته نشد", "error");
        return;
      }
    }
    go("home");
    setTouring(true);
  }

  // Ref guard: React's development double-run must not create two samples.
  const sampleRequested = useRef(false);
  useEffect(() => {
    if (needsSample && !sampleRequested.current) {
      sampleRequested.current = true;
      void startTour();
    }
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  async function removeSample() {
    try {
      await api("/api/v1/onboarding/sample", { method: "DELETE" });
      setSampleId(null);
      setHomeKey((k) => k + 1);
      notify("داده‌های نمونه حذف شد");
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "حذف نشد", "error");
    }
  }

  async function finishTour(remove: boolean) {
    setTouring(false);
    go("home");
    api("/api/v1/onboarding/tour-done", { method: "POST" }).catch(() => {});
    if (remove) await removeSample();
  }

  function openSample() {
    setTab("cases");
    setNewCase(false);
    setMorePage(null);
    setCaseId(sampleId);
  }

  // Owners and managers walk through the sample case; technicians get the short version.
  const tourSteps: TourStep[] = can("cases.create") && sampleId ? [
    { title: "صف کار امروز", target: "queue", go: () => go("home"),
      body: "هر بار که برنامه را باز می‌کنید، این‌جا می‌بینید چه کاری مانده: پرونده‌های باز، کارهای متوقف، آماده‌ی تحویل و بی‌مسئول. روی هر خانه بزنید تا فهرستش باز شود." },
    { title: "پذیرش جدید", target: "new-case", go: () => go("home"),
      body: "وسیله که رسید، از این‌جا ثبتش کنید: شماره‌ی مشتری، نوع و مدل وسیله و ایراد. کمتر از یک دقیقه طول می‌کشد و بقیه را بعداً هم می‌شود کامل کرد." },
    { title: "پرونده‌ی نمونه", target: "case-head", go: openSample,
      body: "برای آشنایی یک پرونده‌ی نمونه ساخته‌ایم. مشتری و تماس، وسیله و پلاک، مرحله‌ی فعلی، مسئول و قول تحویل همه بالای صفحه‌اند." },
    { title: "قدم بعدی", target: "next-action", go: openSample,
      body: "دکمه‌ی بزرگ همیشه کار بعدی پرونده است. کارهای دیگر مثل «کار متوقف است» یا یادداشت، در دکمه‌های کوچک‌تر زیر آن هستند." },
    { title: "قطعه، اجرت و پرداخت", target: "billing", go: openSample,
      body: "هر قطعه و اجرتی که ثبت شود در صورت‌حساب می‌آید. قیمت خرید فقط برای شما دیده می‌شود و سود پرونده را حساب می‌کند. بیعانه و پرداخت‌ها هم همین‌جا ثبت می‌شوند." },
    { title: "عکس کار", target: "photos", go: openSample,
      body: "از وسیله و کار انجام‌شده عکس بگیرید؛ دوربین گوشی مستقیم باز می‌شود و عکس با مرحله‌ی کار در پرونده می‌ماند." },
    { title: "تاریخچه", target: "timeline", go: openSample,
      body: "هر تغییری با نام انجام‌دهنده و ساعتش ثبت می‌شود؛ هیچ اتفاقی در پرونده گم نمی‌شود." },
    { title: "همکاران و تنظیمات", target: "staff", go: () => go("settings"),
      body: "همکاران، نقش و دستمزدشان این‌جا تعریف می‌شود. فهرست قیمت، قوانین پذیرش، گزارش‌ها و نسیه‌ها هم در همین صفحه‌اند و این راهنما را هم از همین‌جا دوباره می‌بینید." },
    { title: "آماده‌اید", go: () => go("home"),
      body: "پرونده‌ی نمونه فقط برای آشنایی است. همین حالا حذفش کنید، یا بعداً از تنظیمات ← «حذف داده‌های نمونه»." },
  ] : [
    { title: "کارهای شما", target: "queue", go: () => go("home"),
      body: "این‌جا می‌بینید چه کارهایی به شما سپرده شده و کدام منتظر یا آماده‌ی تحویل است." },
    { title: "پرونده‌ها", target: "nav",
      body: "پرونده‌هایتان در «پرونده‌ها» هستند. داخل هر پرونده، دکمه‌ی بزرگ قدم بعدی کار است و از کار انجام‌شده عکس می‌گیرید." },
    { title: "آماده‌اید", body: "هر وقت خواستید، این راهنما از تنظیمات ← «راهنمای برنامه» دوباره باز می‌شود." },
  ];

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
    page = <CasesView key={JSON.stringify(caseFilter)} initialFilter={caseFilter} onOpen={setCaseId} onNewCase={startNewCase} canCreate={canIntake} />;
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
    else if (morePage === "license") page = <LicensePage onBack={back} />;
    else if (morePage === "admin" && me.isPlatformAdmin) page = <AdminPanel onBack={back} />;
    else if (morePage === "appearance") page = <AppearancePage onBack={back} applyTheme={applyTheme} />;
    else page = <SettingsHome name={me.displayName ?? me.mobile} mobile={me.mobile} role={me.business!.role} can={can}
      onOpen={setMorePage} onSignOut={onSignOut} openMode={me.openMode}
      onTour={startTour} onRemoveSample={sampleId && can("cases.create") ? removeSample : undefined}
      isPlatformAdmin={me.isPlatformAdmin} licenseText={licenseLine(me.business!.license)} />;
  }
  else page = <HomeView key={`${caseId}-${homeKey}`} onOpen={(id) => { setTab("cases"); setCaseId(id); }} onOpenCases={openCases} onNewCase={startNewCase} canCreate={canIntake} />;

  return (
    <div className="shell">
      {me.openMode && (
        <div className="open-banner" role="status">
          این نسخه‌ی نمایشی آرته سرویس است و هر بازدیدکننده‌ای آن را می‌بیند؛ اطلاعات واقعی وارد نکنید.
        </div>
      )}
      <header className="topbar">
        <strong>{me.business!.name}</strong>
        <span className="muted small">{me.displayName ?? me.mobile}</span>
      </header>
      {!me.openMode && (
        <LicenseBanner status={me.business!.license}
          onOpen={can("settings.manage") ? () => { go("settings"); setMorePage("license"); } : undefined} />
      )}
      <main className="content">
        {undoCase && tab === "cases" && !caseId && (
          <div className="toast" role="status">
            <span>پرونده <span dir="ltr">CASE-{undoCase.number}</span> حذف شد.</span>
            <button className="link" onClick={restoreCase}>بازگردانی</button>
          </div>
        )}
        {tab === "home" && !caseId && !newCase && !me.openMode && can("settings.manage") && (
          <LicenseStrip status={me.business!.license} onOpen={() => { go("settings"); setMorePage("license"); }} />
        )}
        {page}
      </main>
      {touring && <Tour steps={tourSteps} canRemoveSample={!!sampleId && can("cases.create")} onFinish={finishTour} />}
      <nav className="bottom-nav" aria-label="بخش‌ها" data-tour="nav">
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


function licenseLine(s: LicenseStatus) {
  if (s.state === "expired") return "تمام شده";
  return `${s.kind === "trial" ? "دوره‌ی رایگان" : "فعال"} · ${new Intl.NumberFormat("fa-IR").format(s.daysLeft)} روز مانده`;
}
