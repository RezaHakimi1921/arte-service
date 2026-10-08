// Sends uncaught browser errors to the API, which forwards them to error tracking (Bugsink).
// At most a few reports per page load, each message once; never any form data.

const sent = new Set<string>();
const MAX_PER_LOAD = 5;

function report(message: string, stack?: string) {
  if (!message || sent.size >= MAX_PER_LOAD || sent.has(message)) return;
  sent.add(message);
  try {
    void fetch("/api/v1/client-errors", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ message, stack, url: location.href.split("?")[0] }),
      keepalive: true,
    }).catch(() => {});
  } catch {
    // Reporting must never break the app.
  }
}

export function reportError(error: unknown) {
  if (import.meta.env.DEV) return;
  report(error instanceof Error ? error.message : String(error), error instanceof Error ? error.stack : undefined);
}

export function installErrorReporting() {
  if (import.meta.env.DEV) return;
  window.addEventListener("error", (e) => report(String(e.message || e.error || "error"), e.error?.stack));
  window.addEventListener("unhandledrejection", (e) => {
    const r = e.reason;
    // Expected API errors (validation, offline) are shown to the user already; only real bugs are reported.
    if (r && typeof r === "object" && "status" in r) return;
    report(r instanceof Error ? r.message : String(r), r instanceof Error ? r.stack : undefined);
  });
}
