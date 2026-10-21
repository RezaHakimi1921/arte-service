import { useEffect, useId, useState } from "react";
import { VEHICLE_SHAPES } from "./vehicleShapes";

export type VehicleTone = "going" | "waiting" | "done" | "cancelled";

/**
 * The customer's tracking page shows where the work stands as the customer's own kind of vehicle (car, SUV, van,
 * pickup, motorcycle) that fills with colour from the rear to the front (the page is right-to-left). It is the
 * only progress picture on that page; the step counter sits under it. Shapes live in vehicleShapes.ts.
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
  // Start empty and fill on the next frame so the colour sweeps in once when the page opens.
  const [shown, setShown] = useState(0);
  useEffect(() => {
    const frame = requestAnimationFrame(() => setShown(Math.min(1, Math.max(0, progress))));
    return () => cancelAnimationFrame(frame);
  }, [progress]);

  return (
    <div className={`vp ${tone}`}>
      <svg viewBox="0 0 340 150" role="img" aria-label={label}>
        <defs>
          <linearGradient id={`${id}-b`} x1="0" y1="0" x2="0" y2="1"><stop offset="0" className="vp-b1" /><stop offset="1" className="vp-b2" /></linearGradient>
          <linearGradient id={`${id}-g`} x1="0" y1="0" x2="1" y2="1"><stop offset="0" stopColor="#2a3347" /><stop offset="1" stopColor="#0f1626" /></linearGradient>
          <linearGradient id={`${id}-f`} x1="0" y1="0" x2="1" y2="0"><stop offset="0" className="vp-f1" /><stop offset="1" className="vp-f2" /></linearGradient>
          <clipPath id={`${id}-c`}><path d={v.body} /></clipPath>
        </defs>
        {v.tyres.map((d, i) => <path key={i} className="vp-tyre" d={d} />)}
        {v.pipe && <path className="vp-pipe" d={v.pipe} />}
        <path className="vp-body" d={v.body} fill={`url(#${id}-b)`} />
        {v.bed && <path className="vp-bed" d={v.bed} />}
        {v.glass && <path d={v.glass} fill={`url(#${id}-g)`} />}
        {v.roof && <path className="vp-roof" d={v.roof} />}
        <g clipPath={`url(#${id}-c)`}>
          <rect className="vp-fill" x="0" y="0" width="340" height="150" fill={`url(#${id}-f)`} style={{ transform: `scaleX(${shown})` }} />
        </g>
        {v.lines && <path className="vp-sheen" d={v.lines} />}
        {v.lamps.map((d, i) => <path key={`l${i}`} className="vp-lamp" d={d} />)}
        {v.tails.map((d, i) => <path key={`t${i}`} className="vp-tail" d={d} />)}
        {v.wheels?.map(([cx, cy, r]) => (
          <g key={cx}>
            <circle className="vp-tyre" cx={cx} cy={cy} r={r} />
            <circle className="vp-rim" cx={cx} cy={cy} r={r * 0.62} />
            <circle className="vp-hub" cx={cx} cy={cy} r={r * 0.18} />
          </g>
        ))}
      </svg>
    </div>
  );
}
