import { useEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";

/**
 * Bottom sheet for frequent quick actions (assign, move stage, note, wait reason).
 * Enters from the bottom and leaves to the bottom; CSS transitions with @starting-style, so it is
 * interruptible and needs no JS timers. Esc and the scrim close it.
 */
export function BottomSheet({ open, title, onClose, children }: { open: boolean; title: string; onClose: () => void; children: ReactNode }) {
  const panel = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const previous = document.activeElement as HTMLElement | null;
    panel.current?.querySelector<HTMLElement>("button, input, select, textarea")?.focus();
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", onKey);
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = "";
      previous?.focus?.();
    };
  }, [open, onClose]);

  // Rendered at the page root, so a sheet opened from the sticky top bar still sits above everything.
  return createPortal(
    <div className="sheet-root" hidden={!open}>
      <div className="sheet-scrim" onClick={onClose} aria-hidden="true" />
      <div className="sheet" role="dialog" aria-modal="true" aria-label={title} ref={panel}>
        <div className="sheet-handle" aria-hidden="true" />
        <header className="sheet-header">
          <h3>{title}</h3>
          <button type="button" className="icon-button" onClick={onClose} aria-label="بستن">✕</button>
        </header>
        <div className="sheet-body">{children}</div>
      </div>
    </div>,
    document.body,
  );
}

/** A large, full-width option row for sheets (≥ 48px touch target). */
export function SheetOption({ label, hint, onClick, tone, disabled }: {
  label: string; hint?: string; onClick: () => void; tone?: "primary" | "danger"; disabled?: boolean;
}) {
  return (
    <button type="button" className={`sheet-option${tone ? ` ${tone}` : ""}`} onClick={onClick} disabled={disabled}>
      <span>{label}</span>
      {hint && <span className="muted small">{hint}</span>}
    </button>
  );
}

export type SelectItem = { value: string; label: string; hint?: string; group?: string };

/**
 * The one picker for dynamic lists (assignee, and any list that grows): a bottom sheet with a search box
 * once the list is longer than a few rows, optional group headings, the current choice marked, and an
 * optional "none" row. Fixed short choices with explanations keep using SheetOption directly.
 */
export function SelectSheet({ open, title, items, value, onSelect, onClose, noneLabel, busy, footer }: {
  open: boolean; title: string; items: SelectItem[]; value: string | null | undefined;
  onSelect: (value: string | null) => void; onClose: () => void; noneLabel?: string; busy?: boolean; footer?: ReactNode;
}) {
  const [q, setQ] = useState("");
  useEffect(() => { if (!open) setQ(""); }, [open]);
  const norm = (x: string) => x.replace(/[‌\s]/g, "").replace(/ي/g, "ی").replace(/ك/g, "ک").toLowerCase();
  const shown = q.trim() ? items.filter((i) => norm(`${i.label} ${i.hint ?? ""}`).includes(norm(q.trim()))) : items;
  const groups = [...new Set(shown.map((i) => i.group ?? ""))];
  return (
    <BottomSheet open={open} title={title} onClose={onClose}>
      {items.length > 6 && (
        <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="جستجو" aria-label={`جستجو در ${title}`} />
      )}
      {groups.map((g) => (
        <div key={g} className="select-group" role="group" aria-label={g || title}>
          {g && <p className="select-group-title muted small">{g}</p>}
          {shown.filter((i) => (i.group ?? "") === g).map((i) => (
            <SheetOption key={i.value} label={i.label} disabled={busy}
              hint={i.value === value ? ["انتخاب فعلی", i.hint].filter(Boolean).join(" · ") : i.hint}
              tone={i.value === value ? "primary" : undefined} onClick={() => onSelect(i.value)} />
          ))}
        </div>
      ))}
      {shown.length === 0 && <p className="muted small">موردی پیدا نشد.</p>}
      {noneLabel && value && <SheetOption label={noneLabel} disabled={busy} onClick={() => onSelect(null)} />}
      {footer}
    </BottomSheet>
  );
}

/** The form field that opens a SelectSheet: looks like an input, shows the chosen label. */
export function SelectField({ label, placeholder, invalid, onOpen }: { label: string | undefined; placeholder: string; invalid?: boolean; onOpen: () => void }) {
  return (
    <button type="button" className={`select-field${invalid ? " invalid" : ""}${label ? "" : " empty"}`} onClick={onOpen} aria-haspopup="dialog">
      <span>{label ?? placeholder}</span>
      <span aria-hidden="true" className="chev">▾</span>
    </button>
  );
}
