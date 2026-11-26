import type { CardCycle } from './api/types';

/**
 * CYC-5 (ADR-0012, decisão 2): a mesma regra dos períodos encadeados do accounts e do reporting, só para a
 * prévia da troca de ciclo na tela (quem valida e calcula de verdade é o servidor). Meses em `yyyy-MM`, datas
 * em `yyyy-MM-dd`, sem passar por fuso.
 */

export interface Term {
  from: string | null;
  closingDay: number;
  dueDay: number;
}

const pad = (n: number) => String(n).padStart(2, '0');

/** Hoje (data local), `yyyy-MM-dd`. */
export function todayIso(): string {
  const d = new Date();
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** `2027-03` + n meses. */
export function addMonths(month: string, n: number): string {
  const [y, m] = month.split('-').map(Number);
  const total = y * 12 + (m - 1) + n;
  return `${Math.floor(total / 12)}-${pad((total % 12) + 1)}`;
}

/** `2027-03-05` → `05/03/2027`. */
export const dateLabel = (iso: string) => iso.split('-').reverse().join('/');

/** `2027-03` → `03/2027`. */
export const monthLabel = (month: string) => month.split('-').reverse().join('/');

function closingOf(term: Term, dueMonth: string): string {
  const closingMonth = term.dueDay <= term.closingDay ? addMonths(dueMonth, -1) : dueMonth;
  return `${closingMonth}-${pad(term.closingDay)}`;
}

function dueOf(term: Term, dueMonth: string): string {
  return `${dueMonth}-${pad(term.dueDay)}`;
}

/** Os ciclos do cartão em ordem (sem o histórico, o ciclo de hoje vale desde sempre). */
export function termsOf(cycles: CardCycle[] | undefined, closingDay: number, dueDay: number): Term[] {
  const base: Term[] = cycles?.length
    ? cycles.map((c) => ({ from: c.fromDueMonth, closingDay: c.closingDay, dueDay: c.dueDay }))
    : [{ from: null, closingDay, dueDay }];
  return base.sort((a, b) => (a.from ?? '').localeCompare(b.from ?? ''));
}

/** Os ciclos depois da troca proposta (que substitui as vigências do mês dela em diante). */
export function withProposed(terms: Term[], proposed: Term): Term[] {
  return [...terms.filter((t) => t.from === null || t.from < proposed.from!), proposed];
}

function indexOf(terms: Term[], month: string): number {
  for (let i = terms.length - 1; i > 0; i--) if (terms[i].from! <= month) return i;
  return 0;
}

/** Se a fatura do mês existe (some quando fecharia no mesmo dia ou depois do primeiro fechamento do ciclo seguinte). */
export function exists(terms: Term[], month: string): boolean {
  const i = indexOf(terms, month);
  if (i === terms.length - 1) return true;
  const next = terms[i + 1];
  return closingOf(terms[i], month) < closingOf(next, next.from!);
}

/** Período da fatura do mês: início (dia seguinte ao fechamento da anterior que existe), fechamento e vencimento. */
export function periodOf(terms: Term[], month: string): { start: string; closing: string; due: string } {
  const term = terms[indexOf(terms, month)];
  let previous = addMonths(month, -1);
  for (let i = 0; i < 3 && !exists(terms, previous); i++) previous = addMonths(previous, -1);
  const previousClosing = closingOf(terms[indexOf(terms, previous)], previous);
  const [y, m, d] = previousClosing.split('-').map(Number);
  const start = new Date(Date.UTC(y, m - 1, d + 1));
  return {
    start: `${start.getUTCFullYear()}-${pad(start.getUTCMonth() + 1)}-${pad(start.getUTCDate())}`,
    closing: closingOf(term, month),
    due: dueOf(term, month),
  };
}

/** A troca pode começar no mês (decisão 3): primeiro fechamento novo não passou e a fatura substituída não fechou. */
export function canStartAt(current: Term[], proposed: Term, today: string): boolean {
  const month = proposed.from!;
  if (current.some((t) => t.from !== null && t.from >= month)) return false;
  return closingOf(proposed, month) >= today && (!exists(current, month) || closingOf(current[indexOf(current, month)], month) >= today);
}

/** Primeira fatura (a partir do mês de hoje) em que a troca pode começar. */
export function firstPossibleMonth(current: Term[], closingDay: number, dueDay: number, today: string): string | null {
  const thisMonth = today.slice(0, 7);
  for (let i = 0; i < 4; i++) {
    const month = addMonths(thisMonth, i);
    if (canStartAt(current, { from: month, closingDay, dueDay }, today)) return month;
  }
  return null;
}

/** Trocas que ainda não começaram a valer (o início da fatura delas é depois de hoje). */
export function pendingChanges(cycles: CardCycle[] | undefined, closingDay: number, dueDay: number, today: string): CardCycle[] {
  const terms = termsOf(cycles, closingDay, dueDay);
  return (cycles ?? []).filter((c) => c.fromDueMonth !== null && periodOf(terms, c.fromDueMonth).start > today);
}
