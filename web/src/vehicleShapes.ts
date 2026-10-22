/**
 * Vehicle pictures for the customer tracking page (see vehicleProgress.tsx). Every vehicle is seen from above,
 * facing left, in a 520×260 box and symmetrical about y = 130 (the motorcycle's exhaust excepted). The colour fill
 * sweeps in from the rear (the right edge of `span`) and is clipped to `body`.
 *
 * Paint tokens: "@body" (white metal), "@glass" (dark glass), "@paint" (the progress colour).
 */
export type Layer = { d: string; fill?: string; stroke?: string; sw?: number; op?: number; fillOp?: number; cap?: boolean };

export type VehicleShape = {
  /** Left and right edge of the body, for the colour sweep. */
  span: [number, number];
  /** Scale about the picture's centre (a motorcycle is narrow and would look small next to a car). */
  zoom?: number;
  /** Outline: the base metal and the clip for the colour. */
  body: string;
  /** Drawn first: tyres, forks, exhaust. */
  under: Layer[];
  /** Highlights painted inside the colour (clipped to the body, above the fill). */
  paint: Layer[];
  /** Drawn last: glass, mirrors, lamps, details. */
  over: Layer[];
};

const H = 260;

/** Mirror a path made of M/L/C/Q/Z commands top-to-bottom (y → 260 − y). */
function flip(d: string): string {
  let n = 0;
  return d.replace(/[MLCQZ]|-?\d+(?:\.\d+)?/g, (t) => {
    if (/[A-Z]/.test(t)) { n = 0; return t; }
    return n++ % 2 === 1 ? String(H - Number(t)) : t;
  });
}
const both = (d: string) => `${d} ${flip(d)}`;

function rr(x: number, y: number, w: number, h: number, r: number) {
  return `M${x + r} ${y}H${x + w - r}A${r} ${r} 0 0 1 ${x + w} ${y + r}V${y + h - r}A${r} ${r} 0 0 1 ${x + w - r} ${y + h}H${x + r}A${r} ${r} 0 0 1 ${x} ${y + h - r}V${y + r}A${r} ${r} 0 0 1 ${x + r} ${y}Z`;
}
/** A rounded rectangle and its mirror image. */
const rrBoth = (x: number, y: number, w: number, h: number, r: number) => `${rr(x, y, w, h, r)} ${rr(x, H - y - h, w, h, r)}`;

const TYRE = "#202938";
const RIM = "#394354";
const GLASS_EDGE = "#DCE3ED";
const MIRROR: Pick<Layer, "fill" | "stroke" | "sw"> = { fill: "#DCE3EC", stroke: "#BFC9D8", sw: 1 };
const LAMP = "#FDE68A";
const TAIL = "#F05260";

function wheels(xs: number[], y: number, w: number, h: number, r: number): Layer[] {
  return [
    { d: xs.map((x) => rrBoth(x, y, w, h, r)).join(" "), fill: TYRE },
    { d: xs.map((x) => rrBoth(x + 6, y + 5, w - 12, h - 10, r - 3)).join(" "), fill: RIM },
  ];
}

const glint = (d: string, op = 0.38): Layer => ({ d, stroke: "#FFFFFF", sw: 2, op, cap: true });

/* ───────── car (sedan) ───────── */
const CAR_BODY = "M105 130C108 98 137 78 182 67L246 55Q267 51 289 53L380 56C422 59 448 80 456 108L460 130L456 152C448 180 422 201 380 204L289 207Q267 209 246 205L182 193C137 182 108 162 105 130Z";
const car: VehicleShape = {
  span: [105, 460],
  body: CAR_BODY,
  under: wheels([143, 383], 39, 56, 46, 11),
  paint: [
    { d: "M250 58L380 61Q432 65 448 104", stroke: "#FFFFFF", sw: 3, op: 0.22 },
    { d: "M393 78Q427 89 437 112L440 130L437 148Q427 171 393 182L383 173L383 87Z", fill: "#FFFFFF", fillOp: 0.055, stroke: "#FFFFFF", sw: 1, op: 0.12 },
  ],
  over: [
    { d: "M221 78Q203 85 197 106L193 130L197 154Q203 175 221 182L270 182L270 78Z", fill: "@glass", stroke: "#F1F5F9", sw: 2 },
    { d: "M244 76Q265 69 286 71L356 75Q374 77 381 96L385 130L381 164Q374 183 356 185L286 189Q265 191 244 184L226 165L222 130L226 95Z", fill: "@glass", stroke: GLASS_EDGE, sw: 2 },
    glint("M251 83L356 84"), glint("M251 177L356 176", 0.25),
    { d: both("M243 56L256 46L268 52L258 61Z"), ...MIRROR },
    { d: both("M111 101L133 92L137 99L115 108Z"), fill: LAMP },
    { d: both("M451 106L459 111L459 120L452 116Z"), fill: TAIL },
    { d: both("M113 123L121 123"), stroke: "#9CA9B9", sw: 2, cap: true },
    { d: "M127 94Q152 78 199 72", stroke: "#FFFFFF", sw: 2, op: 0.9, cap: true },
    { d: "M127 166Q152 182 199 188", stroke: "#FFFFFF", sw: 2, op: 0.65, cap: true },
  ],
};

/* ───────── SUV: taller, longer cabin, roof rails ───────── */
const suv: VehicleShape = {
  span: [100, 466],
  body: "M100 130C103 96 134 74 180 63L240 51Q262 47 286 49L392 52C436 55 458 78 463 108L466 130L463 152C458 182 436 205 392 208L286 211Q262 213 240 209L180 197C134 186 103 164 100 130Z",
  under: wheels([138, 386], 33, 62, 50, 12),
  paint: [
    { d: "M246 54L392 57Q440 61 456 102", stroke: "#FFFFFF", sw: 3, op: 0.22 },
  ],
  over: [
    { d: "M238 72Q262 65 288 67L384 71Q408 73 415 96L419 130L415 164Q408 187 384 189L288 193Q262 195 238 188L220 168L216 130L220 94Z", fill: "#FFFFFF", fillOp: 0.12, stroke: "#FFFFFF", sw: 1.6, op: 0.55 },
    { d: "M214 74Q196 82 190 104L186 130L190 156Q196 178 214 186L262 186L262 74Z", fill: "@glass", stroke: "#F1F5F9", sw: 2 },
    { d: "M278 88L368 90Q386 92 388 108L390 130L388 152Q386 168 368 170L278 172Q264 170 262 154L260 130L262 106Q264 90 278 88Z", fill: "@glass", stroke: GLASS_EDGE, sw: 2 },
    glint("M272 96L364 98"), glint("M272 164L364 162", 0.25),
    { d: "M398 86Q410 92 412 108L414 130L412 152Q410 168 398 174Z", fill: "@glass", stroke: GLASS_EDGE, sw: 1.6 },
    { d: both("M242 60L396 62"), stroke: "#8995A8", sw: 3, cap: true },
    { d: both("M234 52L248 41L261 48L251 58Z"), ...MIRROR },
    { d: both("M106 100L130 90L134 98L110 108Z"), fill: LAMP },
    { d: both("M456 104L464 109L464 119L457 114Z"), fill: TAIL },
    { d: both("M108 122L117 122"), stroke: "#9CA9B9", sw: 2, cap: true },
    { d: "M124 92Q150 76 196 70", stroke: "#FFFFFF", sw: 2, op: 0.9, cap: true },
    { d: "M124 168Q150 184 196 190", stroke: "#FFFFFF", sw: 2, op: 0.65, cap: true },
  ],
};

/* ───────── van: short nose, long box, ribbed roof ───────── */
const van: VehicleShape = {
  span: [108, 472],
  body: "M108 130C110 100 132 80 170 72L216 63Q232 60 248 60L440 60C458 60 468 72 470 92L472 130L470 168C468 188 458 200 440 200L248 200Q232 200 216 197L170 188C132 180 110 160 108 130Z",
  under: wheels([148, 392], 43, 58, 46, 11),
  paint: [
    { d: "M250 64L438 64Q460 66 464 94", stroke: "#FFFFFF", sw: 3, op: 0.2 },
  ],
  over: [
    { d: "M196 78Q180 86 176 106L173 130L176 154Q180 174 196 182L232 182L232 78Z", fill: "@glass", stroke: "#F1F5F9", sw: 2 },
    { d: "M244 76L440 76Q456 76 458 92L460 130L458 168Q456 184 440 184L244 184Z", fill: "#FFFFFF", fillOp: 0.1, stroke: "#FFFFFF", sw: 1.6, op: 0.55 },
    { d: "M258 100L446 100M258 130L446 130M258 160L446 160", stroke: "#FFFFFF", sw: 2, op: 0.4, cap: true },
    glint("M184 88L226 88"),
    { d: both("M188 62L200 50L211 56L203 66Z"), ...MIRROR },
    { d: both("M114 102L136 94L140 101L118 109Z"), fill: LAMP },
    { d: both("M464 80L470 84L470 94L465 90Z"), fill: TAIL },
    { d: both("M116 124L124 124"), stroke: "#9CA9B9", sw: 2, cap: true },
    { d: "M132 96Q154 82 192 76", stroke: "#FFFFFF", sw: 2, op: 0.9, cap: true },
    { d: "M132 164Q154 178 192 184", stroke: "#FFFFFF", sw: 2, op: 0.65, cap: true },
  ],
};

/* ───────── pickup: cab and an open load bed ───────── */
const pickup: VehicleShape = {
  span: [104, 468],
  body: "M104 130C107 98 134 78 176 68L236 56Q256 52 276 54L440 56C456 56 464 66 466 84L468 130L466 176C464 194 456 204 440 204L276 206Q256 208 236 204L176 192C134 182 107 162 104 130Z",
  under: wheels([140, 386], 39, 58, 46, 11),
  paint: [
    { d: "M240 58L330 59", stroke: "#FFFFFF", sw: 3, op: 0.22 },
  ],
  over: [
    { d: "M214 78Q198 86 192 106L189 130L192 154Q198 174 214 182L250 182L250 78Z", fill: "@glass", stroke: "#F1F5F9", sw: 2 },
    { d: "M260 78L300 78Q314 80 316 96L318 130L316 164Q314 180 300 182L260 182Z", fill: "@glass", stroke: GLASS_EDGE, sw: 2 },
    glint("M266 85L304 85"),
    // load bed: dark liner with ribs
    { d: "M332 72L436 72Q448 72 449 86L451 130L449 174Q448 188 436 188L332 188Z", fill: "#0F172A", fillOp: 0.42, stroke: "#FFFFFF", sw: 1.6, op: 0.5 },
    { d: "M350 80L350 180M372 80L372 180M394 80L394 180M416 80L416 180", stroke: "#FFFFFF", sw: 2, op: 0.18 },
    { d: both("M226 58L239 47L251 53L241 63Z"), ...MIRROR },
    { d: both("M110 101L132 92L136 99L114 108Z"), fill: LAMP },
    { d: both("M460 80L466 84L466 94L461 90Z"), fill: TAIL },
    { d: both("M112 123L120 123"), stroke: "#9CA9B9", sw: 2, cap: true },
    { d: "M128 94Q152 80 196 73", stroke: "#FFFFFF", sw: 2, op: 0.9, cap: true },
    { d: "M128 166Q152 180 196 187", stroke: "#FFFFFF", sw: 2, op: 0.65, cap: true },
  ],
};

/* ───────── motorcycle, from above: fender, tank, seat, tail, handlebar, exhaust ───────── */
const MOTO_TANK = "M250 116C258 98 296 92 332 102C348 107 352 118 352 130C352 142 348 153 332 158C296 168 258 162 250 144Z";
const motorcycle: VehicleShape = {
  span: [112, 462],
  zoom: 1.2,
  body: `${rr(112, 119, 80, 22, 11)} ${MOTO_TANK} M414 117L458 120Q463 130 458 140L414 143Z`,
  under: [
    { d: `${rr(104, 114, 68, 32, 14)} ${rr(388, 113, 72, 34, 15)}`, fill: TYRE },
    { d: `${rr(110, 119, 56, 22, 10)} ${rr(394, 118, 60, 24, 11)}`, fill: RIM },
    { d: "M168 123L236 123M168 137L236 137", stroke: "#9AA6B8", sw: 5, cap: true },
    { d: rr(190, 120, 66, 20, 7), fill: "#CBD5E1", stroke: "#B3BFD0", sw: 1 },
    { d: "M300 170L440 167", stroke: "#AEB8C9", sw: 10, cap: true },
    { d: "M436 166.5L448 166", stroke: "#475569", sw: 10, cap: true },
  ],
  paint: [
    { d: "M262 112Q300 102 338 110", stroke: "#FFFFFF", sw: 3, op: 0.3 },
  ],
  over: [
    { d: "M340 109C364 103 398 103 420 111Q428 130 420 149C398 157 364 157 340 151Q334 130 340 109Z", fill: "#263041", stroke: "#475569", sw: 1.4 },
    { d: "M352 112Q384 107 414 114", stroke: "#FFFFFF", sw: 2, op: 0.22, cap: true },
    { d: rr(228, 112, 22, 36, 9), fill: "#394354" },
    { d: "M222 70Q244 80 244 130Q244 180 222 190", stroke: "#2B3444", sw: 7, cap: true },
    { d: both("M222 70L208 64"), stroke: "#111827", sw: 10, cap: true },
    { d: both("M232 78L226 54"), stroke: "#64748B", sw: 3, cap: true },
    { d: both("M214 50Q216 42 230 42Q242 44 240 54Q234 60 220 58Q214 56 214 50Z"), ...MIRROR },
    { d: "M105 125L112 124L112 136L105 135Z", fill: LAMP },
    { d: "M456 123L462 125L462 135L456 137Z", fill: TAIL },
  ],
};

export const VEHICLE_SHAPES: Record<string, VehicleShape> = { car, suv, van, pickup, motorcycle };
