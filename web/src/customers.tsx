import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, ApiError } from "./api";
import { PlateView } from "./plate";
import { Field, MobileInput } from "./ui";

type CustomerRow = { id: string; mobile: string; fullName: string | null; assetCount: number };
type AssetView = { id: string; title: string; identifier: string | null };
type CustomerView = { id: string; mobile: string; fullName: string | null; notes: string | null; assets: AssetView[] };
type TrashRow = { id: string; mobile: string; fullName: string | null; deletedAt: string };

const fieldErrors = (err: unknown): Record<string, string> =>
  err instanceof ApiError
    ? Object.keys(err.fields).length
      ? Object.fromEntries(Object.entries(err.fields).map(([k, v]) => [k, v[0]]))
      : { form: err.message }
    : { form: "خطا در ارتباط با سرور" };

export function Customers({ canEdit }: { canEdit: boolean }) {
  const [q, setQ] = useState("");
  const [rows, setRows] = useState<CustomerRow[]>([]);
  const [openId, setOpenId] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [showTrash, setShowTrash] = useState(false);
  const [undo, setUndo] = useState<{ id: string; name: string } | null>(null);

  const load = useCallback(async (term: string) => {
    setRows(await api<CustomerRow[]>(`/api/v1/customers?q=${encodeURIComponent(term)}`));
  }, []);

  useEffect(() => {
    const t = setTimeout(() => load(q).catch(() => {}), 250);
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

  if (showTrash) return <Trash onBack={() => { setShowTrash(false); load(q); }} />;
  if (openId)
    return (
      <CustomerDetail
        id={openId}
        canEdit={canEdit}
        onBack={() => { setOpenId(null); load(q); }}
        onDeleted={(name) => { setUndo({ id: openId, name }); setOpenId(null); load(q); }}
      />
    );

  return (
    <section>
      <div className="toolbar">
        <input type="search" placeholder="جستجو با نام یا شماره" value={q} onChange={(e) => setQ(e.target.value)} aria-label="جستجوی مشتری" />
        {canEdit && <button className="primary" onClick={() => setAdding(true)}>+ مشتری</button>}
      </div>
      {undo && (
        <div className="toast" role="status">
          <span>«{undo.name}» حذف شد.</span>
          <button className="link" onClick={() => restore(undo.id)}>بازگردانی</button>
        </div>
      )}
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
      {canEdit && (
        <button className="link trash-link" onClick={() => setShowTrash(true)}>سطل بازیافت مشتریان</button>
      )}
    </section>
  );
}

function AddCustomer({ onDone }: { onDone: (id: string | null) => void }) {
  const [mobile, setMobile] = useState("");
  const [fullName, setFullName] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});

  async function submit(e: FormEvent) {
    e.preventDefault();
    try {
      const c = await api<{ id: string }>("/api/v1/customers", { body: { mobile, fullName } });
      onDone(c.id);
    } catch (err) {
      setErrors(fieldErrors(err));
    }
  }

  return (
    <form className="card" onSubmit={submit} noValidate>
      <Field label="شماره موبایل" error={errors.mobile ?? errors.form}>
        <MobileInput value={mobile} onChange={setMobile} autoFocus />
      </Field>
      <Field label="نام و نام خانوادگی" error={errors.fullName}>
        <input value={fullName} onChange={(e) => setFullName(e.target.value)} maxLength={120} />
      </Field>
      <div className="actions">
        <button className="primary">ثبت</button>
        <button type="button" onClick={() => onDone(null)}>انصراف</button>
      </div>
    </form>
  );
}

function CustomerDetail({ id, canEdit, onBack, onDeleted }: {
  id: string; canEdit: boolean; onBack: () => void; onDeleted: (name: string) => void;
}) {
  const [c, setC] = useState<CustomerView | null>(null);
  const [editing, setEditing] = useState(false);
  const [addingAsset, setAddingAsset] = useState(false);
  const [editAssetId, setEditAssetId] = useState<string | null>(null);

  const load = useCallback(async () => setC(await api<CustomerView>(`/api/v1/customers/${id}`)), [id]);
  useEffect(() => { load().catch(() => {}); }, [load]);

  async function remove() {
    await api(`/api/v1/customers/${id}`, { method: "DELETE" });
    onDeleted(c?.fullName ?? c?.mobile ?? "");
  }

  async function removeAsset(assetId: string) {
    await api(`/api/v1/assets/${assetId}`, { method: "DELETE" }).catch(() => {});
    await load();
  }

  if (!c) return <div className="splash" aria-busy="true" />;
  return (
    <section>
      <button className="link back" onClick={onBack}>→ مشتریان</button>

      {editing ? (
        <CustomerForm customer={c} onDone={(updated) => { setEditing(false); if (updated) setC({ ...c, ...updated }); }} />
      ) : (
        <div className="card">
          <h2>{c.fullName ?? "بدون نام"}</h2>
          <p className="font-num" dir="ltr">{c.mobile}</p>
          {c.notes && <p className="muted">{c.notes}</p>}
          {canEdit && (
            <div className="actions">
              <button onClick={() => setEditing(true)}>ویرایش</button>
              <button className="danger" onClick={remove}>حذف</button>
            </div>
          )}
        </div>
      )}

      <h3>وسایل نقلیه</h3>
      <ul className="list">
        {c.assets.map((a) =>
          editAssetId === a.id ? (
            <li key={a.id}>
              <AssetForm customerId={c.id} asset={a} onDone={() => { setEditAssetId(null); load(); }} />
            </li>
          ) : (
            <li key={a.id} className="row-static">
              <span>
                {a.title}
                {a.identifier && <> <PlateView identifier={a.identifier} /></>}
              </span>
              {canEdit && (
                <span className="row-actions">
                  <button onClick={() => setEditAssetId(a.id)}>ویرایش</button>
                  <button className="danger" onClick={() => removeAsset(a.id)}>حذف</button>
                </span>
              )}
            </li>
          ),
        )}
        {c.assets.length === 0 && <li className="empty muted">هنوز وسیله‌ای ثبت نشده.</li>}
      </ul>
      {canEdit &&
        (addingAsset ? (
          <AssetForm customerId={c.id} onDone={() => { setAddingAsset(false); load(); }} />
        ) : (
          <button className="primary block" onClick={() => setAddingAsset(true)}>+ افزودن وسیله نقلیه</button>
        ))}
    </section>
  );
}

function CustomerForm({ customer, onDone }: { customer: CustomerView; onDone: (updated: Partial<CustomerView> | null) => void }) {
  const [mobile, setMobile] = useState(customer.mobile);
  const [fullName, setFullName] = useState(customer.fullName ?? "");
  const [notes, setNotes] = useState(customer.notes ?? "");
  const [errors, setErrors] = useState<Record<string, string>>({});

  async function submit(e: FormEvent) {
    e.preventDefault();
    try {
      onDone(await api<CustomerView>(`/api/v1/customers/${customer.id}`, { method: "PATCH", body: { mobile, fullName, notes } }));
    } catch (err) {
      setErrors(fieldErrors(err));
    }
  }

  return (
    <form className="card" onSubmit={submit} noValidate>
      <h2>ویرایش مشتری</h2>
      <Field label="شماره موبایل" error={errors.mobile ?? errors.form}>
        <MobileInput value={mobile} onChange={setMobile} />
      </Field>
      <Field label="نام و نام خانوادگی" error={errors.fullName}>
        <input value={fullName} onChange={(e) => setFullName(e.target.value)} maxLength={120} />
      </Field>
      <Field label="یادداشت" error={errors.notes}>
        <textarea value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} rows={3} />
      </Field>
      <div className="actions">
        <button className="primary">ذخیره</button>
        <button type="button" onClick={() => onDone(null)}>انصراف</button>
      </div>
    </form>
  );
}

function AssetForm({ customerId, asset, onDone }: { customerId: string; asset?: AssetView; onDone: () => void }) {
  const [title, setTitle] = useState(asset?.title ?? "");
  const [identifier, setIdentifier] = useState(asset?.identifier ?? "");
  const [errors, setErrors] = useState<Record<string, string>>({});

  async function submit(e: FormEvent) {
    e.preventDefault();
    try {
      const body = { title, identifier: identifier || null };
      if (asset) await api(`/api/v1/assets/${asset.id}`, { method: "PATCH", body });
      else await api(`/api/v1/customers/${customerId}/assets`, { body });
      onDone();
    } catch (err) {
      setErrors(fieldErrors(err));
    }
  }

  return (
    <form className="card" onSubmit={submit} noValidate>
      <Field label="برند و مدل" error={errors.title ?? errors.form}>
        <input placeholder="مثلاً پژو ۲۰۶" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={120} required autoFocus />
      </Field>
      <Field label="پلاک یا شناسه" error={errors.identifier}>
        <input value={identifier} onChange={(e) => setIdentifier(e.target.value)} maxLength={60} />
      </Field>
      <div className="actions">
        <button className="primary">{asset ? "ذخیره" : "افزودن"}</button>
        <button type="button" onClick={onDone}>انصراف</button>
      </div>
    </form>
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
      <button className="link back" onClick={onBack}>→ مشتریان</button>
      <h2>سطل بازیافت مشتریان</h2>
      {error && <p className="error" role="alert">{error}</p>}
      <ul className="list">
        {rows?.map((c) => (
          <li key={c.id} className="row-static">
            <span>
              {c.fullName ?? "بدون نام"} <span className="muted font-num" dir="ltr">{c.mobile}</span>
            </span>
            <button onClick={() => restore(c.id)}>بازگردانی</button>
          </li>
        ))}
        {rows?.length === 0 && <li className="empty muted">سطل بازیافت خالی است.</li>}
      </ul>
    </section>
  );
}
