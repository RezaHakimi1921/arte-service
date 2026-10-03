import { useState } from "react";
import { formatNumber, toLatinDigits } from "./ui";
import { serviceCategoriesFor } from "./vehicles";

/**
 * Multi-select for requested services, from the categorised list only (no free text):
 * search across every category, browse by category, tick as many as needed, remove from the tags.
 */
export function ServicePicker({ value, onChange, kind }: { value: string[]; onChange: (v: string[]) => void; kind?: string | null }) {
  const categories = serviceCategoriesFor(kind);
  const all = categories.flatMap((c) => c.services);
  const [category, setCategory] = useState(categories[0].name);
  const [q, setQ] = useState("");
  const toggle = (s: string) => onChange(value.includes(s) ? value.filter((x) => x !== s) : [...value, s]);
  const norm = (x: string) => toLatinDigits(x).replace(/[‌\s]/g, "").replace(/ي/g, "ی").replace(/ك/g, "ک");
  const searching = q.trim().length > 0;
  const shown = searching
    ? all.filter((s) => norm(s).includes(norm(q.trim())))
    : (categories.find((c) => c.name === category) ?? categories[0]).services;

  return (
    <div className="service-picker">
      {value.length > 0 && (
        <div className="tags" aria-label="خدمات انتخاب‌شده">
          {value.map((s) => (
            <button type="button" key={s} className="tag" onClick={() => toggle(s)} aria-label={`حذف ${s}`}>
              {s} <span aria-hidden="true">×</span>
            </button>
          ))}
        </div>
      )}
      <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="جستجوی خدمت (مثلاً لنت، روغن)" aria-label="جستجوی خدمت" />
      {!searching && (
        <div className="category-tabs" role="tablist" aria-label="دسته خدمات">
          {categories.map((c) => {
            const picked = c.services.filter((s) => value.includes(s)).length;
            return (
              <button type="button" key={c.name} role="tab" aria-selected={category === c.name}
                className={`category-tab${category === c.name ? " on" : ""}`} onClick={() => setCategory(c.name)}>
                {c.name}{picked > 0 && <span className="count font-num">{formatNumber(picked)}</span>}
              </button>
            );
          })}
        </div>
      )}
      <div className="checks" role="group" aria-label="خدمات (چند انتخابی)">
        {shown.map((s) => (
          <label key={s} className={`check-chip${value.includes(s) ? " on" : ""}`}>
            <input type="checkbox" checked={value.includes(s)} onChange={() => toggle(s)} />
            <span className="box" aria-hidden="true">{value.includes(s) ? "✓" : ""}</span>
            {s}
          </label>
        ))}
        {shown.length === 0 && <p className="muted small">خدمتی با این نام در فهرست نیست.</p>}
      </div>
      <p className="hint">چند خدمت را می‌توانید با هم انتخاب کنید.</p>
    </div>
  );
}
