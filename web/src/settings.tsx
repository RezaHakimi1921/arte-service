import { useCallback, useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { api, ApiError } from "./api";
import { useFeedback } from "./feedback";
import { BottomSheet, SheetOption } from "./sheet";
import { Field, MobileInput, NumberInput, formatNumber, toLatinDigits } from "./ui";
import { resetWorkflow } from "./workflow";
import { SMS_PATTERNS, fillSms, type SmsKey } from "./smsTexts";

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
          {can("cases.create") && <Row icon="price" title="فهرست قیمت" sub="کالا، اجرت و خدمات" onClick={() => onOpen("catalog")} />}
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
  surveyEnabled: boolean; surveySendOn: boolean; surveyDelayMinutes: number; surveyAlertBelow: number; requireTransferReceipt: boolean;
};

function delayText(m: number) {
  const n = (x: number) => new Intl.NumberFormat("fa-IR").format(x);
  if (m < 60) return `${n(m)} دقیقه`;
  const h = Math.floor(m / 60), r = m % 60;
  return r ? `${n(h)} ساعت و ${n(r)} دقیقه` : `${n(h)} ساعت`;
}

const SURVEY_DELAYS: [number, string][] = [[30, "۳۰ دقیقه"], [60, "۱ ساعت"], [180, "۳ ساعت"], [1440, "۲۴ ساعت"]];

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

function Switch({ title, sub, checked, disabled, onChange, link }: {
  title: string; sub: string; checked: boolean; disabled: boolean; onChange: (v: boolean) => void;
  /** A small action inside the row (e.g. see the SMS text); tapping it never flips the switch. */
  link?: { label: string; onClick: () => void };
}) {
  return (
    <label className="setting-row">
      <span>
        <strong>{title}</strong><span className="muted small">{sub}</span>
        {link && <button type="button" className="link row-link" onClick={(e) => { e.preventDefault(); link.onClick(); }}>{link.label}</button>}
      </span>
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
          <Switch title="رسید کارت به کارت لازم باشد" sub="با هر پرداخت کارت به کارت باید عکس رسید بارگذاری شود؛ برای بقیه‌ی روش‌ها اختیاری است."
            checked={s.requireTransferReceipt} disabled={false} onChange={(v) => save({ requireTransferReceipt: v })} />
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
                onClick={() => { if (s.businessType !== b.key) save({ businessType: b.key }); }}>
                <span>{b.label}</span><span className="muted small">{b.hint}</span>
              </button>
            ))}
          </div>
          {(() => {
            // Changing the type never touches the vehicle list; the defaults are only offered.
            const type = BUSINESS_TYPES.find((b) => b.key === s.businessType);
            const same = type && type.kinds.length === s.vehicleKinds.length && type.kinds.every((k) => s.vehicleKinds.includes(k));
            return type && !same ? (
              <div className="hint-action">
                <p className="hint">وسایل پیش‌فرض «{type.label}»: {type.kinds.map((k) => KIND_NAMES[k] ?? k).join("، ")}</p>
                <button type="button" className="secondary" onClick={() => save({ vehicleKinds: type.kinds })}>همین وسایل را انتخاب کن</button>
              </div>
            ) : <p className="hint">عوض کردن نوع، وسایل انتخاب‌شده را تغییر نمی‌دهد؛ پایین‌تر هر وسیله را روشن یا خاموش کنید.</p>;
          })()}
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
  const [confirmSms, setConfirmSms] = useState(false);
  const [support, setSupport] = useState<string | null>(null);
  const [preview, setPreview] = useState<SmsKey | null>(null);
  const [delay, setDelay] = useState("");
  const [customDelay, setCustomDelay] = useState(false);
  useEffect(() => { api<{ supportPhone: string | null }>("/api/v1/public/info").then((i) => setSupport(i.supportPhone)).catch(() => {}); }, []);
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
            <Switch title="ارسال پیامک به مشتری" sub="با نام تعمیرگاه، نام مشتری و لینک پیگیری. پیامک «ارسال دوباره‌ی لینک» از صفحه‌ی پرونده فرستاده می‌شود."
              link={{ label: "دیدن متن پیامک لینک", onClick: () => setPreview("case.link") }}
              checked={s.customerSmsEnabled} disabled={false} onChange={(v) => (v ? setConfirmSms(true) : save({ customerSmsEnabled: false }))} />
            <Switch title="هنگام پذیرش" link={{ label: "دیدن متن پیامک", onClick: () => setPreview("case.opened") }} sub="«… شما پذیرش شد و در نوبت کار قرار گرفت.»"
              checked={s.smsOnOpened} disabled={!s.customerSmsEnabled} onChange={(v) => save({ smsOnOpened: v })} />
            <Switch title="آماده‌ی تحویل" link={{ label: "دیدن متن پیامک", onClick: () => setPreview("case.ready") }} sub="«… شما آماده‌ی تحویل است.»"
              checked={s.smsOnReady} disabled={!s.customerSmsEnabled} onChange={(v) => save({ smsOnReady: v })} />
            <Switch title="هنگام تحویل" link={{ label: "دیدن متن پیامک", onClick: () => setPreview("case.delivered") }} sub="«… شما تحویل شد.» همراه با لینک ضمانت و سابقه."
              checked={s.smsOnDelivered} disabled={!s.customerSmsEnabled} onChange={(v) => save({ smsOnDelivered: v })} />
          </div>
          <h3>صفحه‌ی پیگیری مشتری</h3>
          <p className="hint">وضعیت فعلی، قول تحویل و نام و تلفن تعمیرگاه همیشه نشان داده می‌شود.</p>
          <div className="settings-list">
            <Switch title="همه‌ی مراحل کار" sub="هر مرحله با ساعتش؛ خاموش: فقط وضعیت فعلی."
              checked={s.trackShowStages} disabled={false} onChange={(v) => save({ trackShowStages: v })} />
            <Switch title="کالا و کارها" sub="فهرست کالاهای مصرف‌شده و اجرت‌ها (قیمت خرید هیچ‌وقت نشان داده نمی‌شود)."
              checked={s.trackShowItems} disabled={false} onChange={(v) => save({ trackShowItems: v })} />
            <Switch title="مبلغ‌ها" sub="جمع، پرداخت‌شده و مانده‌ی حساب."
              checked={s.trackShowAmounts} disabled={false} onChange={(v) => save({ trackShowAmounts: v })} />
            <Switch title="عکس‌های جدید را مشتری هم ببیند" sub="پیش‌فرض هنگام گرفتن عکس؛ برای هر عکس در پرونده جدا هم قابل تغییر است."
              checked={s.photosVisibleByDefault} disabled={false} onChange={(v) => save({ photosVisibleByDefault: v })} />
          </div>

          <h3>نظرسنجی رضایت مشتری</h3>
          {s.surveyEnabled ? (
            <>
              <p className="hint">
                بعد از تحویل، یک پیامک کوتاه با لینک نظرسنجی برای مشتری فرستاده می‌شود (فقط بین ساعت ۹ تا ۲۳). نظر مشتری در پرونده،
                زنگوله‌ی اعلان و گزارش‌ها می‌آید و رضایت پایین در خانه نشان داده می‌شود.
              </p>
              <div className="settings-list">
                <Switch title="ارسال نظرسنجی بعد از تحویل" sub="مشتری از لینک پیگیری هم می‌تواند نظر بدهد."
                  link={{ label: "دیدن متن پیامک", onClick: () => setPreview("survey.request") }}
                  checked={s.surveySendOn} disabled={false} onChange={(v) => save({ surveySendOn: v })} />
                <div className="setting-row block">
                  <span><strong>چه مدت بعد از تحویل؟</strong><span className="muted small">الان: {delayText(s.surveyDelayMinutes)} بعد از تحویل (فقط ساعت ۹ تا ۲۳).</span></span>
                  <div className="chips" role="radiogroup" aria-label="زمان ارسال نظرسنجی">
                    {SURVEY_DELAYS.map(([m, label]) => (
                      <button type="button" key={m} role="radio" aria-checked={!customDelay && s.surveyDelayMinutes === m} disabled={!s.surveySendOn}
                        className={`chip-button${!customDelay && s.surveyDelayMinutes === m ? " active" : ""}`}
                        onClick={() => { setCustomDelay(false); save({ surveyDelayMinutes: m }); }}>{label}</button>
                    ))}
                    <button type="button" role="radio" aria-checked={customDelay || !SURVEY_DELAYS.some(([m]) => m === s.surveyDelayMinutes)} disabled={!s.surveySendOn}
                      className={`chip-button${customDelay || !SURVEY_DELAYS.some(([m]) => m === s.surveyDelayMinutes) ? " active" : ""}`}
                      onClick={() => { setCustomDelay(true); setDelay(String(s.surveyDelayMinutes)); }}>دلخواه</button>
                  </div>
                  {customDelay && (
                    <form className="delay-form" onSubmit={(e) => {
                      e.preventDefault();
                      const m = Number(delay);
                      if (m >= 5 && m <= 4320) { save({ surveyDelayMinutes: m }); setCustomDelay(false); }
                    }}>
                      <Field label="چند دقیقه بعد از تحویل؟ (۵ تا ۴۳۲۰)"
                        error={delay && (Number(delay) < 5 || Number(delay) > 4320) ? "بین ۵ دقیقه تا ۷۲ ساعت (۴۳۲۰ دقیقه)." : undefined}>
                        <NumberInput value={delay} onChange={setDelay} max={4} suffix="دقیقه" autoFocus />
                      </Field>
                      <button type="submit" className="primary" disabled={!delay || Number(delay) < 5 || Number(delay) > 4320}>ثبت</button>
                    </form>
                  )}
                </div>
                <div className="setting-row block">
                  <span><strong>رضایت پایین یعنی کمتر از چند ستاره؟</strong>
                    <span className="muted small">برای امتیاز کمتر از {formatNumber(s.surveyAlertBelow)} اعلان می‌آید و پرونده در خانه در «رضایت پایین مشتری» می‌ماند تا پیگیری شود.</span></span>
                  <div className="chips" role="radiogroup" aria-label="حد رضایت پایین">
                    {[2, 3, 4, 5].map((n) => (
                      <button type="button" key={n} role="radio" aria-checked={s.surveyAlertBelow === n}
                        className={`chip-button${s.surveyAlertBelow === n ? " active" : ""}`} onClick={() => save({ surveyAlertBelow: n })}>
                        کمتر از {formatNumber(n)}{n === 3 ? " (پیش‌فرض)" : ""}
                      </button>
                    ))}
                  </div>
                </div>
              </div>
            </>
          ) : (
            <p className="hint">
              نظرسنجی رضایت مشتری یک امکان جداست؛ برای فعال‌سازی با پشتیبانی آرته تماس بگیرید
              {support && <> (<a href={`tel:${support}`} dir="ltr" className="font-num">{support}</a>)</>}.
            </p>
          )}
        </>
      )}
      <BottomSheet open={!!preview} title={SMS_PATTERNS.find((p) => p.key === preview)?.title ?? "متن پیامک"} onClose={() => setPreview(null)}>
        {preview && s && (
          <>
            <div className="sms-preview" dir="rtl">
              {fillSms(SMS_PATTERNS.find((p) => p.key === preview)!.text, {
                shop: s.name, name: "سیما کریمی",
                vehicle: s.businessType === "motorcycle_repair" ? "موتور هوندا CG 125" : "خودرو پژو ۲۰۶",
                code: "k7m2q9xd4p",
              })}
            </div>
            <p className="hint">نمونه با نام نمونه‌ی مشتری و وسیله؛ در پیامک واقعی نام مشتری، وسیله و لینک همان پرونده می‌آید.</p>
          </>
        )}
      </BottomSheet>
      <BottomSheet open={confirmSms} title="ارسال پیامک به مشتری روشن شود؟" onClose={() => setConfirmSms(false)}>
        <p>
          با روشن کردن این گزینه، در مرحله‌هایی که پایین‌تر روشن کرده‌اید (پذیرش، آماده‌ی تحویل، تحویل) برای مشتری پیامک
          فرستاده می‌شود و هزینه‌ی هر پیامک از اعتبار پیامک کم می‌شود.
        </p>
        <SheetOption label="بله، روشن شود" tone="primary" onClick={() => { setConfirmSms(false); save({ customerSmsEnabled: true }); }} />
        <SheetOption label="انصراف" onClick={() => setConfirmSms(false)} />
      </BottomSheet>
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
  commissionFixedRials: number | null; commissionBase: string; surveyNotify: boolean;
};

const COMMISSION_BASES: { value: string; label: string; hint: string }[] = [
  { value: "case_total", label: "کل مبلغ پرونده", hint: "کالا + اجرت و خدمات" },
  { value: "labor", label: "فقط اجرت و خدمات", hint: "بدون کالا" },
  { value: "labor_plus_parts_profit", label: "اجرت + سود کالا", hint: "فروش کالا منهای خرید آن" },
];
const tomanDigits = (rials: number | null | undefined) => (rials ? String(Math.round(rials / 10)) : "");

/** Pay settings for one staff member: fixed monthly salary and/or a commission per delivered case. */
function StaffSheet({ row, surveyEnabled, onClose, onSaved }: { row: StaffRow | null; surveyEnabled: boolean; onClose: () => void; onSaved: () => void }) {
  const { notify } = useFeedback();
  const [name, setName] = useState("");
  const [role, setRole] = useState("technician");
  const [salary, setSalary] = useState("");
  const [type, setType] = useState("none");
  const [percent, setPercent] = useState("");
  const [fixed, setFixed] = useState("");
  const [base, setBase] = useState("case_total");
  const [surveyNotify, setSurveyNotify] = useState(false);
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
    setSurveyNotify(row.surveyNotify);
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
      if (surveyEnabled) body.surveyNotify = surveyNotify;
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
              مثال: پرونده‌ای با <span className="font-num">۳٬۰۰۰٬۰۰۰</span> تومان کالا (سود <span className="font-num">۵۰۰٬۰۰۰</span>) و <span className="font-num">۱٬۰۰۰٬۰۰۰</span> تومان اجرت
              ← سهم این همکار <strong className="font-num">{formatNumber(example)}</strong> تومان.
            </p>
          )}
          {surveyEnabled && (
            <div className="settings-list">
              <Switch title="اعلان نظر مشتری" disabled={false} checked={surveyNotify} onChange={setSurveyNotify}
                sub={row.role === "technician" ? "وقتی مشتری برای کار این همکار نظر داد، در زنگوله‌اش می‌بیند." : "هر نظری که مشتری‌ها می‌دهند، در زنگوله‌اش می‌بیند."} />
            </div>
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

export function StaffPage({ onBack, surveyEnabled = false }: { onBack: () => void; surveyEnabled?: boolean }) {
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
      <StaffSheet row={editing} surveyEnabled={surveyEnabled} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); load(); }} />
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
