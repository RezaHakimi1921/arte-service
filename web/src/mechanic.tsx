import { useEffect, useState } from "react";

/**
 * Arte's mechanic: navy coveralls and cap with the logo's orange, a wrench in one hand and a notebook with the
 * Arte «A» in the other. Drawn in code (no image files): sharp at any size and a few hundred bytes.
 * mode "walk" walks in and stops (the opening screen); "stand" stands with a small idle sway (login page).
 */
export function Mechanic({ mode = "stand", className = "" }: { mode?: "walk" | "stand"; className?: string }) {
  return (
    <svg className={`mech mech-${mode} ${className}`} viewBox="0 0 160 240" role="img" aria-label="تعمیرکار آرته با آچار و دفتر">
      <ellipse className="mech-shadow" cx="80" cy="231" rx="38" ry="6" />
      <g className="mech-body">
        {/* legs (behind the torso) */}
        <g className="mech-leg mech-leg-a">
          <rect x="61" y="136" width="17" height="80" rx="8" className="mech-navy-dark" />
          <path d="M57 214h24a5 5 0 0 1 5 5v3H54v-3a5 5 0 0 1 3-5Z" className="mech-shoe" />
        </g>
        <g className="mech-leg mech-leg-b">
          <rect x="82" y="136" width="17" height="80" rx="8" className="mech-navy" />
          <path d="M79 214h24a5 5 0 0 1 3 5v3H74v-3a5 5 0 0 1 5-5Z" className="mech-shoe" />
        </g>

        {/* wrench arm (behind) */}
        <g className="mech-arm mech-arm-wrench">
          <rect x="44" y="86" width="15" height="56" rx="7" className="mech-navy-dark" />
          <circle cx="51.5" cy="144" r="7.5" className="mech-skin" />
          <g className="mech-wrench">
            <rect x="48.5" y="146" width="6" height="40" rx="3" />
            <path d="M51.5 182c-7 0-11.5 5-11.5 10.5 0 3 1.2 5.6 3.2 7.5l3.3-6.5h10l3.3 6.5a10 10 0 0 0 3.2-7.5C63 187 58.5 182 51.5 182Z" />
          </g>
        </g>

        {/* torso: coveralls with orange collar, belt and pocket */}
        <path d="M56 84c0-6 5-10 11-10h26c6 0 11 4 11 10v62c0 4-3 7-7 7H63c-4 0-7-3-7-7Z" className="mech-navy" />
        <path d="M68 74h24l-12 14Z" className="mech-orange" />
        <rect x="56" y="128" width="48" height="6" className="mech-navy-dark" />
        <rect x="76" y="127" width="8" height="8" rx="1.5" className="mech-orange" />
        <rect x="85" y="96" width="13" height="10" rx="2" className="mech-navy-dark" />
        <line x1="80" y1="88" x2="80" y2="126" className="mech-seam" />

        {/* head and cap */}
        <rect x="74" y="64" width="12" height="12" rx="4" className="mech-skin" />
        <circle cx="80" cy="50" r="19" className="mech-skin" />
        <circle cx="61.5" cy="52" r="4" className="mech-skin" />
        <circle cx="98.5" cy="52" r="4" className="mech-skin" />
        <path d="M60.5 46c0-12 8.7-20 19.5-20s19.5 8 19.5 20Z" className="mech-navy" />
        <path d="M60 45.5h40v4H60Z" className="mech-orange" />
        <path d="M70 49.5h-17a4 4 0 0 1 4-4h13Z" className="mech-navy-dark" />
        <circle cx="73" cy="54" r="2" className="mech-ink" />
        <circle cx="87" cy="54" r="2" className="mech-ink" />
        <path d="M74 61c3.5 3 8.5 3 12 0" className="mech-smile" />

        {/* notebook arm (front): the Arte notebook held at the side */}
        <g className="mech-arm mech-arm-book">
          <rect x="101" y="86" width="15" height="40" rx="7" className="mech-navy" />
          <g className="mech-book" transform="translate(5 12)">
            <rect x="96" y="114" width="34" height="42" rx="4" className="mech-book-cover" />
            <rect x="96" y="114" width="5" height="42" rx="2" className="mech-navy-dark" />
            <path d="M115 124 106 147h4.6l2-5.2h5.6l2 5.2h4.6L115 124Zm-1.3 13.5 1.3-3.6 1.3 3.6Z" className="mech-book-logo" />
            <path d="M104.5 148.5c4-3.5 10-5.5 21-6.5" className="mech-book-swoosh" />
          </g>
          <circle cx="110" cy="128" r="7.5" className="mech-skin" />
        </g>
      </g>
    </svg>
  );
}

let shownThisSession = false;

/**
 * Opening screen: the mechanic walks in and stops, then «آرته سرویس» appears. Once per visit, about two seconds,
 * on top of the app while it loads underneath; a tap skips it. With «reduce motion» it is not shown at all.
 */
export function Intro() {
  const [show, setShow] = useState(() => {
    if (shownThisSession) return false;
    try {
      if (sessionStorage.getItem("arte-intro")) return false;
    } catch { /* storage blocked: show it */ }
    return !window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
  });
  const [leaving, setLeaving] = useState(false);

  useEffect(() => {
    if (!show) return;
    shownThisSession = true;
    try { sessionStorage.setItem("arte-intro", "1"); } catch { /* ignore */ }
    const out = window.setTimeout(() => setLeaving(true), 2300);
    const gone = window.setTimeout(() => setShow(false), 2700);
    return () => { window.clearTimeout(out); window.clearTimeout(gone); };
  }, [show]);

  if (!show) return null;
  return (
    <div className={`intro${leaving ? " leaving" : ""}`} onClick={() => { setLeaving(true); window.setTimeout(() => setShow(false), 300); }} aria-hidden="true">
      <div className="intro-stage">
        <Mechanic mode="walk" className="intro-mech" />
        <div className="intro-brand">
          <strong>آرته <span>سرویس</span></strong>
          <span>همراه حرفه‌ای تعمیرگاه</span>
        </div>
      </div>
    </div>
  );
}
