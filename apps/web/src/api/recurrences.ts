import { apiFetch } from './client';
import type { Transaction } from './types';

export type Frequency = 'Monthly' | 'Yearly';

export type EconomicIndex = 'IPCA' | 'INPC' | 'IGPM';

/** RJT-4 (ADR-0010, decisão 3): reajuste anual da regra, pelo índice ou por percentual fixo. */
export interface RecurrenceAdjustment {
  /** 1 a 12; na anual, o próprio mês dela. Sem ele, vale o mês do início. */
  anniversaryMonth?: number;
  kind: 'Index' | 'Fixed';
  index?: EconomicIndex | null;
  /** Percentual (só no `Fixed`). */
  rate?: number | null;
  /** Não deixar o reajuste reduzir o valor (padrão ligado). */
  noReduction?: boolean;
}

/** RJT-2: valor a partir de um período. */
export interface RecurrenceAmount {
  fromPeriod: string;
  amount: number;
}

/** RJT-4 (ADR-0010, decisão 5): reajuste do aniversário ainda não confirmado, com a sugestão quando há. */
export interface PendingAdjustment {
  year: number;
  period: string;
  currentAmount: number;
  rate: number | null;
  suggestedAmount: number | null;
  /** RJT-7: mês do índice (`yyyy-MM`) que falta para sugerir; dá para confirmar à mão assim mesmo. */
  waitingFor: string | null;
}

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
  /** RJT-4: reajuste anual; ausente = sem reajuste (no PUT, tira o que havia). */
  adjustment?: RecurrenceAdjustment | null;
}

export interface Recurrence
  extends Omit<RecurrenceInput, 'creditCardId' | 'notes' | 'endsOn' | 'currency' | 'fromTransactionId' | 'adjustment'> {
  id: string;
  creditCardId: string | null;
  notes: string | null;
  endsOn: string | null;
  currency: string;
  version: number;
  /** Próxima ocorrência em aberto de hoje em diante (nula quando a regra acabou), com o valor do período. */
  nextOccurrence: { period: string; scheduledOn: string; amount: number } | null;
  createdAtUtc: string;
  /** RJT-2: todas as vigências, a primeira (o início) inclusive. `amount` é o valor que vale hoje. */
  amounts: RecurrenceAmount[];
  adjustment: Required<RecurrenceAdjustment> | null;
  pendingAdjustment: PendingAdjustment | null;
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

  /** RJT-2: o valor passa a `amount` a partir de `fromPeriod` (período da agenda, ainda em aberto). */
  setAmount: (id: string, fromPeriod: string, amount: number, version: number) =>
    apiFetch<Recurrence>(`${base}/${id}/amounts`, { method: 'POST', body: JSON.stringify({ fromPeriod, amount, version }) }),

  /** RJT-2: tira um valor que ainda não começou a valer. */
  removeAmount: (id: string, fromPeriod: string, version: number) =>
    apiFetch<Recurrence>(`${base}/${id}/amounts/${fromPeriod}?version=${version}`, { method: 'DELETE' }),

  /** RJT-4: confirma o reajuste de um aniversário pelo percentual ou pelo valor novo. */
  applyAdjustment: (id: string, input: { year: number; rate?: number; amount?: number; version: number }) =>
    apiFetch<Recurrence>(`${base}/${id}/adjustments`, { method: 'POST', body: JSON.stringify(input) }),

  due: () => apiFetch<DueOccurrence[]>(`${base}/due`),

  launch: (id: string, period: string, input: LaunchInput = {}) =>
    apiFetch<Transaction>(`${base}/${id}/occurrences/${period}/launch`, { method: 'POST', body: JSON.stringify(input) }),

  skip: (id: string, period: string) => apiFetch<void>(`${base}/${id}/occurrences/${period}/skip`, { method: 'POST' }),

  unskip: (id: string, period: string) => apiFetch<void>(`${base}/${id}/occurrences/${period}/skip`, { method: 'DELETE' }),
};
