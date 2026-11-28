import type { GoalScope, MonthlyGoal, MonthlyGoalTerm } from './api/goals';

/**
 * MET-8 (ADR-0013, regras 3 e 4): a vigência da meta num mês e a sobra depois dela, com a mesma regra do reporting
 * (`MonthlyGoal.TermFor` e `MonthlyGoalStatusCalculator`), para a previsão mostrar a meta de cada mês sem outra chamada.
 */

/** A vigência que vale no mês (`yyyy-MM`) no escopo, ou `null` se não há meta nele. */
export function termFor(goals: MonthlyGoal[], scope: GoalScope, month: string): MonthlyGoalTerm | null {
  for (const goal of goals) {
    if (goal.scope !== scope || month < goal.startMonth) continue;
    if (goal.closedFromMonth !== null && month >= goal.closedFromMonth) continue;
    const terms = goal.terms.filter((t) => t.fromMonth <= month).sort((a, b) => a.fromMonth.localeCompare(b.fromMonth));
    if (terms.length > 0) return terms[terms.length - 1];
  }
  return null;
}

/** Arredonda em 2 casas com o meio para longe do zero, como o `MidpointRounding.AwayFromZero` do reporting. */
function round2(value: number): number {
  return (Math.sign(value) * Math.round(Math.abs(value) * 100 + Number.EPSILON)) / 100;
}

/**
 * O alvo do mês e a sobra depois da meta: para guardar, resultado − alvo; para o teto, teto − saídas. Negativa quando
 * a meta não fecha.
 */
export function goalOfMonth(
  term: MonthlyGoalTerm,
  flow: { income: number; expense: number; invoices: number },
): { target: number; left: number } {
  const outflows = flow.expense + flow.invoices;
  const target = term.kind === 'SavePercent' ? round2((flow.income * (term.percent ?? 0)) / 100) : term.amount ?? 0;
  const left = term.kind === 'SpendCeiling' ? target - outflows : flow.income - outflows - target;
  return { target, left: round2(left) };
}
