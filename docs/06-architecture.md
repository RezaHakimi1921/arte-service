# 06 — Architecture

## استک

| لایه | انتخاب | چرا |
|------|--------|-----|
| API | ASP.NET Core (.NET 10), Minimal APIs | تجربه ArtePos، کارایی، تایپ قوی |
| داده | PostgreSQL 16+ با EF Core (Npgsql) | `jsonb` برای Attributes/Event، Multi-tenant ساده |
| Background | `BackgroundService` داخل همان پروسه | SMS و Outbox؛ بدون صف جدا در MVP |
| فرانت | PWA: React + TypeScript + Vite | Mobile First، نصب روی گوشی، RTL |
| فایل | ذخیره S3-compatible (در Dev: دیسک محلی) | عکس پرونده |
| احراز هویت | OTP موبایل → JWT کوتاه‌مدت + Refresh Token در کوکی httpOnly | کاربر رمز یادش نمی‌ماند |
| استقرار | Docker Compose روی VPS (api + postgres + caddy) | مثل مسیر فعلی ArtePos |

> فرانت React است چون چند ده صفحه و فرم وابسته به Workflow داریم؛ با Vanilla JS (مثل PersonalWallet) سریع شروع می‌شود ولی در این مقیاس نگه‌داری سخت می‌شود.

## ساختار Repository

```text
ARTE-SERVICE/
├── docs/
├── src/
│   ├── Arte.Api/            ← Endpoints, Auth, DI, BackgroundServices
│   └── Arte.Core/           ← Domain entities, EF DbContext, Migrations, Services, Templates (JSON)
├── tests/
│   └── Arte.Tests/          ← xUnit + Testcontainers (Postgres واقعی)
├── web/                     ← PWA (React + Vite)
├── deploy/                  ← docker-compose, Caddyfile
└── Arte.sln
```

دو پروژه کافی است (Modular Monolith). جدا کردن لایه‌ها فقط وقتی که واقعاً درد داشته باشیم.
داخل `Arte.Core` پوشه‌بندی بر اساس Feature: `Customers/`, `Cases/`, `Workflows/`, `Messaging/`, `Integrations/`.

## Multi-tenancy

- یک دیتابیس، ستون `TenantId` در همه جدول‌ها.
- `ITenantContext` از Claim توکن (`tid`) پر می‌شود.
- EF Core **Global Query Filter** روی `TenantId` برای همه موجودیت‌ها + تست خودکار که هر Entity جدید فیلتر داشته باشد.
- ایندکس‌ها همیشه با `TenantId` شروع می‌شوند.

## اصول

- **Timeline append-only:** هر Command که وضعیت را عوض می‌کند، در همان تراکنش `CaseEvent` (و در صورت لزوم `OutboxEvent` و `Message`) می‌نویسد.
- **Stage فقط از طریق Transition عوض می‌شود** (`CaseService.Transition(caseId, transitionId, reason)`)؛ هیچ endpointی `StageId` را مستقیم ست نمی‌کند.
- **Optimistic concurrency** روی Case (`xmin` در Postgres).
- مبالغ `long` به ریال؛ تاریخ‌ها `timestamptz` به UTC، نمایش شمسی در فرانت.
- API نسخه‌دار: `/api/v1/...`.

## API (نمای اولیه)

```text
POST /api/v1/auth/otp/request          POST /api/v1/auth/otp/verify
GET  /api/v1/me                        POST /api/v1/tenants        (onboarding از Template)

GET  /api/v1/customers?q=              POST /api/v1/customers
GET  /api/v1/customers/{id}            (با Assetها و Caseها)
POST /api/v1/customers/{id}/assets

GET  /api/v1/cases?stage=&category=&assignee=
POST /api/v1/cases                     (mobile + request → customer را پیدا/ایجاد می‌کند)
GET  /api/v1/cases/{id}                (شامل availableTransitions و primaryTransition)
POST /api/v1/cases/{id}/transitions/{transitionId}
POST /api/v1/cases/{id}/items          DELETE /api/v1/cases/{id}/items/{itemId}
POST /api/v1/cases/{id}/payments
POST /api/v1/cases/{id}/attachments
POST /api/v1/cases/{id}/approvals      GET/POST /p/approve/{token}   (عمومی)
POST /api/v1/cases/{id}/notes

GET  /api/v1/dashboard/attention
GET/PUT /api/v1/settings/workflow      GET/PUT /api/v1/settings/notifications
```

## امنیت

- OTP: ۵ رقمی، اعتبار ۲ دقیقه، حداکثر ۵ تلاش، hash شده در DB.
- لینک تأیید مشتری: توکن تصادفی ۱۲۸ بیتی، یک‌بار مصرف، انقضا ۴۸ ساعت.
- Webhook: امضای HMAC-SHA256.
- دسترسی: Role (`owner` / `supervisor` / `technician`) فقط پیش‌تنظیم است؛ بررسی‌ها همیشه روی Permission انجام می‌شود (`RequirePermission("cases.assign")`) تا استاد بتواند per نفر دسترسی بدهد. جزئیات در [03-domain-model](03-domain-model.md).
