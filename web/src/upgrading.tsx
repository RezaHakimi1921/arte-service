import { useCallback, useEffect, useRef, useState } from "react";
import { serverIsUp } from "./api";
import { AuthLayout } from "./login";

/** One gear as a single outline: `teeth` trapezoid teeth, a root circle `depth` below the tip, and a hub hole. */
function gearPath(cx: number, cy: number, outer: number, teeth: number, depth: number, phaseDeg: number): string {
  const root = outer - depth;
  const step = (Math.PI * 2) / teeth;
  const phase = (phaseDeg * Math.PI) / 180;
  const pt = (angle: number, r: number) => `${(cx + r * Math.cos(angle)).toFixed(2)} ${(cy + r * Math.sin(angle)).toFixed(2)}`;
  const parts: string[] = [];
  for (let i = 0; i < teeth; i++) {
    const a = phase + i * step;
    parts.push(pt(a - step * 0.3, root), pt(a - step * 0.14, outer), pt(a + step * 0.14, outer), pt(a + step * 0.3, root));
  }
  const hub = root * 0.36;
  return `M${parts.join("L")}ZM${cx + hub} ${cy}A${hub} ${hub} 0 1 0 ${cx - hub} ${cy}A${hub} ${hub} 0 1 0 ${cx + hub} ${cy}Z`;
}

// Big gear (12 teeth) at the centre; a small one (8 teeth) meshes with it. Starting phases put a tooth of the big gear
// on the line between the centres and a gap of the small one, and the speeds keep the 12:8 ratio, so they stay meshed.
const BIG = gearPath(70, 70, 40, 12, 8, -30);
const SMALL = gearPath(122, 40, 28, 8, 8, 150 - 22.5);

/**
 * Shown instead of the sign-in form (or the boot error) while the server is being replaced.
 * It checks the server every few seconds and hands control back as soon as it answers.
 */
export function ServerUpgrading({ onBack }: { onBack: () => void }) {
  const [checking, setChecking] = useState(false);
  const [tries, setTries] = useState(0);
  const running = useRef(false);
  const back = useRef(onBack);
  back.current = onBack;

  const check = useCallback(async () => {
    if (running.current) return;
    running.current = true;
    setChecking(true);
    const up = await serverIsUp();
    running.current = false;
    if (up) { back.current(); return; }
    setChecking(false);
    setTries((n) => n + 1);
  }, []);

  useEffect(() => {
    const t = window.setInterval(() => { if (!document.hidden) void check(); }, 5000);
    const onVisible = () => { if (!document.hidden) void check(); };
    document.addEventListener("visibilitychange", onVisible);
    return () => { window.clearInterval(t); document.removeEventListener("visibilitychange", onVisible); };
  }, [check]);

  return (
    <AuthLayout>
      <div className="up">
        <div className="up-gears" aria-hidden="true">
          <svg viewBox="0 0 170 120">
            <path className="up-gear up-gear-big" d={BIG} fillRule="evenodd" />
            <path className="up-gear up-gear-small" d={SMALL} fillRule="evenodd" />
          </svg>
        </div>
        <div className="lg-heading">
          <span className="lg-overline">به‌روزرسانی در جریان است</span>
          <h2>سرور در حال ارتقاست</h2>
          <p>نسخه‌ی تازه‌ی آرته در حال نصب است و کمی زمان می‌برد. این صفحه خودکار وصل می‌شود؛ لازم نیست کاری بکنید.</p>
        </div>
        <div className="up-track" aria-hidden="true"><span /></div>
        <p className="up-state" role="status" aria-live="polite">
          {checking ? "در حال بررسی اتصال…" : tries > 0 ? "هنوز آماده نیست؛ چند ثانیه‌ی دیگر دوباره بررسی می‌شود." : "هر چند ثانیه اتصال بررسی می‌شود."}
        </p>
        <button className="lg-submit" type="button" onClick={() => void check()} disabled={checking} aria-busy={checking}>
          <span>{checking ? "در حال بررسی…" : "بررسی دوباره"}</span>
          {checking && <span className="lg-spinner" aria-hidden="true" />}
        </button>
      </div>
    </AuthLayout>
  );
}
