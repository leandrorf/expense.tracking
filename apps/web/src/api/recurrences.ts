import { apiFetch } from './client';
import type { Transaction } from './types';

export type Frequency = 'Monthly' | 'Yearly';

/** REC-2: campos da regra (criar e editar). O início é a âncora da agenda (dia, e mês na anual). */
export interface RecurrenceInput {
  accountId: string;
  type: 'Expense' | 'Income';
  paymentMethod: string;
  creditCardId?: string;
  description: string;
  category: string;
  /** Valor estimado de cada ocorrência. */
  amount: number;
  currency?: string;
  notes?: string;
  frequency: Frequency;
  /** `yyyy-MM-dd`. */
  startsOn: string;
  /** `yyyy-MM-dd`, inclusivo; ausente = sem fim. */
  endsOn?: string;
  /** REC-10: lançamento que vira a ocorrência do próprio período ("repetir todo mês"). Só na criação. */
  fromTransactionId?: string;
}

export interface Recurrence extends Omit<RecurrenceInput, 'creditCardId' | 'notes' | 'endsOn' | 'currency' | 'fromTransactionId'> {
  id: string;
  creditCardId: string | null;
  notes: string | null;
  endsOn: string | null;
  currency: string;
  version: number;
  /** Próxima ocorrência em aberto de hoje em diante (nula quando a regra acabou). */
  nextOccurrence: { period: string; scheduledOn: string } | null;
  createdAtUtc: string;
}

/** REC-3: ocorrência que já devia ter acontecido e não foi lançada nem pulada. */
export interface DueOccurrence {
  recurrenceId: string;
  /** `yyyy-MM` (mensal) ou `yyyy` (anual). */
  period: string;
  scheduledOn: string;
  accountId: string;
  type: 'Expense' | 'Income';
  paymentMethod: string;
  creditCardId: string | null;
  description: string;
  category: string;
  amount: number;
  currency: string;
}

/** Lançar com o valor e a data reais (sem eles, valem os da regra e a data agendada). */
export interface LaunchInput {
  amount?: number;
  occurredOn?: string;
  notes?: string;
}

const base = '/api/transactions/recurrences';

export const recurrencesApi = {
  list: () => apiFetch<Recurrence[]>(base),

  create: (input: RecurrenceInput) => apiFetch<Recurrence>(base, { method: 'POST', body: JSON.stringify(input) }),

  /** Regra inteira mais a versão lida; 409 se outra aba salvou antes. */
  update: (id: string, input: RecurrenceInput & { version: number }) =>
    apiFetch<Recurrence>(`${base}/${id}`, { method: 'PUT', body: JSON.stringify(input) }),

  remove: (id: string) => apiFetch<void>(`${base}/${id}`, { method: 'DELETE' }),

  due: () => apiFetch<DueOccurrence[]>(`${base}/due`),

  launch: (id: string, period: string, input: LaunchInput = {}) =>
    apiFetch<Transaction>(`${base}/${id}/occurrences/${period}/launch`, { method: 'POST', body: JSON.stringify(input) }),

  skip: (id: string, period: string) => apiFetch<void>(`${base}/${id}/occurrences/${period}/skip`, { method: 'POST' }),

  unskip: (id: string, period: string) => apiFetch<void>(`${base}/${id}/occurrences/${period}/skip`, { method: 'DELETE' }),
};
