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
