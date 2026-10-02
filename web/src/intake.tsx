import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { api, ApiError } from "./api";
import { PlateInput, PlateView, emptyPlate } from "./plate";
import { Field, MobileInput, NumberInput, formatNumber } from "./ui";
import {
  ACCOMPANYING, COLORS, FUELS, FUEL_LEVELS, GEARBOXES, SERVICES, VEHICLE_CATALOG, VEHICLE_KINDS,
  formatPlate, modelYears, type PlateParts, type VehicleKind,
} from "./vehicles";

const OTHER = "__other__";

type CustomerHit = { id: string; mobile: string; fullName: string | null };
type AssetOption = { id: string; title: string; identifier: string | null };
type CustomerFull = { id: string; fullName: string | null; assets: AssetOption[] };
type ParentSuggestion = { id: string; number: number; closedAt: string; request: string } | null;
type Assignable = { id: string; name: string; role: string };
type PlateHit = { asset: { id: string; title: string; identifier: string }; customer: { id: string; fullName: string | null; mobile: string } };

const faNumber = new Intl.NumberFormat("fa-IR");

const ICONS = {
  phone: "M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2z",
  vehicle: "M5 17h14M5 17a2 2 0 1 0 4 0M15 17a2 2 0 1 0 4 0M3 17V11l2-5h14l2 5v6M3 11h18",
  gauge: "M12 14l4-4M3.3 17a9 9 0 1 1 17.4 0",
  body: "M12 9v4M12 17h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z",
  list: "M9 11l3 3L22 4M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11",
  user: "M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z",
  note: "M4 6h16M4 12h16M4 18h10",
};

function Section({ icon, title, badge, children, tone }: { icon: keyof typeof ICONS; title: string; badge?: ReactNode; children: ReactNode; tone?: "warn" }) {
  return (
    <section className="intake-section">
      <header>
        <span className={`section-icon${tone ? ` ${tone}` : ""}`} aria-hidden="true">
          <svg viewBox="0 0 24 24" width="18" height="18"><path d={ICONS[icon]} /></svg>
        </span>
        <h3>{title}</h3>
        {badge}
      </header>
      {children}
    </section>
  );
}

export function NewCaseView({ canAssign, onCreated, onCancel, onOpenStaff }: {
  canAssign: boolean; onCreated: (id: string) => void; onCancel: () => void; onOpenStaff: () => void;
}) {
  // customer
  const [mobile, setMobile] = useState("");
  const [customer, setCustomer] = useState<CustomerFull | null>(null);
  const [lookedUp, setLookedUp] = useState(false);
  const [customerName, setCustomerName] = useState("");
  // vehicle
  const [assetId, setAssetId] = useState<string | "new">("new");
  const [kind, setKind] = useState<VehicleKind | null>(null);
  const [brandPick, setBrandPick] = useState("");
  const [brandText, setBrandText] = useState("");
  const [modelPick, setModelPick] = useState("");
  const [modelText, setModelText] = useState("");
  const [calendar, setCalendar] = useState<"jalali" | "gregorian">("jalali");
  const [year, setYear] = useState("");
  const [allYears, setAllYears] = useState(false);
  const [plate, setPlate] = useState<PlateParts>(emptyPlate());
  const [color, setColor] = useState("");
  const [vin, setVin] = useState("");
  const [fuelType, setFuelType] = useState("");
  const [gearbox, setGearbox] = useState("");
  const [odometer, setOdometer] = useState("");
  // condition
  const [fuel, setFuel] = useState<number | null>(null);
  const [body, setBody] = useState<"ok" | "damaged" | null>(null);
  const [bodyNotes, setBodyNotes] = useState("");
  const [items, setItems] = useState<string[]>([]);
  // job
  const [services, setServices] = useState<string[]>([]);
  const [notes, setNotes] = useState("");
  const [assigneeId, setAssigneeId] = useState("");
  const [staff, setStaff] = useState<Assignable[] | null>(null);
  const [parent, setParent] = useState<ParentSuggestion>(null);
  const [linkParent, setLinkParent] = useState(true);

  // staged intake: essentials first, the rest on demand
  const [full, setFull] = useState(false);
  // lookup by plate instead of mobile
  const [byPlate, setByPlate] = useState(false);
  const [lookupPlate, setLookupPlate] = useState<PlateParts>(emptyPlate());
  const [lookupMiss, setLookupMiss] = useState(false);
  const [wantAssetId, setWantAssetId] = useState<string | null>(null);
  const [plateOwner, setPlateOwner] = useState<PlateHit | null>(null);

  const [errors, setErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (canAssign) api<Assignable[]>("/api/v1/staff/assignable").then(setStaff).catch(() => setStaff([]));
  }, [canAssign]);

  useEffect(() => {
    setCustomer(null);
    setLookedUp(false);
    setAssetId("new");
    if (mobile.length !== 11) return;
    api<CustomerHit[]>(`/api/v1/customers?q=${mobile}`)
      .then(async (hits) => {
        const hit = hits.find((h) => h.mobile === mobile);
        if (hit) {
          const full = await api<CustomerFull>(`/api/v1/customers/${hit.id}`);
          setCustomer(full);
          const wanted = full.assets.find((a) => a.id === wantAssetId);
          if (wanted) setAssetId(wanted.id);
          else if (full.assets.length > 0) setAssetId(full.assets[full.assets.length - 1].id);
        }
      })
      .catch(() => {})
      .finally(() => setLookedUp(true));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mobile]);

  // Lookup by plate: find the vehicle and its owner, then continue as if the owner's mobile was typed.
  const lookupId = formatPlate(lookupPlate);
  useEffect(() => {
    setLookupMiss(false);
    if (!byPlate || !lookupId) return;
    api<PlateHit>(`/api/v1/assets/lookup?identifier=${encodeURIComponent(lookupId)}`)
      .then((hit) => {
        setWantAssetId(hit.asset.id);
        setMobile(hit.customer.mobile);
        setByPlate(false);
      })
      .catch(() => {
        setLookupMiss(true);
        setPlate(lookupPlate);
      });
  }, [byPlate, lookupId, lookupPlate]);

  useEffect(() => {
    setParent(null);
    if (assetId === "new") return;
    api<ParentSuggestion>(`/api/v1/cases/suggest-parent?assetId=${assetId}`).then(setParent).catch(() => {});
  }, [assetId]);

  const brands = kind ? Object.keys(VEHICLE_CATALOG[kind]).filter((b) => b !== "سایر") : [];
  const models = kind && brandPick && brandPick !== OTHER ? VEHICLE_CATALOG[kind][brandPick] ?? [] : [];
  const brand = brandPick === OTHER ? brandText : brandPick;
  const model = modelPick === OTHER || models.length === 0 ? modelText : modelPick;
  const modelEntry = models.find((m) => m[0] === modelPick);
  const years = useMemo(() => modelYears(modelEntry, calendar, allYears), [modelEntry, calendar, allYears]);

  function pickKind(k: VehicleKind) {
    setKind(k);
    setBrandPick("");
    setModelPick("");
    setYear("");
    setAllYears(false);
  }

  const plateParts = formatPlate(plate);

  // A new vehicle's plate may already exist: same customer → pick it; another customer → warn.
  useEffect(() => {
    setPlateOwner(null);
    if (assetId !== "new" || !plateParts) return;
    api<PlateHit>(`/api/v1/assets/lookup?identifier=${encodeURIComponent(plateParts)}`).then(setPlateOwner).catch(() => {});
  }, [assetId, plateParts]);
  const plateStarted = plate.two || plate.letter || plate.three || plate.region;
  const newVehicle = assetId === "new";

  const toggle = (list: string[], set: (v: string[]) => void, v: string) =>
    set(list.includes(v) ? list.filter((x) => x !== v) : [...list, v]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const local: Record<string, string> = {};
    if (mobile.length !== 11) local.mobile = "شماره همراه باید ۱۱ رقم باشد.";
    if (newVehicle && (kind || brand.trim() || model.trim() || plateStarted) && !(brand.trim() && model.trim())) local.vehicle = "نوع، برند و مدل را انتخاب کنید.";
    if (plateStarted && !plateParts) local.plate = "پلاک کامل نیست.";
    if (services.length === 0 && !notes.trim()) local.services = "حداقل یک سرویس انتخاب کنید یا توضیحی بنویسید.";
    if (plateOwner && plateOwner.customer.mobile !== mobile) local.plate = "این پلاک برای مشتری دیگری ثبت شده است.";
    if (Object.keys(local).length) {
      setErrors(local);
      return;
    }

    const attributes: Record<string, string> = {};
    if (brand.trim()) attributes.brand = brand.trim();
    if (model.trim()) attributes.model = model.trim();
    if (year) { attributes.year = year; attributes.yearCalendar = calendar; }
    if (color) attributes.color = color;
    if (vin.trim()) attributes.vin = vin.trim().toUpperCase();
    if (fuelType) attributes.fuelType = fuelType;
    if (gearbox) attributes.gearbox = gearbox;

    const intake: Record<string, string> = {};
    for (const i of items) intake[i] = "دارد";

    setBusy(true);
    setErrors({});
    try {
      const created = await api<{ id: string }>("/api/v1/cases", {
        body: {
          mobile,
          customerName: customer?.fullName ? undefined : customerName.trim() || undefined,
          assetId: newVehicle ? undefined : assetId,
          newAsset: newVehicle && brand.trim() && model.trim()
            ? { title: `${brand.trim()} ${model.trim()}`, identifier: plateParts, kind: kind ?? "car", attributes }
            : undefined,
          requestedServices: services,
          request: notes.trim(),
          odometerKm: odometer ? Number(odometer) : undefined,
          fuelLevel: fuel ?? undefined,
          bodyStatus: body ?? undefined,
          bodyNotes: body === "damaged" ? bodyNotes.trim() : undefined,
          intake: Object.keys(intake).length ? intake : undefined,
          assigneeId: assigneeId || undefined,
          parentCaseId: parent && linkParent ? parent.id : undefined,
        },
      });
      onCreated(created.id);
    } catch (err) {
      if (err instanceof ApiError) {
        const f = Object.fromEntries(Object.entries(err.fields).map(([k, v]) => [k, v[0]]));
        setErrors(Object.keys(f).length ? f : { form: err.message });
      } else setErrors({ form: "خطا در ارتباط با سرور" });
    } finally {
      setBusy(false);
    }
  }

  const sameOwner = plateOwner && plateOwner.customer.mobile === mobile;

  return (
    <form className="intake" onSubmit={submit} noValidate>
      <div className="intake-title">
        <h2>پذیرش جدید</h2>
        <button type="button" className="icon-button" onClick={onCancel} aria-label="بستن">✕</button>
      </div>

      {/* ── 1. essentials ── */}
      <Section icon="phone" title={byPlate ? "جستجو با پلاک" : "شماره همراه مشتری"}>
        {byPlate ? (
          <>
            <PlateInput value={lookupPlate} onChange={setLookupPlate} />
            {lookupMiss && <p className="hint">این پلاک قبلاً ثبت نشده؛ شماره همراه مشتری را وارد کنید.</p>}
            <button type="button" className="link" onClick={() => setByPlate(false)}>جستجو با شماره همراه</button>
          </>
        ) : (
          <>
            <Field label="" error={errors.mobile}>
              <MobileInput value={mobile} onChange={(v) => { setWantAssetId(null); setMobile(v); }} autoFocus />
            </Field>
            {!mobile && <button type="button" className="link" onClick={() => setByPlate(true)}>یا جستجو با پلاک</button>}
          </>
        )}
        {customer && (
          <p className="customer-found">
            <span className="badge good">مشتری قبلی</span>
            <strong>{customer.fullName ?? "بدون نام"}</strong>
            <span className="muted small">{customer.assets.length > 0 ? `${formatNumber(customer.assets.length)} وسیله ثبت‌شده` : ""}</span>
          </p>
        )}
        {!customer && lookedUp && (
          <div className="new-customer">
            <p><span className="badge warn">مشتری جدید</span> این شماره قبلاً ثبت نشده؛ نام مشتری را وارد کنید.</p>
            <input placeholder="نام و نام خانوادگی" value={customerName} onChange={(e) => setCustomerName(e.target.value)} maxLength={120} aria-label="نام مشتری" />
          </div>
        )}
      </Section>

      <Section icon="vehicle" title="وسیله نقلیه">
        {customer && customer.assets.length > 0 && (
          <div className="chips">
            {customer.assets.map((a) => (
              <button type="button" key={a.id} className={`chip-button${assetId === a.id ? " active" : ""}`} onClick={() => setAssetId(a.id)}>
                {a.title}{a.identifier ? <> · <PlateView identifier={a.identifier} /></> : ""}
              </button>
            ))}
            <button type="button" className={`chip-button${newVehicle ? " active" : ""}`} onClick={() => setAssetId("new")}>+ وسیله دیگر</button>
          </div>
        )}

        {newVehicle && (
          <>
            <div className="field">
              <span className="label">نوع وسیله</span>
              <div className="kinds" role="radiogroup" aria-label="نوع وسیله">
                {VEHICLE_KINDS.map((k) => (
                  <button type="button" key={k.key} role="radio" aria-checked={kind === k.key}
                    className={`kind${kind === k.key ? " on" : ""}`} onClick={() => pickKind(k.key)}>
                    <svg viewBox="0 0 24 24" width="26" height="26" aria-hidden="true"><path d={k.icon} /></svg>
                    <span>{k.label}</span>
                  </button>
                ))}
              </div>
              {errors.vehicle && <span className="error">{errors.vehicle}</span>}
            </div>

            {kind && (
              <div className="grid-2">
                <Field label="برند">
                  <select value={brandPick} onChange={(e) => { setBrandPick(e.target.value); setModelPick(""); setYear(""); setAllYears(false); }}>
                    <option value="">انتخاب برند</option>
                    {brands.map((b) => <option key={b} value={b}>{b}</option>)}
                    <option value={OTHER}>سایر (تایپ کنید)</option>
                  </select>
                  {brandPick === OTHER && (
                    <input value={brandText} onChange={(e) => setBrandText(e.target.value)} maxLength={60} placeholder="نام برند" aria-label="نام برند" autoFocus />
                  )}
                </Field>
                <Field label="مدل">
                  {models.length > 0 ? (
                    <select value={modelPick} onChange={(e) => { setModelPick(e.target.value); setYear(""); setAllYears(false); }}>
                      <option value="">انتخاب مدل</option>
                      {models.map((m) => <option key={m[0]} value={m[0]}>{m[0]}</option>)}
                      <option value={OTHER}>سایر (تایپ کنید)</option>
                    </select>
                  ) : null}
                  {(models.length === 0 || modelPick === OTHER) && (
                    <input value={modelText} onChange={(e) => setModelText(e.target.value)} maxLength={60}
                      placeholder={brand ? "نام مدل" : "اول برند"} disabled={!brand.trim()} aria-label="نام مدل" />
                  )}
                </Field>
              </div>
            )}

            {kind && model.trim() && (
              <div className="field">
                <div className="label-row">
                  <span className="label">سال ساخت</span>
                  <span className="segmented" role="radiogroup" aria-label="تقویم سال ساخت">
                    <button type="button" role="radio" aria-checked={calendar === "jalali"} className={calendar === "jalali" ? "on" : ""} onClick={() => { setCalendar("jalali"); setYear(""); }}>شمسی</button>
                    <button type="button" role="radio" aria-checked={calendar === "gregorian"} className={calendar === "gregorian" ? "on" : ""} onClick={() => { setCalendar("gregorian"); setYear(""); }}>میلادی</button>
                  </span>
                </div>
                <select value={year} aria-label="سال ساخت"
                  onChange={(e) => { if (e.target.value === OTHER) { setAllYears(true); setYear(""); } else setYear(e.target.value); }}>
                  <option value="">انتخاب سال</option>
                  {years.map((y) => <option key={y} value={y}>{new Intl.NumberFormat("fa-IR", { useGrouping: false }).format(y)}</option>)}
                  {!allYears && modelEntry?.[1] && <option value={OTHER}>سال دیگر…</option>}
                </select>
              </div>
            )}

            <div className="field">
              <span className="label">پلاک</span>
              <PlateInput value={plate} onChange={setPlate} invalid={!!errors.plate} />
              {errors.plate ? <span className="error">{errors.plate}</span>
                : <span className="hint">پلاک را می‌توانید بعداً هم وارد کنید، ولی قبل از شروع کار لازم است.</span>}
              {plateOwner && (sameOwner ? (
                <div className="notice good">
                  این وسیله قبلاً برای همین مشتری ثبت شده.
                  <button type="button" className="link" onClick={() => setAssetId(plateOwner.asset.id)}>انتخاب «{plateOwner.asset.title}»</button>
                </div>
              ) : (
                <div className="notice warn">
                  این پلاک برای «{plateOwner.customer.fullName ?? plateOwner.customer.mobile}» ثبت شده است.
                  <button type="button" className="link" onClick={() => { setWantAssetId(plateOwner.asset.id); setMobile(plateOwner.customer.mobile); }}>
                    پذیرش برای همان مشتری
                  </button>
                </div>
              ))}
            </div>

            {full && model.trim() && (
              <details className="extra">
                <summary>اطلاعات تکمیلی وسیله (اختیاری)</summary>
                <div className="grid-2">
                  <Field label="رنگ">
                    <select value={color} onChange={(e) => setColor(e.target.value)}>
                      <option value="">—</option>
                      {COLORS.map((c) => <option key={c}>{c}</option>)}
                    </select>
                  </Field>
                  <Field label="نوع سوخت">
                    <select value={fuelType} onChange={(e) => setFuelType(e.target.value)}>
                      <option value="">—</option>
                      {FUELS.map((f) => <option key={f}>{f}</option>)}
                    </select>
                  </Field>
                  <Field label="گیربکس">
                    <select value={gearbox} onChange={(e) => setGearbox(e.target.value)}>
                      <option value="">—</option>
                      {GEARBOXES.map((g) => <option key={g}>{g}</option>)}
                    </select>
                  </Field>
                </div>
                <Field label="شماره شاسی (VIN)">
                  <input dir="ltr" className="font-num vin" value={vin} maxLength={17} autoCapitalize="characters"
                    onChange={(e) => setVin(e.target.value.replace(/[^A-Za-z0-9]/g, "").slice(0, 17))} />
                </Field>
              </details>
            )}
          </>
        )}

        {parent && (
          <label className="check">
            <input type="checkbox" checked={linkParent} onChange={(e) => setLinkParent(e.target.checked)} />
            <span>برگشتی پرونده <span className="font-num">#{faNumber.format(parent.number)}</span></span>
          </label>
        )}
      </Section>

      <Section icon="list" title="مشکل یا سرویس درخواستی">
        <div className="chips">
          {SERVICES.map((s) => (
            <button type="button" key={s} aria-pressed={services.includes(s)} className={`chip-button${services.includes(s) ? " active" : ""}`}
              onClick={() => toggle(services, setServices, s)}>{s}</button>
          ))}
        </div>
        <textarea rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000}
          placeholder="توضیح کوتاه مشتری (اختیاری)" aria-label="توضیحات" />
        {errors.services && <span className="error">{errors.services}</span>}
        {errors.requestedServices && <span className="error">{errors.requestedServices}</span>}
        {errors.request && <span className="error">{errors.request}</span>}
      </Section>

      {/* ── 2. recommended, on demand ── */}
      {full && (
        <>
          <Section icon="gauge" title="وضعیت هنگام پذیرش" badge={fuel === null && <span className="badge warn">سوخت مشخص نشده</span>}>
            <Field label="کیلومتر کارکرد" error={errors.odometerKm}>
              <NumberInput value={odometer} onChange={setOdometer} max={7} suffix="کیلومتر" />
            </Field>
            <div className="field">
              <span className="label">میزان سوخت</span>
              <div className="fuel" role="radiogroup" aria-label="میزان سوخت">
                {FUEL_LEVELS.map((label, i) => (
                  <button type="button" key={label} role="radio" aria-checked={fuel === i}
                    className={fuel !== null && i <= fuel ? `filled level-${fuel}` : ""} onClick={() => setFuel(i)}>
                    <span className="fuel-bar" />
                    <span className="fuel-label">{label}</span>
                  </button>
                ))}
              </div>
            </div>
          </Section>

          <Section icon="body" title="وضعیت بدنه" tone="warn" badge={body === null && <span className="badge warn">بررسی نشده</span>}>
            <div className="actions">
              <button type="button" className={`toggle good${body === "ok" ? " on" : ""}`} onClick={() => setBody("ok")}>بدنه سالم است</button>
              <button type="button" className={`toggle bad${body === "damaged" ? " on" : ""}`} onClick={() => setBody("damaged")}>ثبت آسیب</button>
            </div>
            {body === "damaged" && (
              <Field label="شرح آسیب (محل، نوع)" error={errors.bodyNotes}>
                <textarea rows={2} value={bodyNotes} onChange={(e) => setBodyNotes(e.target.value)} maxLength={500} placeholder="مثلاً خط روی درب جلو راست" />
              </Field>
            )}
            <div className="field">
              <span className="label">همراه وسیله</span>
              <div className="chips">
                {ACCOMPANYING.map((i) => (
                  <button type="button" key={i} aria-pressed={items.includes(i)} className={`chip-button${items.includes(i) ? " active" : ""}`}
                    onClick={() => toggle(items, setItems, i)}>{i}</button>
                ))}
              </div>
            </div>
          </Section>

          {canAssign && (
            <Section icon="user" title="مسئول پرونده">
              <select value={assigneeId} onChange={(e) => setAssigneeId(e.target.value)} aria-label="مسئول پرونده">
                <option value="">بعداً تعیین می‌کنم</option>
                {staff?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
              {staff && staff.filter((s) => s.role !== "owner").length === 0 && (
                <p className="hint">هیچ همکاری ثبت نشده است. <button type="button" className="link" onClick={onOpenStaff}>رفتن به صفحه همکاران</button></p>
              )}
            </Section>
          )}
        </>
      )}

      {errors.form && <p className="error" role="alert">{errors.form}</p>}
      <div className="intake-submit">
        <button className="primary block big" disabled={busy}>{busy ? "در حال ثبت…" : full ? "ثبت پذیرش" : "ثبت سریع"}</button>
        {!full && (
          <button type="button" className="block secondary" onClick={() => setFull(true)}>
            تکمیل پذیرش: کیلومتر، سوخت، بدنه، مسئول…
          </button>
        )}
      </div>
    </form>
  );
}
