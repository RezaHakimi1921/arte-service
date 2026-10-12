---
name: arte-planning
description: Plan a new Arte Service feature or a batch of requests before any code is written — research, a Persian plan document in docs/, phases, tests, open questions, then wait for the owner's approval. Use whenever the owner says «برنامه‌ریزی», «بررسی کن»، «بیار تو برنامه‌ها», asks to research a competitor or an idea, or brings several requests at once.
---

# Arte planning

The owner decides; code starts only after they say so («شروع کن», «پیاده‌سازی کن»). A plan is a document they read on a phone,
in Persian, short sentences, no jargon. It must be complete enough that nothing is discovered for the first time while coding.

## Steps

1. **Collect the requests.** Number every item the owner wrote, in their words. Nothing is dropped or merged silently;
   if two items overlap, say so in the plan.
2. **Read before planning.**
   - `CLAUDE.md` (rules), `docs/03-domain-model.md`, the latest `docs/NN-plan-*.md` and `docs/10-ideas-backlog.md`.
   - The code each item touches (endpoints, entities, screens). Note what already exists, so the plan builds on it.
3. **Research when asked or when the answer depends on the outside world** (competitors, platform limits, laws, providers).
   - Web search / pages; Context7 (`context7-mcp` skill) for library and platform docs.
   - Report facts with their source and date; mark guesses as guesses. Never copy a competitor's wording or rules.
   - Iran specifics matter: SMS patterns (Faraz), payment, sanctions (Google/Apple services, Firebase), mobile networks.
4. **Write `docs/NN-plan-<topic>.md`** (next free number) in Persian with these sections:
   - وضعیت (planning only, date in Jalali)
   - per item: what the owner wants · what exists today · the proposal (screens, rules, who sees what, permissions) ·
     data (tables/fields) · API · edge cases and abuse (rate limits, tenant isolation, file types/sizes, privacy) · tests
   - what this changes for: owner, supervisor, technician, customer
   - licence / pricing impact (is it an add-on?)
   - phases with rough size (کوتاه / متوسط / زیاد) and order, smallest useful piece first
   - «بعداً» (explicitly out of scope)
   - open questions the owner must answer, each with my recommendation
5. **Respect the project rules in the plan itself:** soft delete / deactivate, CaseEvent for case changes, permissions not
   roles, money in rials, Jalali dates, mobile first, Persian digits accepted, tests for every endpoint (happy path,
   permission denied, other tenant 404).
6. **Commit and push the plan** (docs only), send it with `SendUserFile`, and reply in Persian: a short summary per item,
   the recommended order, and the open questions. Do not start implementation.
7. When approved: implement phase by phase; each phase follows the post-change checklist in `CLAUDE.md`
   (build → test → commit → push → deploy → health check → summary) and updates the plan's status line.
