import { createPortal } from "react-dom";
import { VehicleForm } from "./vehicleForm";
import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError } from "./api";
import { PlateView } from "./plate";
import { Field, MobileInput, formatNumber, toLatinDigits } from "./ui";

type CustomerRow = {
  id: string; mobile: string; fullName: string | null; assetCount: number;
  vehicleTitle: string | null; visitCount: number; valueRials: number;
};
type AssetView = { id: string; title: string; identifier: string | null; kind: string | null; attributes: Record<string, string> | null };
type CustomerView = {
  id: string; mobile: string; fullName: string | null; notes: string | null;
  visitCount: number; valueRials: number; lastVisitAt: string | null; assets: AssetView[];
};
type TrashRow = { id: string; mobile: string; fullName: string | null; deletedAt: string };

const visitDay = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long" });
const toman = (rials: number) => `${formatNumber(Math.round(rials / 10))} تومان`;

const fieldErrors = (err: unknown): Record<string, string> =>
  err instanceof ApiError
    ? Object.keys(err.fields).length
      ? Object.fromEntries(Object.entries(err.fields).map(([k, v]) => [k, v[0]]))
      : { form: err.message }
    : { form: "خطا در ارتباط با سرور" };

function initials(name: string | null) {
  const n = name?.trim();
  if (!n) return null;
  return n.split(/\s+/).slice(0, 2).map((w) => w[0]).join("‌");
}

function visitWhen(iso: string | null) {
  if (!iso) return "هنوز مراجعه نداشته";
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return "هنوز مراجعه نداشته";
  const days = Math.floor((Date.now() - then) / 86_400_000);
  if (days <= 0) return "آخرین مراجعه: امروز";
  if (days === 1) return "آخرین مراجعه: دیروز";
  if (days < 60) return `آخرین مراجعه: ${formatNumber(days)} روز پیش`;
  return `آخرین مراجعه: ${visitDay.format(new Date(iso))}`;
}

function Stroke({ d, size = 18 }: { d: string; size?: number }) {
  return <svg viewBox="0 0 24 24" width={size} height={size} aria-hidden="true"><path d={d} /></svg>;
}

const I = {
  plus: "M12 5v14M5 12h14",
  phone: "M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2z",
  vehicle: "M5 17h14M5 17a2 2 0 1 0 4 0M15 17a2 2 0 1 0 4 0M3 17V11l2-5h14l2 5v6M3 11h18",
  chevron: "M15 6l-6 6 6 6",
  pencil: "M12 20h9M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z",
  trash: "M3 6h18M8 6V4h8v2M6 6l1 14h10l1-14",
  user: "M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z",
};

export function Customers({ canEdit, onNewCase }: { canEdit: boolean; onNewCase?: (mobile: string) => void }) {
  const [q, setQ] = useState("");
  const [rows, setRows] = useState<CustomerRow[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [showTrash, setShowTrash] = useState(false);
  const [undo, setUndo] = useState<{ id: string; name: string } | null>(null);

  const load = useCallback(async (term: string) => {
    setLoadError(null);
    try {
      setRows(await api<CustomerRow[]>(`/api/v1/customers?q=${encodeURIComponent(toLatinDigits(term))}`));
    } catch (err) {
      setRows([]);
      setLoadError(err instanceof ApiError ? err.message : "خطا در ارتباط با سرور");
    }
  }, []);

  useEffect(() => {
    const t = setTimeout(() => { void load(q); }, 250);
    return () => clearTimeout(t);
  }, [q, load]);

  useEffect(() => {
    if (!undo) return;
    const t = setTimeout(() => setUndo(null), 8000);
    return () => clearTimeout(t);
  }, [undo]);

  async function restore(id: string) {
    await api(`/api/v1/customers/${id}/restore`, { method: "POST" }).catch(() => {});
    setUndo(null);
    await load(q);
  }

  if (showTrash) return <Trash onBack={() => { setShowTrash(false); void load(q); }} />;

  return (
    <section className="customers2">
      <div className="page-intro">
        <div>
          <span>باشگاه مشتریان</span>
          <h1>مشتریان</h1>
          <p>سابقه مراجعه، وسیله و پرداخت هر مشتری در یک نگاه</p>
        </div>
        {canEdit && (
          <button type="button" className="primary-action" onClick={() => { setAdding(true); setOpenId(null); }}>
            <Stroke d={I.plus} />مشتری جدید
          </button>
        )}
      </div>

      <label className="search-box cust-search">
        <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></svg>
        <input type="search" placeholder="جست‌وجوی نام یا شماره تماس" value={q} onChange={(e) => setQ(e.target.value)} aria-label="جستجوی مشتری" />
        {q && <button type="button" onClick={() => setQ("")}>پاک کردن</button>}
      </label>

      {undo && (
        <div className="toast" role="status">
          <span>«{undo.name}» حذف شد.</span>
          <button type="button" className="link" onClick={() => restore(undo.id)}>بازگردانی</button>
        </div>
      )}

      {rows === null && <div className="splash" aria-busy="true" />}
      {loadError && (
        <div className="card" role="alert">
          <p>{loadError}</p>
          <button type="button" onClick={() => load(q)}>تلاش دوباره</button>
        </div>
      )}
      {rows && !loadError && rows.length === 0 && (
        q.trim()
          ? <p className="empty muted">مشتری‌ای با این مشخصات پیدا نشد.</p>
          : <div className="empty-state"><h2>هنوز مشتری‌ای ثبت نشده</h2><p className="muted">مشتری‌ها هنگام پذیرش خودکار ثبت می‌شوند. این‌جا سابقه هر مشتری و وسیله‌اش را می‌بینید.</p></div>
      )}
      {rows && rows.length > 0 && (
        <div className="cust-table">
          <div className="cust-head">
            <span>مشتری</span><span>وسیله فعلی</span><span>مراجعه</span><span>پرداخت‌شده</span><span />
          </div>
          {rows.map((c) => (
            <button type="button" key={c.id} className="cust-row" onClick={() => { setOpenId(c.id); setAdding(false); }}>
              <span className="cust-person">
                <i className="cust-avatar" aria-hidden="true">{initials(c.fullName) ?? <Stroke d={I.user} size={16} />}</i>
                <span>
                  <b>{c.fullName ?? "بدون نام"}</b>
                  <small className="font-num" dir="ltr">{c.mobile}</small>
                </span>
              </span>
              <span className="cust-meta"><Stroke d={I.vehicle} size={16} />{c.vehicleTitle ?? "وسیله‌ای ثبت نشده"}</span>
              <span className="cust-meta font-num">{formatNumber(c.visitCount)} مرتبه</span>
              <span className="cust-meta font-num">{toman(c.valueRials)}</span>
              <Stroke d={I.chevron} />
            </button>
          ))}
        </div>
      )}

      {canEdit && (
        <button type="button" className="link trash-link" onClick={() => setShowTrash(true)}>سطل بازیافت مشتریان</button>
      )}

      {(adding || openId) && (
        <ProfileDrawer
          key={adding ? "add" : openId}
          mode={adding ? "add" : "view"}
          id={openId}
          canEdit={canEdit}
          onClose={() => { setAdding(false); setOpenId(null); }}
          onCreated={(id) => { setAdding(false); setOpenId(id); void load(q); }}
          onChanged={() => load(q)}
          onDeleted={(name) => {
            if (openId) setUndo({ id: openId, name });
            setOpenId(null);
            void load(q);
          }}
          onNewCase={onNewCase ? (mobile) => { setOpenId(null); onNewCase(mobile); } : undefined}
        />
      )}
    </section>
  );
}

function ProfileDrawer({ mode, id, canEdit, onClose, onCreated, onChanged, onDeleted, onNewCase }: {
  mode: "add" | "view"; id: string | null; canEdit: boolean; onClose: () => void;
  onCreated: (id: string) => void; onChanged: () => void; onDeleted: (name: string) => void;
  onNewCase?: (mobile: string) => void;
}) {
  const panel = useRef<HTMLElement>(null);
  const [c, setC] = useState<CustomerView | null>(null);
  const [missing, setMissing] = useState(false);
  const [editing, setEditing] = useState(false);
  const [assetId, setAssetId] = useState<string | "new" | null>(null);
  const [busyDelete, setBusyDelete] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!id) return;
    try {
      setC(await api<CustomerView>(`/api/v1/customers/${id}`));
      setMissing(false);
    } catch {
      setMissing(true);
    }
  }, [id]);

  useEffect(() => { void load(); }, [load]);

  const closeRef = useRef(onClose);
  closeRef.current = onClose;
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    const root = panel.current;
    (root?.querySelector<HTMLElement>("input, textarea") ?? root?.querySelector<HTMLElement>("button"))?.focus();
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") closeRef.current(); };
    document.addEventListener("keydown", onKey);
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = "";
      previous?.focus?.();
    };
  }, []);

  const mark = initials(c?.fullName ?? null);
  const eyebrow = mode === "add" ? "افزودن به باشگاه" : editing ? "ویرایش" : assetId ? "وسیله نقلیه" : "پروفایل مشتری";
  const title = mode === "add" ? "مشتری جدید" : editing ? "ویرایش اطلاعات" : assetId === "new" ? "افزودن وسیله" : assetId ? "ویرایش وسیله" : (c?.fullName ?? "بدون نام");

  return createPortal(
    <div className="cust-root">
      <button type="button" className="cust-scrim" aria-label="بستن" onClick={onClose} />
      <aside className="cust-drawer" role="dialog" aria-modal="true" aria-labelledby="cust-drawer-title" ref={panel}>
        <header>
          <div>
            <span>{eyebrow}</span>
            <h2 id="cust-drawer-title">{title}</h2>
          </div>
          <button type="button" className="icon-button" onClick={onClose} aria-label="بستن">✕</button>
        </header>

        {mode === "add" && <AddCustomer onDone={(created) => { if (created) onCreated(created); else onClose(); }} />}

        {mode === "view" && !c && !missing && <div className="splash" aria-busy="true" />}
        {missing && <p className="error" role="alert">این مشتری پیدا نشد.</p>}

        {c && editing && (
          <CustomerForm customer={c} onDone={(updated) => { setEditing(false); if (updated) { setC({ ...c, ...updated }); onChanged(); } }} />
        )}

        {c && assetId && (
          <AssetForm
            customerId={c.id}
            asset={assetId === "new" ? undefined : c.assets.find((a) => a.id === assetId)}
            onDone={() => { setAssetId(null); void load().then(onChanged); }}
          />
        )}

        {c && !editing && !assetId && (
          <>
            <div className="cust-hero">
              <i className="cust-avatar lg" aria-hidden="true">{mark ?? <Stroke d={I.user} size={28} />}</i>
              <b>{c.fullName ?? "بدون نام"}</b>
              <a href={`tel:${c.mobile}`}><Stroke d={I.phone} size={16} /><span className="font-num" dir="ltr">{c.mobile}</span></a>
              {c.notes && <p className="muted">{c.notes}</p>}
            </div>
            <div className="cust-stats">
              <div><b className="font-num">{formatNumber(c.visitCount)}</b><small>مراجعه</small></div>
              <div><b className="font-num">{toman(c.valueRials)}</b><small>جمع پرداخت</small></div>
            </div>
            <section className="cust-section">
              <header>
                <h3>وسایل نقلیه</h3>
                {canEdit && <button type="button" onClick={() => setAssetId("new")}><Stroke d={I.plus} size={14} />افزودن</button>}
              </header>
              {(c.visitCount > 0 || c.lastVisitAt) && <p className="muted small">{visitWhen(c.lastVisitAt)}</p>}
              {c.assets.map((a) => (
                <div key={a.id} className="cust-vehicle">
                  {canEdit ? (
                    <button type="button" className="cust-vehicle-main" onClick={() => setAssetId(a.id)}>
                      <VehicleLine asset={a} />
                    </button>
                  ) : (
                    <div className="cust-vehicle-main"><VehicleLine asset={a} /></div>
                  )}
                </div>
              ))}
              {c.assets.length === 0 && <p className="muted">هنوز وسیله‌ای ثبت نشده.</p>}
            </section>
            {canEdit && (
              <section className="cust-section">
                <header><h3>دسترسی سریع</h3></header>
                <div className="cust-actions">
                  {onNewCase && (
                    <button type="button" onClick={() => onNewCase(c.mobile)}><Stroke d={I.plus} />پذیرش جدید</button>
                  )}
                  <button type="button" onClick={() => setEditing(true)}><Stroke d={I.pencil} />ویرایش اطلاعات</button>
                  <button type="button" className="danger" disabled={busyDelete} aria-busy={busyDelete} onClick={async () => {
                    setBusyDelete(true);
                    setDeleteError(null);
                    try {
                      await api(`/api/v1/customers/${c.id}`, { method: "DELETE" });
                      onDeleted(c.fullName ?? c.mobile);
                    } catch (err) {
                      setDeleteError(err instanceof ApiError ? err.message : "حذف نشد.");
                      setBusyDelete(false);
                    }
                  }}><Stroke d={I.trash} />{busyDelete ? "در حال حذف…" : "حذف مشتری"}</button>
                  {deleteError && <p className="error" role="alert">{deleteError}</p>}
                </div>
              </section>
            )}
          </>
        )}
      </aside>
    </div>,
    document.body,
  );
}

function AddCustomer({ onDone }: { onDone: (id: string | null) => void }) {
  const [mobile, setMobile] = useState("");
  const [fullName, setFullName] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!/^09\d{9}$/.test(mobile)) {
      setErrors({ mobile: "شماره باید با ۰۹ شروع شود و ۱۱ رقم باشد." });
      return;
    }
    setBusy(true);
    try {
      const created = await api<{ id: string }>("/api/v1/customers", { body: { mobile, fullName } });
      onDone(created.id);
    } catch (err) {
      setErrors(fieldErrors(err));
      setBusy(false);
    }
  }

  return (
    <form className="cust-form" onSubmit={submit} noValidate>
      <Field label="نام و نام خانوادگی" error={errors.fullName}>
        <input value={fullName} onChange={(e) => setFullName(e.target.value)} maxLength={120} placeholder="نام مشتری" />
      </Field>
      <Field label="شماره موبایل" error={errors.mobile ?? errors.form}>
        <MobileInput value={mobile} onChange={setMobile} />
      </Field>
      <button type="submit" className="primary block" disabled={busy} aria-busy={busy}>{busy ? "در حال ذخیره…" : "ذخیره مشتری"}</button>
      <button type="button" onClick={() => onDone(null)}>انصراف</button>
    </form>
  );
}

function CustomerForm({ customer, onDone }: { customer: CustomerView; onDone: (updated: Partial<CustomerView> | null) => void }) {
  const [fullName, setFullName] = useState(customer.fullName ?? "");
  const [notes, setNotes] = useState(customer.notes ?? "");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      onDone(await api<CustomerView>(`/api/v1/customers/${customer.id}`, { method: "PATCH", body: { fullName, notes } }));
    } catch (err) {
      setErrors(fieldErrors(err));
      setBusy(false);
    }
  }

  return (
    <form className="cust-form" onSubmit={submit} noValidate>
      <Field label="شماره موبایل">
        <input className="font-num" dir="ltr" value={customer.mobile} readOnly aria-readonly="true" />
      </Field>
      <Field label="نام و نام خانوادگی" error={errors.fullName ?? errors.form}>
        <input value={fullName} onChange={(e) => setFullName(e.target.value)} maxLength={120} />
      </Field>
      <Field label="یادداشت" error={errors.notes}>
        <textarea value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} rows={3} />
      </Field>
      <button type="submit" className="primary block" disabled={busy} aria-busy={busy}>{busy ? "در حال ذخیره…" : "ذخیره"}</button>
      <button type="button" onClick={() => onDone(null)}>انصراف</button>
    </form>
  );
}

function VehicleLine({ asset }: { asset: AssetView }) {
  return (
    <>
      <span className="cust-vehicle-icon"><Stroke d={I.vehicle} size={16} /></span>
      <span><b>{asset.title}</b></span>
      {asset.identifier && <PlateView identifier={asset.identifier} />}
    </>
  );
}

function AssetForm({ customerId, asset, onDone }: { customerId: string; asset?: AssetView; onDone: () => void }) {
  const [busy, setBusy] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  async function save(body: { kind: string; title: string; identifier: string | null; attributes: Record<string, string> }) {
    setBusy(true);
    setError(null);
    try {
      if (asset) await api(`/api/v1/assets/${asset.id}`, { method: "PATCH", body: { ...body, identifier: body.identifier ?? "" } });
      else await api(`/api/v1/customers/${customerId}/assets`, { body });
      onDone();
    } catch (err) {
      const f = fieldErrors(err);
      setError(Object.values(f)[0] ?? "ذخیره نشد.");
      setBusy(false);
    }
  }
  async function remove() {
    if (!asset) return;
    setRemoving(true);
    try {
      await api(`/api/v1/assets/${asset.id}`, { method: "DELETE" });
      onDone();
    } catch (err) {
      const f = fieldErrors(err);
      setError(Object.values(f)[0] ?? "حذف نشد.");
      setRemoving(false);
    }
  }
  return (
    <div className="cust-form">
      <VehicleForm value={asset} busy={busy || removing} error={error} onSave={save} onCancel={onDone} />
      {asset && (
        <button type="button" className="danger" disabled={busy || removing} aria-busy={removing} onClick={remove}>
          {removing ? "در حال حذف…" : "حذف این وسیله"}
        </button>
      )}
    </div>
  );
}

function Trash({ onBack }: { onBack: () => void }) {
  const [rows, setRows] = useState<TrashRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => setRows(await api<TrashRow[]>("/api/v1/customers/trash")), []);
  useEffect(() => { load().catch(() => setRows([])); }, [load]);

  async function restore(id: string) {
    setError(null);
    try {
      await api(`/api/v1/customers/${id}/restore`, { method: "POST" });
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا");
    }
  }

  return (
    <section>
      <button type="button" className="link back" onClick={onBack}>→ مشتریان</button>
      <h2>سطل بازیافت مشتریان</h2>
      {rows === null && <div className="splash" aria-busy="true" />}
      {error && <p className="error" role="alert">{error}</p>}
      <ul className="list">
        {rows?.map((c) => (
          <li key={c.id} className="row-static">
            <span>
              {c.fullName ?? "بدون نام"} <span className="muted font-num" dir="ltr">{c.mobile}</span>
            </span>
            <button type="button" onClick={() => restore(c.id)}>بازگردانی</button>
          </li>
        ))}
        {rows?.length === 0 && <li className="empty muted">سطل بازیافت خالی است.</li>}
      </ul>
    </section>
  );
}
