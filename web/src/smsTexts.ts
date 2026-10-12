/**
 * The customer SMS patterns exactly as registered with the SMS provider (Faraz / Iran Payamak). The provider wants
 * the brand or site in every pattern, a label before every variable («نام: %name%»), and no advertising. Keep this
 * file and the provider in step: the settings page shows the customer these texts, filled with sample values.
 */
export type SmsKey = "case.opened" | "case.ready" | "case.delivered" | "case.link" | "survey.request";

export const SMS_PATTERNS: { key: SmsKey; title: string; text: string }[] = [
  {
    key: "case.opened", title: "هنگام پذیرش",
    text: "آرته سرویس\nنام: %name%\nوسیله: %vehicle%\nتعمیرگاه: %shop%\nوضعیت: پذیرش شد و در نوبت کار قرار گرفت.\nلینک پیگیری: service.artepersia.com/t/%code%",
  },
  {
    key: "case.ready", title: "آماده‌ی تحویل",
    text: "آرته سرویس\nنام: %name%\nوسیله: %vehicle%\nتعمیرگاه: %shop%\nوضعیت: آماده‌ی تحویل است.\nلینک جزئیات و هزینه: service.artepersia.com/t/%code%",
  },
  {
    key: "case.delivered", title: "هنگام تحویل",
    text: "آرته سرویس\nنام: %name%\nوسیله: %vehicle%\nتعمیرگاه: %shop%\nوضعیت: تحویل شد.\nلینک سابقه و ضمانت: service.artepersia.com/t/%code%",
  },
  {
    key: "case.link", title: "ارسال دوباره‌ی لینک (از پرونده)",
    text: "آرته سرویس\nنام: %name%\nوسیله: %vehicle%\nتعمیرگاه: %shop%\nلینک پیگیری: service.artepersia.com/t/%code%",
  },
  {
    key: "survey.request", title: "نظرسنجی بعد از تحویل",
    text: "آرته سرویس\nنام: %name%\nتعمیرگاه: %shop%\nلطفاً نظر خود را درباره‌ی این تعمیر ثبت کنید.\nلینک نظرسنجی: service.artepersia.com/s/%code%",
  },
];

/** The text with sample values, as the customer would read it. */
export function fillSms(text: string, values: { shop: string; name: string; vehicle: string; code: string }) {
  return text.replace(/%(shop|name|vehicle|code)%/g, (_, k: keyof typeof values) => values[k]);
}
