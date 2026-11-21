import { apiFetch } from './client';

export type BudgetScope = 'Personal' | 'Business';

export interface BudgetLimit {
  /** Mês (`yyyy-MM`) a partir do qual o valor vale. */
  fromMonth: string;
  amount: number;
}

export interface Budget {
  id: string;
  category: string;
  scope: BudgetScope;
  /** Valor da vigência mais recente (ou do mês consultado, com `?month=`). */
  amount: number;
  currency: string;
  startMonth: string;
  /** Mês a partir do qual o orçamento não vale mais; `null` se aberto. */
  closedFromMonth: string | null;
  limits: BudgetLimit[];
}

export interface CreateBudgetInput {
  category: string;
  scope: BudgetScope;
  amount: number;
  startMonth: string;
  currency?: string;
}

/** Situação no mês: abaixo de 80%, de 80% a 100%, acima de 100%. */
export type BudgetHealth = 'OnTrack' | 'Warning' | 'Exceeded';

export interface BudgetStatus {
  budgetId: string;
  category: string;
  scope: BudgetScope;
  currency: string;
  limit: number;
  spent: number;
  /** Negativo quando estourado (o quanto passou). */
  remaining: number;
  percent: number;
  status: BudgetHealth;
}

/** Gastos do mês que não caem em nenhum orçamento vigente, num escopo e moeda. */
export interface UnbudgetedSpending {
  scope: BudgetScope;
  currency: string;
  total: number;
  categories: { category: string; total: number }[];
}

export interface BudgetMonth {
  month: string;
  budgets: BudgetStatus[];
  unbudgeted: UnbudgetedSpending[];
}

export const budgetsApi = {
  list: () => apiFetch<Budget[]>('/api/budgets'),

  status: (month: string) => apiFetch<BudgetMonth>(`/api/budgets/status?month=${encodeURIComponent(month)}`),

  create: (input: CreateBudgetInput) =>
    apiFetch<Budget>('/api/budgets', { method: 'POST', body: JSON.stringify(input) }),

  changeAmount: (id: string, amount: number, fromMonth: string) =>
    apiFetch<Budget>(`/api/budgets/${id}/amount`, { method: 'PUT', body: JSON.stringify({ amount, fromMonth }) }),

  close: (id: string, fromMonth: string) =>
    apiFetch<Budget>(`/api/budgets/${id}/close`, { method: 'POST', body: JSON.stringify({ fromMonth }) }),
};
