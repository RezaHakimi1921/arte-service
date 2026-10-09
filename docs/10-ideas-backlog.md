# Ideas backlog (R&D, ۱۴۰۵/۰۷/۱۷)

Everything found during R&D that we may build later. Nothing here is scheduled; the owner picks.
Priorities already decided live in `09-roadmap-signup-license-admin.md` (licence → branches).

---

## 0. NEXT (owner, ۱۴۰۵/۰۷/۱۸): customer tracking link

When a case is opened (and on key changes) the customer gets an SMS with a link to a public, read-only page
for **their case only**, showing where the work is: stages done and current (as the technician/master moves it),
parts bought, labor, totals and payments, promised delivery, photos the shop chooses to share.
- The link carries a long random token per case (not the case id); it can be revoked/rotated; no customer login.
- The manager chooses in Settings which sections the customer sees; some are always shown (stage, promise, total).
- SMS on: case opened (link), ready for delivery, delivered (with warranty); each event switchable per business.
- Builds on: Faraz pattern sending (needs one pattern per message type), and the GT-Car / Odoo ideas below
  (status SMS, shareable quote/invoice, customer approval from the link, periodic service reminder).

## 0b. SMS credit per business (owner, ۱۴۰۵/۰۷/۱۸)
Customer SMS is paid from Arte's Faraz credit today. Next: each business buys an SMS package (sold with or beside the
licence), a per-business SMS balance decremented per message part, low-balance warning, and sending stops at zero.
Admin panel: grant/sell SMS packages, see usage per business.

## 0c. Customer satisfaction survey (owner, ۱۴۰۵/۰۷/۱۸ — to be designed together, sold as a licence option)
A short survey link (sent with or instead of the «delivered» SMS) with a few questions; results become manager
reports: satisfaction per technician, per service, over time. Open design questions in the chat summary.

## 1. From the main competitor: GT-Car (gt-car.ir)

What they sell: cloud software for any vehicle business (repair, car wash, oil change, body shop, tyres, tuning,
mobile service). **990,000 toman/month**, a yearly plan (price not shown on the page), **14-day free trial that starts
when the first customer is entered** (not at sign-up), eNamad, support helps import existing data.

**They have, we don't (candidates):**
| Idea | Why it matters | Notes for us |
|---|---|---|
| Automatic SMS to the customer on status change («آماده‌ی تحویل است») | Removes most «ماشینم آماده شد؟» calls | Needs a working SMS line (see §6); one template per status; per-business on/off; counts against an SMS quota per licence |
| Periodic service reminder (oil change by km / months) | Brings customers back | Store next-service km/date on the vehicle at delivery; daily job sends reminders |
| Parts inventory (stock goes down with each case) | Shops lose money on missing parts | Catalog item + stock movements; low-stock list; links to cost price we already have |
| Import customers/vehicles from Excel | Removes the switching barrier | CSV/XLSX upload with a preview and per-row errors |
| Trial starts at first real customer | Users don't lose trial days while exploring | Our sample case must not start the clock |
| One landing page per trade (car wash, oil change…) | Search traffic | Marketing site, not the app |

**We have, they don't advertise (keep and sell on these):** team workflow (assign to an apprentice, master's review,
wait reasons), photos of the work, staff pay and commission, motorcycle-specific catalog and plates, mobile-first UI,
reports of staff pay.

---

## 2. From Odoo Repairs (open-source ERP)

| Idea | What it is | Fit for Arte |
|---|---|---|
| **Under warranty** | A come-back case flagged «زیر ضمانت»: its parts and labor are automatically billed 0 | High — we already have warranty days and reopen/come-back |
| **Removed (old) parts** | Record the part taken out and whether it was handed back to the customer | High — builds trust («قطعه‌ی کهنه تحویل شد») |
| **Shareable quote and invoice** | A link or PDF to send by WhatsApp/SMS; the customer approves the quote from the link | High — also replaces the customer-approval stage |
| **Priority and tags** | «فوری», «VIP», «بیمه» on a case; filter by them | Medium |
| **Parts availability** | When a part is added, show if it is in stock / reserve it | After inventory (§1) |

---

## 3. CRM for Arte itself (later — owner said: not now, keep in mind)

Arte's customers are the businesses that sign up, and their licence, trial end and usage are already in our database,
so the CRM belongs **inside the platform admin panel**, not in a separate product.
- Per business: contact person and phones, timeline of **calls** (in/out, result, next follow-up date), notes,
  licence history, last activity, cases per week.
- «Who to call today»: trial ends in ≤ 3 days, licence ends soon, no activity for 7 days, new sign-up yesterday.
- Later: tags (lead source, city), simple pipeline (trial → paying → renewed / lost).
- If a separate tool is ever preferred: **EspoCRM** (light, self-hosted, calls and history) — but it would not see licences and usage.

---

## 4. Licence, admin panel, pricing (decided — see 09)
- Plans 1/3/6/9/12 months, prices editable in the admin panel. Proposed by owner: 1M / 2.5M / 5M / 7.5M / 10M toman.
  Note: 3–12 months all cost the same per month (833K); a stepped discount (e.g. 900K → 750K/month) rewards longer plans.
  Second branch onward could get a discount.
- Licence per branch; branch active switch; read-only after expiry; manual payment until a gateway (needs eNamad).

## 5. Branches (structure only — see 09 and 03)
Owner → brands → branches; today's tenant = one branch.

---

## 6. SMS / OTP

**Chosen (۱۴۰۵/۰۷/۱۸): Faraz SMS / Iran Payamak** — accepts individuals without eNamad; OTP pattern `XBk5FjbVlz`
approved. Adapter `IranPayamakProvider` (POST https://api.iranpayamak.com/ws/v1/sms/pattern, header `Api-Key`).
Server settings: `SMS_PROVIDER=iranpayamak`, `IRANPAYAMAK_API_KEY`, `IRANPAYAMAK_LINE`, `IRANPAYAMAK_OTP_PATTERN`,
`IRANPAYAMAK_OTP_PARAM`.

### History: sms.ir needs a business licence for the OTP template

Status (۱۴۰۵/۰۷/۱۷): the sms.ir adapter is built and deployed, but sms.ir will not approve the template without a
business licence (مجوز کسب‌وکار). Until then: password login (username or mobile), open demo mode.
Options to evaluate:
- **Bale OTP (safir.bale.ai)** — free, code delivered inside the Bale app (no SMS fallback); check its registration requirements.
- Other SMS panels that accept an individual (e.g. a personal shared service line); compare requirements, not just price.
- Voice OTP (some panels read the code by phone call) — check whether it has the same licence rule.
- Getting eNamad / a business licence also unlocks a payment gateway (§4).

## 7. Monitoring
- **Done:** Bugsink at https://error.artepersia.com (server + browser errors, no personal data).
- Later: **Microsoft Clarity** session recordings with strict masking (names, mobiles, amounts hidden); its dashboard may need a non-Iranian connection.
- Not now: Elasticsearch / SigNoz — too heavy for the server (2 cores, 4 GB, ~1.4 GB free).

## 8. Onboarding
- **Done:** sample case + 9-step tour; replay from Settings.
- Later: a short tour per new feature («چه چیزی تازه است»); a checklist on the home screen for the first week
  (add a staff member, set prices, first real intake).
