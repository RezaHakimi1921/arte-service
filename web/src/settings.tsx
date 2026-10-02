import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from "react";
import { api, ApiError } from "./api";
import { useFeedback } from "./feedback";
import { Field, MobileInput } from "./ui";
import { resetWorkflow } from "./workflow";

export type SettingsPage = "account" | "business" | "intake" | "catalog" | "receivables" | "staff" | "appearance";

export const ROLE_NAMES: Record<string, string> = { owner: "استاد (مالک)", supervisor: "مدیر داخلی", technician: "شاگرد" };

const APP_VERSION = "۰٫۵";

/* ───────── menu ───────── */

const ICON = {
  user: "M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z",
  shop: "M3 9l1.5-5h15L21 9M3 9h18M3 9v11h18V9M9 20v-6h6v6",
  rules: "M9 11l3 3L22 4M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11",
  price: "M20.6 13.4 13.4 20.6a2 2 0 0 1-2.8 0L3 13V3h10l7.6 7.6a2 2 0 0 1 0 2.8zM7.5 7.5h.01",
  credit: "M3 7h18v12H3zM3 11h18M7 15h4",
  staff: "M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM23 21v-2a4 4 0 0 0-3-3.9M16 3.1a4 4 0 0 1 0 7.8",
  theme: "M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z",
  logout: "M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9",
};

function Row({ icon, title, sub, onClick, danger }: { icon: keyof typeof ICON; title: string; sub?: string; onClick: () => void; danger?: boolean }) {
  return (
    <button className={`settings-row${danger ? " danger" : ""}`} onClick={onClick}>
      <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d={ICON[icon]} /></svg>
      <span className="settings-row-text">
        <span>{title}</span>
        {sub && <span className="muted small">{sub}</span>}
      </span>
      {!danger && <span className="chev" aria-hidden="true">‹</span>}
    </button>
  );
}

function Group({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="settings-group">
      <h3>{title}</h3>
      <div className="settings-list">{children}</div>
    </section>
  );
}

/** One simple list; every item opens its own page with one job. */
export function SettingsHome({ name, mobile, role, can, onOpen, onSignOut, openMode }: {
  name: string; mobile: string; role: string; can: (p: string) => boolean; onOpen: (p: SettingsPage) => void;
  onSignOut: () => void; openMode: boolean;
}) {
  async function logout() {
    await api("/api/v1/auth/logout", { method: "POST" }).catch(() => {});
    onSignOut();
  }
  return (
    <section className="settings">
      <h2>تنظیمات</h2>
      <Group title="حساب">
        <Row icon="user" title="حساب من" sub={`${name} · ${ROLE_NAMES[role] ?? role}`} onClick={() => onOpen("account")} />
      </Group>
      {can("settings.manage") && (
        <Group title="کسب‌وکار">
          <Row icon="shop" title="اطلاعات کسب‌وکار" sub="نام، تلفن، نشانی" onClick={() => onOpen("business")} />
          <Row icon="rules" title="قوانین پذیرش و روند کار" sub="مسئول الزامی، بررسی استاد، تأیید مشتری" onClick={() => onOpen("intake")} />
        </Group>
      )}
      {(can("cases.create") || can("payments.record") || can("reports.view")) && (
        <Group title="فروش">
          {can("cases.create") && <Row icon="price" title="فهرست قیمت" sub="قطعه، اجرت، خدمت" onClick={() => onOpen("catalog")} />}
          {(can("payments.record") || can("reports.view")) && <Row icon="credit" title="نسیه‌ها" sub="طلب از مشتریان" onClick={() => onOpen("receivables")} />}
        </Group>
      )}
      {can("staff.manage") && (
        <Group title="مدیریت">
          <Row icon="staff" title="کارکنان و دسترسی‌ها" onClick={() => onOpen("staff")} />
        </Group>
      )}
      <Group title="سیستم">
        <Row icon="theme" title="ظاهر" sub="روشن یا تیره" onClick={() => onOpen("appearance")} />
        {!openMode && <Row icon="logout" title="خروج از حساب" onClick={logout} danger />}
      </Group>
      <p className="muted small version">آرته سرویس · نسخه {APP_VERSION} · <span dir="ltr" className="font-num">{mobile}</span></p>
    </section>
  );
}

export function SubPage({ title, onBack, children }: { title: string; onBack: () => void; children: ReactNode }) {
  return (
    <section>
      <button className="link back" onClick={onBack}>→ تنظیمات</button>
      <h2>{title}</h2>
      {children}
    </section>
  );
}

/* ───────── account ───────── */

type Account = { displayName: string | null; username: string | null; hasPassword: boolean };

export function AccountPage({ onBack }: { onBack: () => void }) {
  const { notify } = useFeedback();
  const [account, setAccount] = useState<Account | null>(null);
  const [displayName, setDisplayName] = useState("");
  const [username, setUsername] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [repeat, setRepeat] = useState("");
  const [currentPassword, setCurrentPassword] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api<Account>("/api/v1/account").then((a) => {
      setAccount(a);
      setDisplayName(a.displayName ?? "");
      setUsername(a.username ?? "");
    }).catch(() => {});
  }, []);

  async function save(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
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
      notify("ذخیره شد");
    } catch (err) {
      if (err instanceof ApiError) {
        const fields = Object.fromEntries(Object.entries(err.fields).map(([k, v]) => [k, v[0]]));
        setErrors(Object.keys(fields).length ? fields : { form: err.message });
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <SubPage title="حساب من" onBack={onBack}>
      {!account ? <div className="splash" aria-busy="true" /> : (
        <form className="plain-form" onSubmit={save} noValidate>
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
          <button className="primary" disabled={busy} aria-busy={busy}>{busy ? "در حال ذخیره…" : "ذخیره"}</button>
        </form>
      )}
    </SubPage>
  );
}

/* ───────── business ───────── */

type BusinessSettings = {
  name: string; phone: string | null; address: string | null;
  requireAssigneeOnIntake: boolean; requireCustomerApproval: boolean; requireFinalReview: boolean;
};

function useBusinessSettings(onSaved: () => void) {
  const { notify } = useFeedback();
  const [s, setS] = useState<BusinessSettings | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => { api<BusinessSettings>("/api/v1/settings/business").then(setS).catch(() => {}); }, []);
  const save = useCallback(async (next: Partial<BusinessSettings>) => {
    if (!s || busy) return;
    setBusy(true);
    try {
      setS(await api<BusinessSettings>("/api/v1/settings/business", { method: "PUT", body: { ...s, ...next } }));
      resetWorkflow();
      notify("ذخیره شد");
      onSaved();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "خطا", "error");
    } finally {
      setBusy(false);
    }
  }, [s, busy, notify, onSaved]);
  return { s, setS, save, busy };
}

export function BusinessPage({ onBack, onSaved }: { onBack: () => void; onSaved: () => void }) {
  const { s, setS, save, busy } = useBusinessSettings(onSaved);
  return (
    <SubPage title="اطلاعات کسب‌وکار" onBack={onBack}>
      {!s ? <div className="splash" aria-busy="true" /> : (
        <form className="plain-form" onSubmit={(e) => { e.preventDefault(); save({}); }}>
          <Field label="نام کسب‌وکار"><input value={s.name} onChange={(e) => setS({ ...s, name: e.target.value })} maxLength={120} /></Field>
          <Field label="تلفن"><input type="tel" inputMode="tel" dir="ltr" value={s.phone ?? ""} onChange={(e) => setS({ ...s, phone: e.target.value })} maxLength={20} /></Field>
          <Field label="نشانی"><textarea rows={2} value={s.address ?? ""} onChange={(e) => setS({ ...s, address: e.target.value })} maxLength={300} /></Field>
          <button className="primary" disabled={busy} aria-busy={busy}>{busy ? "در حال ذخیره…" : "ذخیره"}</button>
        </form>
      )}
    </SubPage>
  );
}

function Switch({ title, sub, checked, disabled, onChange }: { title: string; sub: string; checked: boolean; disabled: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className="setting-row">
      <span><strong>{title}</strong><span className="muted small">{sub}</span></span>
      <input type="checkbox" role="switch" className="switch" checked={checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
    </label>
  );
}

export function IntakeRulesPage({ onBack, onSaved }: { onBack: () => void; onSaved: () => void }) {
  const { s, save, busy } = useBusinessSettings(onSaved);
  return (
    <SubPage title="قوانین پذیرش و روند کار" onBack={onBack}>
      {!s ? <div className="splash" aria-busy="true" /> : (
        <div className="settings-list">
          <Switch title="تعیین مسئول هنگام پذیرش الزامی باشد" sub="هر پرونده جدید همان لحظه به یک همکار سپرده شود."
            checked={s.requireAssigneeOnIntake} disabled={busy} onChange={(v) => save({ requireAssigneeOnIntake: v })} />
          <Switch title="بررسی نهایی توسط استاد" sub="شاگرد پایان کار را اعلام می‌کند، استاد بررسی و تأیید می‌کند، بعد به مشتری اطلاع داده می‌شود."
            checked={s.requireFinalReview} disabled={busy} onChange={(v) => save({ requireFinalReview: v })} />
          <Switch title="تأیید هزینه توسط مشتری قبل از تعمیر" sub="بعد از عیب‌یابی، کار تا تأیید مشتری متوقف می‌ماند. معمولاً لازم نیست چون مشتری خودش کار را سپرده است."
            checked={s.requireCustomerApproval} disabled={busy} onChange={(v) => save({ requireCustomerApproval: v })} />
        </div>
      )}
    </SubPage>
  );
}

/* ───────── appearance ───────── */

export function AppearancePage({ onBack, applyTheme }: { onBack: () => void; applyTheme: (t: string) => void }) {
  const [theme, setTheme] = useState(() => document.documentElement.dataset.theme ?? "light");
  const pick = (t: string) => { applyTheme(t); setTheme(t); };
  return (
    <SubPage title="ظاهر" onBack={onBack}>
      <div className="segmented wide" role="radiogroup" aria-label="تم">
        <button type="button" role="radio" aria-checked={theme === "light"} className={theme === "light" ? "on" : ""} onClick={() => pick("light")}>روشن</button>
        <button type="button" role="radio" aria-checked={theme === "dark"} className={theme === "dark" ? "on" : ""} onClick={() => pick("dark")}>تیره</button>
      </div>
      <p className="hint">در روز و نور زیاد تعمیرگاه، حالت روشن خواناتر است.</p>
    </SubPage>
  );
}

/* ───────── staff ───────── */

type StaffRow = { id: string; mobile: string; displayName: string | null; role: string; isActive: boolean };

export function StaffPage({ onBack }: { onBack: () => void }) {
  const { notify } = useFeedback();
  const [rows, setRows] = useState<StaffRow[] | null>(null);
  const [mobile, setMobile] = useState("");
  const [name, setName] = useState("");
  const [role, setRole] = useState("technician");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => setRows(await api<StaffRow[]>("/api/v1/staff")), []);
  useEffect(() => { load().catch(() => setRows([])); }, [load]);

  async function add(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    setBusy(true);
    try {
      await api("/api/v1/staff", { body: { mobile, displayName: name, role } });
      setMobile("");
      setName("");
      setError(null);
      notify("همکار اضافه شد");
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    } finally {
      setBusy(false);
    }
  }

  async function toggle(s: StaffRow) {
    await api(`/api/v1/staff/${s.id}`, { method: "PATCH", body: { isActive: !s.isActive } }).catch(() => {});
    notify(s.isActive ? "غیرفعال شد" : "فعال شد");
    await load();
  }

  return (
    <SubPage title="کارکنان و دسترسی‌ها" onBack={onBack}>
      {!rows ? <div className="splash" aria-busy="true" /> : (
        <div className="settings-list">
          {rows.map((s) => (
            <div key={s.id} className={`settings-row static${s.isActive ? "" : " inactive"}`}>
              <span className="settings-row-text">
                <span>{s.displayName ?? s.mobile}</span>
                <span className="muted small">{ROLE_NAMES[s.role] ?? s.role}{s.isActive ? "" : " · غیرفعال"}</span>
              </span>
              {s.role !== "owner" && <button onClick={() => toggle(s)}>{s.isActive ? "غیرفعال" : "فعال"}</button>}
            </div>
          ))}
        </div>
      )}
      <h3>افزودن همکار</h3>
      <form className="plain-form" onSubmit={add}>
        <Field label="شماره موبایل" error={error}><MobileInput value={mobile} onChange={setMobile} /></Field>
        <Field label="نام"><input value={name} onChange={(e) => setName(e.target.value)} maxLength={80} /></Field>
        <Field label="نقش">
          <select value={role} onChange={(e) => setRole(e.target.value)}>
            <option value="technician">شاگرد: فقط پرونده‌های خودش</option>
            <option value="supervisor">مدیر داخلی: ثبت پرونده و تخصیص</option>
          </select>
        </Field>
        <button className="primary" disabled={busy} aria-busy={busy}>{busy ? "در حال افزودن…" : "افزودن"}</button>
      </form>
    </SubPage>
  );
}
