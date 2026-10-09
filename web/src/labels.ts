// Plain-language status: say *why* a case is where it is, not only the workflow stage name.

export const WAIT_REASONS: Record<string, string> = {
  customer_approval: "منتظر تأیید هزینه توسط مشتری",
  customer_parts: "منتظر قطعه‌ای که مشتری می‌آورد",
  shop_parts: "منتظر تأمین قطعه توسط تعمیرگاه",
  owner_decision: "منتظر تصمیم استاد",
  payment: "منتظر پرداخت",
  customer: "منتظر مشتری",
};

/** Reasons someone can set by hand to say a job is stopped (without changing its stage). */
export const MANUAL_WAIT_REASONS = ["owner_decision", "customer", "payment", "shop_parts", "customer_parts"];

export const PARTS_OPTIONS: { value: string; label: string; hint: string }[] = [
  { value: "customer_parts", label: "مشتری قطعه را تهیه می‌کند", hint: "به مشتری اطلاع داده می‌شود" },
  { value: "shop_parts", label: "تعمیرگاه قطعه را تهیه می‌کند", hint: "خرید یا سفارش قطعه با ما" },
];

type StageLike = { key: string; name: string; category: string };

/** "در حال عیب‌یابی توسط محمد", "منتظر تأیید هزینه توسط مشتری", "آماده تحویل". */
export function statusText(stage: StageLike, waitReason: string | null | undefined, assigneeName: string | null | undefined): string {
  if (waitReason && WAIT_REASONS[waitReason]) return WAIT_REASONS[waitReason];
  if (stage.category === "active" && assigneeName) return `${stage.name} توسط ${assigneeName}`;
  if (stage.key === "testing") return "تعمیر تمام شده؛ نیازمند تست";
  return stage.name;
}

export type Alert = { code: string; text: string; severity: "warn" | "danger" };

export const ALERT_ICON: Record<string, string> = { warn: "⚠", danger: "⏰" };

/** Case number as used in search, messages and the API: CASE-1024 (Latin digits, never grouped). */
export const caseCode = (n: number) => `CASE-${n}`;

const tomanText = (rials: unknown) => `${new Intl.NumberFormat("fa-IR").format(Math.round(Number(rials ?? 0) / 10))} تومان`;
const ITEM_KIND: Record<string, string> = { part: "قطعه", labor: "اجرت", service: "خدمت" };
const METHOD: Record<string, string> = { cash: "نقد", card: "کارت‌خوان", transfer: "کارت به کارت", other: "سایر" };
const FIELD_NAMES: Record<string, string> = {
  request: "شرح مشتری", requestedServices: "خدمات درخواستی", reportedProblems: "ایراد اعلامی", fuelLevel: "میزان سوخت",
  bodyStatus: "وضعیت بدنه", bodyNotes: "شرح آسیب", diagnosis: "عیب‌یابی", odometerKm: "کیلومتر",
  estimatedAmountRials: "برآورد هزینه", promisedAt: "قول تحویل", intake: "همراه وسیله",
};

const EVENT_TEXT: Record<string, (d: Record<string, unknown>) => string> = {
  "case.opened": () => "پرونده باز شد",
  "case.stage_changed": (d) =>
    `${d.action ?? "تغییر مرحله"}: از «${d.from}» به «${d.to}»`
    + (d.waitReason ? ` — ${WAIT_REASONS[d.waitReason as string] ?? ""}` : "")
    + (d.reason ? ` (دلیل: ${d.reason})` : ""),
  "case.assigned": (d) => (d.to ? `سپرده شد به ${d.to}` : "مسئول برداشته شد"),
  "case.updated": (d) => `ویرایش: ${Object.keys(d).map((k) => FIELD_NAMES[k] ?? k).join("، ")}`,
  "case.note_added": (d) => `یادداشت: ${d.text}`,
  "case.photo_added": () => "عکس اضافه شد",
  "customer.sms": (d) => `${d.sent ? "پیامک" : "پیامک ارسال نشد:"} ${({ "case.opened": "پذیرش", "case.ready": "آماده‌ی تحویل", "case.delivered": "تحویل" } as Record<string, string>)[d.kind as string] ?? ""} برای مشتری`,
  "case.reopened": (d) => `پرونده دوباره باز شد${d.reason ? ` (دلیل: ${d.reason})` : ""}`,
  "case.delivered": () => "وسیله تحویل مشتری شد",
  "case.cancelled": (d) => `پرونده لغو شد${d.reason ? ` (دلیل: ${d.reason})` : ""}`,
  "case.deleted": () => "پرونده حذف شد",
  "case.restored": () => "پرونده بازگردانی شد",
  "case.custody_changed": (d) => (d.custodyStatus === "in_shop" ? "وسیله در تعمیرگاه است" : "وسیله دست مشتری است"),
  "case.wait_changed": (d) => (d.waitReason ? `کار متوقف شد: ${WAIT_REASONS[d.waitReason as string] ?? ""}` : "کار دوباره جریان گرفت"),
  "case.item_added": (d) => d.restored
    ? `ردیف «${d.title}» بازگردانی شد`
    : `${ITEM_KIND[d.kind as string] ?? "ردیف"} ثبت شد: ${d.title}${d.quantity && Number(d.quantity) !== 1 ? ` × ${new Intl.NumberFormat("fa-IR").format(Number(d.quantity))}` : ""}${d.supplier === "customer" ? " (قطعه مشتری)" : ""}${d.status === "needed" ? " (هنوز نصب نشده)" : ""}`,
  "case.item_updated": (d) => `ردیف «${d.title}» ویرایش شد`,
  "case.item_removed": (d) => `ردیف «${d.title}» حذف شد`,
  "payment.recorded": (d) => `پرداخت ${tomanText(d.amountRials)} (${METHOD[d.method as string] ?? ""})${d.note ? ` — ${d.note}` : ""}`,
  "payment.voided": (d) => `پرداخت ${tomanText(d.amountRials)} باطل شد`,
  "case.credit": (d) => `تحویل به‌صورت نسیه؛ مانده ${tomanText(d.balanceRials)}`,
};

/** Plain Persian for any timeline event; never shows a raw code. */
export function eventText(type: string, data: Record<string, unknown> | null | undefined): string {
  return (EVENT_TEXT[type] ?? (() => "تغییر در پرونده"))(data ?? {});
}

export const ROLE_NAMES: Record<string, string> = { owner: "استاد (مالک)", supervisor: "مدیر داخلی", technician: "شاگرد" };
