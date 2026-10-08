import { useEffect, useState, type ReactNode } from "react";
import { api } from "./api";
import { toman } from "./billing";
import { CaseCard, type CaseCardData, type CaseFilter } from "./cases";
import { WAIT_REASONS } from "./labels";
import { formatNumber } from "./ui";

type Inbox = {
  role: string;
  needsAction: CaseCardData[];
  mine: CaseCardData[];
  ready: CaseCardData[];
  blocked: { reason: string; cases: CaseCardData[] }[];
  dueToday: CaseCardData[];
  stats: {
    open: number; active: number; waiting: number; ready: number; openedToday: number; deliveredToday: number;
    unassigned: number; blocked: number; readyBalanceRials: number;
  };
};

/**
 * Dashboard: the state of the shop at a glance (tiles, always visible), then what needs attention and why.
 * Technicians see their own numbers and jobs.
 */
export function HomeView({ canCreate, onOpen, onNewCase, onOpenCases }: {
  canCreate: boolean; onOpen: (id: string) => void; onNewCase: () => void; onOpenCases: (f: CaseFilter) => void;
}) {
  const [inbox, setInbox] = useState<Inbox | null>(null);
  useEffect(() => { api<Inbox>("/api/v1/inbox").then(setInbox).catch(() => {}); }, []);
  if (!inbox) return <div className="splash" aria-busy="true" />;

  const manager = inbox.role !== "technician";
  const st = inbox.stats;

  return (
    <section>
      {canCreate && <button className="primary block big" onClick={onNewCase} data-tour="new-case">+ پذیرش جدید</button>}

      <div className="tiles" data-tour="queue">
        <Tile label={manager ? "پرونده‌های باز" : "کارهای من"} value={st.open} onClick={() => onOpenCases({})} />
        <Tile label="در حال کار" value={st.active} onClick={() => onOpenCases({ category: "active" })} />
        <Tile label="منتظر / متوقف" value={st.waiting} tone={st.waiting > 0 ? "warn" : undefined} onClick={() => onOpenCases({ category: "waiting" })} />
        <Tile label="آماده تحویل" value={st.ready} tone={st.ready > 0 ? "good" : undefined} onClick={() => onOpenCases({ category: "done" })}
          sub={st.readyBalanceRials > 0 ? `${toman(st.readyBalanceRials)} مانده` : undefined} />
        {manager && <Tile label="بدون مسئول" value={st.unassigned} tone={st.unassigned > 0 ? "warn" : undefined} onClick={() => onOpenCases({})} />}
        <Tile label="امروز" value={st.openedToday} onClick={() => onOpenCases({ all: true })} sub={`پذیرش · ${formatNumber(st.deliveredToday)} تحویل`} />
      </div>

      {st.open === 0 && (
        <p className="muted empty-inline">
          {canCreate ? "پرونده بازی ندارید. با «پذیرش جدید» اولین وسیله را ثبت کنید." : "فعلاً کاری به شما سپرده نشده است."}
        </p>
      )}

      {!manager && inbox.mine.length > 0 && (
        <Group title="کارهای من" count={inbox.mine.length}>
          {inbox.mine.map((c) => <CaseCard key={c.id} c={c} onOpen={onOpen} />)}
        </Group>
      )}

      {manager && inbox.needsAction.length > 0 && (
        <Group title="نیازمند اقدام" count={inbox.needsAction.length} tone="warn">
          {inbox.needsAction.slice(0, 8).map((c) => <CaseCard key={c.id} c={c} onOpen={onOpen} />)}
        </Group>
      )}

      {manager && inbox.ready.length > 0 && (
        <Group title="آماده تحویل" count={inbox.ready.length} tone="good">
          {inbox.ready.slice(0, 6).map((c) => <CaseCard key={c.id} c={c} onOpen={onOpen} />)}
        </Group>
      )}

      {inbox.blocked.length > 0 && (
        <section className="group">
          <h3 className="group-title">چه چیزی کار را متوقف کرده؟</h3>
          <ul className="list">
            {inbox.blocked.map((g) => (
              <li key={g.reason}>
                <details className="card blocked-group">
                  <summary>
                    <span>{WAIT_REASONS[g.reason] ?? g.reason}</span>
                    <span className="count font-num">{formatNumber(g.cases.length)}</span>
                  </summary>
                  <div className="list">{g.cases.map((c) => <CaseCard key={c.id} c={c} onOpen={onOpen} />)}</div>
                </details>
              </li>
            ))}
          </ul>
        </section>
      )}

      {inbox.dueToday.length > 0 && (
        <Group title="قول تحویل امروز" count={inbox.dueToday.length}>
          {inbox.dueToday.map((c) => <CaseCard key={c.id} c={c} onOpen={onOpen} />)}
        </Group>
      )}
    </section>
  );
}

function Tile({ label, value, sub, tone, onClick }: { label: string; value: number; sub?: string; tone?: "warn" | "good"; onClick: () => void }) {
  return (
    <button className={`tile${tone ? ` ${tone}` : ""}`} onClick={onClick}>
      <span className="tile-value font-num">{formatNumber(value)}</span>
      <span className="tile-label">{label}</span>
      {sub && <span className="tile-sub muted small">{sub}</span>}
    </button>
  );
}

function Group({ title, count, tone, children }: { title: string; count: number; tone?: "warn" | "good"; children: ReactNode }) {
  return (
    <section className="group">
      <h3 className={`group-title${tone ? ` ${tone}` : ""}`}>
        {title} <span className="count font-num">{formatNumber(count)}</span>
      </h3>
      <div className="list">{children}</div>
    </section>
  );
}
