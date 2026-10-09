import { useCallback, useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { api, ApiError } from "./api";
import { useFeedback } from "./feedback";
import { BottomSheet } from "./sheet";
import { Field, MobileInput, NumberInput, formatNumber, toLatinDigits } from "./ui";
import { resetWorkflow } from "./workflow";

export type SettingsPage = "account" | "business" | "intake" | "vehicles" | "customer" | "catalog" | "receivables" | "staff" | "appearance" | "reports" | "license" | "admin";

export { ROLE_NAMES } from "./labels";
import { ROLE_NAMES } from "./labels";

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
  report: "M3 3v18h18M7 15l4-4 3 3 5-6",
  vehicle: "M5 17h14M5 17a2 2 0 1 0 4 0M15 17a2 2 0 1 0 4 0M3 17v-5l2.5-5h13l2.5 5v5M3 12h18",
  sms: "M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2zM8 9h8M8 13h5",
  license: "M9 12l2 2 4-4M12 3l7 3v6c0 4.5-3 7.7-7 9-4-1.3-7-4.5-7-9V6z",
  admin: "M3 3h7v7H3zM14 3h7v7h-7zM14 14h7v7h-7zM3 14h7v7H3z",
  help: "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM9.1 9a3 3 0 0 1 5.8 1c0 2-3 3-3 3M12 17h.01",
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

function Group({ title, children, tour }: { title: string; children: ReactNode; tour?: string }) {
  return (
    <section className="settings-group" data-tour={tour}>
      <h3>{title}</h3>
      <div className="settings-list">{children}</div>
    </section>
  );
}

/** One simple list; every item opens its own page with one job. */
export function SettingsHome({ name, mobile, role, can, onOpen, onSignOut, openMode, onTour, onRemoveSample, isPlatformAdmin, licenseText }: {
  name: string; mobile: string; role: string; can: (p: string) => boolean; onOpen: (p: SettingsPage) => void;
  onSignOut: () => void; openMode: boolean; onTour: () => void; onRemoveSample?: () => void;
  isPlatformAdmin: boolean; licenseText: string;
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
          <Row icon="license" title="اشتراک" sub={licenseText} onClick={() => onOpen("license")} />
          <Row icon="vehicle" title="نوع کسب‌وکار و وسایل نقلیه" sub="کدام وسایل در پذیرش نشان داده شوند" onClick={() => onOpen("vehicles")} />
          <Row icon="sms" title="پیامک و پیگیری مشتری" sub="لینک وضعیت پرونده و پیامک به مشتری" onClick={() => onOpen("customer")} />
          <Row icon="rules" title="قوانین پذیرش و روند کار" sub="مسئول الزامی، بررسی استاد، تأیید مشتری" onClick={() => onOpen("intake")} />
        </Group>
      )}
      {(can("cases.create") || can("payments.record") || can("reports.view")) && (
        <Group title="فروش">
          {can("reports.view") && <Row icon="report" title="گزارش‌ها" sub="فروش، دریافتی، نسیه‌ها، دستمزد کارکنان" onClick={() => onOpen("reports")} />}
          {can("cases.create") && <Row icon="price" title="فهرست قیمت" sub="قطعه، اجرت، خدمت" onClick={() => onOpen("catalog")} />}
        </Group>
      )}
      {can("staff.manage") && (
        <Group title="مدیریت" tour="staff">
          <Row icon="staff" title="کارکنان و دسترسی‌ها" onClick={() => onOpen("staff")} />
        </Group>
      )}
      {isPlatformAdmin && (
        <Group title="آرته">
          <Row icon="admin" title="پنل مدیریت آرته" sub="کسب‌وکارها، اشتراک‌ها، قیمت‌ها" onClick={() => onOpen("admin")} />
        </Group>
      )}
      <Group title="سیستم">
        <Row icon="theme" title="ظاهر" sub="روشن یا تیره" onClick={() => onOpen("appearance")} />
        <Row icon="help" title="راهنمای برنامه" sub="تور معرفی روی پرونده‌ی نمونه" onClick={onTour} />
        {onRemoveSample && <Row icon="rules" title="حذف داده‌های نمونه" sub="مشتری و پرونده‌ای که برای آشنایی ساخته شد" onClick={onRemoveSample} />}
        {openMode
          ? <Row icon="user" title="ورود با حساب خودم" sub="خروج از نسخه‌ی نمایشی" onClick={logout} />
          : <Row icon="logout" title="خروج از حساب" onClick={logout} danger />}
      </Group>
      <p className="muted small version">آرته سرویس · نسخه {APP_VERSION} · <span dir="ltr" className="font-num">{mobile}</span></p>
    </section>
  );
}

export function SubPage({ title, onBack, children, backLabel = "تنظیمات" }: { title: string; onBack: () => void; children: ReactNode; backLabel?: string }) {
  return (
    <section>
      <button className="link back" onClick={onBack}>→ {backLabel}</button>
      <h2>{title}</h2>
      {children}
    </section>
  );
}

/* ───────── account ───────── */

type Account = { displayName: string | null; username: string | null; mobile: string; hasPassword: boolean };

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
          {/* The mobile number is the sign-in name; the password is optional (sign-in by SMS code always works). */}
          <Field label="شماره‌ی ورود">
            <input dir="ltr" className="font-num" value={account.mobile} readOnly autoComplete="username" />
          </Field>
          <p className="hint">برای ورود با رمز، همین شماره را بزنید و رمزی که این‌جا می‌گذارید. ورود با کد پیامکی همیشه هم فعال است.</p>
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
  businessType: string; vehicleKinds: string[];
  customerSmsEnabled: boolean; smsOnOpened: boolean; smsOnReady: boolean; smsOnDelivered: boolean;
  trackShowStages: boolean; trackShowItems: boolean; trackShowAmounts: boolean; photosVisibleByDefault: boolean;
};

export const BUSINESS_TYPES: { key: string; label: string; hint: string; kinds: string[] }[] = [
  { key: "motorcycle_repair", label: "موتورسازی", hint: "تعمیر موتورسیکلت", kinds: ["motorcycle"] },
  { key: "car_repair", label: "تعمیرات خودرو", hint: "مکانیکی و تعمیرگاه خودرو", kinds: ["car", "suv", "van", "pickup"] },
  { key: "quick_service", label: "آپاراتی و تعویض روغن", hint: "خدمات سریع خودرو و موتور", kinds: ["car", "suv", "van", "pickup", "motorcycle"] },
];

/**
 * Business settings with instant switches: a change shows at once and only that change is sent; changes go to the
 * server one after another, so quick taps are never lost or overwritten by an older copy. On an error the page
 * reloads the saved values.
 */
function useBusinessSettings(onSaved: () => void) {
  const { notify } = useFeedback();
  const [s, setS] = useState<BusinessSettings | null>(null);
  const [busy, setBusy] = useState(false);
  const queue = useRef<Promise<void>>(Promise.resolve());
  const load = useCallback(() => api<BusinessSettings>("/api/v1/settings/business").then(setS).catch(() => {}), []);
  useEffect(() => { load(); }, [load]);

  const save = useCallback((next: Partial<BusinessSettings>) => {
    setS((prev) => (prev ? { ...prev, ...next } : prev));
    queue.current = queue.current.then(async () => {
      setBusy(true);
      try {
        await api<BusinessSettings>("/api/v1/settings/business", { method: "PUT", body: next });
        resetWorkflow();
        notify("ذخیره شد");
        onSaved();
      } catch (err) {
        notify(err instanceof ApiError ? err.message : "ذخیره نشد", "error");
        await load();
      } finally {
        setBusy(false);
      }
    });
  }, [notify, onSaved, load]);
  return { s, setS, save, busy };
}

export function BusinessPage({ onBack, onSaved }: { onBack: () => void; onSaved: () => void }) {
  const { s, setS, save, busy } = useBusinessSettings(onSaved);
  return (
    <SubPage title="اطلاعات کسب‌وکار" onBack={onBack}>
      {!s ? <div className="splash" aria-busy="true" /> : (
        <form className="plain-form" onSubmit={(e) => { e.preventDefault(); save({ name: s.name, phone: s.phone ?? "", address: s.address ?? "" }); }}>
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
  const { s, save } = useBusinessSettings(onSaved);
  return (
    <SubPage title="قوانین پذیرش و روند کار" onBack={onBack}>
      {!s ? <div className="splash" aria-busy="true" /> : (
        <div className="settings-list">
          <Switch title="تعیین مسئول هنگام پذیرش الزامی باشد" sub="هر پرونده جدید همان لحظه به یک همکار سپرده شود."
            checked={s.requireAssigneeOnIntake} disabled={false} onChange={(v) => save({ requireAssigneeOnIntake: v })} />
          <Switch title="بررسی نهایی توسط استاد" sub="شاگرد پایان کار را اعلام می‌کند، استاد بررسی و تأیید می‌کند، بعد به مشتری اطلاع داده می‌شود."
            checked={s.requireFinalReview} disabled={false} onChange={(v) => save({ requireFinalReview: v })} />
          <Switch title="تأیید هزینه توسط مشتری قبل از تعمیر" sub="بعد از عیب‌یابی، کار تا تأیید مشتری متوقف می‌ماند. معمولاً لازم نیست چون مشتری خودش کار را سپرده است."
            checked={s.requireCustomerApproval} disabled={false} onChange={(v) => save({ requireCustomerApproval: v })} />
        </div>
      )}
    </SubPage>
  );
}

const KIND_NAMES: Record<string, string> = { car: "سواری", suv: "شاسی‌بلند", van: "ون", pickup: "وانت", motorcycle: "موتورسیکلت" };

export function VehiclesPage({ onBack, onSaved }: { onBack: () => void; onSaved: () => void }) {
  const { s, save } = useBusinessSettings(onSaved);
  return (
    <SubPage title="نوع کسب‌وکار و وسایل نقلیه" onBack={onBack}>
      {!s ? <div className="splash" aria-busy="true" /> : (
        <>
          <h3>نوع کسب‌وکار</h3>
          <div className="choice-list" role="radiogroup" aria-label="نوع کسب‌وکار">
            {BUSINESS_TYPES.map((b) => (
              <button type="button" key={b.key} role="radio" aria-checked={s.businessType === b.key}
                className={`choice${s.businessType === b.key ? " on" : ""}`}
                onClick={() => save({ businessType: b.key, vehicleKinds: b.kinds })}>
                <span>{b.label}</span><span className="muted small">{b.hint}</span>
              </button>
            ))}
          </div>
          <p className="hint">با عوض کردن نوع، وسایل پیش‌فرض همان نوع انتخاب می‌شود؛ پایین‌تر می‌توانید دستی تغییرشان دهید.</p>
          <h3>وسایلی که می‌پذیرید</h3>
          <div className="settings-list">
            {Object.entries(KIND_NAMES).map(([k, label]) => {
              const on = s.vehicleKinds.includes(k);
              return (
                <Switch key={k} title={label} sub={on ? "در پذیرش نشان داده می‌شود" : "در پذیرش نشان داده نمی‌شود"} checked={on}
                  disabled={on && s.vehicleKinds.length === 1}
                  onChange={(v) => save({ vehicleKinds: v ? [...s.vehicleKinds, k] : s.vehicleKinds.filter((x) => x !== k) })} />
              );
            })}
          </div>
        </>
      )}
    </SubPage>
  );
}

export function CustomerPage({ onBack, onSaved }: { onBack: () => void; onSaved: () => void }) {
  const { s, save } = useBusinessSettings(onSaved);
  return (
    <SubPage title="پیامک و پیگیری مشتری" onBack={onBack}>
      {!s ? <div className="splash" aria-busy="true" /> : (
        <>
          <p className="hint">
            هر پرونده یک لینک اختصاصی دارد که مشتری بدون ورود، وضعیت کارش را در آن می‌بیند. لینک را از صفحه‌ی پرونده هم
            می‌توانید بفرستید (مثلاً در واتساپ).
          </p>
          <h3>پیامک به مشتری</h3>
          <div className="settings-list">
            <Switch title="ارسال پیامک به مشتری" sub="با نام تعمیرگاه، نام مشتری و لینک پیگیری."
              checked={s.customerSmsEnabled} disabled={false} onChange={(v) => save({ customerSmsEnabled: v })} />
            <Switch title="هنگام پذیرش" sub="«… شما پذیرش شد و در نوبت کار قرار گرفت.»"
              checked={s.smsOnOpened} disabled={!s.customerSmsEnabled} onChange={(v) => save({ smsOnOpened: v })} />
            <Switch title="آماده‌ی تحویل" sub="«… شما آماده‌ی تحویل است.»"
              checked={s.smsOnReady} disabled={!s.customerSmsEnabled} onChange={(v) => save({ smsOnReady: v })} />
            <Switch title="هنگام تحویل" sub="«… شما تحویل شد.» همراه با لینک ضمانت و سابقه."
              checked={s.smsOnDelivered} disabled={!s.customerSmsEnabled} onChange={(v) => save({ smsOnDelivered: v })} />
          </div>
          <h3>صفحه‌ی پیگیری مشتری</h3>
          <p className="hint">وضعیت فعلی، قول تحویل و نام و تلفن تعمیرگاه همیشه نشان داده می‌شود.</p>
          <div className="settings-list">
            <Switch title="همه‌ی مراحل کار" sub="هر مرحله با ساعتش؛ خاموش: فقط وضعیت فعلی."
              checked={s.trackShowStages} disabled={false} onChange={(v) => save({ trackShowStages: v })} />
            <Switch title="قطعات و کارها" sub="فهرست قطعه‌ها، اجرت و خدمت‌های ثبت‌شده (قیمت خرید هیچ‌وقت نشان داده نمی‌شود)."
              checked={s.trackShowItems} disabled={false} onChange={(v) => save({ trackShowItems: v })} />
            <Switch title="مبلغ‌ها" sub="جمع، پرداخت‌شده و مانده‌ی حساب."
              checked={s.trackShowAmounts} disabled={false} onChange={(v) => save({ trackShowAmounts: v })} />
            <Switch title="عکس‌های جدید را مشتری هم ببیند" sub="پیش‌فرض هنگام گرفتن عکس؛ برای هر عکس در پرونده جدا هم قابل تغییر است."
              checked={s.photosVisibleByDefault} disabled={false} onChange={(v) => save({ photosVisibleByDefault: v })} />
          </div>
        </>
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

type StaffRow = {
  id: string; mobile: string; displayName: string | null; role: string; isActive: boolean;
  fixedMonthlyRials: number | null; commissionType: string; commissionPercent: number | null;
  commissionFixedRials: number | null; commissionBase: string;
};

const COMMISSION_BASES: { value: string; label: string; hint: string }[] = [
  { value: "case_total", label: "کل مبلغ پرونده", hint: "قطعه + اجرت + خدمات" },
  { value: "labor", label: "فقط اجرت و خدمات", hint: "بدون قطعه" },
  { value: "labor_plus_parts_profit", label: "اجرت + سود قطعه", hint: "فروش قطعه منهای خرید آن" },
];
const tomanDigits = (rials: number | null | undefined) => (rials ? String(Math.round(rials / 10)) : "");

/** Pay settings for one staff member: fixed monthly salary and/or a commission per delivered case. */
function StaffSheet({ row, onClose, onSaved }: { row: StaffRow | null; onClose: () => void; onSaved: () => void }) {
  const { notify } = useFeedback();
  const [name, setName] = useState("");
  const [role, setRole] = useState("technician");
  const [salary, setSalary] = useState("");
  const [type, setType] = useState("none");
  const [percent, setPercent] = useState("");
  const [fixed, setFixed] = useState("");
  const [base, setBase] = useState("case_total");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!row) return;
    setName(row.displayName ?? "");
    setRole(row.role);
    setSalary(tomanDigits(row.fixedMonthlyRials));
    setType(row.commissionType || "none");
    setPercent(row.commissionPercent != null ? String(row.commissionPercent) : "");
    setFixed(tomanDigits(row.commissionFixedRials));
    setBase(row.commissionBase || "case_total");
    setError(null);
  }, [row]);

  const pct = Number(percent || 0);
  // Worked example: parts sold 3,000,000 (bought 2,500,000) + labor 1,000,000 toman.
  const exampleBase = base === "labor" ? 1_000_000 : base === "labor_plus_parts_profit" ? 1_500_000 : 4_000_000;
  const example = type === "percent" ? Math.round((exampleBase * pct) / 100) : type === "fixed_per_case" ? Number(fixed || 0) : 0;

  async function save(e: FormEvent) {
    e.preventDefault();
    if (!row || busy) return;
    if (type === "percent" && !(pct > 0 && pct <= 100)) { setError("درصد باید بین ۱ تا ۱۰۰ باشد."); return; }
    if (type === "fixed_per_case" && !fixed) { setError("مبلغ ثابت هر پرونده را بنویسید."); return; }
    setBusy(true);
    try {
      const body: Record<string, unknown> = {
        displayName: name.trim(), fixedMonthlyRials: Number(salary || 0) * 10,
        commissionType: type, commissionBase: base,
        commissionFixedRials: Number(fixed || 0) * 10,
      };
      if (type === "percent") body.commissionPercent = pct;
      if (row.role !== "owner") body.role = role;
      await api(`/api/v1/staff/${row.id}`, { method: "PATCH", body });
      notify("ذخیره شد");
      onSaved();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    } finally {
      setBusy(false);
    }
  }

  async function toggleActive() {
    if (!row) return;
    setBusy(true);
    try {
      await api(`/api/v1/staff/${row.id}`, { method: "PATCH", body: { isActive: !row.isActive } });
      notify(row.isActive ? "غیرفعال شد" : "فعال شد");
      onSaved();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    } finally {
      setBusy(false);
    }
  }

  return (
    <BottomSheet open={!!row} title={row ? `ویرایش ${row.displayName ?? row.mobile}` : "ویرایش همکار"} onClose={onClose}>
      {row && (
        <form onSubmit={save} noValidate>
          <Field label="نام"><input value={name} onChange={(e) => setName(e.target.value)} maxLength={80} /></Field>
          {row.role !== "owner" && (
            <Field label="نقش">
              <select value={role} onChange={(e) => setRole(e.target.value)}>
                <option value="technician">شاگرد: فقط پرونده‌های خودش</option>
                <option value="supervisor">مدیر داخلی: ثبت پرونده و تخصیص</option>
              </select>
            </Field>
          )}
          <Field label="حقوق ثابت ماهانه (اختیاری)"><NumberInput value={salary} onChange={setSalary} max={11} suffix="تومان" /></Field>

          <span className="label">پورسانت هر پرونده تحویل‌شده</span>
          <div className="segmented wide" role="radiogroup" aria-label="نوع پورسانت">
            {[["none", "ندارد"], ["percent", "درصدی"], ["fixed_per_case", "مبلغ ثابت"]].map(([v, l]) => (
              <button type="button" key={v} role="radio" aria-checked={type === v} className={type === v ? "on" : ""} onClick={() => setType(v)}>{l}</button>
            ))}
          </div>
          {type === "percent" && (
            <>
              <Field label="درصد">
                <input inputMode="decimal" dir="ltr" className="font-num" value={percent}
                  onChange={(e) => setPercent(toLatinDigits(e.target.value).replace(/[٫،,]/g, ".").replace(/[^\d.]/g, "").slice(0, 5))} />
              </Field>
              <span className="label">درصد از چه مبلغی؟</span>
              <div className="choice-list" role="radiogroup" aria-label="مبنای پورسانت">
                {COMMISSION_BASES.map((b) => (
                  <button type="button" key={b.value} role="radio" aria-checked={base === b.value} className={`choice${base === b.value ? " on" : ""}`} onClick={() => setBase(b.value)}>
                    <span>{b.label}</span><span className="muted small">{b.hint}</span>
                  </button>
                ))}
              </div>
            </>
          )}
          {type === "fixed_per_case" && (
            <Field label="مبلغ برای هر پرونده"><NumberInput value={fixed} onChange={setFixed} max={11} suffix="تومان" /></Field>
          )}
          {type !== "none" && (
            <p className="hint">
              مثال: پرونده‌ای با <span className="font-num">۳٬۰۰۰٬۰۰۰</span> تومان قطعه (سود <span className="font-num">۵۰۰٬۰۰۰</span>) و <span className="font-num">۱٬۰۰۰٬۰۰۰</span> تومان اجرت
              ← سهم این همکار <strong className="font-num">{formatNumber(example)}</strong> تومان.
            </p>
          )}
          {error && <span className="error" role="alert">{error}</span>}
          <button className="primary block" disabled={busy} aria-busy={busy}>{busy ? "در حال ذخیره…" : "ذخیره"}</button>
          {row.role !== "owner" && (
            <button type="button" disabled={busy} onClick={toggleActive}>
              {row.isActive ? "غیرفعال کردن همکار" : "فعال کردن دوباره"}
            </button>
          )}
        </form>
      )}
    </BottomSheet>
  );
}

function payText(s: StaffRow) {
  const parts: string[] = [];
  if (s.fixedMonthlyRials) parts.push(`حقوق ${formatNumber(Math.round(s.fixedMonthlyRials / 10))} تومان`);
  if (s.commissionType === "percent" && s.commissionPercent) parts.push(`${formatNumber(s.commissionPercent)}٪ از ${COMMISSION_BASES.find((b) => b.value === s.commissionBase)?.label ?? ""}`);
  if (s.commissionType === "fixed_per_case" && s.commissionFixedRials) parts.push(`${formatNumber(Math.round(s.commissionFixedRials / 10))} تومان هر پرونده`);
  return parts.join(" · ");
}

export function StaffPage({ onBack }: { onBack: () => void }) {
  const { notify } = useFeedback();
  const [rows, setRows] = useState<StaffRow[] | null>(null);
  const [mobile, setMobile] = useState("");
  const [name, setName] = useState("");
  const [role, setRole] = useState("technician");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [editing, setEditing] = useState<StaffRow | null>(null);

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


  return (
    <SubPage title="کارکنان و دسترسی‌ها" onBack={onBack}>
      {!rows ? <div className="splash" aria-busy="true" /> : (
        <div className="settings-list">
          {rows.map((s) => (
            <button type="button" key={s.id} className={`settings-row${s.isActive ? "" : " inactive"}`} onClick={() => setEditing(s)}>
              <span className="settings-row-text">
                <span>{s.displayName ?? s.mobile}</span>
                <span className="muted small">{ROLE_NAMES[s.role] ?? s.role}{s.isActive ? "" : " · غیرفعال"}</span>
                {payText(s) && <span className="muted small">{payText(s)}</span>}
              </span>
              <span className="muted" aria-hidden="true">‹</span>
            </button>
          ))}
        </div>
      )}
      <StaffSheet row={editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); load(); }} />
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
