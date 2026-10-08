# Roadmap: sign-up, licences, admin panel (۱۴۰۵/۰۷/۱۷)

Priorities set by the owner: **1. finish OTP → 2. licences → 3. branches (structure only for now).**

## 1. OTP and sign-in — done in this change, waiting for sms.ir template approval
- Self sign-up: verify mobile by SMS code → «ثبت‌نام» asks only for full name (shop name optional,
  defaults to «تعمیرگاه {name}») → straight into the app. No invite code (`Signup:RequireInviteCode=false`).
- Password login accepts the username **or** the mobile number.
- Forgotten password: SMS code → new password → signed in; every other session of that user ends.
- sms.ir adapter is deployed with the key; it switches on when the template is approved
  (`SMS_PROVIDER=smsir`, `OPEN_MODE=false`). No OTP on/off switch is needed in the admin panel.

## 2. Licences (next)
**Model**
- `Plan` (platform-level): 3 / 6 / 9 / 12 months, price in rials, active flag.
- `License` (per business): kind `trial | paid`, starts/ends, plan, source (`signup_trial | admin | payment`), who granted it.
  A business's access runs to the latest `EndsAt`. Never deleted; a wrong grant is revoked (soft delete).
- Every new sign-up gets a **14-day trial** automatically.

**Behaviour (proposed, to confirm)**
- Banner from 3 days before the end; after the end the business becomes **read-only**
  (see cases, record payments and deliveries, but no new intake) instead of locking people out of their data.
- Buying: first version is manual (owner pays, admin extends in the admin panel). An online gateway
  (Zarinpal / IDPay…) needs eNamad — added when that is ready.

**Admin panel (platform owner, separate from shop roles)**
- Businesses: list, status, licence end, usage; activate/deactivate.
- Licences: grant / extend / revoke; plans and prices.
- SMS credit and failed sends. Signup invite switch.
- Access: a platform-admin flag set from the server command line; its own audit log.

**First-run tour**
- A new business gets one **sample customer and case** (marked as sample) and a short guided tour on it:
  intake → case page → parts & labor → delivery. «حذف داده‌های نمونه» removes them (soft delete).

## 3. Branches (later — structure only)
Owner → brands → branches. Today's `Tenant` = one shop = **one branch**, and stays the isolation boundary.
Later: `Organization` (the owner's account) → `Brand` → `Branch` (= current tenant); an owner membership at
organization level sees all its branches, staff stay per branch. Nothing today may assume "one owner = one shop".

## Monitoring (after licences)
- **Sentry (sentry.io) blocks Iran**: from our server it answers 403. Use **GlitchTip** instead — self-hosted on our
  server, speaks the Sentry SDK protocol, for server and browser errors.
- **Microsoft Clarity** answers from the server, so recording can work; the dashboard may need a non-Iranian
  connection. It records screens, so it must run with **strict masking** (customer names, mobiles and amounts hidden).

## Decisions (۱۴۰۵/۰۷/۱۷, owner)
- Sign-up without a shop name → «تعمیرگاه {family name}», editable in Settings → اطلاعات کسب‌وکار.
- After the licence ends the branch becomes **read-only** (agreed).
- Admin panel: every **branch** has an **active** switch; the platform owner can switch a branch off completely.
- Licences are granted **per branch** (today's tenant = one branch).
- Payment: manual for now (no gateway until eNamad); the admin extends the licence.
- Proposed prices (toman): 1 month 1,000,000 · 3 months 2,500,000 · 6 months 5,000,000 · 9 months 7,500,000 · 12 months 10,000,000.
  Prices live in the admin panel (editable), not in code.
- Monitoring after licences: **Bugsink** (Sentry-SDK compatible, one container) for errors; no Elasticsearch —
  the server has 2 cores / 4 GB with ~1.4 GB free, and Elasticsearch alone wants 1–2 GB.
