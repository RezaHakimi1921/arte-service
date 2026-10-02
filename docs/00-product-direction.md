# 00 — Product Direction (خلاصه)

منبع: «سند تغییر مسیر محصول Arte — نسخه 1.0». این فایل خلاصه تصمیم‌هاست، نه جایگزین سند اصلی.

## تغییر مدل ذهنی

```text
POS قبلی:   Product → Sale → Invoice → Payment            (Transaction-centric)
Arte جدید:  Customer → Case → Workflow → Work → Items → Payment → Delivery → Next Visit
                                                         (Case / Workflow / Customer-centric)
```

## استراتژی

```text
Generic Core  +  Vertical First (موتورسازی)
```

هسته عمومی ساخته می‌شود (Customer, Asset, Case, Workflow, Timeline, Item, Payment, Communication)
ولی همه تصمیم‌های UX و Template برای **یک صنف** بهینه می‌شود. Template دوم فقط وقتی ساخته می‌شود
که موتورسازی end-to-end داخل Arte اجرا شود.

## تصمیم‌های قطعی

| تصمیم | توضیح |
|-------|-------|
| Cloud / Web، Mobile First | کاربر کنار موتور است، نه پشت میز |
| Case-centric | پرونده قلب سیستم است، نه فاکتور |
| Configurable Workflow | Stage و Transition در داده، نه در کد |
| Product + Service + Labor | Line Item عمومی |
| Role / Assignment، Payment، Timeline | |
| Industry Template | کاربر از صفحه خالی شروع نمی‌کند |
| **صنف اول: موتورسازی** | تصمیم ۱۴۰۵/۰۷ |
| **نظرسنجی و باشگاه مشتریان: خارج از Arte** | از طریق API بیرونی؛ Arte فقط Event می‌فرستد ([05](05-integrations.md)) |
| **SMS: مستقل از Provider** | Provider بعد از مقایسه انتخاب می‌شود ([04](04-sms-design.md)) |

## خارج از محدوده MVP

Accounting کامل، ERP، انبارداری پیچیده، CRM سازمانی، Marketing Automation، Loyalty Engine،
Survey داخلی، AI، BI پیشرفته، Marketplace، اپ Native مشتری، Workflow Builder گرافیکی، Rule Engine عمومی.

## معیار موفقیت MVP

> درصد پرونده‌های فعالی که end-to-end داخل Arte مدیریت می‌شوند.

MVP موفق است اگر یک موتورسازی بدون برگشت به دفتر بتواند
`Customer → Case → Work → Items → Payment → Delivery` را در Arte انجام دهد و مدیر در هر لحظه بداند
چه پرونده‌هایی باز است، هر کدام کجاست، دست کیست، چه چیزی گیر کرده و چه چیزی آماده تحویل است.
