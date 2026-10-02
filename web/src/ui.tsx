import type React from "react";
import type { ReactNode } from "react";

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
