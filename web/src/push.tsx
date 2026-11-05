import { useEffect, useState } from "react";
import { api, ApiError } from "./api";
import { useFeedback } from "./feedback";

function keyBytes(base64Url: string) {
  const pad = "=".repeat((4 - (base64Url.length % 4)) % 4);
  const raw = atob((base64Url + pad).replace(/-/g, "+").replace(/_/g, "/"));
  return Uint8Array.from(raw, (c) => c.charCodeAt(0));
}

const supported = () => "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;

/** Turn push notifications on or off for this phone or browser. iOS needs the app added to the Home Screen first. */
export function PushToggle() {
  const { notify } = useFeedback();
  const [server, setServer] = useState<{ enabled: boolean; publicKey: string | null } | null>(null);
  const [on, setOn] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api<{ enabled: boolean; publicKey: string | null }>("/api/v1/push/config").then(setServer).catch(() => {});
    if (!supported()) return;
    navigator.serviceWorker.getRegistration().then((r) => r?.pushManager.getSubscription()).then((s) => setOn(!!s)).catch(() => {});
  }, []);

  if (!server?.enabled) return null;
  if (!supported()) {
    return <p className="hint">این مرورگر اعلان را پشتیبانی نمی‌کند. در آیفون ابتدا برنامه را به صفحه‌ی اصلی (Home Screen) اضافه کنید.</p>;
  }

  async function toggle() {
    if (busy) return;
    setBusy(true);
    try {
      const reg = (await navigator.serviceWorker.getRegistration()) ?? (await navigator.serviceWorker.register("/sw.js"));
      await navigator.serviceWorker.ready;
      const existing = await reg.pushManager.getSubscription();
      if (existing) {
        await api("/api/v1/push/unsubscribe", { body: { endpoint: existing.endpoint } });
        await existing.unsubscribe();
        setOn(false);
        notify("اعلان‌ها خاموش شد");
        return;
      }
      if ((await Notification.requestPermission()) !== "granted") { notify("اجازه‌ی اعلان داده نشد؛ از تنظیمات مرورگر آن را فعال کنید.", "error"); return; }
      const sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(server!.publicKey!) });
      const json = sub.toJSON();
      await api("/api/v1/push/subscribe", { body: { endpoint: json.endpoint, p256dh: json.keys?.p256dh, auth: json.keys?.auth } });
      setOn(true);
      notify("اعلان‌ها روشن شد");
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "فعال‌سازی اعلان انجام نشد.", "error");
    } finally {
      setBusy(false);
    }
  }

  return (
    <label className="setting-row">
      <span><strong>اعلان روی این دستگاه</strong><span className="muted small">شروع کار، پایان کار، تحویل و رسید جدید مشتری را همان لحظه خبر می‌دهد.</span></span>
      <input type="checkbox" role="switch" className="switch" checked={on} disabled={busy} onChange={() => void toggle()} />
    </label>
  );
}
