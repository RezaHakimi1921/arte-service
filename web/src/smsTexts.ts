/**
 * System default SMS texts (mirrors Arte.Core Messaging.SmsTemplates.Branch).
 * Shops only preview these; wording changes go through a change-request to the platform admin.
 */
export type SmsKey =
  | "case.opened" | "case.ready" | "case.delivered" | "case.link"
  | "case.waiting_part" | "survey.request" | "staff.work_finished";

export const SMS_PATTERNS: { key: SmsKey; title: string; text: string }[] = [
  {
    key: "case.opened", title: "هنگام پذیرش",
    text: "{{WorkshopName}}\n{{CustomerName}} عزیز\n{{Vehicle}} پذیرش شد.\nپیگیری: {{CustomerLink}}",
  },
  {
    key: "case.ready", title: "آماده‌ی تحویل",
    text: "{{WorkshopName}}\nهمراه گرامی\n{{Vehicle}} آماده‌ی تحویل است.\nجزئیات: {{CustomerLink}}",
  },
  {
    key: "case.delivered", title: "هنگام تحویل",
    text: "{{WorkshopName}}\n{{Vehicle}} تحویل شد.\nسابقه و ضمانت: {{CustomerLink}}",
  },
  {
    key: "case.link", title: "ارسال دوباره‌ی لینک (از پرونده)",
    text: "{{WorkshopName}}\nلینک پیگیری {{Vehicle}}:\n{{CustomerLink}}",
  },
  {
    key: "case.waiting_part", title: "منتظر قطعه‌ی مشتری",
    text: "{{WorkshopName}}\nبرای ادامه‌ی کار {{Vehicle}} منتظر قطعه‌ی شما هستیم.\n{{CustomerLink}}",
  },
  {
    key: "survey.request", title: "نظرسنجی بعد از تحویل",
    text: "{{WorkshopName}}\nنظر شما برای ما مهم است.\nثبت نظر: {{CustomerLink}}",
  },
  {
    key: "staff.work_finished", title: "به استادکار: کار در انتظار بازبینی",
    text: "{{WorkshopName}}\n{{CaseCode}} ({{Vehicle}}) تمام شد؛ منتظر بازبینی شماست.",
  },
];

const SAMPLE: Record<string, string> = {
  WorkshopName: "مکانیک موتور",
  CustomerName: "سیما کریمی",
  Vehicle: "موتور هوندا ۱۲۵",
  CustomerLink: "service.artepersia.com/t/k7m2q9xd4p",
  CaseCode: "CASE-123",
  Code: "123456",
};

/** Preview with sample or shop-provided values. Legacy %name% tokens still work. */
export function fillSms(text: string, values?: { shop?: string; name?: string; vehicle?: string; code?: string }) {
  const map: Record<string, string> = {
    ...SAMPLE,
    ...(values?.shop ? { WorkshopName: values.shop } : {}),
    ...(values?.name ? { CustomerName: values.name } : {}),
    ...(values?.vehicle ? { Vehicle: values.vehicle } : {}),
    ...(values?.code ? { CustomerLink: `service.artepersia.com/t/${values.code}`, Code: values.code } : {}),
  };
  return text
    .replace(/\{\{\s*([A-Za-z]+)\s*\}\}/g, (_, k: string) => map[k] ?? "")
    .replace(/%(shop|name|vehicle|code)%/g, (_, k: string) => {
      if (k === "shop") return map.WorkshopName;
      if (k === "name") return map.CustomerName;
      if (k === "vehicle") return map.Vehicle;
      return values?.code ?? "k7m2q9xd4p";
    });
}
