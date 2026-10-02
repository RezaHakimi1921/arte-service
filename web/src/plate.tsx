import { useRef } from "react";
import { PLATE_LETTERS, parseMotoPlate, parsePlate, type MotoPlateParts, type PlateParts } from "./vehicles";

const toDigits = (raw: string, max: number) =>
  raw
    .replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0))
    .replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660))
    .replace(/\D/g, "")
    .slice(0, max);

const fa = (digits: string) => digits.replace(/\d/g, (d) => "۰۱۲۳۴۵۶۷۸۹"[Number(d)]);

/**
 * Iranian private plate, laid out like the real one (left to right):
 * [IR strip] [2 digits] [letter] [3 digits] | [ایران + 2-digit region].
 * Focus jumps to the next box as each part is completed.
 */
export function PlateInput({ value, onChange, invalid }: { value: PlateParts; onChange: (p: PlateParts) => void; invalid?: boolean }) {
  const letterRef = useRef<HTMLSelectElement>(null);
  const threeRef = useRef<HTMLInputElement>(null);
  const regionRef = useRef<HTMLInputElement>(null);

  return (
    <div className={`plate${invalid ? " invalid" : ""}`} dir="ltr" role="group" aria-label="پلاک">
      <span className="plate-strip" aria-hidden="true">
        <span className="plate-flag" />
        <span>I.R.</span>
        <span>IRAN</span>
      </span>
      <input
        className="plate-two" inputMode="numeric" aria-label="دو رقم اول پلاک" placeholder="۱۲" maxLength={2}
        value={fa(value.two)}
        onChange={(e) => {
          const two = toDigits(e.target.value, 2);
          onChange({ ...value, two });
          if (two.length === 2) letterRef.current?.focus();
        }}
      />
      <select
        ref={letterRef} className={`plate-letter${value.letter ? "" : " empty"}`} aria-label="حرف پلاک" value={value.letter}
        onChange={(e) => {
          onChange({ ...value, letter: e.target.value });
          threeRef.current?.focus();
        }}
      >
        <option value="" disabled>حرف</option>
        {PLATE_LETTERS.map((l) => <option key={l} value={l}>{l}</option>)}
      </select>
      <input
        ref={threeRef} className="plate-three" inputMode="numeric" aria-label="سه رقم پلاک" placeholder="۳۴۵"
        value={fa(value.three)}
        onChange={(e) => {
          // Fast typing can land the region digits here before focus moves; carry the overflow over.
          const digits = toDigits(e.target.value, 5);
          const three = digits.slice(0, 3);
          const overflow = digits.slice(3);
          onChange({ ...value, three, region: overflow ? overflow : value.region });
          if (three.length === 3) regionRef.current?.focus();
        }}
      />
      <span className="plate-region">
        <span className="plate-iran">ایران</span>
        <input
          ref={regionRef} inputMode="numeric" aria-label="کد شهر پلاک" placeholder="۱۱" maxLength={2}
          value={fa(value.region)}
          onChange={(e) => onChange({ ...value, region: toDigits(e.target.value, 2) })}
        />
      </span>
    </div>
  );
}

/** Motorcycle plate: 3 digits on top, 5 below, IR strip on the left. */
export function MotoPlateInput({ value, onChange, invalid }: { value: MotoPlateParts; onChange: (p: MotoPlateParts) => void; invalid?: boolean }) {
  const bottomRef = useRef<HTMLInputElement>(null);
  return (
    <div className={`plate moto-plate${invalid ? " invalid" : ""}`} dir="ltr" role="group" aria-label="پلاک موتورسیکلت">
      <span className="plate-strip" aria-hidden="true">
        <span className="plate-flag" />
        <span>I.R.</span>
        <span>IRAN</span>
      </span>
      <span className="moto-rows">
        <input
          className="moto-top" inputMode="numeric" aria-label="سه رقم بالای پلاک" placeholder="۱۲۳"
          value={fa(value.top)}
          onChange={(e) => {
            const digits = toDigits(e.target.value, 8);
            const top = digits.slice(0, 3);
            const overflow = digits.slice(3);
            onChange({ top, bottom: overflow ? overflow : value.bottom });
            if (top.length === 3) bottomRef.current?.focus();
          }}
        />
        <input
          ref={bottomRef} className="moto-bottom" inputMode="numeric" aria-label="پنج رقم پایین پلاک" placeholder="۴۵۶۷۸"
          value={fa(value.bottom)}
          onChange={(e) => onChange({ ...value, bottom: toDigits(e.target.value, 5) })}
        />
      </span>
    </div>
  );
}

/** Read-only plate. Falls back to plain text for identifiers that are not a standard plate. */
export function PlateView({ identifier }: { identifier: string | null }) {
  const moto = parseMotoPlate(identifier);
  if (moto)
    return (
      <span className="plate small-plate moto-plate" dir="ltr" aria-label={`پلاک موتورسیکلت ${moto.top} ${moto.bottom}`}>
        <span className="plate-strip" aria-hidden="true"><span className="plate-flag" /></span>
        <span className="moto-rows"><span className="plate-text">{fa(moto.top)}</span><span className="plate-text">{fa(moto.bottom)}</span></span>
      </span>
    );
  const p = parsePlate(identifier);
  if (!p) return identifier ? <span className="font-num">{identifier}</span> : null;
  return (
    <span className="plate small-plate" dir="ltr" aria-label={`پلاک ${p.two} ${p.letter} ${p.three} ایران ${p.region}`}>
      <span className="plate-strip" aria-hidden="true"><span className="plate-flag" /></span>
      <span className="plate-text">{fa(p.two)}</span>
      <span className="plate-text">{p.letter}</span>
      <span className="plate-text">{fa(p.three)}</span>
      <span className="plate-region"><span className="plate-iran">ایران</span><span className="plate-text">{fa(p.region)}</span></span>
    </span>
  );
}

export const emptyPlate = (): PlateParts => ({ two: "", letter: "", three: "", region: "" });
export const emptyMotoPlate = (): MotoPlateParts => ({ top: "", bottom: "" });
