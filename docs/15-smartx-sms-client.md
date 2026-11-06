# 15 — کلاینت ارسال پیامک اسمارت‌ایکس (فاز ۱)

**وضعیت:** کد آماده است ولی هنوز به برنامه وصل نشده؛ تا نهایی شدن کار اسمارت، ارسال روی پنل قبلی می‌ماند (`Sms:Provider`).

کد در `src/Arte.Api/Sms/SmartX/` و تست‌ها در `tests/Arte.Tests/SmartX/` است. Arte فقط درخواست ارسال را به Partner API اسمارت می‌دهد (`POST /connect/token` و `POST /api/v1/notification/direct-send`) و اسمارت پیامک را می‌فرستد. صف، Provider، خط، وضعیت تحویل، موجودی و لغو ۱۱ پیاده نشده‌اند.

## تنظیمات (متغیر محیطی)

| نام | توضیح |
|---|---|
| `SmartXSms__BaseUrl` | staging: `https://staging-api.smartx.ir` ، production: `https://api.smartx.ir` |
| `SmartXSms__ClientId` | از بک‌آفیس اسمارت |
| `SmartXSms__ClientSecret` | از بک‌آفیس؛ فقط در `deploy/.env` روی سرور |
| `SmartXSms__TenantId` | شناسه تننت |
| `SmartXSms__SenderNumber` | خط ارسال (یک مقدار ثابت) |

اختیاری: `MaxAttempts` (۴)، `RetryBaseDelaySeconds` (۲)، `RequestTimeoutSeconds` (۳۰)، `ShouldCheckQuota/Consent/QuietHours` (هر سه `true`).

## رفتار

- **سقف ۱۰۰:** لیست بزرگ به بچ‌های حداکثر ۱۰۰ گیرنده شکسته می‌شود؛ هر بچ یک UUID (`correlationId`) تازه دارد.
- **ذخیره قبل از ارسال:** UUID و گیرندگان هر بچ قبل از اولین تماس شبکه‌ای در `ISmsBatchLog` ثبت می‌شود.
- **retry:** روی timeout، خطای شبکه، `408`، `429` و `5xx` با فاصلهٔ ۲، ۴، ۸ ثانیه (یا `Retry-After`) و **همیشه با همان UUID**. `401` یک بار با توکن تازه تکرار می‌شود. سایر `4xx` تکرار نمی‌شود (بچ `Failed`).
- اگر retryها تمام شود و خطا هنوز موقت باشد، بچ `Pending` می‌ماند و `ResumePendingAsync()` آن را با همان UUID می‌فرستد.
- موفق بودن = اسمارت درخواست را پذیرفت (HTTP 2xx)، نه رسیدن به گوشی.
- مستند اسمارت می‌گوید تکرار `correlationId` «ممکن است» idempotent باشد؛ برای همین UUID یک بچ هیچ‌وقت عوض نمی‌شود.
- شماره‌ها با `Mobile.TryNormalize` بررسی می‌شوند (ارقام فارسی هم پذیرفته است) و به شکل `98912…` فرستاده می‌شوند. شماره یا متن نامعتبر ارسال نمی‌شود و در `SendResult.Invalid` برمی‌گردد.
- متن هر گیرنده نهایی و آماده است (شخصی‌سازی قبل از فراخوانی)؛ این API الگو (template) ندارد.

## محدودیت‌ها و کارهای بعدی

- `ISmsBatchLog` فعلاً فقط در حافظه است (`InMemorySmsBatchLog`)؛ با ری‌استارت برنامه بچ‌های `Pending` از دست می‌روند. ذخیرهٔ پایدار در Postgres به یک entity و migration جدا نیاز دارد.
- `ISmsProvider` فعلی الگومحور است (`SendTemplateAsync`) ولی اسمارت متن نهایی می‌گیرد؛ وصل کردن به `Sms:Provider` نیاز به آداپتری دارد که الگو را به متن تبدیل کند.
- خط `989982008609` پیامک تبلیغاتی نمی‌فرستد؛ ایونت تبلیغاتی به خط جدا نیاز دارد.

## وصل کردن (وقتی اسمارت آماده شد)

1. در `Program.cs`: `builder.Services.AddSmartXSms(builder.Configuration);`
2. پنج متغیر بالا را در `deploy/.env` و `environment` سرویس `api` در `deploy/docker-compose.yml` اضافه کن (الان فقط `Sms__*` به کانتینر می‌رسد).
3. اول با staging و یک پیامک تست امتحان کن.
