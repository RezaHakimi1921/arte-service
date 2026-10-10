import { useRef, type KeyboardEvent, type PointerEvent } from "react";
import { FUEL_LEVELS } from "./vehicles";

// Dial geometry: a half circle from E (left, 180°) to F (right, 0°), like a dashboard gauge.
const W = 260, H = 150, CX = 130, CY = 132, R = 104;
const STEPS = FUEL_LEVELS.length - 1; // 0 … 4
const angleOf = (level: number) => Math.PI - (level / STEPS) * Math.PI;
const point = (a: number, r = R) => [CX + r * Math.cos(a), CY - r * Math.sin(a)] as const;

function arc(from: number, to: number, r = R) {
  const [x1, y1] = point(angleOf(from), r);
  const [x2, y2] = point(angleOf(to), r);
  return `M ${x1} ${y1} A ${r} ${r} 0 0 1 ${x2} ${y2}`;
}

/**
 * Fuel at intake as a dashboard gauge: drag the needle (or tap the dial, or use the arrow keys) and it snaps to
 * empty, ¼, ½, ¾ or full. Colours run red (empty) to green (full), like the fuel bar elsewhere.
 */
export function FuelGauge({ value, onChange }: { value: number | null; onChange: (level: number) => void }) {
  const svg = useRef<SVGSVGElement>(null);
  const dragging = useRef(false);

  function levelAt(e: PointerEvent) {
    const box = svg.current!.getBoundingClientRect();
    const x = ((e.clientX - box.left) / box.width) * W - CX;
    const y = CY - ((e.clientY - box.top) / box.height) * H;
    const a = Math.atan2(Math.max(y, 0), x);                        // 0 (F) … π (E)
    return Math.round(((Math.PI - a) / Math.PI) * STEPS);
  }
  const move = (e: PointerEvent) => { if (dragging.current) onChange(levelAt(e)); };
  function down(e: PointerEvent) {
    dragging.current = true;
    (e.currentTarget as Element).setPointerCapture(e.pointerId);
    onChange(levelAt(e));
  }
  function key(e: KeyboardEvent) {
    const v = value ?? 0;
    if (e.key === "ArrowRight" || e.key === "ArrowUp") { e.preventDefault(); onChange(Math.min(STEPS, v + 1)); }
    if (e.key === "ArrowLeft" || e.key === "ArrowDown") { e.preventDefault(); onChange(Math.max(0, v - 1)); }
  }

  const needle = value === null ? null : point(angleOf(value), R - 22);
  return (
    <div className={`fuel-gauge${value === null ? " unset" : ` level-${value}`}`}>
      <svg ref={svg} viewBox={`0 0 ${W} ${H}`} role="slider" tabIndex={0} aria-label="میزان سوخت"
        aria-valuemin={0} aria-valuemax={STEPS} aria-valuenow={value ?? undefined}
        aria-valuetext={value === null ? "مشخص نشده" : FUEL_LEVELS[value]}
        onPointerDown={down} onPointerMove={move} onPointerUp={(e) => { if (dragging.current) onChange(levelAt(e)); dragging.current = false; }} onPointerCancel={() => { dragging.current = false; }}
        onKeyDown={key}>
        <path d={arc(0, STEPS)} className="fg-track" />
        {[0, 1, 2, 3].map((i) => <path key={i} d={arc(i + 0.06, i + 0.94)} className={`fg-seg f${i}`} />)}
        {FUEL_LEVELS.map((_, i) => {
          const [x1, y1] = point(angleOf(i), R + 10);
          const [x2, y2] = point(angleOf(i), R - 12);
          return <line key={i} x1={x1} y1={y1} x2={x2} y2={y2} className={`fg-tick${value === i ? " on" : ""}`} />;
        })}
        <text x={point(angleOf(0), R + 2)[0] + 4} y={CY + 16} className="fg-end">E</text>
        <text x={point(angleOf(STEPS), R + 2)[0] - 14} y={CY + 16} className="fg-end">F</text>
        <text x={CX} y={point(angleOf(2), R + 18)[1] + 4} className="fg-half">½</text>
        {needle && <line x1={CX} y1={CY} x2={needle[0]} y2={needle[1]} className="fg-needle" />}
        <circle cx={CX} cy={CY} r={9} className="fg-hub" />
      </svg>
      <span className="fg-value" aria-hidden="true">{value === null ? "سوزن را بکشید" : FUEL_LEVELS[value]}</span>
    </div>
  );
}
