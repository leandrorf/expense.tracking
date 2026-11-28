export interface Account {
  id: string;
  name: string;
  scope: 'Personal' | 'Business';
  taxId: string | null;
  currency: string;
  isActive: boolean;
  createdAtUtc: string;
  /** INV-13: saldo informado no fim do dia `openingBalanceOn` (`yyyy-MM-dd`); nulo = sem saldo. */
  openingBalance: number | null;
  openingBalanceOn: string | null;
}

export interface CreditCard {
  id: string;
  accountId: string;
  label: string;
  brand: string;
  lastFourDigits: string;
  creditLimit: number;
  /** CYC-3: fechamento e vencimento do ciclo de hoje. */
  closingDay: number;
  dueDay: number;
  isActive: boolean;
  createdAtUtc: string;
  /** CYC-3: todos os ciclos, com vigência (`fromDueMonth` nulo = o da criação, desde sempre). */
  cycles?: CardCycle[];
}

/** CYC-3 (ADR-0012): um ciclo do cartão valendo a partir da fatura que vence em `fromDueMonth` (`yyyy-MM`). */
export interface CardCycle {
  fromDueMonth: string | null;
  closingDay: number;
  dueDay: number;
}

export interface PaymentMethod {
  code: string;
  name: string;
  requiresCard: boolean;
}

export interface Transaction {
  id: string;
  accountId: string;
  type: 'Expense' | 'Income';
  paymentMethod: string;
  creditCardId: string | null;
  description: string;
  category: string;
  amount: number;
  currency: string;
  occurredOn: string;
  notes: string | null;
  createdAtUtc: string;
  /** INV-5: parcelas no cartão de crédito (1 = à vista). */
  installments: number;
  /** LNC-3: versão lida; a edição a devolve e recebe 409 se ela mudou. */
  version: number;
  /** LNC-3: veio de uma importação (desfazer a importação ainda o apaga). */
  imported: boolean;
  /** REC-3: regra de recorrência de onde o lançamento veio, e o período que ele fechou. */
  recurrenceId?: string | null;
  recurrencePeriod?: string | null;
}
