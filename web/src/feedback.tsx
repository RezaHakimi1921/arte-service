import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from "react";

type Toast = { id: number; text: string; kind: "ok" | "error" };
type Ctx = { notify: (text: string, kind?: "ok" | "error") => void };

const FeedbackContext = createContext<Ctx>({ notify: () => {} });

export const useFeedback = () => useContext(FeedbackContext);

/**
 * Clear outcome for every important action ("ذخیره شد") and a visible banner when the connection drops,
 * because workshop internet is unreliable.
 */
export function FeedbackProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const online = useOnline();

  const notify = useCallback((text: string, kind: "ok" | "error" = "ok") => {
    const id = Date.now() + Math.random();
    setToasts((t) => [...t.slice(-2), { id, text, kind }]);
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), kind === "error" ? 5000 : 2500);
  }, []);

  return (
    <FeedbackContext.Provider value={{ notify }}>
      {!online && (
        <div className="offline-banner" role="alert">اینترنت قطع است. تغییرات ارسال نمی‌شوند؛ بعد از وصل شدن دوباره تلاش کنید.</div>
      )}
      {children}
      <div className="toasts" aria-live="polite">
        {toasts.map((t) => (
          <div key={t.id} className={`toast-pop ${t.kind}`} role={t.kind === "error" ? "alert" : "status"}>
            {t.kind === "ok" ? "✓ " : ""}{t.text}
          </div>
        ))}
      </div>
    </FeedbackContext.Provider>
  );
}

function useOnline() {
  const [online, setOnline] = useState(() => navigator.onLine);
  useEffect(() => {
    const up = () => setOnline(true);
    const down = () => setOnline(false);
    window.addEventListener("online", up);
    window.addEventListener("offline", down);
    return () => {
      window.removeEventListener("online", up);
      window.removeEventListener("offline", down);
    };
  }, []);
  return online;
}
