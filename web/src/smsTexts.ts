/**
 * The customer SMS patterns exactly as registered with the SMS provider (Faraz / Iran Payamak). The provider wants
 * a label before every variable, so each one stands after its own word. Keep this file and the provider in step:
 * the settings page shows the customer these texts, filled with sample values.
 */
export type SmsKey = "case.opened" | "case.ready" | "case.delivered" | "case.link" | "survey.request";

export const SMS_PATTERNS: { key: SmsKey; title: string; text: string }[] = [
  {
    key: "case.opened", title: "هنگام پذیرش",
    text: "مشتری گرامی: %name%\nوسیله: %vehicle%\nپذیرش شد و در نوبت کار قرار گرفت.\nپیگیری: service.artepersia.com/t/%code%\nفرستنده: %shop%",
  },
  {
    key: "case.ready", title: "آماده‌ی تحویل",
    text: "مشتری گرامی: %name%\nوسیله: %vehicle%\nآماده‌ی تحویل است.\nجزئیات و هزینه: service.artepersia.com/t/%code%\nفرستنده: %shop%",
  },
  {
    key: "case.delivered", title: "هنگام تحویل",
    text: "مشتری گرامی: %name%\nوسیله: %vehicle%\nتحویل شد. از اعتمادتان سپاسگزاریم.\nسابقه و ضمانت: service.artepersia.com/t/%code%\nفرستنده: %shop%",
  },
  {
    key: "case.link", title: "ارسال دوباره‌ی لینک (از پرونده)",
    text: "مشتری گرامی: %name%\nوسیله: %vehicle%\nلینک پیگیری: service.artepersia.com/t/%code%\nفرستنده: %shop%",
  },
  {
    key: "survey.request", title: "نظرسنجی بعد از تحویل",
    text: "مشتری گرامی: %name%\nاز اینکه ما را انتخاب کردید سپاسگزاریم.\nلطفاً در چند ثانیه نظرتان را بگویید.\nنظرسنجی: service.artepersia.com/s/%code%\nفرستنده: %shop%",
  },
];

/** The text with sample values, as the customer would read it. */
export function fillSms(text: string, values: { shop: string; name: string; vehicle: string; code: string }) {
  return text.replace(/%(shop|name|vehicle|code)%/g, (_, k: keyof typeof values) => values[k]);
}
