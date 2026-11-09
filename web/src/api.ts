// The access token lives only in memory. The refresh token is an httpOnly cookie the page cannot read,
// so an injected script cannot steal a long-lived session.

export type Membership = { tenantId: string; tenantName: string; role: string };
export type Session = { accessToken: string; expiresAt: string; tenantId: string | null; memberships: Membership[] };

export class ApiError extends Error {
  constructor(public status: number, message: string, public fields: Record<string, string[]> = {}, public body: Record<string, unknown> | null = null) {
    super(message);
  }
}

/**
 * The server is down or being replaced (a deploy): the proxy answers 502/503/504 with no JSON body, or the connection
 * fails while the phone itself is online. A 5xx WITH a JSON problem body is the API itself answering (for example 503
 * «ارسال پیامک انجام نشد»), so it keeps its own message. Not for 4xx, rate limiting or an offline phone either.
 */
export function isServerDown(e: unknown): boolean {
  if (!(e instanceof ApiError)) return false;
  if (e.status === 0) return navigator.onLine;
  return (e.status === 502 || e.status === 503 || e.status === 504) && e.body === null;
}

/** True when the server answers at all; used to know when an upgrade is over. A proxy failure has no JSON body. */
export async function serverIsUp(): Promise<boolean> {
  try {
    const res = await fetch("/api/v1/public/info", { cache: "no-store", credentials: "same-origin" });
    const json = (res.headers.get("content-type") ?? "").includes("json");
    return !([502, 503, 504].includes(res.status) && !json);
  } catch {
    return false;
  }
}

let accessToken: string | null = null;
let refreshing: Promise<Session | null> | null = null;
let onSignedOut: () => void = () => {};

export function setSignedOutHandler(fn: () => void) {
  onSignedOut = fn;
}

export function applySession(s: Session | null) {
  accessToken = s?.accessToken ?? null;
}

async function parse(res: Response) {
  if (res.status === 204) return null;
  const text = await res.text();
  let body: any = null;
  try { body = text ? JSON.parse(text) : null; } catch { body = null; }
  if (!res.ok) {
    const fields: Record<string, string[]> = body?.errors ?? {};
    const message = body?.title ?? Object.values(fields)[0]?.[0] ?? "خطا در ارتباط با سرور";
    throw new ApiError(res.status, message, fields, body);
  }
  return body;
}

/**
 * Null only when the session is really gone (401). Rate limiting or a dropped connection throws instead,
 * so a busy moment or bad signal never looks like being logged out.
 */
export function refresh(): Promise<Session | null> {
  refreshing ??= fetchOrOffline("/api/v1/auth/refresh", { method: "POST", credentials: "same-origin" })
    .then(async (res) => {
      if (res.status === 401) return null;
      if (!res.ok) throw new ApiError(res.status, res.status === 429 ? "درخواست‌ها زیاد است؛ چند ثانیه بعد دوباره تلاش کنید." : "خطا در ارتباط با سرور");
      const s = (await res.json()) as Session;
      applySession(s);
      return s;
    })
    .finally(() => {
      refreshing = null;
    });
  return refreshing;
}

/** A dropped connection becomes a clear Persian error instead of a raw TypeError. */
async function fetchOrOffline(input: string, init: RequestInit): Promise<Response> {
  try {
    return await fetch(input, init);
  } catch {
    throw new ApiError(0, navigator.onLine
      ? "ارتباط با سرور برقرار نشد. دوباره تلاش کنید."
      : "اینترنت قطع است. تغییرات ذخیره نشد؛ بعد از وصل شدن دوباره تلاش کنید.");
  }
}

/** Authenticated request; refreshes the access token once on a 401. A FormData body is sent as multipart. */
async function authorized(path: string, init: { method?: string; body?: unknown }): Promise<Response> {
  const form = init.body instanceof FormData;
  const send = () =>
    fetchOrOffline(path, {
      method: init.method ?? (init.body === undefined ? "GET" : "POST"),
      credentials: "same-origin",
      headers: {
        ...(init.body === undefined || form ? {} : { "Content-Type": "application/json" }),
        ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      },
      body: init.body === undefined ? undefined : form ? (init.body as FormData) : JSON.stringify(init.body),
    });

  let res = await send();
  if (res.status === 401 && !path.startsWith("/api/v1/auth/")) {
    const s = await refresh(); // throws on network/rate-limit: the caller shows the error, the user stays signed in
    if (!s) {
      applySession(null);
      onSignedOut();
      throw new ApiError(401, "نشست شما تمام شده است. دوباره وارد شوید.");
    }
    res = await send();
  }
  return res;
}

export async function api<T = unknown>(path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  return parse(await authorized(path, init)) as Promise<T>;
}

/** Protected files (case photos) need the bearer token, so they are fetched as blobs rather than plain <img src>. */
export async function apiBlob(path: string): Promise<Blob> {
  const res = await authorized(path, {});
  if (!res.ok) await parse(res);
  return res.blob();
}
