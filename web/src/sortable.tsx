import { useRef, useState, type KeyboardEvent, type PointerEvent, type ReactNode } from "react";

export type HandleProps = {
  className: string; role: "button"; tabIndex: number; "aria-label": string; style: { touchAction: "none" };
  onPointerDown: (e: PointerEvent<HTMLElement>) => void; onPointerMove: (e: PointerEvent<HTMLElement>) => void;
  onPointerUp: (e: PointerEvent<HTMLElement>) => void; onPointerCancel: (e: PointerEvent<HTMLElement>) => void;
  onKeyDown: (e: KeyboardEvent<HTMLElement>) => void;
};

/** The six-dot grip drawn inside a drag handle. */
export const GripIcon = () => (
  <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true" fill="currentColor">
    <circle cx="9" cy="6" r="1.6" /><circle cx="15" cy="6" r="1.6" /><circle cx="9" cy="12" r="1.6" /><circle cx="15" cy="12" r="1.6" /><circle cx="9" cy="18" r="1.6" /><circle cx="15" cy="18" r="1.6" />
  </svg>
);

/**
 * A list the user reorders by pressing a row's grip and dragging it up or down (touch and mouse), or with the arrow
 * keys on the focused grip. The list reorders live while dragging; `onReorder` gets the final ids once, on release.
 */
export function SortableList<T extends { id: string }>({ items, onReorder, disabled, label, render }: {
  items: T[]; onReorder: (ids: string[]) => void; disabled?: boolean; label: (item: T) => string;
  render: (item: T, handle: HandleProps, dragging: boolean) => ReactNode;
}) {
  const [order, setOrder] = useState<string[] | null>(null);
  const [dragId, setDragId] = useState<string | null>(null);
  const rows = useRef(new Map<string, HTMLLIElement>());
  const live = order ?? items.map((i) => i.id);
  const byId = new Map(items.map((i) => [i.id, i]));

  function reorderTo(id: string, y: number, from: string[]) {
    const others = from.filter((x) => x !== id);
    let at = others.length;
    for (let i = 0; i < others.length; i++) {
      const el = rows.current.get(others[i]);
      if (!el) continue;
      const r = el.getBoundingClientRect();
      if (y < r.top + r.height / 2) { at = i; break; }
    }
    const next = [...others.slice(0, at), id, ...others.slice(at)];
    return next.join() === from.join() ? from : next;
  }

  const handleFor = (item: T): HandleProps => ({
    className: "drag-handle", role: "button", tabIndex: disabled ? -1 : 0, "aria-label": `جابه‌جایی ${label(item)}؛ بکشید یا با کلید بالا و پایین`, style: { touchAction: "none" },
    onPointerDown: (e) => {
      if (disabled) return;
      e.currentTarget.setPointerCapture(e.pointerId);
      setDragId(item.id); setOrder(items.map((i) => i.id));
    },
    onPointerMove: (e) => {
      if (dragId !== item.id) return;
      // Near the screen edge the page scrolls, so a long list can be dragged all the way.
      if (e.clientY < 80) window.scrollBy(0, -12); else if (e.clientY > window.innerHeight - 80) window.scrollBy(0, 12);
      setOrder((cur) => reorderTo(item.id, e.clientY, cur ?? live));
    },
    onPointerUp: () => {
      if (dragId !== item.id) return;
      const done = order ?? live;
      setDragId(null); setOrder(null);
      if (done.join() !== items.map((i) => i.id).join()) onReorder(done);
    },
    onPointerCancel: () => { setDragId(null); setOrder(null); },
    onKeyDown: (e) => {
      if (disabled || (e.key !== "ArrowUp" && e.key !== "ArrowDown")) return;
      e.preventDefault();
      const ids = items.map((i) => i.id);
      const i = ids.indexOf(item.id), j = i + (e.key === "ArrowUp" ? -1 : 1);
      if (j < 0 || j >= ids.length) return;
      [ids[i], ids[j]] = [ids[j], ids[i]];
      onReorder(ids);
    },
  });

  return (
    <ul className="opt-list sortable">
      {live.map((id) => {
        const item = byId.get(id);
        if (!item) return null;
        return (
          <li key={id} ref={(el) => { if (el) rows.current.set(id, el); else rows.current.delete(id); }} className={`opt-row${dragId === id ? " dragging" : ""}`}>
            {render(item, handleFor(item), dragId === id)}
          </li>
        );
      })}
    </ul>
  );
}
