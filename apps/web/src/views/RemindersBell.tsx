import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '../api/client';
import { remindersApi, type Reminder, type ReminderEmail, type ReminderSettings, type Reminders } from '../api/reminders';

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
  // NTF-6: preferências editadas dentro do painel.
  const [settings, setSettings] = useState<ReminderSettings | null>(null);
  const [settingsError, setSettingsError] = useState<string | null>(null);
  // O campo guarda o texto digitado: apagar para redigitar nao vira "0" no meio do caminho.
  const [leadDaysText, setLeadDaysText] = useState('');
  // NTF-8: resumo por e-mail (o endereço gravado, para saber se mudou).
  const [email, setEmail] = useState<ReminderEmail | null>(null);
  const [emailText, setEmailText] = useState('');
  const [emailNotice, setEmailNotice] = useState<string | null>(null);
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

  async function toggleSettings() {
    setSettingsError(null);
    if (settings) {
      setSettings(null);
      return;
    }
    try {
      const { email: currentEmail, ...current } = await remindersApi.settings();
      setSettings(current);
      setLeadDaysText(String(current.leadDays));
      setEmail(currentEmail);
      setEmailText(currentEmail.email ?? '');
      setEmailNotice(null);
    } catch (err) {
      setSettingsError(err instanceof Error ? err.message : 'Erro inesperado.');
    }
  }

  async function saveSettings(e: React.FormEvent) {
    e.preventDefault();
    if (!settings) return;
    setSettingsError(null);
    try {
      await remindersApi.saveSettings({ ...settings, leadDays: Number(leadDaysText) });
      if (email) {
        const typed = emailText.trim() || null;
        const saved = await remindersApi.saveEmail({ email: typed, enabled: email.enabled, sendHour: email.sendHour });
        if (typed && !saved.emailConfirmed && typed.toLowerCase() !== (email.email ?? '')) {
          // Fica aberto para mostrar o aviso: o link foi para o endereço novo.
          setEmail(saved);
          setEmailNotice(`Enviamos um link de confirmação para ${typed}. O resumo só sai depois de confirmar.`);
          load();
          return;
        }
      }
      setSettings(null);
      load();
    } catch (err) {
      setSettingsError(err instanceof ApiError || err instanceof Error ? err.message : 'Erro inesperado.');
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
          <div className="reminders-head">
            <strong>Avisos</strong>
            <button type="button" className="link" data-cy="reminders-settings-toggle" onClick={() => void toggleSettings()}>
              {settings ? 'fechar preferências' : 'preferências'}
            </button>
          </div>
          {settingsError && <p className="error">{settingsError}</p>}
          {settings && (
            <form className="reminders-settings" data-cy="reminders-settings" onSubmit={saveSettings}>
              <label>
                Avisar com{' '}
                <input
                  data-cy="reminders-lead-days"
                  type="number"
                  min={0}
                  max={15}
                  value={leadDaysText}
                  onChange={(e) => setLeadDaysText(e.target.value)}
                  required
                />{' '}
                dias de antecedência
              </label>
              <label>
                <input
                  type="checkbox"
                  data-cy="reminders-kind-invoices"
                  checked={settings.invoices}
                  onChange={(e) => setSettings({ ...settings, invoices: e.target.checked })}
                />{' '}
                faturas
              </label>
              <label>
                <input
                  type="checkbox"
                  data-cy="reminders-kind-recurrences"
                  checked={settings.recurrences}
                  onChange={(e) => setSettings({ ...settings, recurrences: e.target.checked })}
                />{' '}
                recorrências
              </label>
              <label>
                <input
                  type="checkbox"
                  data-cy="reminders-kind-budgets"
                  checked={settings.budgets}
                  onChange={(e) => setSettings({ ...settings, budgets: e.target.checked })}
                />{' '}
                orçamentos
              </label>
              <fieldset className="reminders-email" data-cy="reminders-email-section">
                <legend>Resumo diário por e-mail</legend>
                <input
                  data-cy="reminders-email"
                  type="email"
                  placeholder="seu@email.com"
                  aria-label="E-mail para o resumo"
                  value={emailText}
                  onChange={(e) => setEmailText(e.target.value)}
                />
                <label>
                  <input
                    type="checkbox"
                    data-cy="reminders-email-enabled"
                    checked={email?.enabled ?? false}
                    onChange={(e) => email && setEmail({ ...email, enabled: e.target.checked })}
                  />{' '}
                  receber às{' '}
                  <select
                    data-cy="reminders-email-hour"
                    aria-label="Hora do resumo"
                    value={email?.sendHour ?? 8}
                    onChange={(e) => email && setEmail({ ...email, sendHour: Number(e.target.value) })}
                  >
                    {Array.from({ length: 24 }, (_, h) => (
                      <option key={h} value={h}>
                        {String(h).padStart(2, '0')}h
                      </option>
                    ))}
                  </select>
                </label>
                {email?.email && (
                  <span className="sub" data-cy="reminders-email-status" data-confirmed={email.emailConfirmed}>
                    {email.emailConfirmed ? (
                      'e-mail confirmado'
                    ) : (
                      <>
                        aguardando confirmação: abra o link enviado para {email.email}{' '}
                        <button
                          type="button"
                          className="link"
                          data-cy="reminders-email-resend"
                          onClick={() =>
                            void remindersApi
                              .resendEmail()
                              .then(() => setEmailNotice(`Link reenviado para ${email.email}.`))
                              .catch((err: unknown) => setSettingsError(err instanceof Error ? err.message : 'Erro inesperado.'))
                          }
                        >
                          reenviar link
                        </button>
                      </>
                    )}
                  </span>
                )}
                {emailNotice && (
                  <span className="sub" data-cy="reminders-email-notice">
                    {emailNotice}
                  </span>
                )}
              </fieldset>
              <button type="submit" className="primary small" data-cy="reminders-settings-save">
                Salvar
              </button>
            </form>
          )}
          {!reminders ? (
            <p className="muted" data-cy="reminders-loading">
              Carregando…
            </p>
          ) : items.length === 0 ? (
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
