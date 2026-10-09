import { useCallback, useEffect, useState, type FormEvent } from "react";
import { ApiError, api } from "./api";
import { useFeedback } from "./feedback";
import { KIND_NAMES, jalaliDate, licenseHeadline, type LicenseStatus } from "./license";
import { ROLE_NAMES } from "./labels";
import { BottomSheet, SelectField, SelectSheet, SheetOption } from "./sheet";
import { Field, NumberInput, formatNumber } from "./ui";

type Plan = { id: string; name: string; months: number; priceRials: number; isActive: boolean; sortOrder: number };
type BusinessRow = {
  id: string; name: string; phone: string | null; createdAt: string; isActive: boolean;
  owner: { displayName: string | null; mobile: string } | null; staff: number; cases: number; lastCaseAt: string | null;
  license: LicenseStatus;
};
type BusinessDetail = {
  id: string; name: string; phone: string | null; address: string | null; createdAt: string; isActive: boolean;
  deactivatedReason: string | null; status: LicenseStatus;
  licenses: { id: string; kind: string; startsAt: string; endsAt: string; priceRials: number; note: string | null; createdAt: string; revoked: boolean; plan: string | null }[];
  members: { role: string; isActive: boolean; displayName: string | null; mobile: string; lastLoginAt: string | null }[];
};

const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;
const errorText = (e: unknown) => (e instanceof ApiError ? e.message : "خطا در ارتباط با سرور");

type Filter = "all" | "trial" | "paying" | "expiring" | "expired" | "inactive";
const FILTERS: { key: Filter; label: string; match: (b: BusinessRow) => boolean }[] = [
  { key: "all", label: "همه", match: () => true },
  { key: "trial", label: "دوره‌ی رایگان", match: (b) => b.isActive && b.license.kind === "trial" && b.license.state !== "expired" },
  { key: "paying", label: "مشترک", match: (b) => b.isActive && b.license.kind !== "trial" && b.license.state !== "expired" },
  { key: "expiring", label: "رو به پایان", match: (b) => b.isActive && b.license.state === "expiring" },
  { key: "expired", label: "تمام‌شده", match: (b) => b.isActive && b.license.state === "expired" },
  { key: "inactive", label: "غیرفعال", match: (b) => !b.isActive },
];

function StatusChip({ b }: { b: Pick<BusinessRow, "isActive" | "license"> }) {
  if (!b.isActive) return <span className="status-chip danger">غیرفعال</span>;
  const tone = b.license.state === "expired" ? "danger" : b.license.state === "expiring" ? "warning" : "ok";
  return <span className={`status-chip ${tone}`}>{licenseHeadline(b.license)}</span>;
}

function AdminPage({ title, back, onBack, children }: { title: string; back: string; onBack?: () => void; children: React.ReactNode }) {
  return (
    <section className="admin">
      {onBack && <button className="link back" onClick={onBack}>→ {back}</button>}
      <h2>{title}</h2>
      {children}
    </section>
  );
}

/** Platform admin panel: every business (branch), its subscription and switch, and the price list. */
export function AdminPanel({ onBack }: { onBack?: () => void }) {
  const [view, setView] = useState<{ page: "list" } | { page: "business"; id: string } | { page: "plans" }>({ page: "list" });
  useEffect(() => { window.scrollTo(0, 0); }, [view]);
  if (view.page === "business") return <AdminBusiness id={view.id} onBack={() => setView({ page: "list" })} />;
  if (view.page === "plans") return <AdminPlans onBack={() => setView({ page: "list" })} />;
  return <AdminList onBack={onBack} onOpen={(id) => setView({ page: "business", id })} onPlans={() => setView({ page: "plans" })} />;
}

function AdminList({ onBack, onOpen, onPlans }: { onBack?: () => void; onOpen: (id: string) => void; onPlans: () => void }) {
  const [q, setQ] = useState("");
  const [filter, setFilter] = useState<Filter>("all");
  const [rows, setRows] = useState<BusinessRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const t = window.setTimeout(() => {
      api<BusinessRow[]>(`/api/v1/admin/businesses${q.trim() ? `?q=${encodeURIComponent(q.trim())}` : ""}`)
        .then((r) => { setRows(r); setError(null); }).catch((e) => setError(errorText(e)));
    }, 250);
    return () => window.clearTimeout(t);
  }, [q]);

  const match = FILTERS.find((f) => f.key === filter)!.match;
  const shown = rows?.filter(match) ?? [];
  return (
    <AdminPage title={onBack ? "پنل مدیریت آرته" : "کسب‌وکارها"} back="تنظیمات" onBack={onBack}>
      {onBack && (
        <p className="muted small">
          نسخه‌ی کامل برای کامپیوتر: <a href="https://adminservice.artepersia.com" target="_blank" rel="noopener">adminservice.artepersia.com</a>
        </p>
      )}
      <div className="admin-tools">
        <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="نام تعمیرگاه، نام یا موبایل مالک" aria-label="جستجوی کسب‌وکار" />
        <button onClick={onPlans}>پلن‌ها و قیمت‌ها</button>
      </div>
      <div className="chips" role="tablist" aria-label="وضعیت">
        {FILTERS.map((f) => (
          <button key={f.key} role="tab" aria-selected={filter === f.key} className={`chip-button${filter === f.key ? " active" : ""}`} onClick={() => setFilter(f.key)}>
            {f.label}{rows && <span className="count font-num"> {formatNumber(rows.filter(f.match).length)}</span>}
          </button>
        ))}
      </div>
      {error && <p className="error" role="alert">{error}</p>}
      {!rows && !error && <div className="splash" aria-busy="true" />}
      {rows && shown.length === 0 && <p className="muted">کسب‌وکاری با این شرایط نیست.</p>}
      <div className="settings-list">
        {shown.map((b) => (
          <button type="button" key={b.id} className="settings-row admin-row" onClick={() => onOpen(b.id)}>
            <span className="settings-row-text admin-cells">
              <span className="admin-row-top"><strong>{b.name}</strong><StatusChip b={b} /></span>
              <span className="muted small admin-cell-owner">
                {b.owner?.displayName ?? "بی‌نام"} · <span dir="ltr" className="font-num">{b.owner?.mobile}</span>
              </span>
              <span className="muted small admin-cell-usage">
                {b.license.state === "expired" ? "بدون اشتراک" : `${formatNumber(b.license.daysLeft)} روز مانده`}
                {" · "}{formatNumber(b.cases)} پرونده · {formatNumber(b.staff)} نفر
                {b.lastCaseAt && <> · آخرین پذیرش {jalaliDate.format(new Date(b.lastCaseAt))}</>}
              </span>
            </span>
            <span className="muted" aria-hidden="true">‹</span>
          </button>
        ))}
      </div>
    </AdminPage>
  );
}

function AdminBusiness({ id, onBack }: { id: string; onBack: () => void }) {
  const { notify } = useFeedback();
  const [b, setB] = useState<BusinessDetail | null>(null);
  const [plans, setPlans] = useState<Plan[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [sheet, setSheet] = useState<null | "grant" | "deactivate" | { revoke: string }>(null);
  const [busy, setBusy] = useState(false);
  const [reason, setReason] = useState("");

  const load = useCallback(async () => {
    try {
      const [detail, p] = await Promise.all([api<BusinessDetail>(`/api/v1/admin/businesses/${id}`), api<Plan[]>("/api/v1/admin/plans")]);
      setB(detail);
      setPlans(p.filter((x) => x.isActive));
    } catch (e) {
      setError(errorText(e));
    }
  }, [id]);
  useEffect(() => { load(); }, [load]);

  async function run(work: () => Promise<unknown>, done: string) {
    setBusy(true);
    try {
      await work();
      notify(done);
      setSheet(null);
      await load();
    } catch (e) {
      notify(errorText(e), "error");
    } finally {
      setBusy(false);
    }
  }

  if (error) return <AdminPage title="کسب‌وکار" back="فهرست" onBack={onBack}><p className="error" role="alert">{error}</p></AdminPage>;
  if (!b) return <div className="splash" aria-busy="true" />;
  const owner = b.members.find((m) => m.role === "owner");
  return (
    <AdminPage title={b.name} back="فهرست" onBack={onBack}>
      <div className={`card license-status ${b.isActive ? b.status.state : "expired"}`}>
        <StatusChip b={{ isActive: b.isActive, license: b.status }} />
        {b.status.state !== "expired" && b.status.endsAt && (
          <p className="license-days">
            <strong className="font-num">{formatNumber(b.status.daysLeft)}</strong> روز مانده
            <span className="muted"> · تا {jalaliDate.format(new Date(b.status.endsAt))}</span>
          </p>
        )}
        {!b.isActive && b.deactivatedReason && <p className="muted small">دلیل: {b.deactivatedReason}</p>}
        <button className="primary block" onClick={() => setSheet("grant")}>ثبت اشتراک یا هدیه</button>
      </div>

      <div className="card admin-facts">
        <p><span className="muted">مالک:</span> {owner?.displayName ?? "بی‌نام"} · <a href={`tel:${owner?.mobile}`} dir="ltr" className="font-num">{owner?.mobile}</a></p>
        {b.phone && <p><span className="muted">تلفن:</span> <span dir="ltr" className="font-num">{b.phone}</span></p>}
        <p><span className="muted">ثبت‌نام:</span> {jalaliDate.format(new Date(b.createdAt))}</p>
        <label className="setting-row">
          <span><strong>شعبه فعال است</strong><span className="muted small">خاموش: هیچ‌کس نمی‌تواند وارد این شعبه شود؛ اطلاعات می‌ماند.</span></span>
          <input type="checkbox" role="switch" className="switch" checked={b.isActive} disabled={busy}
            onChange={(e) => e.target.checked
              ? run(() => api(`/api/v1/admin/businesses/${id}`, { method: "PATCH", body: { isActive: true } }), "شعبه فعال شد")
              : (setReason(""), setSheet("deactivate"))} />
        </label>
      </div>

      <h3>سابقه‌ی اشتراک</h3>
      <div className="settings-list">
        {b.licenses.map((l) => (
          <div key={l.id} className={`settings-row static${l.revoked ? " inactive" : ""}`}>
            <span className="settings-row-text">
              <span>{l.plan ?? KIND_NAMES[l.kind] ?? l.kind}{l.revoked && " (لغو شده)"}</span>
              <span className="muted small">{jalaliDate.format(new Date(l.startsAt))} تا {jalaliDate.format(new Date(l.endsAt))}{l.priceRials > 0 && ` · ${toman(l.priceRials)}`}</span>
              {l.note && <span className="muted small">{l.note}</span>}
            </span>
            {!l.revoked && <button className="link" onClick={() => setSheet({ revoke: l.id })}>لغو</button>}
          </div>
        ))}
      </div>

      <h3>همکاران</h3>
      <div className="settings-list">
        {b.members.map((m) => (
          <div key={m.mobile} className={`settings-row static${m.isActive ? "" : " inactive"}`}>
            <span className="settings-row-text">
              <span>{m.displayName ?? "بی‌نام"} <span className="muted small">· {ROLE_NAMES[m.role] ?? m.role}</span></span>
              <span className="muted small"><span dir="ltr" className="font-num">{m.mobile}</span>{m.lastLoginAt && ` · آخرین ورود ${jalaliDate.format(new Date(m.lastLoginAt))}`}</span>
            </span>
          </div>
        ))}
      </div>

      <GrantSheet open={sheet === "grant"} plans={plans} currentEnd={b.status.state === "expired" ? null : b.status.endsAt} busy={busy}
        onClose={() => setSheet(null)}
        onGrant={(body) => run(() => api(`/api/v1/admin/businesses/${id}/licenses`, { body }), "اشتراک ثبت شد")} />

      <BottomSheet open={sheet === "deactivate"} title="غیرفعال کردن شعبه" onClose={() => setSheet(null)}>
        <p>هیچ‌کدام از کاربران {b.name} تا فعال‌سازی دوباره نمی‌توانند کار کنند. اطلاعات پاک نمی‌شود.</p>
        <Field label="دلیل (برای سابقه)">
          <input value={reason} onChange={(e) => setReason(e.target.value)} maxLength={300} />
        </Field>
        <SheetOption label={busy ? "در حال ذخیره…" : "بله، غیرفعال شود"} tone="danger" disabled={busy}
          onClick={() => run(() => api(`/api/v1/admin/businesses/${id}`, { method: "PATCH", body: { isActive: false, reason } }), "شعبه غیرفعال شد")} />
      </BottomSheet>

      <BottomSheet open={typeof sheet === "object" && sheet !== null} title="لغو اشتراک" onClose={() => setSheet(null)}>
        <p>این دوره از اعتبار شعبه کم می‌شود (برای ثبت اشتباه). در سابقه با برچسب «لغو شده» می‌ماند.</p>
        <SheetOption label={busy ? "در حال لغو…" : "بله، لغو شود"} tone="danger" disabled={busy}
          onClick={() => typeof sheet === "object" && sheet && run(() => api(`/api/v1/admin/licenses/${sheet.revoke}`, { method: "DELETE" }), "اشتراک لغو شد")} />
      </BottomSheet>
    </AdminPage>
  );
}

/** Paid: pick a plan (price prefilled, editable for a discount). Gift: a number of days. Both start where the current one ends. */
function GrantSheet({ open, plans, currentEnd, busy, onClose, onGrant }: {
  open: boolean; plans: Plan[]; currentEnd: string | null; busy: boolean; onClose: () => void; onGrant: (body: Record<string, unknown>) => void;
}) {
  const [kind, setKind] = useState<"paid" | "gift">("paid");
  const [planId, setPlanId] = useState<string | null>(null);
  const [pickPlan, setPickPlan] = useState(false);
  const [price, setPrice] = useState("");
  const [days, setDays] = useState("14");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setKind("paid"); setPlanId(null); setPrice(""); setDays("14"); setNote(""); setError(null);
  }, [open]);

  const plan = plans.find((p) => p.id === planId);
  function submit(e: FormEvent) {
    e.preventDefault();
    if (kind === "paid" && !plan) { setError("پلن را انتخاب کنید."); return; }
    if (kind === "gift" && !(Number(days) > 0)) { setError("تعداد روز را بنویسید."); return; }
    onGrant(kind === "paid"
      ? { kind, planId, priceRials: Number(price || 0) * 10, note: note.trim() || undefined }
      : { kind, days: Number(days), note: note.trim() || undefined });
  }

  return (
    <BottomSheet open={open} title="ثبت اشتراک" onClose={onClose}>
      <form onSubmit={submit} noValidate>
        <div className="segmented wide" role="radiogroup" aria-label="نوع">
          <button type="button" role="radio" aria-checked={kind === "paid"} className={kind === "paid" ? "on" : ""} onClick={() => setKind("paid")}>خرید</button>
          <button type="button" role="radio" aria-checked={kind === "gift"} className={kind === "gift" ? "on" : ""} onClick={() => setKind("gift")}>هدیه</button>
        </div>
        {kind === "paid" ? (
          <>
            <Field label="پلن" error={error}>
              <SelectField label={plan ? `${plan.name} · ${toman(plan.priceRials)}` : undefined} placeholder="انتخاب پلن" invalid={!!error} onOpen={() => setPickPlan(true)} />
            </Field>
            <Field label="مبلغ دریافتی (برای تخفیف تغییر دهید)">
              <NumberInput value={price} onChange={setPrice} max={11} suffix="تومان" />
            </Field>
          </>
        ) : (
          <>
            <div className="chips">
              {["7", "14", "30"].map((d) => (
                <button type="button" key={d} className={`chip-button${days === d ? " active" : ""}`} onClick={() => setDays(d)}>{formatNumber(Number(d))} روز</button>
              ))}
            </div>
            <Field label="تعداد روز" error={error}>
              <NumberInput value={days} onChange={setDays} max={3} suffix="روز" />
            </Field>
          </>
        )}
        <Field label="یادداشت (مثلاً شماره‌ی پیگیری کارت به کارت)">
          <input value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} />
        </Field>
        <p className="hint">{currentEnd ? `از پایان اشتراک فعلی (${jalaliDate.format(new Date(currentEnd))}) اضافه می‌شود.` : "از امروز شروع می‌شود."}</p>
        <button className="primary block" disabled={busy} aria-busy={busy}>{busy ? "در حال ثبت…" : "ثبت"}</button>
      </form>
      <SelectSheet open={pickPlan} title="پلن" value={planId}
        items={plans.map((p) => ({ value: p.id, label: p.name, hint: toman(p.priceRials) }))}
        onClose={() => setPickPlan(false)}
        onSelect={(v) => { setPlanId(v); setPickPlan(false); setError(null); const p = plans.find((x) => x.id === v); if (p) setPrice(String(Math.round(p.priceRials / 10))); }} />
    </BottomSheet>
  );
}

function AdminPlans({ onBack }: { onBack: () => void }) {
  const { notify } = useFeedback();
  const [plans, setPlans] = useState<Plan[] | null>(null);
  const [editing, setEditing] = useState<Plan | "new" | null>(null);
  const [name, setName] = useState("");
  const [months, setMonths] = useState("");
  const [price, setPrice] = useState("");
  const [active, setActive] = useState(true);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => api<Plan[]>("/api/v1/admin/plans").then(setPlans).catch(() => setPlans([])), []);
  useEffect(() => { load(); }, [load]);

  function open(p: Plan | "new") {
    setEditing(p);
    setName(p === "new" ? "" : p.name);
    setMonths(p === "new" ? "" : String(p.months));
    setPrice(p === "new" ? "" : String(Math.round(p.priceRials / 10)));
    setActive(p === "new" ? true : p.isActive);
    setErrors({});
  }

  async function save(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      const body = { name: name.trim(), months: Number(months), priceRials: Number(price || 0) * 10, isActive: active };
      if (editing === "new") await api("/api/v1/admin/plans", { body });
      else if (editing) await api(`/api/v1/admin/plans/${editing.id}`, { method: "PATCH", body });
      notify("ذخیره شد");
      setEditing(null);
      await load();
    } catch (err) {
      const f = err instanceof ApiError ? err.fields : {};
      setErrors(Object.keys(f).length ? Object.fromEntries(Object.entries(f).map(([k, v]) => [k, v[0]])) : { form: errorText(err) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <AdminPage title="پلن‌ها و قیمت‌ها" back="فهرست" onBack={onBack}>
      {!plans ? <div className="splash" aria-busy="true" /> : (
        <div className="settings-list">
          {plans.map((p) => (
            <button type="button" key={p.id} className={`settings-row${p.isActive ? "" : " inactive"}`} onClick={() => open(p)}>
              <span className="settings-row-text">
                <span>{p.name}{!p.isActive && <span className="muted small"> · غیرفعال</span>}</span>
                <span className="muted small">ماهی {toman(p.priceRials / p.months)}</span>
              </span>
              <strong className="font-num">{toman(p.priceRials)}</strong>
            </button>
          ))}
        </div>
      )}
      <button onClick={() => open("new")}>+ پلن تازه</button>
      <BottomSheet open={!!editing} title={editing === "new" ? "پلن تازه" : "ویرایش پلن"} onClose={() => setEditing(null)}>
        <form onSubmit={save} noValidate>
          <Field label="نام" error={errors.name}><input value={name} onChange={(e) => setName(e.target.value)} maxLength={60} /></Field>
          <Field label="مدت (ماه)" error={errors.months}><NumberInput value={months} onChange={setMonths} max={2} suffix="ماه" /></Field>
          <Field label="قیمت" error={errors.priceRials ?? errors.form}><NumberInput value={price} onChange={setPrice} max={11} suffix="تومان" /></Field>
          <label className="setting-row">
            <span><strong>به مشتری‌ها نشان داده شود</strong><span className="muted small">خاموش: در فهرست قیمت نمی‌آید؛ سابقه‌اش می‌ماند.</span></span>
            <input type="checkbox" role="switch" className="switch" checked={active} onChange={(e) => setActive(e.target.checked)} />
          </label>
          <button className="primary block" disabled={busy} aria-busy={busy}>{busy ? "در حال ذخیره…" : "ذخیره"}</button>
        </form>
      </BottomSheet>
    </AdminPage>
  );
}
