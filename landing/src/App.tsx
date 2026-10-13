import { useEffect, useRef, useState } from "react";

type IconName =
  | "arrow"
  | "branch"
  | "camera"
  | "check"
  | "chevron"
  | "clock"
  | "dashboard"
  | "fuel"
  | "history"
  | "lock"
  | "menu"
  | "message"
  | "people"
  | "receipt"
  | "report"
  | "shield"
  | "spark"
  | "star"
  | "tool"
  | "wifi";

function Icon({ name, className = "" }: { name: IconName; className?: string }) {
  const icons: Record<IconName, React.ReactNode> = {
    arrow: <path d="M5 12h14m-5-5 5 5-5 5" />,
    branch: <path d="M6 3v12a4 4 0 0 0 4 4h8M6 8h7a4 4 0 0 0 4-4V3m-3 0h6v6" />,
    camera: <><rect x="3" y="6" width="18" height="14" rx="3" /><path d="m8 6 1.5-2h5L16 6" /><circle cx="12" cy="13" r="3.5" /></>,
    check: <path d="m5 12 4 4L19 6" />,
    chevron: <path d="m9 18 6-6-6-6" />,
    clock: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>,
    dashboard: <><rect x="3" y="3" width="7" height="7" rx="2" /><rect x="14" y="3" width="7" height="7" rx="2" /><rect x="3" y="14" width="7" height="7" rx="2" /><rect x="14" y="14" width="7" height="7" rx="2" /></>,
    fuel: <><path d="M4 21V5a2 2 0 0 1 2-2h7a2 2 0 0 1 2 2v16M3 21h13M7 7h5" /><path d="m15 7 3 3v7a2 2 0 0 0 4 0v-5l-2-2" /></>,
    history: <><path d="M3 12a9 9 0 1 0 3-6.7L3 8" /><path d="M3 3v5h5M12 7v5l3 2" /></>,
    lock: <><rect x="4" y="10" width="16" height="11" rx="3" /><path d="M8 10V7a4 4 0 0 1 8 0v3" /></>,
    menu: <path d="M4 7h16M4 12h16M4 17h16" />,
    message: <><path d="M21 12a8 8 0 0 1-8 8H5l-3 2 1-5a9 9 0 1 1 18-5Z" /><path d="M8 12h8M8 8h5" /></>,
    people: <><circle cx="9" cy="8" r="3" /><path d="M3 20v-2a5 5 0 0 1 5-5h2a5 5 0 0 1 5 5v2M16 5a3 3 0 0 1 0 6M17 14a5 5 0 0 1 4 5v1" /></>,
    receipt: <><path d="M5 3h14v19l-3-2-4 2-4-2-3 2V3Z" /><path d="M9 8h6M9 12h6M9 16h3" /></>,
    report: <><path d="M4 20V10M10 20V4M16 20v-7M22 20H2" /></>,
    shield: <><path d="M12 22s8-4 8-11V5l-8-3-8 3v6c0 7 8 11 8 11Z" /><path d="m9 12 2 2 4-5" /></>,
    spark: <path d="m12 3 1.6 4.4L18 9l-4.4 1.6L12 15l-1.6-4.4L6 9l4.4-1.6L12 3ZM19 15l.8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8L19 15Z" />,
    star: <path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-2.9-5.6 2.9 1.1-6.2L3 9.6l6.2-.9L12 3Z" />,
    tool: <path d="M14.7 6.3a4 4 0 0 0-5-5L12 3.6 9.6 6 7.3 3.7a4 4 0 0 0 5 5l-7.8 7.8a2.1 2.1 0 0 0 3 3l7.8-7.8a4 4 0 0 0 5-5L18 9l-2.4-2.4 2.3-2.3a4 4 0 0 0-3.2 2Z" />,
    wifi: <><path d="M5 12.5a10 10 0 0 1 14 0M8.5 16a5 5 0 0 1 7 0" /><path d="M12 20h.01M3 3l18 18" /></>,
  };

  return (
    <svg aria-hidden="true" className={className} fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8" viewBox="0 0 24 24">
      {icons[name]}
    </svg>
  );
}

function Logo({ compact = false }: { compact?: boolean }) {
  return (
    <div className="logo" aria-label="آرته سرویس">
      <img className="logo-mark" src="/icon.svg" alt="" width="44" height="44" />
      {!compact && <span><strong>آرته</strong><small>سرویس</small></span>}
    </div>
  );
}

function Mechanic({ className = "" }: { className?: string }) {
  return (
    <svg className={`mechanic ${className}`} viewBox="0 0 220 330" role="img" aria-label="تعمیرکار آرته با آچار و دفتر">
      <ellipse cx="112" cy="313" rx="74" ry="10" fill="#142A66" opacity=".1" />
      <g className="mechanic-body">
        <path d="M107 82c-27 0-42 18-42 43v92h91v-91c0-27-18-44-49-44Z" fill="#1E3A8A" />
        <path d="M88 82c4 13 13 21 24 21s20-8 24-21" fill="#fff" opacity=".18" />
        <path d="M65 124c-18 12-25 35-32 58l18 6 29-51-15-13Z" fill="#1E3A8A" />
        <circle cx="43" cy="188" r="11" fill="#D99B73" />
        <path d="m157 123 31 46-16 11-36-40 21-17Z" fill="#1E3A8A" />
        <circle cx="181" cy="177" r="11" fill="#D99B73" />
        <rect x="146" y="149" width="48" height="60" rx="6" fill="#fff" transform="rotate(-8 146 149)" />
        <path d="m164 188 8-25 12 22m-17-8h13" fill="none" stroke="#1E3A8A" strokeLinecap="round" strokeLinejoin="round" strokeWidth="5" />
        <path d="M78 207h34v95H69l9-95Zm38 0h32l13 95h-43l-2-95Z" fill="#263247" />
        <path d="M64 302h49v13H55c0-7 3-11 9-13Zm54 0h45l7 13h-52v-13Z" fill="#162036" />
        <path d="M70 127h80M110 104v101" fill="none" stroke="#fff" strokeOpacity=".18" strokeWidth="3" />
        <circle cx="110" cy="63" r="34" fill="#D99B73" />
        <path d="M76 59c0-29 18-43 38-43 23 0 38 15 34 46-9-2-15-11-17-22-9 13-27 19-55 19Z" fill="#253047" />
        <path d="M81 52c4-28 17-40 36-40 17 0 31 11 34 33l-70 7Z" fill="#1E3A8A" />
        <path d="M76 50c22-7 50-9 80-4" fill="none" stroke="#E07B2C" strokeLinecap="round" strokeWidth="7" />
        <circle cx="98" cy="64" r="2.5" fill="#263247" /><circle cx="125" cy="64" r="2.5" fill="#263247" />
        <path d="M103 79c6 4 13 4 19 0" fill="none" stroke="#915C46" strokeLinecap="round" strokeWidth="2.5" />
      </g>
      <g className="wrench-arm">
        <path d="M60 133 38 93l14-9 27 43-19 6Z" fill="#1E3A8A" />
        <circle cx="44" cy="88" r="11" fill="#D99B73" />
        <path d="M40 83 14 44m5-25c-7 5-9 15-4 22s15 9 22 4 9-15 4-22l-8 10-8-2-2-8 8-10c-4-1-8 1-12 6Z" fill="#8E9AAC" />
      </g>
    </svg>
  );
}

function PhoneShot({ src, alt, compact = false }: { src: string; alt: string; compact?: boolean }) {
  return (
    <div className={`phone ${compact ? "phone-compact" : ""}`}>
      <div className="phone-speaker" />
      <div className="phone-screen"><img src={src} alt={alt} width="750" height="1520" loading={compact ? "lazy" : "eager"} /></div>
    </div>
  );
}

const features: { icon: IconName; title: string; text: string; visual: React.ReactNode }[] = [
  { icon: "fuel", title: "پذیرش در یک دقیقه", text: "شماره، وسیله و ایراد را سریع ثبت کنید؛ جزئیات همیشه در دسترس است.", visual: <div className="fuel-visual"><span>خالی</span><div><i /></div><span>پر</span><b>نصف</b></div> },
  { icon: "history", title: "مسیر کار روشن", text: "هر پرونده مرحله‌به‌مرحله جلو می‌رود و می‌گوید منتظر چه چیزی است.", visual: <div className="steps-visual">{[1,2,3,4,5,6].map(n => <i key={n} />)}</div> },
  { icon: "dashboard", title: "صف کار امروز", text: "با باز کردن برنامه می‌دانید چه مانده، چه متوقف است و چه آماده تحویل.", visual: <div className="numbers-visual"><b>۱۲</b><b>۵</b><b className="green">۳</b><b>۲</b></div> },
  { icon: "message", title: "لینک پیگیری مشتری", text: "مشتری بدون نصب برنامه، وضعیت و عکس‌ها را می‌بیند؛ پیامک خودکار می‌رود.", visual: <div className="message-visual"><Icon name="message" /><span>موتور شما آماده تحویل است</span></div> },
  { icon: "receipt", title: "قطعه، اجرت و پرداخت", text: "بسته‌های آماده، نسیه، رسید واریز و همه هزینه‌ها را یک‌جا نگه دارید.", visual: <div className="receipt-visual"><span>تعویض روغن</span><b>۱٬۴۵۰٬۰۰۰</b></div> },
  { icon: "camera", title: "عکس کار", text: "از وسیله و کار انجام‌شده عکس بگیرید و نمایش به مشتری را انتخاب کنید.", visual: <div className="photo-visual"><Icon name="camera" /><i /><i /></div> },
  { icon: "star", title: "نظرسنجی رضایت", text: "بعد از تحویل، مشتری با ستاره نظر می‌دهد و نظر پایین فوراً اعلام می‌شود.", visual: <div className="stars-visual">{[1,2,3,4,5].map(n => <Icon name="star" key={n} />)}</div> },
  { icon: "report", title: "گزارش ساده", text: "فروش، دریافتی، پرفروش‌ها، سود کالا و دستمزد کارکنان را ببینید.", visual: <div className="chart-visual"><i /><i /><i /><i /></div> },
  { icon: "people", title: "کارکنان و دسترسی", text: "مالک، مدیر داخلی و شاگرد؛ هر کس فقط اطلاعات موردنیاز خودش را می‌بیند.", visual: <div className="avatars-visual"><i>م</i><i>د</i><i>ش</i></div> },
  { icon: "history", title: "مشتری و تاریخچه", text: "هر مشتری و هر وسیله سابقه‌ی خودش را دارد؛ دفعه‌ی بعد با یک جست‌وجو همه‌چیز پیداست.", visual: <div className="list-visual"><i /><i /><i /></div> },
  { icon: "wifi", title: "روی گوشی، حتی با اینترنت ضعیف", text: "مثل یک برنامه روی صفحه اصلی می‌نشیند و قطع اینترنت را با نوار هشدار اعلام می‌کند.", visual: <div className="wifi-visual"><Icon name="wifi" /><span>اینترنت قطع است</span></div> },
  { icon: "branch", title: "چند شعبه و برند", text: "همه شعب را در یک داشبورد ببینید و با یک لمس وارد هر شعبه شوید.", visual: <div className="branch-visual"><i /><i /><i /></div> },
];

const journey = [
  { time: "۰۸:۳۰", title: "صبح؛ پذیرش مشتری", text: "مشتری با موتورش می‌آید. در یک دقیقه پذیرش می‌شود و همان لحظه پیامک «پذیرش شد» را می‌گیرد.", status: "پذیرش شد" },
  { time: "۱۱:۱۵", title: "حین کار؛ همه‌چیز ثبت می‌شود", text: "شاگرد عکس می‌گیرد و قطعه ثبت می‌کند؛ استاد کار را بررسی و تأیید می‌کند.", status: "در حال تعمیر" },
  { time: "۱۴:۲۰", title: "آماده؛ مشتری باخبر است", text: "پیامک آماده تحویل با هزینه نهایی می‌رود و مشتری مسیر کار را در لینک خودش می‌بیند.", status: "آماده تحویل" },
  { time: "۱۷:۰۰", title: "تحویل؛ حساب روشن", text: "پرداخت نقدی یا نسیه با موعد ثبت می‌شود و رسید واریز کنار پرونده می‌ماند.", status: "تسویه شد" },
  { time: "۲۰:۱۰", title: "عصر؛ صدای مشتری", text: "مشتری نظرش را ثبت می‌کند و مدیر همان لحظه اعلان رضایت را می‌گیرد.", status: "۵ ستاره" },
];

const businesses = [
  { kind: "motor", title: "موتورسازی", text: "موتور، اسکوتر، ATV و خدمات مخصوص دوچرخ", symbol: "M" },
  { kind: "car", title: "تعمیرات خودرو", text: "خودرو، ایرادهای فنی و سرویس‌های دوره‌ای", symbol: "A" },
  { kind: "tire", title: "آپاراتی و تعویض روغن", text: "خدمات سریع، بسته‌های آماده و صف روزانه", symbol: "O" },
];

const plans = [
  { months: "۱ ماهه", price: "۱", suffix: "میلیون" },
  { months: "۳ ماهه", price: "۲٫۵", suffix: "میلیون" },
  { months: "۶ ماهه", price: "۵", suffix: "میلیون" },
  { months: "۹ ماهه", price: "۷٫۵", suffix: "میلیون" },
  { months: "۱۲ ماهه", price: "۱۰", suffix: "میلیون", featured: true },
];

const faqs = [
  ["بدون اینترنت چه می‌شود؟", "نوار «اینترنت قطع است» نمایش داده می‌شود و عملیات در انتظار می‌ماند. با برگشت اینترنت ادامه می‌دهید و چیزی گم نمی‌شود."],
  ["باید برنامه نصب کنم؟", "نه؛ آرته سرویس از مرورگر باز می‌شود و می‌توانید آن را روی صفحه اصلی آیفون و اندروید هم اضافه کنید."],
  ["اطلاعات من کجاست؟", "اطلاعات روی سرور ایران نگه‌داری می‌شود و داده‌های هر تعمیرگاه و هر شعبه کاملاً جداست."],
  ["اگر اشتراکم تمام شود؟", "حساب شما فقط‌خواندنی می‌شود؛ هیچ اطلاعاتی پاک نمی‌شود و هر زمان اشتراک را تمدید کنید دوباره فعال می‌شود."],
  ["پیامک به مشتری حتماً فعال است؟", "نه؛ خودتان آن را روشن می‌کنید و می‌توانید برای هر مرحله جداگانه تصمیم بگیرید."],
];

function SectionHeading({ eyebrow, title, text }: { eyebrow: string; title: string; text?: string }) {
  return <div className="section-heading reveal"><span>{eyebrow}</span><h2>{title}</h2>{text && <p>{text}</p>}</div>;
}

function App() {
  const [menuOpen, setMenuOpen] = useState(false);
  const [openFaq, setOpenFaq] = useState<number | null>(0);
  const [heroVisible, setHeroVisible] = useState(true);
  const heroRef = useRef<HTMLElement>(null);

  useEffect(() => {
    const reveals = document.querySelectorAll(".reveal");
    const observer = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) {
          entry.target.classList.add("is-visible");
          observer.unobserve(entry.target);
        }
      });
    }, { threshold: 0.14 });
    reveals.forEach((element) => observer.observe(element));

    const heroObserver = new IntersectionObserver(([entry]) => setHeroVisible(entry.isIntersecting), { threshold: 0.15 });
    if (heroRef.current) heroObserver.observe(heroRef.current);
    return () => { observer.disconnect(); heroObserver.disconnect(); };
  }, []);

  const closeMenu = () => setMenuOpen(false);

  return (
    <div className="site-shell" dir="rtl">
      <header className="site-header">
        <div className="container header-inner">
          <a className="brand-link" href="#top" aria-label="خانه آرته سرویس"><Logo /></a>
          <nav className={menuOpen ? "header-nav open" : "header-nav"} aria-label="ناوبری اصلی">
            <a href="#features" onClick={closeMenu}>قابلیت‌ها</a>
            <a href="#journey" onClick={closeMenu}>یک روز با آرته</a>
            <a href="#pricing" onClick={closeMenu}>قیمت</a>
            <a href="#faq" onClick={closeMenu}>سؤال‌ها</a>
          </nav>
          <div className="header-actions">
            <a className="login-link" href="https://service.artepersia.com">ورود</a>
            <a className="button button-small" href="https://service.artepersia.com">ثبت‌نام رایگان</a>
            <button className="menu-button" onClick={() => setMenuOpen((value) => !value)} aria-expanded={menuOpen} aria-label="باز کردن منو"><Icon name={menuOpen ? "chevron" : "menu"} /></button>
          </div>
        </div>
      </header>

      <main>
        <section className="hero" id="top" ref={heroRef}>
          <div className="hero-glow" />
          <div className="container hero-grid">
            <div className="hero-copy">
              <div className="eyebrow reveal"><span><Icon name="spark" /></span>نرم‌افزار مدیریت تعمیرگاه</div>
              <h1><span>تعمیرگاه منظم،</span><strong>ذهن آرام‌تر.</strong></h1>
              <p className="hero-subtitle reveal">از پذیرش تا تحویل، هر وسیله، هر تومان و هر نظر مشتری در یک برنامه‌ی فارسی روی گوشی شما.</p>
              <div className="hero-actions reveal">
                <a className="button" href="https://service.artepersia.com">شروع رایگان ۱۴ روزه <Icon name="arrow" /></a>
                <a className="button button-secondary" href="#journey">ببینید چطور کار می‌کند</a>
              </div>
              <div className="hero-proof reveal"><span><Icon name="check" />بدون نیاز به کارت بانکی</span><span><Icon name="check" />راه‌اندازی در چند دقیقه</span></div>
            </div>
            <div className="hero-visual" aria-label="نمایی از داشبورد آرته سرویس">
              <div className="dashboard-float"><PhoneShot src="/shots/home.webp" alt="صفحه‌ی خانه‌ی آرته سرویس: پرونده‌های باز، در حال کار و آماده تحویل" /></div>
              <Mechanic className="hero-mechanic" />
              <div className="floating-note note-one"><span><Icon name="check" /></span><div><b>آماده تحویل</b><small>پیامک برای مشتری ارسال شد</small></div></div>
              <div className="floating-note note-two"><span><Icon name="star" /></span><div><b>نظر جدید مشتری</b><small>امتیاز ۵ از ۵</small></div></div>
            </div>
          </div>
          <div className="hero-brands container"><span>ساخته‌شده برای</span><b>موتورسازی</b><i /><b>تعمیرگاه خودرو</b><i /><b>آپاراتی و تعویض روغن</b></div>
        </section>

        <section className="problem section">
          <div className="container problem-grid">
            <div className="paper-scene reveal" aria-hidden="true">
              <div className="paper paper-one"><Icon name="receipt" /><span>فاکتور شماره ۲۸</span></div>
              <div className="paper paper-two"><Icon name="message" /><span>کارم چی شد؟</span></div>
              <div className="paper paper-three"><Icon name="tool" /><span>لنت + اجرت؟</span></div>
              <div className="paper-logo">A<small>همه‌چیز، یک‌جا</small></div>
            </div>
            <div className="problem-copy reveal">
              <span className="mini-label">آشنا به نظر می‌رسد؟</span>
              <h2>شلوغی تعمیرگاه نباید به ذهن شما منتقل شود.</h2>
              <p>دفتر کاغذی گم می‌شود، مشتری هر ساعت زنگ می‌زند که «کارم چی شد؟»، شاگرد فراموش می‌کند قطعه را ثبت کند و آخر ماه معلوم نیست پول کجا رفت.</p>
              <strong><Icon name="spark" />آرته همه‌چیز را جمع می‌کند و مرتب تحویل شما می‌دهد.</strong>
            </div>
          </div>
        </section>

        <section className="section features-section" id="features">
          <div className="container">
            <SectionHeading eyebrow="همه‌چیز سر جای خودش" title="آرته دقیقاً چه کار می‌کند؟" text="از اولین سلام مشتری تا آخرین ستاره‌ای که می‌دهد، مسیر کار روشن و قابل پیگیری می‌ماند." />
            <div className="feature-grid">
              {features.map((feature, index) => (
                <article className="feature-card reveal" key={feature.title} style={{ "--delay": `${(index % 3) * 80}ms` } as React.CSSProperties}>
                  <div className="feature-top"><span className="feature-icon"><Icon name={feature.icon} /></span><small>۰{index + 1}</small></div>
                  <div className="feature-visual">{feature.visual}</div>
                  <h3>{feature.title}</h3><p>{feature.text}</p>
                </article>
              ))}
            </div>
            <p className="swipe-hint"><Icon name="arrow" /> کارت‌ها را بکشید و بیشتر ببینید</p>
          </div>
        </section>

        <section className="section journey-section" id="journey">
          <div className="container">
            <SectionHeading eyebrow="یک روز واقعی، بدون آشفتگی" title="یک روز با آرته" text="تعمیرکار آرته کنار شماست؛ از لحظه‌ای که کرکره بالا می‌رود تا وقتی آخرین مشتری نظرش را ثبت می‌کند." />
            <div className="journey-layout">
              <div className="journey-phone reveal"><PhoneShot compact src="/shots/track.webp" alt="لینک پیگیری مشتری: وضعیت وسیله و مسیر کار" /><Mechanic className="journey-mechanic" /></div>
              <div className="journey-list">
                {journey.map((item, index) => (
                  <article className="journey-item reveal" key={item.time}>
                    <div className="journey-marker"><span>{index + 1}</span></div>
                    <div className="journey-card">
                      <div><time>{item.time}</time><span className={index === 2 || index === 3 ? "success-tag" : ""}>{item.status}</span></div>
                      <h3>{item.title}</h3><p>{item.text}</p>
                    </div>
                  </article>
                ))}
              </div>
            </div>
          </div>
        </section>

        <section className="section businesses-section">
          <div className="container">
            <SectionHeading eyebrow="به زبان کسب‌وکار شما" title="برای چه تعمیرگاهی؟" text="هر کسب‌وکار فهرست وسیله، ایراد و خدمات خودش را می‌بیند؛ نه گزینه‌های اضافی و نامرتبط." />
            <div className="business-grid">
              {businesses.map((business) => (
                <article className="business-card reveal" key={business.title}>
                  <div className={`business-symbol ${business.kind}`}><span>{business.symbol}</span><Icon name="tool" /></div>
                  <h3>{business.title}</h3><p>{business.text}</p>
                  <span className="business-more">تنظیم‌شده برای شما <Icon name="arrow" /></span>
                </article>
              ))}
            </div>
          </div>
        </section>

        <section className="trust-section">
          <div className="container trust-grid">
            <div className="trust-intro reveal"><span className="mini-label light">خیالتان راحت</span><h2>اطلاعات تعمیرگاه، فقط برای شماست.</h2><p>ساختار آرته طوری طراحی شده که کنترل داده و دسترسی همیشه دست مالک تعمیرگاه بماند.</p></div>
            <div className="trust-items">
              <article className="reveal"><span><Icon name="lock" /></span><div><h3>جدا و محرمانه</h3><p>اطلاعات هر تعمیرگاه و هر شعبه کاملاً جدا نگه‌داری می‌شود.</p></div></article>
              <article className="reveal"><span><Icon name="history" /></span><div><h3>تاریخچه‌ی قابل بازگشت</h3><p>حذف‌ها به سطل بازیافت می‌روند و تغییرها در تاریخچه می‌مانند.</p></div></article>
              <article className="reveal"><span><Icon name="shield" /></span><div><h3>دسترسی دقیق</h3><p>قیمت خرید و سود کالا فقط برای مالک تعمیرگاه دیده می‌شود.</p></div></article>
            </div>
          </div>
        </section>

        <section className="section pricing-section" id="pricing">
          <div className="container">
            <SectionHeading eyebrow="شفاف و بدون هزینه‌ی پنهان" title="۱۴ روز رایگان؛ بعد، انتخاب با شما" text="همه قابلیت‌های اصلی را امتحان کنید. قیمت اشتراک برای هر شعبه است و هر زمان خواستید می‌توانید تمدید کنید." />
            <div className="pricing-grid">
              {plans.map((plan) => (
                <article className={`price-card reveal ${plan.featured ? "featured" : ""}`} key={plan.months}>
                  {plan.featured && <span className="popular">پیشنهاد ویژه</span>}
                  <h3>{plan.months}</h3>
                  <div className="price"><strong>{plan.price}</strong><span>{plan.suffix}<small>تومان</small></span></div>
                  <ul><li><Icon name="check" />تمام قابلیت‌های مدیریت</li><li><Icon name="check" />پشتیبانی آرته</li><li><Icon name="check" />به‌روزرسانی رایگان</li></ul>
                  <a className={plan.featured ? "button" : "button button-secondary"} href="https://service.artepersia.com">شروع رایگان</a>
                </article>
              ))}
            </div>
            <p className="pricing-note">نظرسنجی مشتری امکان جداگانه است و در صورت نیاز به اشتراک اضافه می‌شود.</p>
          </div>
        </section>

        <section className="section faq-section" id="faq">
          <div className="container faq-layout">
            <div className="faq-intro reveal"><span className="mini-label">سؤال‌های پرتکرار</span><h2>اگر هنوز سؤالی دارید، احتمالاً جوابش اینجاست.</h2><p>پاسختان را پیدا نکردید؟ با ما تماس بگیرید؛ آدم واقعی جواب می‌دهد.</p><a href="tel:09362660550">۰۹۳۶۲۶۶۰۵۵۰ <Icon name="arrow" /></a></div>
            <div className="faq-list">
              {faqs.map(([question, answer], index) => (
                <article className={`faq-item reveal ${openFaq === index ? "open" : ""}`} key={question}>
                  <button onClick={() => setOpenFaq(openFaq === index ? null : index)} aria-expanded={openFaq === index}><span>{question}</span><Icon name="chevron" /></button>
                  <div className="faq-answer"><p>{answer}</p></div>
                </article>
              ))}
            </div>
          </div>
        </section>

        <section className="final-cta">
          <div className="container cta-card reveal">
            <div className="cta-pattern" />
            <Mechanic className="cta-mechanic" />
            <div className="cta-copy"><span>وقت شروع است</span><h2>امروز اولین وسیله را پذیرش کنید.</h2><p>۱۴ روز رایگان، بدون کارت بانکی؛ کمتر از چند دقیقه تا یک تعمیرگاه منظم‌تر فاصله دارید.</p><form onSubmit={(event) => { event.preventDefault(); window.location.href = "https://service.artepersia.com"; }}><label><span>شماره موبایل</span><input type="tel" inputMode="numeric" placeholder="۰۹۱۲ ۱۲۳ ۴۵۶۷" aria-label="شماره موبایل" dir="ltr" /></label><button className="button" type="submit">ثبت‌نام رایگان <Icon name="arrow" /></button></form><a className="support-link" href="tel:09362660550">تماس با پشتیبانی: <b>۰۹۳۶۲۶۶۰۵۵۰</b></a></div>
          </div>
        </section>
      </main>

      <footer>
        <div className="container footer-grid">
          <div><Logo /><p>تعمیرگاه منظم، ذهن آرام‌تر.</p></div>
          <nav aria-label="پیوندهای پاورقی"><a href="#features">قابلیت‌ها</a><a href="#pricing">قیمت</a><a href="#faq">سؤال‌های پرتکرار</a></nav>
          <nav aria-label="قوانین"><a href="#">قوانین استفاده</a><a href="#">حریم خصوصی</a><a href="tel:09362660550">تماس با ما</a></nav>
        </div>
        <div className="container copyright"><span>© ۱۴۰۵ آرته پرشیا؛ همه حقوق محفوظ است.</span><span>ساخته‌شده با دقت در ایران</span></div>
      </footer>

      <a className={`mobile-sticky-cta ${heroVisible ? "hidden" : ""}`} href="https://service.artepersia.com">شروع رایگان ۱۴ روزه <Icon name="arrow" /></a>
    </div>
  );
}

export default App;
