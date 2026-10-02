import { useId, useState, type ReactNode } from "react";
import type React from "react";

/** Iranian mobile: digits only (Persian/Arabic digits converted), never more than 11. */
export function MobileInput({ value, onChange, autoFocus }: { value: string; onChange: (v: string) => void; autoFocus?: boolean }) {
  return (
    <input
      type="tel" inputMode="numeric" autoComplete="tel" dir="ltr" className="font-num"
      placeholder="09xxxxxxxxx" maxLength={11} required autoFocus={autoFocus}
      value={value} onChange={(e) => onChange(toMobileDigits(e.target.value))}
    />
  );
}

export function toMobileDigits(raw: string) {
  return raw
    .replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0))
    .replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660))
    .replace(/\D/g, "")
    .slice(0, 11);
}

export function Field({ label, error, children }: { label: string; error?: string | null; children: ReactNode }) {
  return (
    <label className="field">
      <span className="label">{label}</span>
      {children}
      {error && <span className="error" role="alert">{error}</span>}
    </label>
  );
}

const faGrouped = new Intl.NumberFormat("fa-IR");

/** Quantities and amounts are always shown in groups of three: ۲۳٬۴۰۰. */
export function formatNumber(n: number | null | undefined): string {
  return n == null ? "—" : faGrouped.format(n);
}

/** Digits only, from any keyboard (Persian/Arabic digits converted). */
export function onlyDigits(raw: string, max = 15): string {
  return raw
    .replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0))
    .replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660))
    .replace(/\D/g, "")
    .replace(/^0+(?=\d)/, "")
    .slice(0, max);
}

/**
 * Numeric input for quantities and amounts: stores the raw digit string, shows it grouped by three.
 * Not for identifiers (mobile, plate, year, codes), which must not be grouped.
 */
export function NumberInput({ value, onChange, max = 15, suffix, ...rest }: {
  value: string; onChange: (digits: string) => void; max?: number; suffix?: string;
} & Omit<React.InputHTMLAttributes<HTMLInputElement>, "value" | "onChange">) {
  const shown = value ? faGrouped.format(Number(value)) : "";
  const input = (
    <input
      {...rest}
      inputMode="numeric" dir="ltr" className={`font-num${rest.className ? ` ${rest.className}` : ""}`}
      value={shown}
      onChange={(e) => onChange(onlyDigits(e.target.value, max))}
    />
  );
  return suffix ? <span className="with-suffix">{input}<span className="suffix">{suffix}</span></span> : input;
}

/** Persian/Arabic digits → Latin, everything else unchanged. Use on every value sent to the server. */
export function toLatinDigits(raw: string): string {
  return raw
    .replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0))
    .replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660));
}

/**
 * Searchable select: type to filter a long list (brands, models), pick with a tap, or keep what you typed.
 * The value is always the text in the box, so anything missing from the list can still be entered.
 */
export function Combobox({ value, onChange, options, placeholder, disabled, label }: {
  value: string; onChange: (v: string) => void; options: string[]; placeholder?: string; disabled?: boolean; label: string;
}) {
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const norm = (x: string) => x.replace(/[‌\s‌]/g, "").replace(/ي/g, "ی").replace(/ك/g, "ک").toLowerCase();
  const q = norm(toLatinDigits(value));
  const filtered = (q ? options.filter((o) => norm(toLatinDigits(o)).includes(q)) : options).slice(0, 60);
  const listId = useId();

  function pick(v: string) {
    onChange(v);
    setOpen(false);
  }

  return (
    <div className="combo">
      <input
        role="combobox" aria-expanded={open} aria-controls={listId} aria-autocomplete="list" aria-label={label}
        value={value} placeholder={placeholder} disabled={disabled} autoComplete="off"
        onFocus={() => setOpen(true)}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        onChange={(e) => { onChange(e.target.value); setOpen(true); setActive(0); }}
        onKeyDown={(e) => {
          if (e.key === "ArrowDown") { e.preventDefault(); setActive((a) => Math.min(a + 1, filtered.length - 1)); }
          else if (e.key === "ArrowUp") { e.preventDefault(); setActive((a) => Math.max(a - 1, 0)); }
          else if (e.key === "Enter" && open && filtered[active]) { e.preventDefault(); pick(filtered[active]); }
          else if (e.key === "Escape") setOpen(false);
        }}
      />
      {open && !disabled && filtered.length > 0 && (
        <ul className="combo-list" role="listbox" id={listId}>
          {filtered.map((o, i) => (
            <li key={o} role="option" aria-selected={i === active}
              className={i === active ? "active" : ""} onMouseDown={(e) => { e.preventDefault(); pick(o); }}>
              {o}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
