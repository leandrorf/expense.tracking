import { apiFetch } from './client';

export type ReminderKind = 'InvoiceDue' | 'InvoiceOverdue' | 'RecurrenceDue' | 'RecurrencesToLaunch' | 'Budget';
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
}

export interface Reminders {
  currency: string;
  leadDays: number;
  /** Quantos avisos são de algo já vencido. */
  overdue: number;
  items: Reminder[];
}

export const remindersApi = {
  list: () => apiFetch<Reminders>('/api/reports/reminders'),
};
