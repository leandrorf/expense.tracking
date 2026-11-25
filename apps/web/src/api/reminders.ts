import { apiFetch } from './client';

export type ReminderKind =
  | 'InvoiceDue'
  | 'InvoiceOverdue'
  | 'RecurrenceDue'
  | 'RecurrencesToLaunch'
  | 'Budget'
  | 'RecurrenceAdjustment';
export type ReminderUrgency = 'Overdue' | 'Today' | 'Soon';

/** NTF-2 (ADR-0009): um aviso, calculado na hora pelo reporting. */
export interface Reminder {
  kind: ReminderKind;
  /** Estável por item e estado (para dispensar). */
  key: string;
  urgency: ReminderUrgency;
  /** `yyyy-MM-dd`: o vencimento (no "a lançar", a pendência mais antiga). */
  dueOn: string;
  amount: number;
  title: string;
  /** Fatura: cartão e mês de vencimento (`yyyy-MM`), para abrir a fatura. */
  cardId: string | null;
  dueMonth: string | null;
  /** Ocorrência: regra e período. */
  recurrenceId: string | null;
  period: string | null;
  /** "A lançar": quantas ocorrências. */
  count: number;
  /** NTF-9, orçamento: o limite do mês (`amount` é o consumido). */
  limit: number;
  /** RJT-9, reajuste: valor atual (`amount` é o estimado), taxa (nula = aguardando o índice) e índice. */
  previousAmount: number | null;
  rate: number | null;
  index: string | null;
}

export interface Reminders {
  currency: string;
  leadDays: number;
  /** Quantos avisos são de algo já vencido. */
  overdue: number;
  items: Reminder[];
}

/** NTF-6: preferências dos avisos (sem gravação, os padrões: 3 dias e tudo ligado). */
export interface ReminderSettings {
  /** Dias corridos de antecedência, de 0 a 15. */
  leadDays: number;
  invoices: boolean;
  recurrences: boolean;
  budgets: boolean;
}

/** NTF-8: resumo diário por e-mail; só sai com o endereço confirmado pelo link. */
export interface ReminderEmail {
  email: string | null;
  enabled: boolean;
  emailConfirmed: boolean;
  /** Hora local (0 a 23). */
  sendHour: number;
}

/** O que `GET /reminder-settings` devolve: os tipos e o e-mail. */
export interface ReminderSettingsView extends ReminderSettings {
  email: ReminderEmail;
}

export const remindersApi = {
  list: () => apiFetch<Reminders>('/api/reports/reminders'),

  /** NTF-5: esconde o aviso até a chave dele mudar (ex.: a fatura vence sem pagamento). */
  dismiss: (key: string) =>
    apiFetch<void>('/api/reports/reminders/dismissals', { method: 'POST', body: JSON.stringify({ key }) }),

  settings: () => apiFetch<ReminderSettingsView>('/api/reports/reminder-settings'),

  saveSettings: (settings: ReminderSettings) =>
    apiFetch<ReminderSettings>('/api/reports/reminder-settings', { method: 'PUT', body: JSON.stringify(settings) }),

  /** NTF-8: trocar o endereço manda um link de confirmação para ele. */
  saveEmail: (input: { email: string | null; enabled: boolean; sendHour: number }) =>
    apiFetch<ReminderEmail>('/api/reports/reminder-settings/email', { method: 'PUT', body: JSON.stringify(input) }),

  confirmEmail: (token: string) =>
    apiFetch<ReminderEmail>('/api/reports/reminder-settings/email/confirm', { method: 'POST', body: JSON.stringify({ token }) }),

  resendEmail: () => apiFetch<void>('/api/reports/reminder-settings/email/resend', { method: 'POST' }),
};
