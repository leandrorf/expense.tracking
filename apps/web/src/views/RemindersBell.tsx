import { useCallback, useEffect, useRef, useState } from 'react';
import { remindersApi, type Reminder, type Reminders } from '../api/reminders';

/** `2026-11-23` → `23/11`. */
function shortDate(iso: string): string {
  const [, m, d] = iso.split('-');
  return `${d}/${m}`;
}

function detailOf(r: Reminder, money: (v: number) => string): string {
  switch (r.kind) {
    case 'InvoiceOverdue':
      return `venceu em ${shortDate(r.dueOn)} · falta ${money(r.amount)}`;
    case 'InvoiceDue':
      return `${r.urgency === 'Today' ? 'vence hoje' : `vence em ${shortDate(r.dueOn)}`} · falta ${money(r.amount)}`;
    case 'RecurrenceDue':
      return `${r.urgency === 'Today' ? 'vence hoje' : `vence em ${shortDate(r.dueOn)}`} · ${money(r.amount)}`;
    case 'RecurrencesToLaunch':
      return `pendentes desde ${shortDate(r.dueOn)}: lance ou pule`;
    default:
      return shortDate(r.dueOn);
  }
}

interface Props {
  /** Muda a cada troca de aba: os avisos são relidos (algo pode ter sido resolvido). */
  refreshKey: unknown;
  onOpen: (reminder: Reminder) => void;
}

/**
 * NTF-3 (ADR-0009, decisões 1 e 7): sino com o número de avisos (vencidos em destaque) e o painel, do
 * mais urgente para o menos. Relê ao montar, ao trocar de aba, ao abrir o painel e quando a janela volta
 * ao foco. Falha ao ler os avisos não atrapalha o app: o sino só fica sem número.
 */
export function RemindersBell({ refreshKey, onOpen }: Props) {
  const [reminders, setReminders] = useState<Reminders | null>(null);
  const [open, setOpen] = useState(false);
  const panelRef = useRef<HTMLDivElement>(null);

  const load = useCallback(() => {
    remindersApi
      .list()
      .then(setReminders)
      .catch(() => undefined);
  }, []);

  useEffect(() => {
    load();
  }, [load, refreshKey]);

  useEffect(() => {
    const onFocus = () => load();
    window.addEventListener('focus', onFocus);
    return () => window.removeEventListener('focus', onFocus);
  }, [load]);

  // Fecha ao clicar fora do painel.
  useEffect(() => {
    if (!open) return;
    const onClick = (e: MouseEvent) => {
      if (panelRef.current && !panelRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onClick);
    return () => document.removeEventListener('mousedown', onClick);
  }, [open]);

  /** NTF-5: some na hora (otimista) e relê; se falhar, a releitura o traz de volta. */
  async function dismiss(key: string) {
    setReminders((current) =>
      current ? { ...current, items: current.items.filter((i) => i.key !== key) } : current,
    );
    try {
      await remindersApi.dismiss(key);
    } finally {
      load();
    }
  }

  const items = reminders?.items ?? [];
  const money = (value: number) => value.toLocaleString('pt-BR', { style: 'currency', currency: reminders?.currency ?? 'BRL' });

  return (
    <div className="reminders" ref={panelRef}>
      <button
        type="button"
        className={`ghost bell${reminders && reminders.overdue > 0 ? ' has-overdue' : ''}`}
        data-cy="reminders-bell"
        aria-label={items.length === 0 ? 'Avisos: nenhum' : `Avisos: ${items.length}`}
        aria-expanded={open}
        onClick={() => {
          if (!open) load();
          setOpen(!open);
        }}
      >
        <span aria-hidden="true">🔔</span>
        {items.length > 0 && (
          <span className="badge" data-cy="reminders-count">
            {items.length}
          </span>
        )}
      </button>
      {open && (
        <div className="reminders-panel" data-cy="reminders-panel" role="dialog" aria-label="Avisos">
          <strong>Avisos</strong>
          {items.length === 0 ? (
            <p className="muted" data-cy="reminders-empty">
              Nada vencendo nos próximos {reminders?.leadDays ?? 3} dias.
            </p>
          ) : (
            <ul>
              {items.map((r) => (
                <li key={r.key} className="reminder-row" data-cy="reminder-row">
                  <button
                    type="button"
                    className={`reminder ${r.urgency.toLowerCase()}`}
                    data-cy="reminder-item"
                    data-kind={r.kind}
                    data-urgency={r.urgency}
                    onClick={() => {
                      setOpen(false);
                      onOpen(r);
                    }}
                  >
                    <span className="reminder-title">{r.title}</span>
                    <span className="sub">{detailOf(r, money)}</span>
                  </button>
                  <button
                    type="button"
                    className="link dismiss"
                    data-cy="reminder-dismiss"
                    title={r.kind === 'InvoiceDue' ? 'Dispensar (volta se a fatura vencer sem pagamento)' : 'Dispensar este aviso'}
                    aria-label={`Dispensar: ${r.title}`}
                    onClick={() => void dismiss(r.key)}
                  >
                    dispensar
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
