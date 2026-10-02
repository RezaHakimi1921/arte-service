// The access token lives only in memory. The refresh token is an httpOnly cookie the page cannot read,
// so an injected script cannot steal a long-lived session.

export type Membership = { tenantId: string; tenantName: string; role: string };
export type Session = { accessToken: string; expiresAt: string; tenantId: string | null; memberships: Membership[] };

export class ApiError extends Error {
  constructor(public status: number, message: string, public fields: Record<string, string[]> = {}, public body: Record<string, unknown> | null = null) {
    super(message);
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
  const body = text ? JSON.parse(text) : null;
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

export async function api<T = unknown>(path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  const send = () =>
    fetchOrOffline(path, {
      method: init.method ?? (init.body === undefined ? "GET" : "POST"),
      credentials: "same-origin",
      headers: {
        ...(init.body === undefined ? {} : { "Content-Type": "application/json" }),
        ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      },
      body: init.body === undefined ? undefined : JSON.stringify(init.body),
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
  return parse(res) as Promise<T>;
}
