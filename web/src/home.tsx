import { useEffect, useState, type ReactNode } from "react";
import { api } from "./api";
import { CaseCard, EmptyCases, type CaseCardData, type CaseFilter } from "./cases";
import { WAIT_REASONS } from "./labels";
import { formatNumber } from "./ui";

type Inbox = {
  role: string;
  needsAction: CaseCardData[];
  mine: CaseCardData[];
  ready: CaseCardData[];
  blocked: { reason: string; cases: CaseCardData[] }[];
  dueToday: CaseCardData[];
  stats: { open: number; active: number; waiting: number; ready: number; openedToday: number };
};

/**
 * Home is a work queue, not a statistics page: what needs me now, and why.
 * Technicians land on their own jobs; owners and supervisors on the whole shop.
 */
export function HomeView({ canCreate, onOpen, onNewCase, onOpenCases }: {
  canCreate: boolean; onOpen: (id: string) => void; onNewCase: () => void; onOpenCases: (f: CaseFilter) => void;
}) {
  const [inbox, setInbox] = useState<Inbox | null>(null);
  useEffect(() => { api<Inbox>("/api/v1/inbox").then(setInbox).catch(() => {}); }, []);
  if (!inbox) return <div className="splash" aria-busy="true" />;

  const manager = inbox.role !== "technician";

  if (inbox.stats.open === 0)
    return (
      <section>
        {canCreate && <button className="primary block big" onClick={onNewCase}>+ پذیرش جدید</button>}
        <EmptyCases canCreate={canCreate} onNewCase={onNewCase} />
      </section>
    );

  if (!manager)
    return (
      <section>
        <Group title="کارهای من" count={inbox.mine.length} empty="فعلاً کاری به شما سپرده نشده است.">
          {inbox.mine.map((c) => <CaseCard key={c.id} c={c} onOpen={onOpen} />)}
        </Group>
      </section>
    );

  return (
    <section>
      {canCreate && <button className="primary block big" onClick={onNewCase}>+ پذیرش جدید</button>}

      <Group title="نیازمند اقدام" count={inbox.needsAction.length} tone="warn" empty="همه‌چیز روی روال است.">
        {inbox.needsAction.slice(0, 8).map((c) => <CaseCard key={c.id} c={c} onOpen={onOpen} />)}
      </Group>

      {inbox.ready.length > 0 && (
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

      <div className="stats">
        <Stat label="باز" value={inbox.stats.open} onClick={() => onOpenCases({})} />
        <Stat label="در حال کار" value={inbox.stats.active} onClick={() => onOpenCases({ category: "active" })} />
        <Stat label="منتظر" value={inbox.stats.waiting} onClick={() => onOpenCases({ category: "waiting" })} />
        <Stat label="پذیرش امروز" value={inbox.stats.openedToday} onClick={() => onOpenCases({ all: true })} />
      </div>
    </section>
  );
}

function Group({ title, count, tone, empty, children }: { title: string; count: number; tone?: "warn" | "good"; empty?: string; children: ReactNode }) {
  return (
    <section className="group">
      <h3 className={`group-title${tone ? ` ${tone}` : ""}`}>
        {title} <span className="count font-num">{formatNumber(count)}</span>
      </h3>
      {count === 0 ? <p className="muted empty-inline">{empty}</p> : <div className="list">{children}</div>}
    </section>
  );
}

function Stat({ label, value, onClick }: { label: string; value: number; onClick: () => void }) {
  return (
    <button className="stat" onClick={onClick}>
      <span className="stat-value font-num">{formatNumber(value)}</span>
      <span className="muted small">{label}</span>
    </button>
  );
}
