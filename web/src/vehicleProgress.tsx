import { useEffect, useId, useState } from "react";

/**
 * The customer's tracking page shows where the work stands as a vehicle that fills up with colour, from the
 * rear to the front (the page is right-to-left). It is the only progress picture on that page; the stage name
 * and the "step n of m" line sit next to it. Shapes are drawn here (side view, facing left, viewBox 340×150).
 */
const CAR_BODY =
  "M16 100 C16 92 20 86 30 83 L78 72 C96 50 122 38 156 36 L222 36 C250 38 268 56 280 72 L312 80 C322 83 326 90 326 100 L326 108 C326 112 322 114 318 114 L284 114 A24 24 0 0 0 236 114 L110 114 A24 24 0 0 0 62 114 L24 114 C19 114 16 112 16 108 Z";
const CAR_GLASS = "M100 70 C112 54 130 46 154 45 L186 45 L186 70 Z M196 45 L222 45 C238 48 252 60 260 70 L196 70 Z";
const CAR_WHEELS: [number, number, number][] = [[86, 114, 18], [260, 114, 18]];

const MOTO_BODY =
  "M76 110 L104 50 L116 55 L88 116 Z " +
  "M96 44 L126 38 L130 49 L104 56 Z " +
  "M116 60 C130 44 168 40 190 48 L214 52 C240 52 262 54 280 62 C288 66 292 72 290 78 L212 82 L152 84 C130 84 118 76 116 60 Z " +
  "M150 84 L224 82 L236 100 L220 114 L160 114 L146 100 Z " +
  "M210 102 L264 102 L266 112 L214 114 Z";
const MOTO_WHEELS: [number, number, number][] = [[80, 112, 26], [264, 112, 26]];

export type VehicleTone = "going" | "waiting" | "done" | "cancelled";

export function VehicleProgress({ kind, progress, tone, label }: {
  kind: string | undefined;
  /** 0…1 of the way through the stages. */
  progress: number;
  tone: VehicleTone;
  /** Spoken description of the current state (the picture itself is decoration). */
  label: string;
}) {
  const id = useId();
  const moto = kind === "motorcycle";
  const body = moto ? MOTO_BODY : CAR_BODY;
  const wheels = moto ? MOTO_WHEELS : CAR_WHEELS;
  // Start empty and fill on the next frame so the colour sweeps in once when the page opens.
  const [shown, setShown] = useState(0);
  useEffect(() => {
    const frame = requestAnimationFrame(() => setShown(Math.min(1, Math.max(0, progress))));
    return () => cancelAnimationFrame(frame);
  }, [progress]);

  return (
    <div className={`vp ${tone}`}>
      <svg viewBox="0 0 340 150" role="img" aria-label={label}>
        <defs><clipPath id={`${id}-clip`}><path d={body} /></clipPath></defs>
        <path className="vp-body" d={body} />
        <g clipPath={`url(#${id}-clip)`}>
          <rect className="vp-fill" x="0" y="0" width="340" height="150" style={{ transform: `scaleX(${shown})` }} />
        </g>
        {!moto && <path className="vp-glass" d={CAR_GLASS} />}
        {wheels.map(([cx, cy, r]) => (
          <g key={cx}>
            <circle className="vp-tyre" cx={cx} cy={cy} r={r} />
            <circle className="vp-hub" cx={cx} cy={cy} r={r * 0.42} />
          </g>
        ))}
      </svg>
    </div>
  );
}
