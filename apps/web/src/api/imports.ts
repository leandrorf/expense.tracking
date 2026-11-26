import { apiFetch } from './client';

/** IMP-4/IMP-5: lote de importação (prévia guardada no servidor até confirmar). */
export type ImportLineStatus = 'New' | 'Duplicate' | 'Invalid' | 'ExcludedByDefault' | 'Imported';

export interface ImportLine {
  id: string;
  number: number;
  status: ImportLineStatus;
  included: boolean;
  occurredOn: string | null;
  type: 'Expense' | 'Income' | null;
  amount: number | null;
  description: string;
  paymentMethod: string | null;
  category: string | null;
  problem: string | null;
  transactionId: string | null;
  /** INV-11: saída de extrato sugerida (ou marcada) como pagamento de fatura; não vira gasto. */
  invoicePayment: boolean;
  invoiceCardId: string | null;
  /** Fatura paga, pelo mês de vencimento (`yyyy-MM`). */
  invoiceDueMonth: string | null;
  cardPaymentId: string | null;
  /** REC-9: ocorrência em aberto de uma recorrência que a linha sugere ser (nula quando não casou). */
  recurrenceId?: string | null;
  recurrencePeriod?: string | null;
  recurrenceDescription?: string | null;
  /** RCN-3/RCN-5: na linha duplicada, o lançamento que ela repete (inclusive a compra da parcela). */
  duplicateOf?: string | null;
}

export interface ImportSummary {
  total: number;
  new: number;
  duplicate: number;
  invalid: number;
  excludedByDefault: number;
  included: number;
  imported: number;
}

export interface ImportBatch {
  id: string;
  status: 'Pending' | 'Confirmed' | 'Discarded' | 'Undone';
  format: 'Ofx' | 'Csv';
  kind: 'BankAccount' | 'CreditCard';
  accountId: string;
  creditCardId: string | null;
  currency: string;
  defaultPaymentMethod: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  confirmedAtUtc: string | null;
  undoneAtUtc: string | null;
  summary: ImportSummary;
  lines: ImportLine[];
  /** RCN-3: o que a fatura do banco declara no OFX (total e período). */
  statedTotal?: number | null;
  statementStart?: string | null;
  statementEnd?: string | null;
}

export interface CsvColumnMapping {
  dateColumn: number;
  descriptionColumn: number;
  amountColumn: number | null;
  debitColumn: number | null;
  creditColumn: number | null;
  hasHeader: boolean;
  decimalSeparator: string;
  invertSign: boolean;
}

export interface CsvMappingProposal {
  mapping: CsvColumnMapping | null;
  headers: string[] | null;
  columnCount: number;
  preview: { lineNumber: number; cells: string[] }[];
}

/** IMP-11: mapeamento confirmado e guardado com um nome, com o cabecalho do arquivo em que foi salvo. */
export interface SavedCsvMapping {
  id: string;
  name: string;
  mapping: CsvColumnMapping;
  headers: string[];
  updatedAtUtc: string;
}

/** Nome para salvar o mapeamento e o cabeçalho do arquivo (para reconhecer o mesmo layout depois). */
export interface CsvMappingToSave {
  name: string;
  headers: string[];
}

export interface ImportUpload {
  file: File;
  accountId: string;
  creditCardId?: string;
  paymentMethod?: string;
  mapping?: CsvColumnMapping;
}

export interface ImportLineDecision {
  id: string;
  included: boolean;
  category: string | null;
  paymentMethod: string | null;
  invoicePayment?: boolean;
  invoiceCardId?: string | null;
  invoiceDueMonth?: string | null;
  /** REC-9: `false` troca a sugestão de recorrência por lançamento comum. */
  recurrence?: boolean;
}

/** Mesmo limite do servidor e do gateway: recusar antes de enviar poupa o upload inteiro. */
export const MAX_IMPORT_FILE_BYTES = 5 * 1024 * 1024;

export const ACCEPTED_IMPORT_EXTENSIONS = ['.ofx', '.qfx', '.csv'];

export const isCsv = (file: File) => file.name.toLowerCase().endsWith('.csv');

export const importsApi = {
  preview: (upload: ImportUpload) => {
    const form = new FormData();
    form.append('file', upload.file, upload.file.name);
    form.append('accountId', upload.accountId);
    if (upload.creditCardId) form.append('creditCardId', upload.creditCardId);
    if (upload.paymentMethod) form.append('paymentMethod', upload.paymentMethod);
    if (upload.mapping) form.append('mapping', JSON.stringify(upload.mapping));
    return apiFetch<ImportBatch>('/api/imports', { method: 'POST', body: form });
  },

  proposeCsvMapping: (file: File) => {
    const form = new FormData();
    form.append('file', file, file.name);
    return apiFetch<CsvMappingProposal>('/api/imports/csv/proposal', { method: 'POST', body: form });
  },

  get: (id: string) => apiFetch<ImportBatch>(`/api/imports/${id}`),

  confirm: (id: string, lines: ImportLineDecision[]) =>
    apiFetch<ImportBatch>(`/api/imports/${id}/confirm`, { method: 'POST', body: JSON.stringify({ lines }) }),

  csvMappings: () => apiFetch<SavedCsvMapping[]>('/api/imports/csv/mappings'),

  /** IMP-11: o mesmo nome (sem diferença de maiúsculas) substitui o salvo. */
  saveCsvMapping: (name: string, mapping: CsvColumnMapping, headers: string[]) =>
    apiFetch<SavedCsvMapping>('/api/imports/csv/mappings', {
      method: 'POST',
      body: JSON.stringify({ name, mapping, headers }),
    }),

  removeCsvMapping: (id: string) => apiFetch<void>(`/api/imports/csv/mappings/${id}`, { method: 'DELETE' }),

  /** IMP-9: exclui os lançamentos criados pelo lote (os que ainda existem). Idempotente. */
  undo: (id: string) => apiFetch<ImportBatch>(`/api/imports/${id}/undo`, { method: 'POST' }),
};
