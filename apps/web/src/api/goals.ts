import { apiFetch } from './client';

/** MET (ADR-0013): escopo das metas, como na rota. */
export type GoalScope = 'personal' | 'business';

/** MET-2: forma da meta mensal. */
export type MonthlyGoalKind = 'SaveAmount' | 'SavePercent' | 'SpendCeiling';

export interface MonthlyGoalTerm {
  /** Mês (`yyyy-MM`) a partir do qual a vigência vale. */
  fromMonth: string;
  kind: MonthlyGoalKind;
  amount: number | null;
  percent: number | null;
}

export interface MonthlyGoal {
  id: string;
  scope: GoalScope;
  startMonth: string;
  /** Mês a partir do qual a meta não vale mais; `null` se aberta. */
  closedFromMonth: string | null;
  terms: MonthlyGoalTerm[];
}

export interface SetMonthlyGoalInput {
  kind: MonthlyGoalKind;
  amount?: number | null;
  percent?: number | null;
  fromMonth: string;
}

/** MET-3: situação do mês atual ou de um futuro. */
export type MonthlyGoalSituation = 'Inside' | 'Attention' | 'Outside';

/** MET-3: o mês num escopo; os campos da meta vêm nulos se o escopo não tem meta no mês. */
export interface MonthlyGoalScopeStatus {
  scope: GoalScope;
  kind: MonthlyGoalKind | null;
  amount: number | null;
  percent: number | null;
  target: number | null;
  income: number;
  expense: number;
  invoices: number;
  /** Saídas fora do cartão + faturas que vencem no mês. */
  outflows: number;
  /** Entradas − saídas − faturas, o mesmo resultado da previsão. */
  result: number;
  canStillSpend: number | null;
  situation: MonthlyGoalSituation | null;
}

export interface MonthlyGoalStatus {
  month: string;
  notice: string;
  personal: MonthlyGoalScopeStatus;
  business: MonthlyGoalScopeStatus;
}

export interface SavingsContribution {
  id: string;
  /** `yyyy-MM-dd`. */
  date: string;
  /** Negativo = retirada. */
  amount: number;
  note: string | null;
}

/** MET-6: situação do objetivo. */
export type SavingsObjectiveSituation = 'OnTrack' | 'Behind' | 'Reached' | 'DeadlinePassed';

export interface SavingsObjective {
  id: string;
  scope: GoalScope;
  name: string;
  target: number;
  /** Mês do prazo (`yyyy-MM`); `null` sem prazo. */
  deadline: string | null;
  archived: boolean;
  saved: number;
  /** Do mais novo ao mais antigo. */
  contributions: SavingsContribution[];
  missing: number;
  monthsLeft: number | null;
  neededPerMonth: number | null;
  pace: number;
  /** Mês (`yyyy-MM`) em que o ritmo atual chega ao alvo. */
  expectedArrival: string | null;
  situation: SavingsObjectiveSituation;
}

export interface SavingsObjectiveInput {
  name: string;
  target: number;
  deadline: string | null;
}

export const goalsApi = {
  listMonthly: () => apiFetch<MonthlyGoal[]>('/api/goals/monthly'),

  setMonthly: (scope: GoalScope, input: SetMonthlyGoalInput) =>
    apiFetch<MonthlyGoal>(`/api/goals/monthly/${scope}`, { method: 'PUT', body: JSON.stringify(input) }),

  closeMonthly: (scope: GoalScope, fromMonth: string) =>
    apiFetch<MonthlyGoal>(`/api/goals/monthly/${scope}?fromMonth=${encodeURIComponent(fromMonth)}`, { method: 'DELETE' }),

  status: (month: string) => apiFetch<MonthlyGoalStatus>(`/api/goals/monthly/status?month=${encodeURIComponent(month)}`),

  listObjectives: (includeArchived = false) =>
    apiFetch<SavingsObjective[]>(`/api/goals/objectives${includeArchived ? '?includeArchived=true' : ''}`),

  createObjective: (scope: GoalScope, input: SavingsObjectiveInput) =>
    apiFetch<SavingsObjective>('/api/goals/objectives', { method: 'POST', body: JSON.stringify({ scope, ...input }) }),

  editObjective: (id: string, input: SavingsObjectiveInput) =>
    apiFetch<SavingsObjective>(`/api/goals/objectives/${id}`, { method: 'PUT', body: JSON.stringify(input) }),

  archiveObjective: (id: string) => apiFetch<SavingsObjective>(`/api/goals/objectives/${id}/archive`, { method: 'POST' }),

  unarchiveObjective: (id: string) => apiFetch<SavingsObjective>(`/api/goals/objectives/${id}/unarchive`, { method: 'POST' }),

  contribute: (id: string, date: string, amount: number, note: string | null) =>
    apiFetch<SavingsObjective>(`/api/goals/objectives/${id}/contributions`, {
      method: 'POST',
      body: JSON.stringify({ date, amount, note }),
    }),

  removeContribution: (id: string, contributionId: string) =>
    apiFetch<SavingsObjective>(`/api/goals/objectives/${id}/contributions/${contributionId}`, { method: 'DELETE' }),
};
