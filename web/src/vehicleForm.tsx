import { useMemo, useState, type FormEvent } from "react";
import { MotoPlateInput, PlateInput, emptyMotoPlate, emptyPlate } from "./plate";
import { Combobox, Field, toLatinDigits } from "./ui";
import {
  VEHICLE_CATALOG, VEHICLE_KINDS, formatMotoPlate, formatPlate, modelYears, parseMotoPlate, parsePlate, type VehicleKind,
} from "./vehicles";

export type VehicleValue = { kind: string | null; title: string; identifier: string | null; attributes: Record<string, string> | null };

const faYear = new Intl.NumberFormat("fa-IR", { useGrouping: false });

/** Splits a stored title back into brand and model using the catalog («پژو ۲۰۶» → پژو / ۲۰۶). */
function splitTitle(kind: VehicleKind | null, title: string): [string, string] {
  if (!kind) return ["", title];
  const brand = Object.keys(VEHICLE_CATALOG[kind]).filter((b) => b !== "سایر")
    .sort((a, b) => b.length - a.length).find((b) => title === b || title.startsWith(`${b} `));
  return brand ? [brand, title.slice(brand.length).trim()] : ["", title];
}

/**
 * Adding or editing a vehicle, in the same order as intake: type → brand → model → model year → plate.
 * Brand and model come from the list (typed text is kept for «سایر»). Other attributes (colour, VIN…) are kept.
 */
export function VehicleForm({ value, busy, error, onSave, onCancel }: {
  value?: VehicleValue; busy?: boolean; error?: string | null;
  onSave: (v: { kind: string; title: string; identifier: string | null; attributes: Record<string, string> }) => void; onCancel: () => void;
}) {
  const startKind = (VEHICLE_KINDS.some((k) => k.key === value?.kind) ? value!.kind : null) as VehicleKind | null;
  const [startBrand, startModel] = splitTitle(startKind, value?.title ?? "");
  const [kind, setKind] = useState<VehicleKind | null>(startKind);
  const [brand, setBrand] = useState(startBrand);
  const [model, setModel] = useState(startModel);
  const [calendar, setCalendar] = useState<"jalali" | "gregorian">(value?.attributes?.yearCalendar === "gregorian" ? "gregorian" : "jalali");
  const [year, setYear] = useState(value?.attributes?.year ?? "");
  const [yearText, setYearText] = useState(value?.attributes?.year ? faYear.format(Number(value.attributes.year)) : "");
  const [allYears, setAllYears] = useState(false);
  const [plate, setPlate] = useState(() => parsePlate(value?.identifier) ?? emptyPlate());
  const [motoPlate, setMotoPlate] = useState(() => parseMotoPlate(value?.identifier) ?? emptyMotoPlate());
  const [local, setLocal] = useState<string | null>(null);

  const catalog = kind ? VEHICLE_CATALOG[kind] : {};
  const brands = Object.keys(catalog).filter((b) => b !== "سایر");
  const models = (catalog[brand.trim()] ?? []).map((m) => m[0]);
  const entry = (catalog[brand.trim()] ?? []).find((m) => m[0] === model.trim());
  const years = useMemo(() => modelYears(entry, calendar, allYears), [entry, calendar, allYears]);
  const isMoto = kind === "motorcycle";
  const plateStarted = isMoto ? !!(motoPlate.top || motoPlate.bottom) : !!(plate.two || plate.letter || plate.three || plate.region);
  const plateId = isMoto ? formatMotoPlate(motoPlate) : formatPlate(plate);

  function pickKind(k: VehicleKind) {
    setKind(k);
    setBrand("");
    setModel("");
    setYear("");
    setYearText("");
  }

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!kind || !brand.trim() || !model.trim()) { setLocal("نوع، برند و مدل را انتخاب کنید."); return; }
    if (plateStarted && !plateId) { setLocal("پلاک کامل نیست."); return; }
    const attributes: Record<string, string> = { ...(value?.attributes ?? {}) };
    if (year) { attributes.year = year; attributes.yearCalendar = calendar; } else { delete attributes.year; delete attributes.yearCalendar; }
    onSave({ kind, title: `${brand.trim()} ${model.trim()}`, identifier: plateStarted ? plateId : value?.identifier && !parsePlate(value.identifier) && !parseMotoPlate(value.identifier) ? value.identifier : null, attributes });
  }

  return (
    <form className="card vehicle-form" onSubmit={submit} noValidate>
      <div className="field">
        <span className="label">نوع وسیله</span>
        <div className="kinds" role="radiogroup" aria-label="نوع وسیله">
          {VEHICLE_KINDS.map((k) => (
            <button type="button" key={k.key} role="radio" aria-checked={kind === k.key} className={`kind${kind === k.key ? " on" : ""}`} onClick={() => pickKind(k.key)}>
              <svg viewBox="0 0 24 24" width="26" height="26" aria-hidden="true"><path d={k.icon} /></svg>
              <span>{k.label}</span>
            </button>
          ))}
        </div>
      </div>
      {kind && (
        <div className="grid-2">
          <Field label="برند">
            <Combobox label="برند" value={brand} options={brands} placeholder="جستجوی برند" onChange={(v) => { setBrand(v); setModel(""); setYear(""); setYearText(""); }} />
          </Field>
          <Field label="مدل">
            <Combobox label="مدل" value={model} options={models} placeholder={brand.trim() ? "جستجوی مدل" : "اول برند"} disabled={!brand.trim()}
              onChange={(v) => { setModel(v); setYear(""); setYearText(""); }} />
          </Field>
        </div>
      )}
      {kind && model.trim() && (
        <div className="field">
          <div className="label-row">
            <span className="label">سال ساخت <span className="muted small">(اختیاری)</span></span>
            <span className="segmented" role="radiogroup" aria-label="تقویم سال ساخت">
              <button type="button" role="radio" aria-checked={calendar === "jalali"} className={calendar === "jalali" ? "on" : ""} onClick={() => { setCalendar("jalali"); setYear(""); setYearText(""); }}>شمسی</button>
              <button type="button" role="radio" aria-checked={calendar === "gregorian"} className={calendar === "gregorian" ? "on" : ""} onClick={() => { setCalendar("gregorian"); setYear(""); setYearText(""); }}>میلادی</button>
            </span>
          </div>
          <Combobox label="سال ساخت" value={yearText} placeholder="جستجوی سال، مثلاً ۱۳۹۸" options={years.map((y) => faYear.format(y))}
            onChange={(v) => {
              setYearText(v);
              const digits = toLatinDigits(v).replace(/\D/g, "");
              setYear(digits.length === 4 ? digits : "");
              if (digits.length === 4 && !years.includes(Number(digits))) setAllYears(true);
            }} />
        </div>
      )}
      {kind && (
        <div className="field">
          <span className="label">پلاک <span className="muted small">(اختیاری)</span></span>
          {isMoto ? <MotoPlateInput value={motoPlate} onChange={setMotoPlate} /> : <PlateInput value={plate} onChange={setPlate} />}
        </div>
      )}
      {(local || error) && <span className="error" role="alert">{local ?? error}</span>}
      <div className="actions">
        <button className="primary" disabled={busy} aria-busy={busy}>{busy ? "در حال ذخیره…" : value ? "ذخیره" : "افزودن"}</button>
        <button type="button" onClick={onCancel}>انصراف</button>
      </div>
    </form>
  );
}
