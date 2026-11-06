import { useEffect, useRef, useState } from "react";
import { ApiError, api, apiBlob } from "./api";
import { useFeedback } from "./feedback";
import { BottomSheet, SheetOption } from "./sheet";
import { formatNumber } from "./ui";

export type CasePhoto = { id: string; stageKey: string; caption: string | null; createdAt: string; visibleToCustomer: boolean; isVideo?: boolean };

const MAX_VIDEO_BYTES = 30 * 1024 * 1024;

const STAGE_HINT: Record<string, string> = {
  received: "پذیرش", diagnosing: "عیب‌یابی", awaiting_approval: "تأیید", awaiting_parts: "انتظار قطعه",
  repairing: "حین تعمیر", review: "بازبینی", ready: "آماده تحویل", delivered: "تحویل",
};

/** Shrink to at most 1600px on the long side and re-encode as JPEG; also strips the camera's metadata. */
export async function compress(file: File): Promise<Blob> {
  const bitmap = await createImageBitmap(file).catch(() => null);
  if (!bitmap) return file;
  const scale = Math.min(1, 1600 / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement("canvas");
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  canvas.getContext("2d")!.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  return new Promise((resolve) => canvas.toBlob((b) => resolve(b ?? file), "image/jpeg", 0.82));
}

/** Loads a protected photo with the bearer token and shows it from a blob URL. */
function Thumb({ photo, onOpen }: { photo: CasePhoto; onOpen: (url: string) => void }) {
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    let alive = true;
    let made: string | null = null;
    apiBlob(`/api/v1/attachments/${photo.id}`)
      .then((b) => { made = URL.createObjectURL(b); if (alive) setUrl(made); else URL.revokeObjectURL(made); })
      .catch(() => alive && setFailed(true));
    return () => { alive = false; if (made) URL.revokeObjectURL(made); };
  }, [photo.id]);
  return (
    <button type="button" className="photo-thumb" onClick={() => url && onOpen(url)} aria-label={`${photo.isVideo ? "فیلم" : "عکس"} ${STAGE_HINT[photo.stageKey] ?? ""}`} aria-busy={!url && !failed}>
      {url ? (photo.isVideo ? <video src={url} muted playsInline preload="metadata" /> : <img src={url} alt="" />) : <span className="muted small">{failed ? "!" : ""}</span>}
      {photo.visibleToCustomer && <span className="photo-shared" title="مشتری این عکس را می‌بیند">مشتری</span>}
      <span className="photo-tag small">{STAGE_HINT[photo.stageKey] ?? ""}</span>
    </button>
  );
}

/**
 * Photo evidence on a case without extra workflow steps: anyone working on the case can add a photo at any
 * stage (the camera opens directly on phones); the stage it was taken in is stored with it, so the master
 * sees "work done" photos during review and "delivery" photos afterwards.
 */
export function PhotoSection({ caseId, photos, stageKey, canAdd, onChange, visibleByDefault }: {
  caseId: string; photos: CasePhoto[]; stageKey: string; canAdd: boolean; onChange: () => void; visibleByDefault: boolean;
}) {
  // Whether the next photos are shown on the customer's tracking page; starts from the business setting.
  const [share, setShare] = useState(visibleByDefault);
  const { notify } = useFeedback();
  const input = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [viewing, setViewing] = useState<{ photo: CasePhoto; url: string } | null>(null);
  const [busy, setBusy] = useState(false);

  async function upload(files: FileList | null) {
    if (!files || files.length === 0) return;
    setUploading(true);
    try {
      for (const file of Array.from(files).slice(0, 6)) {
        const form = new FormData();
        if (file.type.startsWith("video/")) {
          // Videos go as recorded: keep clips short (about 30 seconds); the server refuses anything over 30 MB.
          if (file.size > MAX_VIDEO_BYTES) { notify("فیلم بزرگ‌تر از ۳۰ مگابایت است؛ کوتاه‌تر ضبط کنید.", "error"); continue; }
          form.append("file", file, file.name || "video.mp4");
        } else form.append("file", await compress(file), "photo.jpg");
        form.append("visibleToCustomer", share ? "true" : "false");
        await api(`/api/v1/cases/${caseId}/attachments`, { body: form });
      }
      notify(files.length > 1 ? `${formatNumber(files.length)} فایل اضافه شد` : "اضافه شد");
      onChange();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "بارگذاری عکس انجام نشد", "error");
    } finally {
      setUploading(false);
      if (input.current) input.current.value = "";
    }
  }

  async function toggleShare(photo: CasePhoto, visible: boolean) {
    setBusy(true);
    try {
      await api(`/api/v1/attachments/${photo.id}`, { method: "PATCH", body: { visibleToCustomer: visible } });
      setViewing((v) => (v ? { ...v, photo: { ...v.photo, visibleToCustomer: visible } } : v));
      notify(visible ? "مشتری این عکس را می‌بیند" : "عکس از صفحه‌ی مشتری برداشته شد");
      onChange();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "خطا", "error");
    } finally {
      setBusy(false);
    }
  }

  async function remove(photo: CasePhoto) {
    setBusy(true);
    try {
      await api(`/api/v1/attachments/${photo.id}`, { method: "DELETE" });
      setViewing(null);
      notify("عکس حذف شد");
      onChange();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "خطا", "error");
    } finally {
      setBusy(false);
    }
  }

  const hint = stageKey === "repairing" ? "قبل از فرستادن برای بازبینی، از کار انجام‌شده عکس بگیرید."
    : stageKey === "ready" ? "هنگام تحویل از وضعیت وسیله عکس بگیرید." : null;

  return (
    <section className="card photos" aria-label="عکس‌ها" data-tour="photos">
      <div className="photos-head">
        <h3>عکس‌ها{photos.length > 0 && <span className="muted small font-num"> ({formatNumber(photos.length)})</span>}</h3>
        {canAdd && (
          <button type="button" onClick={() => input.current?.click()} disabled={uploading} aria-busy={uploading}>
            {uploading ? "در حال بارگذاری…" : "📷 عکس یا فیلم"}
          </button>
        )}
      </div>
      <input ref={input} type="file" accept="image/*,video/*" capture="environment" multiple hidden onChange={(e) => upload(e.target.files)} />
      {canAdd && (
        <label className="photo-share">
          <input type="checkbox" checked={share} onChange={(e) => setShare(e.target.checked)} />
          <span>عکس‌های بعدی را مشتری هم در لینک پیگیری ببیند</span>
        </label>
      )}
      {hint && canAdd && <p className="hint">{hint}</p>}
      {photos.length > 0 ? (
        <div className="photo-grid">
          {photos.map((p) => <Thumb key={p.id} photo={p} onOpen={(url) => setViewing({ photo: p, url })} />)}
        </div>
      ) : (
        !hint && <p className="muted small">هنوز عکسی ثبت نشده است.</p>
      )}
      <BottomSheet open={!!viewing} title={viewing ? `عکس ${STAGE_HINT[viewing.photo.stageKey] ?? ""}` : "عکس"} onClose={() => setViewing(null)}>
        {viewing && (
          <>
            {viewing.photo.isVideo
              ? <video className="photo-full" src={viewing.url} controls playsInline />
              : <img className="photo-full" src={viewing.url} alt={viewing.photo.caption ?? ""} />}
            <p className="muted small">{new Intl.DateTimeFormat("fa-IR-u-ca-persian", { dateStyle: "medium", timeStyle: "short" }).format(new Date(viewing.photo.createdAt))}</p>
            {!viewing.photo.isVideo && <label className="setting-row">
              <span><strong>مشتری این عکس را ببیند</strong><span className="muted small">در صفحه‌ی پیگیری مشتری نشان داده می‌شود.</span></span>
              <input type="checkbox" role="switch" className="switch" checked={viewing.photo.visibleToCustomer} disabled={busy}
                onChange={(e) => toggleShare(viewing.photo, e.target.checked)} />
            </label>}
            <SheetOption label={viewing.photo.isVideo ? "حذف فیلم" : "حذف عکس"} tone="danger" disabled={busy} onClick={() => remove(viewing.photo)} />
          </>
        )}
      </BottomSheet>
    </section>
  );
}
