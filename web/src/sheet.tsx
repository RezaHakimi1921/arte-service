import { useEffect, useRef, type ReactNode } from "react";

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

  return (
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
    </div>
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
