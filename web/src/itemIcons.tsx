import type { ReactNode } from "react";

/** Light line icons for goods and services on the customer invoice, picked from words in the item's title. */
const PATHS: Record<string, ReactNode> = {
  oil: <><path d="M12 3s6 6.500 6 11a6 6 0 0 1-12 0c0-4.500 6-11 6-11Z" /><path d="M9.500 14.500a2.500 2.500 0 0 0 2.500 2.500" /></>,
  brake: <><circle cx="12" cy="12" r="9" /><circle cx="12" cy="12" r="3" /><path d="M12 3v3M12 18v3M3 12h3M18 12h3" /></>,
  chain: <><rect x="2.500" y="8.500" width="9" height="7" rx="3.500" /><rect x="12.500" y="8.500" width="9" height="7" rx="3.500" /></>,
  filter: <><path d="M4 5h16l-6 7v6l-4 2v-8Z" /></>,
  battery: <><rect x="3" y="7" width="18" height="12" rx="2.500" /><path d="M7 7V5h3v2M14 7V5h3v2M8 13h4M10 11v4M15 13h2" /></>,
  tire: <><circle cx="12" cy="12" r="9" /><circle cx="12" cy="12" r="4.500" /><path d="M12 3v4.500M12 16.500V21M3 12h4.500M16.500 12H21" /></>,
  spark: <><path d="M9 3h6M10 3v5h4V3M10 8v4a2 2 0 0 0 4 0V8M12 14v7" /><path d="m10 18 2 3 2-3" /></>,
  light: <><path d="M9 18h6M10 21h4" /><path d="M12 3a6 6 0 0 0-3.500 10.900c.6.500 1 1.200 1 2V16h5v-.100c0-.800.400-1.500 1-2A6 6 0 0 0 12 3Z" /></>,
  cable: <><path d="M4 18c0-6 4-4 8-6s4-6 8-6" /><circle cx="4" cy="18" r="1.500" /><circle cx="20" cy="6" r="1.500" /></>,
  spring: <><path d="M8 3h8M8 21h8M12 3v2M12 19v2" /><path d="M7 7h10l-10 4h10L7 15h10l-10 4" /></>,
  bolt: <path d="M13 2 4 14h7l-1 8 9-12h-7Z" />,
  gear: <><circle cx="12" cy="12" r="3.500" /><path d="M12 2.500v3M12 18.500v3M2.500 12h3M18.500 12h3M5.300 5.300l2.100 2.100M16.600 16.600l2.100 2.100M5.300 18.700l2.100-2.100M16.600 7.400l2.100-2.100" /></>,
  clean: <path d="m12 3 1.500 4.500L18 9l-4.500 1.500L12 15l-1.500-4.500L6 9l4.500-1.500ZM19 15l.8 2.200L22 18l-2.200.800L19 21l-.8-2.200L16 18l2.200-.800Z" />,
  wrench: <path d="M14.700 6.300a4.500 4.500 0 0 0-5.600 5.600L3 18l3 3 6.100-6.100a4.500 4.500 0 0 0 5.600-5.600L15 12l-3-3 2.700-2.700Z" />,
  box: <><path d="M21 8 12 3 3 8v8l9 5 9-5Z" /><path d="m3 8 9 5 9-5M12 13v8" /></>,
};

const RULES: [string, RegExp][] = [
  ["oil", /روغن|oil/i],
  ["brake", /لنت|ترمز|دیسک|brake/i],
  ["chain", /زنجیر|کیت|chain/i],
  ["filter", /فیلتر|filter/i],
  ["battery", /باتری|battery/i],
  ["tire", /لاستیک|تایر|تیوب|چرخ|tire|tyre/i],
  ["spark", /شمع|spark/i],
  ["light", /چراغ|لامپ|راهنما|lamp|light/i],
  ["cable", /کابل|سیم گاز|کلاچ|سیم/i],
  ["spring", /کمک|فنر|جلوبندی|suspension/i],
  ["bolt", /برق|ریلی|استارت|دینام|سوئیچ|فیوز|باطری|cdi|electric/i],
  ["gear", /موتور|کاربراتور|انژکتور|سوپاپ|پیستون|رینگ|واشر|گاسکت|بلبرینگ|گیربکس|engine/i],
  ["clean", /شستشو|نظافت|سرویس|تمیز/i],
];

/** True when the title names something with its own icon (so a generic work line may show the shop logo instead). */
export const matchesIcon = (title: string) => RULES.some(([, re]) => re.test(title));

/** The icon for an item: by words in its title, else a wrench for work and a box for goods. */
export function ItemIcon({ title, kind }: { title: string; kind: string }) {
  const hit = RULES.find(([, re]) => re.test(title))?.[0];
  const name = hit ?? (kind === "part" ? "box" : "wrench");
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      {PATHS[name]}
    </svg>
  );
}
