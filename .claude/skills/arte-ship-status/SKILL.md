---
name: arte-ship-status
description: After any Arte Service code change, always tell the owner in Persian whether they can test now, whether a PR exists, and whether deploy is needed. Use at the end of every implementation, fix, or "done" reply for this repo.
---

# Arte ship status (الزامی در پایان کار)

بعد از هر تغییر کد در Arte Service (حتی اگر PR نزدی)، **در همان پیام پایانی** این بلوک را به فارسی بگو — کوتاه، صریح، بدون تعارف:

```text
وضعیت انتشار
- تست الان؟  [نه — هنوز روی سرور نیست / بله — روی سرور/لوکال آماده]
- PR؟         [نزده‌ام / لینک: … / merge شده: …]
- Deploy؟     [لازم نیست / لازم — فقط API / لازم — فقط web / لازم — API + web]
- قدم بعدی:   [یک خط]
```

## قواعد

1. اگر کد فقط روی ماشین لوکال است → **تست الان؟ نه** (مگر صریحاً لوکال راه‌اندازی شده باشد).
2. اگر PR نزدی و کاربر انتظار انتشار دارد → در همان جواب بگو «PR نزده‌ام» و بپرس/پیشنهاد بده بزنی؛ پنهان نکن.
3. تغییر `src/` یا `deploy/` معمولاً **Deploy لازم — API**؛ تغییر فقط `web/` یا `web/dist` → **web**؛ هر دو → **API + web**.
4. تا merge + deploy روی سرور انجام نشده، نگو «برو تست کن روی production».
5. بعد از deploy موفق، معیار چک را بگو (مثلاً `/api/v1/me` → 401، یا `smartx-test`).
6. این اسکیل را همراه چک‌لیست `CLAUDE.md` (build → test → commit → PR → deploy) بخوان؛ جایگزین آن نیست — فقط اعلام وضعیت به مالک را اجباری می‌کند.

## نمونه

```text
وضعیت انتشار
- تست الان؟  نه — هنوز روی سرور نیست
- PR؟         https://github.com/…/pull/18
- Deploy؟     لازم — فقط API (بعد از merge)
- قدم بعدی:   merge کن؛ بعد روی سرور docker compose build/up api
```
