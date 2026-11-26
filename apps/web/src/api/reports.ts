import { apiFetch } from './client';

export interface TotalByKey {
  key: string;
  total: number;
}

export interface ReportSummary {
  currency: string;
  totalExpense: number;
  totalIncome: number;
  net: number;
  count: number;
  byCategory: TotalByKey[];
  byScope: TotalByKey[];
  byPaymentMethod: TotalByKey[];
}

export interface SummaryFilters {
  accountId?: string;
  from?: string;
  to?: string;
}

export const reportsApi = {
  summary: (filters: SummaryFilters = {}) => {
    const params = new URLSearchParams();
    if (filters.accountId) params.set('accountId', filters.accountId);
    if (filters.from) params.set('from', filters.from);
    if (filters.to) params.set('to', filters.to);
    const query = params.toString();
    return apiFetch<ReportSummary>(`/api/reports/summary${query ? `?${query}` : ''}`);
  },
};

/** INV-4/INV-6: situação calculada da fatura (nunca guardada). */
export type InvoiceStatus = 'Open' | 'Closed' | 'Paid' | 'PartiallyPaid' | 'Overdue';

export interface InvoiceItem {
  transactionId: string;
  description: string | null;
  category: string;
  purchaseDate: string;
  amount: number;
  installment: number;
  installments: number;
}

export interface InvoicePayment {
  paymentId: string;
  accountId: string;
  amount: number;
  paidOn: string;
}

export interface Invoice {
  /** Mês de vencimento, `yyyy-MM`: identifica a fatura. */
  dueMonth: string;
  startDate: string;
  closingDate: string;
  dueDate: string;
  total: number;
  paid: number;
  remaining: number;
  status: InvoiceStatus;
  items: InvoiceItem[];
  payments: InvoicePayment[];
  /** RCN-2: conferência com a fatura do banco, quando marcada. */
  reconciliation?: InvoiceReconciliation | null;
}

/** RCN-2 (ADR-0011): conferência guardada: total do banco, total do app na hora e a diferença aceita. */
export interface InvoiceReconciliation {
  bankTotal: number;
  appTotal: number;
  difference: number;
  reconciledAtUtc: string;
}

export type ReconciliationGroup = 'Matched' | 'DifferentAmount' | 'OnlyInBank' | 'OnlyInApp';

/** RCN-4: uma entrada da conciliação: linha do banco, item do app, ou os dois (casados). */
export interface ReconciliationEntry {
  group: ReconciliationGroup;
  rule: 'Key' | 'Installment' | 'AmountAndDate' | null;
  lineId: string | null;
  lineNumber: number | null;
  bankDate: string | null;
  bankAmount: number | null;
  bankDescription: string | null;
  transactionId: string | null;
  appDate: string | null;
  appAmount: number | null;
  appDescription: string | null;
  installment: number | null;
  installments: number | null;
  /** Quanto da diferença (banco − app) esta entrada explica. */
  explains: number;
}

export interface ReconciliationDetail {
  batchId: string;
  /** Total do banco pelo arquivo: o declarado no OFX ou a soma das linhas. */
  fileTotal: number;
  statedTotal: number | null;
  linesTotal: number;
  difference: number;
  suggestedDueMonth: string | null;
  explanation: { onlyInBank: number; onlyInApp: number; differentAmount: number; unexplained: number };
  entries: ReconciliationEntry[];
}

export interface InvoiceReconciliationView {
  dueMonth: string;
  /** Total da fatura do app agora. */
  appTotal: number;
  bankTotal: number | null;
  difference: number | null;
  reconciliation: InvoiceReconciliation | null;
  detail?: ReconciliationDetail | null;
}

export interface CardInvoices {
  cardId: string;
  label: string;
  lastFourDigits: string;
  closingDay: number;
  dueDay: number;
  currency: string;
  currentDueMonth: string;
  invoices: Invoice[];
  /** INV-10: limite, o que falta pagar de todas as faturas (com parcelas futuras) e o disponível. */
  creditLimit: number;
  outstanding: number;
  availableLimit: number;
}

/** INV-10: limite disponível de um cartão; negativo quando passou do limite (nada é bloqueado). */
export interface CardLimit {
  cardId: string;
  creditLimit: number;
  outstanding: number;
  availableLimit: number;
}

export const invoicesApi = {
  /** Faturas do cartão: 12 antes da atual até a última parcela (ADR-0005). */
  list: (cardId: string) => apiFetch<CardInvoices>(`/api/reports/cards/${cardId}/invoices`),

  limits: () => apiFetch<CardLimit[]>('/api/reports/cards/limits'),

  /** RCN-2/RCN-4: a fatura vista para conferir; com `batchId`, o detalhe contra o arquivo do banco. */
  reconciliation: (cardId: string, dueMonth: string, batchId?: string) =>
    apiFetch<InvoiceReconciliationView>(
      `/api/reports/cards/${cardId}/invoices/${dueMonth}/reconciliation${batchId ? `?batchId=${batchId}` : ''}`,
    ),

  /** RCN-2: marca (ou substitui) a conferência com o total da fatura do banco. */
  reconcile: (cardId: string, dueMonth: string, bankTotal: number) =>
    apiFetch<InvoiceReconciliationView>(`/api/reports/cards/${cardId}/invoices/${dueMonth}/reconciliation`, {
      method: 'PUT',
      body: JSON.stringify({ bankTotal }),
    }),

  undoReconciliation: (cardId: string, dueMonth: string) =>
    apiFetch<void>(`/api/reports/cards/${cardId}/invoices/${dueMonth}/reconciliation`, { method: 'DELETE' }),
};

/** INV-8: fluxo de um mês num escopo. `result` = entradas − saídas − faturas (total). */
export interface MonthFlow {
  income: number;
  expense: number;
  invoices: number;
  invoicesPaid: number;
  invoicesRemaining: number;
  result: number;
  /** INV-13: saldo projetado no fim do mês; nulo sem conta com saldo informado no escopo. */
  balance: number | null;
  /** REC-4: parte das entradas, saídas e faturas que vem de recorrências ainda não lançadas. */
  recurringIncome: number;
  recurringExpense: number;
  recurringInvoices: number;
  /** RJT-8: quanto da parte recorrente é reajuste estimado, ainda não confirmado (entradas e saídas somadas). */
  estimatedRecurring: number;
}

export interface ForecastMonth {
  /** `yyyy-MM`. */
  month: string;
  personal: MonthFlow;
  business: MonthFlow;
  total: MonthFlow;
}

export interface CashForecast {
  currency: string;
  notice: string;
  months: ForecastMonth[];
  /** INV-13: contas ativas sem saldo informado (ficam fora do saldo projetado). */
  accountsWithoutBalance: number;
}

export const forecastApi = {
  /** Do mês atual até `months` meses (1 a 12). */
  get: (months: number) => apiFetch<CashForecast>(`/api/reports/cash-forecast?months=${months}`),
};
