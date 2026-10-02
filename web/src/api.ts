// The access token lives only in memory. The refresh token is an httpOnly cookie the page cannot read,
// so an injected script cannot steal a long-lived session.

export type Membership = { tenantId: string; tenantName: string; role: string };
export type Session = { accessToken: string; expiresAt: string; tenantId: string | null; memberships: Membership[] };

export class ApiError extends Error {
  constructor(public status: number, message: string, public fields: Record<string, string[]> = {}) {
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
    throw new ApiError(res.status, message, fields);
  }
  return body;
}

export function refresh(): Promise<Session | null> {
  refreshing ??= fetch("/api/v1/auth/refresh", { method: "POST", credentials: "same-origin" })
    .then(async (res) => {
      if (!res.ok) return null;
      const s = (await res.json()) as Session;
      applySession(s);
      return s;
    })
    .catch(() => null)
    .finally(() => {
      refreshing = null;
    });
  return refreshing;
}

export async function api<T = unknown>(path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  const send = () =>
    fetch(path, {
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
    const s = await refresh();
    if (!s) {
      applySession(null);
      onSignedOut();
      throw new ApiError(401, "نشست شما تمام شده است. دوباره وارد شوید.");
    }
    res = await send();
  }
  return parse(res) as Promise<T>;
}
