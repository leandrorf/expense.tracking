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
}

export const invoicesApi = {
  /** Faturas do cartão: 12 antes da atual até a última parcela (ADR-0005). */
  list: (cardId: string) => apiFetch<CardInvoices>(`/api/reports/cards/${cardId}/invoices`),
};
