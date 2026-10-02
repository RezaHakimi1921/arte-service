# 05 — Integrations: نظرسنجی و باشگاه مشتریان (API بیرونی)

## تصمیم

نظرسنجی، امتیاز، باشگاه مشتریان و کمپین **داخل Arte ساخته نمی‌شوند.** سیستم بیرونی مالک آن داده‌هاست.
Arte فقط **منبع Eventهای عملیاتی** است.

## الگو: Transactional Outbox + Webhook

```text
Command (مثلاً ثبت تحویل)
   │  یک تراکنش دیتابیس:
   ├─ Case.Stage = delivered
   ├─ CaseEvent (Timeline)
   └─ OutboxEvent (status = pending)
                │
                ▼
       OutboxDispatcher (BackgroundService)
                │  POST + امضای HMAC
                ▼
       سیستم بیرونی (نظرسنجی / باشگاه)
```

چرا Outbox: اگر سیستم بیرونی قطع باشد، Event گم نمی‌شود و بعداً ارسال می‌شود؛ و هیچ Eventی بدون
اینکه تغییر واقعاً در دیتابیس ثبت شده باشد بیرون نمی‌رود.

### جدول OutboxEvent
`Id (uuid, = event id), TenantId, Type, Payload (jsonb), OccurredAt, Status (pending | sent | failed | dead), Attempts, NextAttemptAt, LastError`

### جدول WebhookSubscription (per Tenant)
`Id, TenantId, Url, Secret, EventTypes (text[]), IsActive`

## قرارداد درخواست

```http
POST {subscription.url}
Content-Type: application/json
X-Arte-Event: case.delivered
X-Arte-Event-Id: 6f1c...           ← برای Idempotency در گیرنده
X-Arte-Timestamp: 1759400000
X-Arte-Signature: sha256=HMAC(secret, timestamp + "." + body)
```

```json
{
  "id": "6f1c...",
  "type": "case.delivered",
  "occurredAt": "2026-10-02T14:10:00+03:30",
  "tenant": { "id": "...", "name": "موتورسازی نمونه" },
  "data": {
    "caseId": "...", "caseNumber": 1024,
    "customer": { "id": "...", "mobile": "0912...", "fullName": "علی رضایی" },
    "asset": { "id": "...", "title": "Honda CG 125" },
    "total": 12500000, "paid": 12500000, "balance": 0,
    "odometerKm": 23400
  }
}
```

- **At-least-once:** گیرنده باید با `X-Arte-Event-Id` تکراری‌ها را نادیده بگیرد.
- Retry: 1m، 5m، 30m، 2h، 12h → بعد `dead` و نمایش در تنظیمات.
- پاسخ 2xx = موفق.

## Eventهای پیشنهادی برای سیستم بیرونی

| Event | استفاده در سیستم بیرونی |
|-------|------------------------|
| `customer.created` | عضویت در باشگاه |
| `case.delivered` | ارسال نظرسنجی بعد از تحویل، امتیاز خرید |
| `payment.recorded` | امتیاز بر اساس مبلغ |
| `case.cancelled` | پیگیری مشتری ناراضی |

## مسیر برگشت (بعد از MVP)

اگر سیستم بیرونی بخواهد چیزی در Timeline پرونده نشان داده شود (مثلاً «مشتری ۲ از ۵ داد»):
`POST /api/integrations/events` با API Key per Tenant → یک `CaseEvent` از نوع `external.*`.
در MVP ساخته نمی‌شود، فقط جای آن در مدل باز است.

## سؤال باز

- [ ] سیستم بیرونی کدام است و قرارداد API آن چیست؟ اگر قرارداد خودش را دارد (به جای Webhook ما)، یک Adapter مثل SMS می‌نویسیم.
