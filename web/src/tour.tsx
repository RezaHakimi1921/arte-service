import { useEffect, useLayoutEffect, useState } from "react";
import { formatNumber } from "./ui";

export type TourStep = {
  title: string;
  body: string;
  /** `[data-tour="…"]` key of the element to highlight; none = a centred message. */
  target?: string;
  /** Navigation to run when the step opens (switch tab, open the sample case…). */
  go?: () => void;
};

type Rect = { top: number; left: number; width: number; height: number };

/** Waits for the target to render after navigation, brings it into view and returns its box. */
function useTargetRect(target: string | undefined, stepKey: number): Rect | null {
  const [rect, setRect] = useState<Rect | null>(null);

  useLayoutEffect(() => {
    setRect(null);
    if (!target) return;
    const selector = `[data-tour="${target}"]`;
    let tries = 0;
    let timer = 0;
    // Looked up on every measure: the page may re-render the element after its data loads.
    const measure = () => {
      const el = document.querySelector(selector);
      if (!el) return;
      const r = el.getBoundingClientRect();
      setRect({ top: r.top, left: r.left, width: r.width, height: r.height });
    };
    const find = () => {
      const el = document.querySelector(selector);
      if (el) {
        // Near the top, so the explanation card at the bottom never covers it.
        window.scrollTo({ top: window.scrollY + el.getBoundingClientRect().top - 72 });
        requestAnimationFrame(measure);
        timer = window.setTimeout(measure, 300);
      } else if (tries++ < 40) {
        timer = window.setTimeout(find, 50);
      }
    };
    find();
    window.addEventListener("scroll", measure, { passive: true });
    window.addEventListener("resize", measure);
    return () => {
      window.clearTimeout(timer);
      window.removeEventListener("scroll", measure);
      window.removeEventListener("resize", measure);
    };
  }, [target, stepKey]);

  return rect;
}

/**
 * Coach-mark tour over the real screens: the highlighted element sits in a cut-out of the scrim,
 * the explanation in a card at the bottom. Esc or «رد شدن» ends it; the last step offers to remove the sample data.
 */
export function Tour({ steps, canRemoveSample, onFinish }: {
  steps: TourStep[]; canRemoveSample: boolean; onFinish: (removeSample: boolean) => void;
}) {
  const [index, setIndex] = useState(0);
  const step = steps[index];
  const rect = useTargetRect(step.target, index);
  const last = index === steps.length - 1;

  useEffect(() => { step.go?.(); }, [index]); // eslint-disable-line react-hooks/exhaustive-deps

  // Extra room at the bottom of the page so an element near the end can scroll above the card.
  useEffect(() => {
    document.body.classList.add("touring");
    return () => document.body.classList.remove("touring");
  }, []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onFinish(false); };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onFinish]);

  const pad = 6;
  return (
    <div className="tour-root" role="dialog" aria-modal="true" aria-label="آشنایی با آرته سرویس">
      {rect ? (
        <div className="tour-spot" aria-hidden="true"
          style={{ top: rect.top - pad, left: rect.left - pad, width: rect.width + pad * 2, height: rect.height + pad * 2 }} />
      ) : (
        <div className="tour-scrim" aria-hidden="true" />
      )}
      <div className="tour-card" key={index}>
        <p className="tour-count muted small">{formatNumber(index + 1)} از {formatNumber(steps.length)}</p>
        <h3>{step.title}</h3>
        <p>{step.body}</p>
        {last && !canRemoveSample ? (
          <div className="tour-actions">
            <button className="primary" onClick={() => onFinish(false)} autoFocus>شروع کار</button>
            <button onClick={() => setIndex(index - 1)}>قبلی</button>
          </div>
        ) : last ? (
          <div className="tour-actions">
            <button className="primary" onClick={() => onFinish(true)}>حذف داده‌های نمونه و شروع کار</button>
            <button onClick={() => onFinish(false)}>نمونه بماند، بعداً حذف می‌کنم</button>
            <button className="link" onClick={() => setIndex(index - 1)}>قبلی</button>
          </div>
        ) : (
          <div className="tour-actions row">
            <button className="primary" onClick={() => setIndex(index + 1)} autoFocus>بعدی</button>
            {index > 0 && <button onClick={() => setIndex(index - 1)}>قبلی</button>}
            <button className="link" onClick={() => onFinish(false)}>رد شدن</button>
          </div>
        )}
      </div>
    </div>
  );
}
