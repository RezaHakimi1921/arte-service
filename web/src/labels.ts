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
