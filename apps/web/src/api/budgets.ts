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
  /** Percentual do limite a partir do qual o status é Atenção (padrão 80). */
  warningThreshold: number;
}

export interface CreateBudgetInput {
  category: string;
  scope: BudgetScope;
  amount: number;
  startMonth: string;
  currency?: string;
  warningThreshold?: number;
}

/** Situação no mês: abaixo do limiar de atenção (padrão 80%), do limiar a 100%, acima de 100%. */
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
  warningThreshold: number;
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

  /** Categorias já usadas (lançamentos + orçamentos), sem variações de maiúscula/acento. */
  categories: () => apiFetch<string[]>('/api/budgets/categories'),

  status: (month: string) => apiFetch<BudgetMonth>(`/api/budgets/status?month=${encodeURIComponent(month)}`),

  create: (input: CreateBudgetInput) =>
    apiFetch<Budget>('/api/budgets', { method: 'POST', body: JSON.stringify(input) }),

  changeAmount: (id: string, amount: number, fromMonth: string) =>
    apiFetch<Budget>(`/api/budgets/${id}/amount`, { method: 'PUT', body: JSON.stringify({ amount, fromMonth }) }),

  changeWarningThreshold: (id: string, warningThreshold: number) =>
    apiFetch<Budget>(`/api/budgets/${id}/warning-threshold`, {
      method: 'PUT',
      body: JSON.stringify({ warningThreshold }),
    }),

  close: (id: string, fromMonth: string) =>
    apiFetch<Budget>(`/api/budgets/${id}/close`, { method: 'POST', body: JSON.stringify({ fromMonth }) }),
};
