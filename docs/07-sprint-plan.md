# 07 — Sprint Plan

**تیم:** ۱ توسعه‌دهنده (رضا) + Claude Code
**طول اسپرینت:** ۲ هفته (شنبه تا پنجشنبه)
**فرض ظرفیت:** ~۲۰ ساعت مفید در هفته → ۴۰ ساعت per اسپرینت؛ برنامه‌ریزی روی **۷۵٪ = ۳۰ ساعت**.
(اگر ساعت واقعی فرق دارد، فقط این عدد را عوض کن؛ برآوردها به ساعت است.)

## نقشه راه MVP

| اسپرینت | تاریخ | هدف (یک جمله) |
|---------|-------|---------------|
| S0 Discovery | موازی با S1 | Workflow واقعی ۵ موتورسازی را دیده و Template را تأیید کرده‌ایم |
| S1 Foundation | ۱۱ تا ۲۳ مهر ۱۴۰۵ | کارمند با OTP وارد کسب‌وکارش می‌شود و مشتری ثبت/جستجو می‌کند |
| S2 Case Core | ۲۵ مهر تا ۷ آبان | پرونده در ۲۰ ثانیه باز می‌شود و از Workflow موتورسازی عبور می‌کند |
| S3 Money | ۹ تا ۲۱ آبان | قطعه، خدمت و اجرت ثبت و مانده پرداخت همیشه معلوم است |
| S4 Communication | ۲۳ آبان تا ۵ آذر | مشتری با SMS از آماده‌شدن و هزینه مطلع می‌شود و آن را تأیید می‌کند |
| S5 Attention + Pilot | ۷ تا ۱۹ آذر | مدیر در یک صفحه می‌بیند چه چیزی توجه می‌خواهد؛ اولین مغازه Pilot شروع می‌کند |

**Gate بعد از S2:** اگر نتایج Discovery با Template فرضی فرق داشت، S3 با اصلاح Template شروع می‌شود، نه با فیچر جدید.

---

## Sprint 1: Foundation

**تاریخ:** شنبه ۱۱ مهر ۱۴۰۵ — پنجشنبه ۲۳ مهر ۱۴۰۵ | **تیم:** ۱ نفر + Claude
**Sprint Goal:** کارمند یک موتورسازی با OTP وارد کسب‌وکار خودش می‌شود و مشتری و موتورش را ثبت و جستجو می‌کند، روی گوشی.

### Capacity
| نفر | روز در دسترس | ظرفیت | یادداشت |
|-----|-------------|-------|---------|
| رضا | ۱۰ از ۱۲ | ۳۰ ساعت (۷۵٪ از ۴۰) | ۲ روز برای مصاحبه‌های Discovery |
| **جمع** | | **۳۰ ساعت** | |

### Sprint Backlog
| اولویت | آیتم | برآورد | وابستگی |
|--------|------|--------|---------|
| P0 | اسکلت Solution (`Arte.Api`, `Arte.Core`, `Arte.Tests`)، Docker Compose با Postgres، EF Core + اولین Migration | ۳h | — |
| P0 | Multi-tenancy: `Tenant`, `ITenantContext`, Global Query Filter + تست ایزوله‌بودن Tenantها | ۴h | اسکلت |
| P0 | `User`, `Membership` با Role + Permission per نفر، OTP (درخواست/تأیید، rate limit)، JWT + Refresh | ۶h | Tenancy |
| P1 | دعوت شاگرد با شماره موبایل + تعیین Role/Permission توسط استاد | ۲h | Membership |
| P0 | `ISmsProvider` + `FakeSmsProvider` (فقط OTP) | ۱.۵h | — |
| P0 | Onboarding: ساخت Tenant از Template موتورسازی (Workflow/Stage/Transition seed می‌شود، UI تنظیم ندارد) | ۳h | Domain |
| P0 | Customer API: ایجاد، جستجو (نام / موبایل / ۴ رقم آخر)، پروفایل | ۳h | Tenancy |
| P0 | Asset API: ایجاد و اتصال به مشتری (`Attributes` jsonb) | ۲h | Customer |
| P1 | PWA اسکلت: Vite + React + TS، RTL، فونت فارسی، تم روشن/تیره، Bottom Nav، manifest | ۴h | — |
| P1 | صفحه‌های ورود OTP، لیست/جستجوی مشتری، پروفایل مشتری + افزودن موتور | ۴h | APIها |
| P2 | CI ساده (build + test روی GitHub Actions) | ۱.۵h | اسکلت |

**بار برنامه‌ریزی‌شده:** P0 = ۲۲.۵h، P1 = ۱۰h → ۳۲.۵h (۱۰۸٪ از ۳۰h) — **بیش از ظرفیت.** | P2 کشش است.
**از اول کنار می‌گذاریم:** صفحه پروفایل مشتری به S2 منتقل می‌شود (−۲h) و P2 فقط اگر وقت ماند.

### Risks
| ریسک | اثر | راه‌حل |
|------|-----|--------|
| Provider پیامک انتخاب نشده | OTP واقعی کار نمی‌کند | `FakeSmsProvider` در Dev؛ Provider واقعی تا قبل از S4 لازم است |
| مصاحبه‌ها وقت توسعه را می‌گیرد | اسپرینت عقب می‌افتد | ۲ روز در ظرفیت کم شده؛ مصاحبه‌ها ساعت شلوغ مغازه |
| نشت داده بین Tenantها | امنیتی و جدی | تست خودکار Global Query Filter برای همه Entityها از روز اول |
| Over-engineering معماری | سرعت کم | دو پروژه، بدون CQRS/MediatR/Repository اضافه |

### Definition of Done
- [ ] کد روی `main` با پیام commit روشن
- [ ] تست‌ها سبز (`dotnet test`)، از جمله تست ایزوله‌بودن Tenant
- [ ] روی عرض گوشی (۳۷۵px) و هر دو تم چک شده
- [ ] اسناد `docs/` اگر مدل عوض شد به‌روز شده

### Key Dates
| تاریخ | رویداد |
|-------|--------|
| شنبه ۱۱ مهر | شروع اسپرینت |
| پنجشنبه ۱۶ مهر | چک میانه + ۲–۳ مصاحبه اول انجام شده |
| پنجشنبه ۲۳ مهر | پایان، دمو روی گوشی |
| جمعه ۲۴ مهر | Retro + بازبینی Template با نتایج Discovery |

---

## Backlog اسپرینت‌های بعدی (برآورد اولیه)

### S2 Case Core
- ایجاد پرونده سریع: موبایل → مشتری (پیدا/ایجاد) → موتور (انتخاب/جدید) → شرح مشکل → ثبت
- `CaseService.Transition` + availableTransitions/primaryTransition + Optimistic concurrency
- Timeline (CaseEvent append-only) و UI آن
- Assignment به کارمند؛ لیست پرونده‌ها با فیلتر Stage/Category/«مال من»
- صفحه پرونده Workflow-aware با دکمه «اقدام بعدی»
- یادداشت و عکس (Attachment)

### S3 Money
- CaseItem (product/service/labor، عنوان آزاد یا از کاتالوگ)، کاتالوگ ساده
- قطعه مشتری (`Supplier = customer`) و لیست قطعه‌های لازم (`needed → used`)
- بیعانه
- Payment، مانده، هشدار نسیه هنگام تحویل
- قبض/فاکتور قابل اشتراک (لینک یا تصویر)
- گزارش فروش و دریافتی روزانه

### S4 Communication
- انتخاب و پیاده‌سازی Provider واقعی پیامک، ثبت الگوها
- Message + SmsDispatcher + Delivery Report
- تنظیم «کدام Stage پیامک دارد»
- ApprovalRequest + صفحه عمومی تأیید/رد
- Outbox + Webhook برای سیستم بیرونی نظرسنجی/باشگاه

### S5 Attention + Pilot
- داشبورد Attention: باز، آماده تحویل، منتظر تأیید، منتظر قطعه، گیرکرده (> N روز در Stage)، فروش امروز
- تنظیم ساده Workflow (تغییر نام، رنگ، فعال/غیرفعال Stage)
- مدیریت کارکنان و نقش‌ها
- استقرار Production، بکاپ خودکار DB
- Onboarding اولین مغازه Pilot و اندازه‌گیری «% پرونده‌های end-to-end در Arte»
