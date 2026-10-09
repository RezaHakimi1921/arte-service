# Arte Service — project rules

Case-centric operations platform for service businesses. First vertical: motorcycle repair (موتورسازی).
Persian UI, `dir="rtl"`, mobile first. ASP.NET Core (.NET 10) minimal APIs + EF Core + Postgres (`src/`),
React + Vite PWA (`web/`). Production: https://service.artepersia.com (stack in `/home/cluadai/arte-service`).

Product and domain decisions live in `docs/` — read `docs/03-domain-model.md` before changing the model.

## Data rules (every entity)

1. **Nothing is hard-deleted from the UI or API.** Every user-created entity supports one of:
   - **Soft delete** (`ISoftDeletable`: `DeletedAt`, `DeletedBy`) with **restore**, for records (customers, assets, cases, items, payments…).
   - **Deactivate** (`IsActive`) for things that are referenced by history (staff, catalog items, stages).
   Pick deactivate when old records must keep pointing at it; otherwise soft delete. Hard delete only in maintenance scripts.
2. **Every entity is editable** after creation (PATCH), with a visible label above each pre-filled field in the UI.
3. Soft-deleted rows are hidden by the `soft_delete` named query filter; the trash view uses
   `IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter])` — **never** plain `IgnoreQueryFilters()`, which also drops tenant isolation.
4. Unique indexes on soft-deletable tables are partial (`WHERE "DeletedAt" IS NULL`) so a deleted record does not block re-creating it.
5. Every new tenant-owned entity implements `ITenantOwned`. The test `Every_tenant_owned_entity_has_a_query_filter` must stay green.
6. Every state change on a case writes a `CaseEvent` (append-only timeline) in the same transaction.
7. Money is `long` in **rials**; the UI shows toman. Dates are `timestamptz` UTC; the UI shows Jalali.

## Input rules

- **Mobile numbers: at most 11 digits** (`09xxxxxxxxx`) everywhere — use the `MobileInput` component (strips non-digits,
  converts Persian/Arabic digits, `maxLength=11`, `inputMode="numeric"`). The server normalizes with `Mobile.TryNormalize`.
- **Numbers are grouped by three** everywhere they are a quantity or amount (prices, km, counts, totals):
  show `۲۳٬۴۰۰`, never `23400`. Inputs use `NumberInput` (stores raw digits, shows grouped); display uses
  `formatNumber`. **Identifiers are never grouped**: mobile, plate, year, case number, VIN, codes.
- Amounts and counts: `inputMode="numeric"`. Inputs ≥ 16px font (iOS zoom).
- Vehicle intake order: type (سواری / شاسی‌بلند / ون / وانت / موتورسیکلت) → brand → model → model year
  (from `web/src/vehicles.ts`; "سایر" allows free text). Production years there are approximate.
- Fuel level colours run red (empty) → green (full) via `--fuel-0…4`.
- **Persian digits are accepted in every input** (mobile, plate, amounts, quantities, search): convert with
  `toLatinDigits` / `onlyDigits` before sending; the server only ever sees Latin digits.
- Long pick lists (brand, model) use the searchable `Combobox`; typed text is kept if not in the list.
- Plates: car plate `12ب345-11` (`PlateInput`), motorcycle plate `123-45678` (`MotoPlateInput`). The plate is optional.
- Reported problems («ایراد اعلامی») and requested services («خدمات درخواستی») are separate lists; services come only
  from the categorised list, never free text. Both depend on the vehicle kind (`serviceCategoriesFor(kind)`,
  `problemsFor(kind)`): a motorcycle never offers A/C or wheel alignment, and does offer chain, carburettor, CVT.
- **One picker pattern for dynamic lists** (assignee, staff, anything that grows): `SelectField` (looks like an input)
  opens `SelectSheet` (bottom sheet; search appears above 6 rows; optional group headings; current choice marked;
  optional "none" row). No native `<select>` and no ad-hoc lists for these. Short fixed choices with an explanation
  (wait reason, parts supplier) stay `SheetOption`s; static vocabularies (brand, model) stay `Combobox`.
- **Purchase price comes before sale price** in every form, and a shop item's purchase price may not exceed its sale
  price (checked inline on the client and enforced by the server). Customer-supplied parts are not checked.
- Photos: `PhotoSection` on the case (camera opens directly; compressed to 1600px JPEG on the client); the stage is
  recorded with the photo, so no extra workflow step is needed. Photos are served only through the authenticated API.
- Never copy a competitor's wording or rules; take ideas, write our own.
- **Loading is always visible**: every button that submits sets `disabled` + `aria-busy` while waiting (CSS draws a
  spinner) and says what it is doing («در حال ثبت پذیرش…»); every page that loads data shows `.splash` (spinner) until
  ready; navigation after a submit happens only when the server answered. Never a frozen button or a blank page.
- Light theme is the default; the theme is stored (`arte-theme`) only when the user picks one in Settings → ظاهر.
- Case numbers are shown as `CASE-123` (Latin, never grouped) everywhere, and search accepts that form.
- Settings is one calm grouped list (حساب، کسب‌وکار، فروش، مدیریت، سیستم); each row opens its own single-purpose page.
- Workflow steps that differ between shops (customer approval, master's final review…) are **business settings**
  that switch stages on/off via `WorkflowUpgrader`, never code branches per shop.
- **Every calendar and date is Persian (Jalali)**: display with `Intl.DateTimeFormat("fa-IR-u-ca-persian", …)`,
  pick dates with Jalali day chips (امروز / فردا / weekday + Jalali date), never a browser `<input type="date">`
  or a Gregorian calendar. Store UTC on the server. The only Gregorian exception is a vehicle's model year, when the user chooses «میلادی».
- Validate on the server, always; show the Persian error next to the field.
- Text fields have explicit max lengths in both the EF model and the validator.

## Security rules

- Authorization is by **permission**, not role (`RequirePermission(...)`). A member can never grant a permission they don't hold.
- Never return exception details; never log secrets, OTP codes (except the dev fake SMS provider) or passwords.
- Secrets come only from `deploy/.env` on the server (generated by `init-env.sh`); never commit them, never read them into chat.
- Open mode (`OPEN_MODE`) stays **off** in production unless the owner explicitly asks.
- Run `/security-review` before shipping auth, permission, or tenant-isolation changes.

## Post-change checklist (mandatory before saying "done")

```text
build → test → commit → push → deploy → health check → summary
```

1. `dotnet build` (and `npm run build` in `web/` when the UI changed). Fix all errors and warnings you introduced.
2. `dotnet test tests/Arte.Tests` (needs Docker Desktop running). Report PASS/FAIL with counts — no "looks fine".
   New endpoints/rules ship with tests in the same change: happy path, permission denied, other tenant gets 404.
3. Schema change → `dotnet ef migrations add <Name> -p src/Arte.Core -s src/Arte.Api -o Data/Migrations`
   (with `ASPNETCORE_ENVIRONMENT=Development`). Migrations apply on startup.
4. Commit with a clear message; push `main`.
5. Deploy when the user wants it live:
   - `git archive HEAD` + `web/dist` → scp to `~/arte-service` → extract **in place**.
   - `docker compose build api && docker compose up -d && docker compose restart web`
     (the web container bind-mounts `web/dist`; it must be restarted after the folder is replaced).
   - Check: `https://service.artepersia.com/` → 200, `/api/v1/me` → 401 unauthenticated, API log shows migrations applied.
6. UI changes: check phone width (375px) and both themes in the browser pane; say what still needs a real phone.

## Server notes

- The PersonalWallet stack owns ports 80/443; its Caddy forwards `service.artepersia.com` → `arte-web:80`.
  Arte publishes **no** host ports. On the shared network use unique aliases (`arte-api`, `arte-web`) — plain `api` is PersonalWallet's.
- Editing PersonalWallet's Caddyfile: back up, append **in place** (bind-mounted single file), `caddy validate`, then `caddy reload`.
- SSH: `ssh -i ~/.ssh/arte_service_deploy -p 2238 cluadai@78.39.51.105`. No sudo.
- **Errors (Bugsink, Sentry-compatible)**: https://error.artepersia.com — edge Caddy block forwards to `arte-bugsink:8000`.
  User `admin@artepersia.com`, password in `.env` → `BUGSINK_ADMIN`. The API reports via `Sentry:Dsn` (`SENTRY_DSN`,
  internal host `arte-bugsink`); browser errors go through `POST /api/v1/client-errors`, never straight to Bugsink.
- **IgnoreQueryFilters is query-wide**: ignoring the soft-delete filter on a joined set (to show a deleted customer's
  name) also un-filters the main set. Add `where c.DeletedAt == null` by hand in such queries.

## UX principles (from the product consultation, ۱۴۰۵/۰۷)

- "Every time I open the app I immediately know what to do now, and no vehicle or money is forgotten."
- **Home is a work queue, not statistics**: needs-action (each with a plain reason), ready for delivery,
  what is blocking work (`WaitReason`), due today; numbers last. Technicians land on «کارهای من».
- **Say why, not only the stage**: `statusText()` — «منتظر تأیید هزینه توسط مشتری»، «در حال عیب‌یابی توسط محمد».
  Colour is never the only signal; always text (and an icon for alerts).
- **Intake in three levels**: essentials (mobile or plate, vehicle, service) with «ثبت سریع» in under a minute;
  «تکمیل پذیرش» reveals mileage, fuel, body, items, assignee; vehicle details last. Anything can be completed later on the case.
- **Case page = command centre**: decision facts on top (customer + call, vehicle + plate, status + time, assignee,
  promise, later money), one big next action, the rest in collapsible sections.
- **Frequent quick actions use a bottom sheet** (`BottomSheet`/`SheetOption`, rows ≥ 48px): other stage moves, assign,
  wait reason, parts supplier, note, promise, later labor and payment.
- **Visual**: light neutral background by default, navy brand (`--accent`), green only for ready/success, orange for
  waiting/warning, red only for errors, serious delay or cancel. Plain cards, clear borders, large numbers. Dark mode for readability, not decoration.
- **Empty states teach** what the screen is for and offer the first action.
- **Unreliable internet**: offline banner, clear Persian network errors, «در حال ذخیره…», disabled buttons while busy,
  and a visible confirmation (toast) after every important action (stage change, assign, payment).

## UI rules

Same design rules as PersonalWallet (tokens in `:root` for both themes, motion tokens, no `letter-spacing` on Persian text,
`:hover` only under `(hover: hover)`, `dvh` not `vh`, safe-area padding, `prefers-reduced-motion/transparency/contrast`).
Use the `ui-ux-pro-max` skill for new screens.
