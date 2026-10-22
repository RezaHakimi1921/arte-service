import { useEffect, useId, useState } from "react";
import { VEHICLE_SHAPES, type Layer } from "./vehicleShapes";

export type VehicleTone = "going" | "waiting" | "done" | "cancelled";

/**
 * The customer's tracking page shows where the work stands as the customer's own kind of vehicle (car, SUV, van,
 * pickup, motorcycle), seen from above, that fills with colour from the rear to the front (the page is
 * right-to-left). It is the only progress picture on that page; the step counter sits under it.
 * Shapes live in vehicleShapes.ts; the colour comes from the state (see .vp in styles.css).
 */
export function VehicleProgress({ kind, progress, tone, label }: {
  kind: string | undefined;
  /** 0…1 of the way through the stages. */
  progress: number;
  tone: VehicleTone;
  /** Spoken description of the current state (the picture itself is decoration). */
  label: string;
}) {
  const id = useId();
  const v = VEHICLE_SHAPES[kind ?? ""] ?? VEHICLE_SHAPES.car;
  const [from, to] = v.span;
  const z = v.zoom ?? 1;
  const fit = z === 1 ? undefined : `translate(${((from + to) / 2) * (1 - z)} ${130 * (1 - z)}) scale(${z})`;
  // Start empty and fill on the next frame so the colour sweeps in once when the page opens.
  const [shown, setShown] = useState(0);
  useEffect(() => {
    const frame = requestAnimationFrame(() => setShown(Math.min(1, Math.max(0, progress))));
    return () => cancelAnimationFrame(frame);
  }, [progress]);

  const paint = (t?: string) => (t === "@body" || t === "@glass" || t === "@paint" ? `url(#${id}-${t.slice(1)})` : t);
  const layer = (l: Layer, i: number) => (
    <path key={i} d={l.d} fill={paint(l.fill) ?? "none"} fillOpacity={l.fillOp} stroke={l.stroke} strokeWidth={l.sw}
      strokeOpacity={l.op} strokeLinecap={l.cap ? "round" : undefined} />
  );

  return (
    <div className={`vp ${tone}`}>
      <svg viewBox="0 0 520 260" role="img" aria-label={label}>
        <defs>
          <linearGradient id={`${id}-body`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stopColor="#FFFFFF" /><stop offset="0.48" stopColor="#F8FAFC" /><stop offset="1" stopColor="#DDE3EB" />
          </linearGradient>
          <linearGradient id={`${id}-glass`} x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="#30394C" /><stop offset="1" stopColor="#111827" />
          </linearGradient>
          <linearGradient id={`${id}-paint`} x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" className="vp-p1" /><stop offset="1" className="vp-p2" />
          </linearGradient>
          <filter id={`${id}-shadow`} x="-30%" y="-40%" width="160%" height="200%">
            <feDropShadow dx="0" dy="13" stdDeviation="12" floodColor="#34466F" floodOpacity="0.17" />
          </filter>
          <clipPath id={`${id}-clip`}><path d={v.body} /></clipPath>
        </defs>
        <g transform={fit}>
        {v.under.map(layer)}
        <g filter={`url(#${id}-shadow)`}>
          <path d={v.body} fill={`url(#${id}-body)`} stroke="#C8D1DF" strokeWidth="1.6" />
          <g clipPath={`url(#${id}-clip)`}>
            <rect className="vp-fill" x={from} y="0" width={to - from} height="260" fill={`url(#${id}-paint)`}
              style={{ transform: `scaleX(${shown})`, transformOrigin: `${to}px 130px` }} />
            {v.paint.map(layer)}
          </g>
          {v.over.map(layer)}
        </g>
        </g>
      </svg>
    </div>
  );
}
