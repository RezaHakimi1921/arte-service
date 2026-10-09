import { useEffect, useState } from "react";

type IconName =
  | "arrow"
  | "calendar"
  | "camera"
  | "car"
  | "check"
  | "chevron"
  | "clock"
  | "close"
  | "document"
  | "moon"
  | "phone"
  | "sun"
  | "wrench";

type StageState = "done" | "current" | "todo";

const stages: Array<{ name: string; state: StageState; time?: string }> = [
  { name: "پذیرش شد", state: "done", time: "۲۱:۱۸" },
  { name: "در حال عیب‌یابی", state: "current", time: "۲۱:۱۸" },
  { name: "در حال تعمیر", state: "todo" },
  { name: "بررسی نهایی استاد", state: "todo" },
  { name: "آماده تحویل", state: "todo" },
  { name: "تحویل شد", state: "todo" },
];

const photos = [
  {
    src: "https://images.unsplash.com/photo-1615906655593-ad0386982a0f?auto=format&fit=crop&w=1200&q=84",
    alt: "بررسی موتور خودرو توسط تعمیرکار",
    caption: "بررسی اولیه موتور",
  },
  {
    src: "https://images.unsplash.com/photo-1734535677978-b97074e264f0?auto=format&fit=crop&w=1200&q=84",
    alt: "عیب‌یابی موتور خودرو در تعمیرگاه",
    caption: "عیب‌یابی بخش فنی",
  },
];

function Icon({ name }: { name: IconName }) {
  const paths: Record<IconName, React.ReactNode> = {
    arrow: <path d="m15 18-6-6 6-6" />,
    calendar: (
      <>
        <rect x="3" y="5" width="18" height="16" rx="3" />
        <path d="M8 3v4M16 3v4M3 10h18" />
      </>
    ),
    camera: (
      <>
        <path d="M4 7h3l1.4-2h7.2L17 7h3a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2Z" />
        <circle cx="12" cy="13" r="4" />
      </>
    ),
    car: (
      <>
        <path d="m5 11 1.5-4h11l1.5 4M3 11h18v7H3z" />
        <path d="M5 18v2M19 18v2M6.5 15h.01M17.5 15h.01" />
      </>
    ),
    check: <path d="m5 12 4 4L19 6" />,
    chevron: <path d="m9 18 6-6-6-6" />,
    clock: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="M12 7v5l3 2" />
      </>
    ),
    close: <path d="M6 6l12 12M18 6 6 18" />,
    document: (
      <>
        <path d="M6 2h8l4 4v16H6z" />
        <path d="M14 2v5h5M9 12h6M9 16h6" />
      </>
    ),
    moon: <path d="M20.5 14.2A8.4 8.4 0 0 1 9.8 3.5a9 9 0 1 0 10.7 10.7Z" />,
    phone: <path d="M5 3h4l2 5-2.5 1.5a15 15 0 0 0 6 6L16 13l5 2v4a2 2 0 0 1-2 2C10.2 20.5 3.5 13.8 3 5a2 2 0 0 1 2-2Z" />,
    sun: (
      <>
        <circle cx="12" cy="12" r="4" />
        <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
      </>
    ),
    wrench: <path d="M14.7 6.3a4.5 4.5 0 0 0-5.6 5.6L3 18l3 3 6.1-6.1a4.5 4.5 0 0 0 5.6-5.6L15 12l-3-3 2.7-2.7Z" />,
  };

  return (
    <svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      {paths[name]}
    </svg>
  );
}

function BrandMark() {
  return (
    <span className="brand-mark" aria-hidden="true">
      <svg viewBox="0 0 48 48">
        <path d="M24 7 10.8 39h6.5l2.9-7.2h7.6l2.9 7.2h6.5L24 7Zm-1.9 18.7L24 20.8l1.9 4.9h-3.8Z" fill="currentColor" />
      </svg>
    </span>
  );
}

function SectionTitle({
  icon,
  eyebrow,
  title,
  action,
}: {
  icon: IconName;
  eyebrow: string;
  title: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="section-title">
      <span className="section-icon"><Icon name={icon} /></span>
      <div>
        <span>{eyebrow}</span>
        <h2>{title}</h2>
      </div>
      {action}
    </div>
  );
}

export default function App() {
  const [theme, setTheme] = useState<"light" | "dark">(() => {
    const saved = localStorage.getItem("arte-theme");
    if (saved === "light" || saved === "dark") return saved;
    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  });
  const [selectedPhoto, setSelectedPhoto] = useState<number | null>(null);
  const [requestsOpen, setRequestsOpen] = useState(false);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    document.documentElement.dir = "rtl";
    localStorage.setItem("arte-theme", theme);
  }, [theme]);

  useEffect(() => {
    if (selectedPhoto === null) return;
    const closeOnEscape = (event: KeyboardEvent) => event.key === "Escape" && setSelectedPhoto(null);
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [selectedPhoto]);

  return (
    <main className="tracking-page">
      <div className="backdrop-orb orb-one" />
      <div className="backdrop-orb orb-two" />

      <header className="topbar">
        <div className="topbar-inner">
          <div className="brand">
            <BrandMark />
            <div>
              <strong>حکیمی ۲</strong>
              <span>مرکز تخصصی خدمات خودرو</span>
            </div>
          </div>
          <div className="topbar-actions">
            <span className="sync-status"><i /> به‌روز</span>
            <button
              className="icon-button"
              type="button"
              onClick={() => setTheme(theme === "light" ? "dark" : "light")}
              aria-label={theme === "light" ? "فعال‌سازی حالت تاریک" : "فعال‌سازی حالت روشن"}
            >
              <Icon name={theme === "light" ? "moon" : "sun"} />
            </button>
          </div>
        </div>
      </header>

      <div className="page-content">
        <section className="hero-card">
          <div className="hero-main">
            <div className="vehicle-icon"><Icon name="car" /></div>
            <div className="hero-copy">
              <div className="customer-greeting">
                <span>زارع عزیز، خودروی شما</span>
                <span className="case-number" dir="ltr">CASE-2</span>
              </div>
              <h1>پارس‌خودرو تندر ۹۰ پلاس</h1>
              <div className="status-pill"><i /> در حال عیب‌یابی</div>
            </div>
          </div>
          <div className="hero-divider" />
          <div className="hero-meta">
            <div><Icon name="calendar" /><span>پذیرش</span><strong>۱۸ مهر ۱۴۰۵</strong></div>
            <div><Icon name="clock" /><span>آخرین به‌روزرسانی</span><strong>امروز، ۲۱:۱۸</strong></div>
          </div>
        </section>

        <div className="content-grid">
          <div className="primary-column">
            <section className="surface progress-card">
              <SectionTitle icon="wrench" eyebrow="وضعیت لحظه‌ای" title="مسیر تعمیر خودرو" />
              <div className="current-note">
                <span className="pulse-dot" />
                <div>
                  <strong>کارشناس در حال بررسی علت ایراد است</strong>
                  <p>پس از پایان عیب‌یابی، نتیجه و هزینه‌های احتمالی در همین صفحه به شما اطلاع داده می‌شود.</p>
                </div>
              </div>
              <ol className="timeline">
                {stages.map((stage, index) => (
                  <li className={stage.state} key={stage.name}>
                    <div className="timeline-rail">
                      <span className="timeline-dot">{stage.state === "done" && <Icon name="check" />}</span>
                      {index < stages.length - 1 && <span className="timeline-line" />}
                    </div>
                    <div className="timeline-content">
                      <strong>{stage.name}</strong>
                      {stage.state === "current" && <span className="current-badge">مرحله فعلی</span>}
                    </div>
                    <time>{stage.time || "—"}</time>
                  </li>
                ))}
              </ol>
            </section>

            <section className="surface gallery-card">
              <SectionTitle
                icon="camera"
                eyebrow="گزارش تصویری"
                title="تصاویر ثبت‌شده"
                action={<span className="image-count">۲ تصویر</span>}
              />
              <p className="section-description">تصاویر ثبت‌شده توسط تعمیرگاه در هر مرحله را اینجا ببینید.</p>
              <div className="photo-grid">
                {photos.map((photo, index) => (
                  <button className="photo-card" type="button" key={photo.src} onClick={() => setSelectedPhoto(index)}>
                    <img src={photo.src} alt={photo.alt} />
                    <span className="photo-overlay">
                      <span><Icon name="camera" /> {photo.caption}</span>
                      <small>مرحله عیب‌یابی</small>
                    </span>
                  </button>
                ))}
              </div>
            </section>
          </div>

          <aside className="secondary-column">
            <section className={`surface request-card ${requestsOpen ? "expanded" : ""}`}>
              <SectionTitle icon="document" eyebrow="شرح پذیرش" title="درخواست شما" />
              <div className="request-summary">
                <span><strong>۵</strong> ایراد گزارش‌شده</span>
                <span><strong>۲</strong> خدمت درخواستی</span>
              </div>
              <div className="request-list">
                <div>
                  <span className="list-label">ایرادهای گزارش‌شده</span>
                  <ul>
                    <li>روشن نمی‌شود</li>
                    <li>دیر روشن می‌شود</li>
                    {requestsOpen && (
                      <>
                        <li>خاموش می‌کند</li>
                        <li>فرمان سنگین یا کج است</li>
                        <li>چراغ هشدار روشن است</li>
                      </>
                    )}
                  </ul>
                </div>
                <div>
                  <span className="list-label">خدمات درخواستی</span>
                  <ul>
                    <li>تعویض روغن</li>
                    <li>تعویض فیلتر روغن</li>
                  </ul>
                </div>
              </div>
              <button className="expand-button" type="button" onClick={() => setRequestsOpen(!requestsOpen)}>
                {requestsOpen ? "نمایش کمتر" : "مشاهده همه جزئیات"}
                <Icon name="chevron" />
              </button>
            </section>

            <section className="surface billing-card">
              <SectionTitle icon="document" eyebrow="صورت‌حساب" title="هزینه‌ها و پرداخت" />
              <div className="empty-bill">
                <span><Icon name="wrench" /></span>
                <strong>هنوز هزینه‌ای ثبت نشده</strong>
                <p>پس از تأیید و ثبت قطعات یا اجرت، جزئیات هزینه در این بخش نمایش داده می‌شود.</p>
              </div>
            </section>

            <section className="help-card">
              <div className="help-icon"><Icon name="phone" /></div>
              <div><strong>اطلاعات تماس تعمیرگاه</strong><span>شماره تماس هنوز برای این پرونده ثبت نشده است.</span></div>
            </section>
          </aside>
        </div>

        <footer className="footer">
          <div><BrandMark /><span>پیگیری شفاف با <strong>آرته سرویس</strong></span></div>
          <span>این صفحه با هر تغییر به‌روز می‌شود.</span>
        </footer>
      </div>

      {selectedPhoto !== null && (
        <div className="lightbox" role="dialog" aria-modal="true" aria-label="نمایش تصویر تعمیر" onClick={() => setSelectedPhoto(null)}>
          <div className="lightbox-content" onClick={(event) => event.stopPropagation()}>
            <button className="lightbox-close" type="button" onClick={() => setSelectedPhoto(null)} aria-label="بستن تصویر">
              <Icon name="close" />
            </button>
            <img src={photos[selectedPhoto].src} alt={photos[selectedPhoto].alt} />
            <div>
              <span>مرحله عیب‌یابی</span>
              <strong>{photos[selectedPhoto].caption}</strong>
            </div>
          </div>
        </div>
      )}

      <div className="mobile-call">
        <div><span className="pulse-dot" /><span>در حال عیب‌یابی</span></div>
        <span className="mobile-updated"><Icon name="clock" /> به‌روزرسانی ۲۱:۱۸</span>
      </div>
    </main>
  );
}
