# 04 — SMS Design (مستقل از Provider)

> Provider هنوز انتخاب نشده. طراحی طوری است که تعویض Provider فقط یک کلاس Adapter و یک تنظیم باشد.

## 1. پیام‌های MVP

| Key | چه زمانی | نوع | متن نمونه |
|-----|----------|-----|-----------|
| `auth.otp` | ورود کارکنان | OTP / الگو | `کد ورود آرته: {code}` |
| `case.opened` | ثبت پرونده (اختیاری per tenant) | خدماتی / الگو | `{customer} عزیز، موتور {asset} در {business} پذیرش شد. پیگیری: {link}` |
| `case.approval` | ارسال برای تأیید | خدماتی / الگو | `هزینه تعمیر {asset}: {amount} تومان. تأیید یا رد: {link}` |
| `case.ready` | ورود به Stage `ready` | خدماتی / الگو | `موتور {asset} آماده تحویل است. مبلغ قابل پرداخت: {balance} تومان. {business}` |
| `case.stage_changed` | سایر Stageها (پیش‌فرض خاموش) | خدماتی / الگو | `وضعیت موتور {asset}: {stage}` |

قاعده: **پیامک به مشتری فقط با الگوی (Template/Pattern) از پیش ثبت‌شده** نزد Provider ارسال شود.
پیام متن‌آزاد از خطوط تبلیغاتی به شماره‌هایی که در لیست سیاه پیامک تبلیغاتی هستند نمی‌رسد؛ پیام‌های خدماتی الگومحور این مشکل را ندارند (جزئیات را برای هر Provider تأیید کن).

هر Tenant در تنظیمات مشخص می‌کند کدام پیام‌ها روشن باشند (نگاشت `StageKey → MessageKey`).

## 2. معماری

```text
Domain Event (case.stage_changed)
        │
        ▼
NotificationRules (tenant settings: stage → message key)
        │
        ▼
Message row (status = queued)      ← در همان تراکنش دیتابیس
        │
        ▼
SmsDispatcher (BackgroundService)  ← retry با backoff، حداکثر ۳ بار
        │
        ▼
ISmsProvider  ──►  KavenegarProvider | SmsIrProvider | MelipayamakProvider | FakeSmsProvider
        │
        ▼
Delivery webhook / poll  →  Message.status = delivered | failed
```

```csharp
public interface ISmsProvider
{
    string Name { get; }
    Task<SmsSendResult> SendTemplateAsync(string mobile, string templateId,
        IReadOnlyDictionary<string, string> tokens, CancellationToken ct);
    Task<SmsStatus> GetStatusAsync(string providerMessageId, CancellationToken ct);
}
```

- `FakeSmsProvider`: در Development پیام را لاگ می‌کند و در UI توسعه نشان می‌دهد. تا انتخاب Provider همه کار با این جلو می‌رود.
- نگاشت `MessageKey → TemplateId` در تنظیمات Provider (appsettings / secrets)، نه در کد.

### جدول Message
`Id, TenantId, CaseId?, CustomerId?, Mobile, MessageKey, Tokens (jsonb), Provider, ProviderMessageId,
Status (queued | sent | delivered | failed), Attempts, LastError, CostRials?, CreatedAt, SentAt`

- هر پیام یک `case.event` هم در Timeline می‌سازد (`message.sent` / `message.failed`).
- **Idempotency:** کلید یکتا `(CaseId, MessageKey, StageEnteredAt)` تا تغییر Stage تکراری دو پیامک نفرستد.
- **Rate limit OTP:** حداکثر ۱ درخواست در ۶۰ ثانیه و ۵ در ساعت per موبایل.

### طول پیام و هزینه
فارسی (UCS-2) حدود ۷۰ کاراکتر در هر بخش؛ پیام چندبخشی ~۶۷ کاراکتر per بخش. متن الگوها کوتاه بماند (هدف: ۱ تا ۲ بخش).

برآورد ماهانه per مغازه:
```text
پرونده در روز × پیام per پرونده × ۲۶ روز × بخش per پیام × قیمت هر بخش
مثال: 8 × 2 × 26 × 2 = 832 بخش در ماه
```

## 3. معیار انتخاب Provider

| معیار | وزن | توضیح |
|-------|-----|-------|
| ارسال الگومحور/خدماتی (Verify/Lookup/Pattern) | بالا | الزامی؛ عبور از لیست سیاه |
| کیفیت API و SDK دات‌نت / REST ساده | بالا | |
| گزارش تحویل (Delivery Report) با Webhook | بالا | برای Timeline قابل اعتماد |
| زمان تأیید الگو | متوسط | چند ساعت یا چند روز؟ |
| قیمت هر بخش فارسی (خدماتی) | متوسط | |
| پایداری و Uptime، پشتیبانی | متوسط | |
| امکان زیرحساب per Tenant (بعدها) | پایین | برای Resell پیامک به مشتری‌ها |

## 4. Providerهای کاندید برای مقایسه

| Provider | نکته برای بررسی |
|----------|-----------------|
| کاوه‌نگار (Kavenegar) | API «Verify Lookup» الگومحور؛ در ArtePos/تجربه قبلی آشناست؟ |
| SMS.ir | ارسال Verify با Template؛ REST جدید |
| ملی‌پیامک (Melipayamak) | «خدماتی اشتراکی» با الگو |
| قاصدک (Ghasedak) | OTP و الگو |
| فراز اس‌ام‌اس / IPPanel | ارسال Pattern |

> این جدول **ادعای قیمت/قابلیت نیست**؛ هر مورد باید از مستندات فعلی خود Provider تأیید شود.
> پیشنهاد: با ۲ Provider حساب تست بساز، الگوی `case.ready` را ثبت کن، و زمان تأیید الگو + زمان تحویل + Delivery Report را مقایسه کن.

## 5. تصمیم‌های باز

- [ ] Provider نهایی
- [ ] پیامک با نام/خط اختصاصی هر مغازه یا خط مشترک Arte؟ (MVP: خط مشترک Arte + نام مغازه در متن)
- [ ] هزینه پیامک داخل اشتراک یا جدا حساب شود؟
