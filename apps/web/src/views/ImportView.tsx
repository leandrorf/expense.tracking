import { useEffect, useMemo, useState } from 'react';
import UploadFile from '@mui/icons-material/UploadFile';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { accountsApi } from '../api/accounts';
import { ApiError } from '../api/client';
import {
  ACCEPTED_IMPORT_EXTENSIONS,
  MAX_IMPORT_FILE_BYTES,
  importsApi,
  isCsv,
  type CsvColumnMapping,
  type CsvMappingProposal,
  type CsvMappingToSave,
  type ImportBatch,
  type ImportLine,
  type ImportLineStatus,
  type SavedCsvMapping,
} from '../api/imports';
import type { Account, CreditCard, PaymentMethod } from '../api/types';
import { CsvMappingStep } from './CsvMappingStep';
import { CategoryDatalist, useCategorySuggestions } from './useCategorySuggestions';
import { Field, Figures, MonthField, Notice, RowForm, SelectField, StatusChip, TableBox, TextAction, type StatusTone } from '../ui';

const STATUS_LABELS: Record<ImportLineStatus, string> = {
  New: 'Nova',
  Duplicate: 'Já lançada',
  Invalid: 'Inválida',
  ExcludedByDefault: 'Fora por padrão',
  Imported: 'Importada',
};

/** MUI-9: cor do selo de situação de cada linha da prévia. */
const STATUS_TONES: Record<ImportLineStatus, StatusTone> = {
  New: 'info',
  Duplicate: 'neutral',
  Invalid: 'outside',
  ExcludedByDefault: 'attention',
  Imported: 'ok',
};

/** O que o usuário decidiu na prévia; só vai ao servidor na confirmação (IMP-5). */
interface LineDraft {
  included: boolean;
  category: string;
  paymentMethod: string | null;
  /** INV-11: pagamento de fatura (cartão + mês de vencimento) em vez de gasto. */
  invoicePayment: boolean;
  invoiceCardId: string;
  invoiceDueMonth: string;
  /** REC-9: mantém a sugestão de que a linha é a ocorrência de uma recorrência. */
  recurrence: boolean;
}

/** Valor do select de meio que troca a linha para pagamento de fatura. */
const INVOICE_PAYMENT = '__invoice';

const canInclude = (line: ImportLine) => line.status === 'New' || line.status === 'ExcludedByDefault';

/** Só saída de extrato de conta pode ser pagamento de fatura (ADR-0005, decisão 10). */
const canBeInvoicePayment = (batch: ImportBatch, line: ImportLine) =>
  batch.kind === 'BankAccount' && line.type === 'Expense' && canInclude(line);

const draftOf = (line: ImportLine): LineDraft => ({
  included: line.included,
  category: line.category ?? '',
  paymentMethod: line.paymentMethod,
  invoicePayment: line.invoicePayment,
  invoiceCardId: line.invoiceCardId ?? '',
  invoiceDueMonth: line.invoiceDueMonth ?? '',
  recurrence: !!line.recurrenceId,
});

/** CYC-6: o dia de vencimento do ciclo da fatura do mês (`yyyy-MM`); sem histórico, o de hoje. */
function dueDayOf(card: CreditCard, month: string): number {
  const cycle = [...(card.cycles ?? [])]
    .sort((a, b) => (a.fromDueMonth ?? '').localeCompare(b.fromDueMonth ?? ''))
    .filter((c) => c.fromDueMonth === null || c.fromDueMonth <= month)
    .pop();
  return cycle?.dueDay ?? card.dueDay;
}

/** Mesma regra do servidor: a fatura de vencimento mais próximo da data paga, pelo ciclo de cada mês (CYC-6). */
function suggestDueMonth(paidOn: string | null, card: CreditCard): string {
  if (!paidOn) return '';
  const [y, m, d] = paidOn.split('-').map(Number);
  const paid = Date.UTC(y, m - 1, d);
  const candidates = [-1, 0, 1].map((offset) => {
    const first = new Date(Date.UTC(y, m - 1 + offset, 1));
    const lastDay = new Date(Date.UTC(first.getUTCFullYear(), first.getUTCMonth() + 1, 0)).getUTCDate();
    const month = `${first.getUTCFullYear()}-${String(first.getUTCMonth() + 1).padStart(2, '0')}`;
    const due = Date.UTC(first.getUTCFullYear(), first.getUTCMonth(), Math.min(dueDayOf(card, month), lastDay));
    return { month, distance: Math.abs(due - paid) };
  });
  return candidates.reduce((best, c) => (c.distance < best.distance ? c : best)).month;
}

/**
 * IMP-7: importar extrato (OFX/CSV) ou fatura de cartão. Envia o arquivo, mostra a prévia com
 * duplicadas e inválidas fora da seleção, deixa ajustar categoria e meio por linha (ou aplicar uma
 * categoria às incluídas) e confirma. O CSV passa antes pelo mapeamento de colunas (IMP-8).
 */
export function ImportView({ onShowTransactions }: { onShowTransactions: () => void }) {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [cards, setCards] = useState<CreditCard[]>([]);
  const [paymentMethods, setPaymentMethods] = useState<PaymentMethod[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [accountId, setAccountId] = useState('');
  const [creditCardId, setCreditCardId] = useState('');
  const [defaultMethod, setDefaultMethod] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [fileInputKey, setFileInputKey] = useState(0);
  const [busy, setBusy] = useState(false);

  const [batch, setBatch] = useState<ImportBatch | null>(null);
  const [drafts, setDrafts] = useState<Record<string, LineDraft>>({});
  const [bulkCategory, setBulkCategory] = useState('');
  const [flaggedLines, setFlaggedLines] = useState<Set<number>>(new Set());
  const [result, setResult] = useState<ImportBatch | null>(null);
  const [csvProposal, setCsvProposal] = useState<CsvMappingProposal | null>(null);
  const [savedMappings, setSavedMappings] = useState<SavedCsvMapping[]>([]);
  const [confirmingUndo, setConfirmingUndo] = useState(false);
  const { categories, remember } = useCategorySuggestions();

  // Extrato de conta não aceita cartão de crédito como meio (ADR-0004, decisão 5).
  const statementMethods = useMemo(() => paymentMethods.filter((m) => !m.requiresCard), [paymentMethods]);
  const accountCards = cards.filter((c) => c.accountId === accountId && c.isActive);

  useEffect(() => {
    void (async () => {
      try {
        const [accs, crds, methods] = await Promise.all([
          accountsApi.list(),
          accountsApi.listCards(),
          accountsApi.paymentMethods(),
        ]);
        setAccounts(accs.filter((a) => a.isActive));
        setCards(crds);
        setPaymentMethods(methods);
        setAccountId(accs.find((a) => a.isActive)?.id ?? '');
        setDefaultMethod(methods.find((m) => m.code === 'Pix')?.code ?? methods.find((m) => !m.requiresCard)?.code ?? '');
      } catch (err) {
        setError(messageOf(err));
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  function startOver() {
    setBatch(null);
    setDrafts({});
    setResult(null);
    setCsvProposal(null);
    setFile(null);
    setFileInputKey((k) => k + 1);
    setBulkCategory('');
    setFlaggedLines(new Set());
    setConfirmingUndo(false);
    setError(null);
  }

  function chooseFile(chosen: File | null) {
    setError(null);
    if (chosen && !ACCEPTED_IMPORT_EXTENSIONS.some((ext) => chosen.name.toLowerCase().endsWith(ext))) {
      setError('Formato não aceito: envie um arquivo .ofx, .qfx ou .csv.');
      setFile(null);
      return;
    }
    if (chosen && chosen.size > MAX_IMPORT_FILE_BYTES) {
      setError('O arquivo passa do limite de 5 MB.');
      setFile(null);
      return;
    }
    setFile(chosen);
  }

  async function handleUpload() {
    if (!file) return;
    if (!isCsv(file)) {
      await sendPreview(file);
      return;
    }

    // IMP-8: CSV passa antes pelo mapeamento de colunas, que o usuário confere ou escolhe.
    setBusy(true);
    setError(null);
    try {
      // IMP-11: os mapeamentos salvos vêm junto, para escolher um sem remapear.
      const [proposal, saved] = await Promise.all([importsApi.proposeCsvMapping(file), importsApi.csvMappings()]);
      setSavedMappings(saved);
      setCsvProposal(proposal);
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  async function sendPreview(file: File, mapping?: CsvColumnMapping, save?: CsvMappingToSave) {
    setBusy(true);
    setError(null);
    try {
      if (mapping && save) {
        await importsApi.saveCsvMapping(save.name, mapping, save.headers);
      }
      const preview = await importsApi.preview({
        file,
        accountId,
        creditCardId: creditCardId || undefined,
        paymentMethod: creditCardId ? undefined : defaultMethod,
        mapping,
      });
      setBatch(preview);
      setCsvProposal(null);
      setDrafts(Object.fromEntries(preview.lines.map((l) => [l.id, draftOf(l)])));
      setFlaggedLines(new Set());
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  async function removeSavedMapping(id: string) {
    setError(null);
    try {
      await importsApi.removeCsvMapping(id);
      setSavedMappings((current) => current.filter((m) => m.id !== id));
    } catch (err) {
      setError(messageOf(err));
    }
  }

  const updateDraft = (id: string, change: Partial<LineDraft>) =>
    setDrafts((current) => ({ ...current, [id]: { ...current[id], ...change } }));

  const includedLines = batch ? batch.lines.filter((l) => drafts[l.id]?.included) : [];
  const missingCategory = includedLines.filter((l) => !drafts[l.id]?.invoicePayment && !drafts[l.id]?.category.trim());
  // INV-11: pagamento de fatura precisa do cartão e do mês da fatura.
  const missingInvoice = includedLines.filter(
    (l) => drafts[l.id]?.invoicePayment && (!drafts[l.id].invoiceCardId || !drafts[l.id].invoiceDueMonth),
  );
  const invoicePayments = includedLines.filter((l) => drafts[l.id]?.invoicePayment).length;
  const canConfirm = includedLines.length > 0 && missingCategory.length === 0 && missingInvoice.length === 0 && !busy;
  const activeCards = cards.filter((c) => c.isActive);

  function chooseMethod(line: ImportLine, value: string) {
    if (value === INVOICE_PAYMENT) {
      const draft = drafts[line.id];
      // Com um só cartão na conta, já escolhe ele e a fatura de vencimento mais próximo.
      const only = draft.invoiceCardId ? null : cards.filter((c) => c.accountId === batch?.accountId && c.isActive);
      const card = only?.length === 1 ? only[0] : null;
      updateDraft(line.id, {
        invoicePayment: true,
        ...(card ? { invoiceCardId: card.id, invoiceDueMonth: suggestDueMonth(line.occurredOn, card) } : {}),
      });
      return;
    }
    updateDraft(line.id, { invoicePayment: false, paymentMethod: value });
  }

  function chooseInvoiceCard(line: ImportLine, cardId: string) {
    const card = cards.find((c) => c.id === cardId);
    updateDraft(line.id, {
      invoiceCardId: cardId,
      invoiceDueMonth: card ? suggestDueMonth(line.occurredOn, card) : drafts[line.id].invoiceDueMonth,
    });
  }

  function applyBulkCategory(onlyEmpty: boolean) {
    const category = bulkCategory.trim();
    if (!category || !batch) return;
    setDrafts((current) => {
      const next = { ...current };
      for (const line of includedLines) {
        if (!onlyEmpty || !next[line.id].category.trim()) {
          next[line.id] = { ...next[line.id], category };
        }
      }
      return next;
    });
  }

  async function handleConfirm() {
    if (!batch) return;
    setBusy(true);
    setError(null);
    try {
      const confirmed = await importsApi.confirm(
        batch.id,
        batch.lines.filter(canInclude).map((l) => ({
          id: l.id,
          included: drafts[l.id].included,
          category: drafts[l.id].category.trim() || null,
          paymentMethod: drafts[l.id].paymentMethod,
          invoicePayment: canBeInvoicePayment(batch, l) ? drafts[l.id].invoicePayment : undefined,
          invoiceCardId: drafts[l.id].invoicePayment ? drafts[l.id].invoiceCardId || null : undefined,
          invoiceDueMonth: drafts[l.id].invoicePayment ? drafts[l.id].invoiceDueMonth || null : undefined,
          recurrence: l.recurrenceId && !drafts[l.id].recurrence ? false : undefined,
        })),
      );
      includedLines.filter((l) => !drafts[l.id].invoicePayment).forEach((l) => remember(drafts[l.id].category));
      setResult(confirmed);
      setBatch(null);
    } catch (err) {
      if (err instanceof ApiError && Array.isArray(err.extensions.lines)) {
        setFlaggedLines(new Set(err.extensions.lines as number[]));
      }
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  async function handleUndo() {
    if (!result) return;
    setBusy(true);
    setError(null);
    try {
      setResult(await importsApi.undo(result.id));
      setConfirmingUndo(false);
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  const money = (value: number, currency: string) => value.toLocaleString('pt-BR', { style: 'currency', currency });
  const methodName = (code: string | null) => paymentMethods.find((m) => m.code === code)?.name ?? code ?? '—';

  const heading = (text: string) => (
    <Typography variant="h2" component="h2" sx={{ mb: 2 }}>
      {text}
    </Typography>
  );
  const errorNotice = error && (
    <Notice severity="error" dataCy="import-error" role="alert">
      {error}
    </Notice>
  );

  if (loading) {
    return (
      <section>
        {heading('Importar')}
        <Typography>Carregando…</Typography>
      </section>
    );
  }

  if (result) {
    const ignored = result.summary.total - result.summary.imported - result.summary.duplicate;
    const undone = result.status === 'Undone';
    const paymentsImported = result.lines.filter((l) => l.cardPaymentId).length;
    return (
      <section>
        {heading('Importar')}
        <Paper variant="outlined" data-cy="import-result" data-status={result.status} role="status" sx={{ p: 2.5 }}>
          <Typography variant="h3" component="h3" sx={{ mb: 1.5 }}>
            {undone ? 'Importação desfeita' : 'Importação concluída'}
          </Typography>
          <Box sx={{ mb: 1.5 }}>
            <Figures
              items={[
                { label: 'Importados', value: result.summary.imported, dataCy: 'import-result-imported' },
                { label: 'Já lançados', value: result.summary.duplicate, dataCy: 'import-result-duplicate' },
                { label: 'Ignorados', value: ignored, dataCy: 'import-result-ignored' },
              ]}
            />
          </Box>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {undone
              ? 'Os lançamentos criados por esta importação foram excluídos. O Resumo e os orçamentos deixam de contá-los em alguns segundos.'
              : 'O Resumo e os orçamentos passam a contar os importados em alguns segundos.'}
            {!undone && paymentsImported > 0 &&
              ` ${paymentsImported} pagamento(s) de fatura foram registrados na fatura do cartão, sem virar gasto.`}
          </Typography>
          {errorNotice}
          {confirmingUndo ? (
            <Notice
              severity="warning"
              dataCy="import-undo-confirm"
              role="alertdialog"
              aria-label="Desfazer importação"
              sx={{ mb: 0 }}
            >
              <Box sx={{ mb: 1.5 }}>
                Excluir os {result.summary.imported - paymentsImported} lançamento(s)
                {paymentsImported > 0 && ` e ${paymentsImported} pagamento(s) de fatura`} criados por esta importação? Os que
                você já excluiu ficam como estão.
              </Box>
              <Stack direction="row" spacing={1}>
                <Button variant="contained" color="error" data-cy="import-undo-yes" onClick={() => void handleUndo()} disabled={busy}>
                  Desfazer importação
                </Button>
                <Button variant="outlined" color="inherit" data-cy="import-undo-no" onClick={() => setConfirmingUndo(false)} disabled={busy}>
                  Manter
                </Button>
              </Stack>
            </Notice>
          ) : (
            <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
              <Button variant="contained" data-cy="import-go-transactions" onClick={onShowTransactions}>
                Ver lançamentos
              </Button>
              <Button variant="outlined" color="inherit" data-cy="import-new" onClick={startOver}>
                Nova importação
              </Button>
              {!undone && result.summary.imported > 0 && (
                <TextAction danger data-cy="import-undo" onClick={() => setConfirmingUndo(true)}>
                  Desfazer importação
                </TextAction>
              )}
            </Stack>
          )}
        </Paper>
      </section>
    );
  }

  if (csvProposal && file) {
    return (
      <section>
        {heading('Importar — colunas do CSV')}
        <CsvMappingStep
          proposal={csvProposal}
          isCard={!!creditCardId}
          busy={busy}
          saved={savedMappings}
          onSubmit={(mapping, save) => void sendPreview(file, mapping, save)}
          onRemoveSaved={(id) => void removeSavedMapping(id)}
          onCancel={startOver}
        />
        <Box sx={{ mt: 2 }}>{errorNotice}</Box>
      </section>
    );
  }

  if (batch) {
    const isCard = batch.kind === 'CreditCard';
    return (
      <section>
        {heading('Importar — prévia')}
        <Typography variant="body2" color="text.secondary" data-cy="import-preview-info" sx={{ mb: 2 }}>
          {isCard ? 'Fatura de cartão' : 'Extrato de conta'} · {batch.summary.total} linhas ·{' '}
          {accounts.find((a) => a.id === batch.accountId)?.name ?? ''}
        </Typography>

        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center', mb: 2 }}>
          <Field
            label="Categoria para as incluídas"
            dataCy="import-bulk-category"
            value={bulkCategory}
            onChange={setBulkCategory}
            slotProps={{ htmlInput: { list: 'import-category-suggestions', autoComplete: 'off' } }}
            sx={{ minWidth: 240 }}
          />
          <Button
            variant="outlined"
            color="inherit"
            data-cy="import-bulk-apply-empty"
            type="button"
            onClick={() => applyBulkCategory(true)}
            disabled={!bulkCategory.trim()}
          >
            Aplicar às sem categoria
          </Button>
          <Button
            variant="outlined"
            color="inherit"
            data-cy="import-bulk-apply-all"
            type="button"
            onClick={() => applyBulkCategory(false)}
            disabled={!bulkCategory.trim()}
          >
            Aplicar a todas as incluídas
          </Button>
        </Stack>
        <CategoryDatalist id="import-category-suggestions" categories={categories} />

        <TableBox>
          <Table data-cy="import-preview" sx={{ minWidth: 960 }}>
            <TableHead>
              <TableRow>
                <TableCell padding="checkbox">Incluir</TableCell>
                <TableCell>#</TableCell>
                <TableCell>Data</TableCell>
                <TableCell>Descrição</TableCell>
                <TableCell align="right">Valor</TableCell>
                <TableCell>Meio</TableCell>
                <TableCell>Categoria</TableCell>
                <TableCell>Situação</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {batch.lines.map((line) => {
                const draft = drafts[line.id];
                const includable = canInclude(line);
                const asInvoice = draft.invoicePayment;
                const needsCategory = draft.included && !asInvoice && !draft.category.trim();
                const needsInvoice = draft.included && asInvoice && (!draft.invoiceCardId || !draft.invoiceDueMonth);
                const flagged = flaggedLines.has(line.number) && (needsCategory || needsInvoice);
                return (
                  <TableRow
                    key={line.id}
                    data-cy="import-row"
                    data-status={line.status}
                    data-invoice-payment={asInvoice ? 'true' : undefined}
                    sx={{
                      verticalAlign: 'top',
                      opacity: draft.included ? 1 : 0.6,
                      bgcolor: flagged ? 'rgba(248, 113, 113, 0.12)' : undefined,
                    }}
                  >
                    <TableCell padding="checkbox">
                      <Checkbox
                        size="small"
                        checked={draft.included}
                        disabled={!includable}
                        onChange={(e) => updateDraft(line.id, { included: e.target.checked })}
                        slotProps={{ input: { 'data-cy': 'import-row-include', 'aria-label': `Incluir linha ${line.number}` } as object }}
                      />
                    </TableCell>
                    <TableCell>{line.number}</TableCell>
                    <TableCell sx={{ whiteSpace: 'nowrap' }}>{line.occurredOn ?? '—'}</TableCell>
                    <TableCell>
                      {line.description || '—'}
                      {includable && !asInvoice && line.recurrenceId && (
                        <Box
                          component="label"
                          data-cy="import-row-recurrence-label"
                          sx={{ display: 'flex', alignItems: 'center', gap: 0.5, fontSize: '0.75rem', color: '#a78bfa', mt: 0.5, cursor: 'pointer' }}
                        >
                          <Checkbox
                            size="small"
                            checked={draft.recurrence}
                            onChange={(e) => updateDraft(line.id, { recurrence: e.target.checked })}
                            slotProps={{ input: { 'data-cy': 'import-row-recurrence' } as object }}
                            sx={{ p: 0.25 }}
                          />
                          <span>
                            lançamento da recorrência <strong>{line.recurrenceDescription}</strong> ({line.recurrencePeriod})
                          </span>
                        </Box>
                      )}
                    </TableCell>
                    <TableCell
                      align="right"
                      sx={{ whiteSpace: 'nowrap', color: line.type === 'Income' ? 'success.main' : 'error.main' }}
                    >
                      {line.amount === null ? '—' : `${line.type === 'Expense' ? '−' : '+'}${money(line.amount, batch.currency)}`}
                    </TableCell>
                    <TableCell>
                      {isCard || !includable ? (
                        methodName(line.paymentMethod)
                      ) : (
                        <SelectField
                          dataCy="import-row-method"
                          value={asInvoice ? INVOICE_PAYMENT : draft.paymentMethod ?? ''}
                          onChange={(v) => chooseMethod(line, v)}
                          slotProps={{ htmlInput: { 'aria-label': `Meio de pagamento da linha ${line.number}` } }}
                          sx={{ minWidth: 170 }}
                        >
                          {statementMethods.map((m) => (
                            <option key={m.code} value={m.code}>
                              {m.name}
                            </option>
                          ))}
                          {canBeInvoicePayment(batch, line) && activeCards.length > 0 && (
                            <option value={INVOICE_PAYMENT}>Pagamento de fatura</option>
                          )}
                        </SelectField>
                      )}
                    </TableCell>
                    <TableCell>
                      {includable && asInvoice && (
                        <Stack spacing={1} sx={{ minWidth: 190 }}>
                          <SelectField
                            dataCy="import-row-invoice-card"
                            value={draft.invoiceCardId}
                            onChange={(v) => chooseInvoiceCard(line, v)}
                            error={needsInvoice && !draft.invoiceCardId}
                            slotProps={{
                              htmlInput: {
                                'aria-label': `Cartão da fatura paga na linha ${line.number}`,
                                'aria-invalid': needsInvoice && !draft.invoiceCardId,
                              },
                            }}
                          >
                            <option value="">Qual cartão?</option>
                            {activeCards.map((c) => (
                              <option key={c.id} value={c.id}>
                                {c.label} •••• {c.lastFourDigits}
                              </option>
                            ))}
                          </SelectField>
                          <MonthField
                            dataCy="import-row-invoice-month"
                            value={draft.invoiceDueMonth}
                            onChange={(v) => updateDraft(line.id, { invoiceDueMonth: v })}
                            error={needsInvoice && !draft.invoiceDueMonth}
                            slotProps={{
                              htmlInput: {
                                'aria-label': `Mês de vencimento da fatura paga na linha ${line.number}`,
                                'aria-invalid': needsInvoice && !draft.invoiceDueMonth,
                              },
                            }}
                          />
                          <Typography variant="caption" color="text.secondary">
                            Não vira gasto: abate a fatura.
                          </Typography>
                        </Stack>
                      )}
                      {includable && !asInvoice && (
                        <Field
                          dataCy="import-row-category"
                          placeholder={draft.included ? 'Obrigatória' : ''}
                          value={draft.category}
                          onChange={(v) => updateDraft(line.id, { category: v })}
                          error={needsCategory}
                          slotProps={{
                            htmlInput: {
                              list: 'import-category-suggestions',
                              autoComplete: 'off',
                              'aria-label': `Categoria da linha ${line.number}`,
                              'aria-invalid': needsCategory,
                            },
                          }}
                          sx={{ minWidth: 170 }}
                        />
                      )}
                    </TableCell>
                    <TableCell>
                      <StatusChip tone={STATUS_TONES[line.status]} label={STATUS_LABELS[line.status]} title={line.problem ?? undefined} />
                      {line.problem && (
                        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
                          {line.problem}
                        </Typography>
                      )}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </TableBox>

        {errorNotice}
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
          <Typography variant="body2" data-cy="import-pending-count" aria-live="polite" sx={{ mr: 1 }}>
            {includedLines.length} incluída(s)
            {invoicePayments > 0 && ` · ${invoicePayments} pagamento(s) de fatura`}
            {missingCategory.length > 0 && ` · ${missingCategory.length} sem categoria`}
            {missingInvoice.length > 0 && ` · ${missingInvoice.length} sem fatura escolhida`}
          </Typography>
          <Button variant="contained" data-cy="import-confirm" onClick={() => void handleConfirm()} disabled={!canConfirm}>
            {busy ? 'Importando…' : `Importar ${includedLines.length} lançamento(s)`}
          </Button>
          <Button variant="outlined" color="inherit" data-cy="import-cancel" onClick={startOver} disabled={busy}>
            Cancelar
          </Button>
        </Stack>
      </section>
    );
  }

  return (
    <section>
      {heading('Importar')}
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        Envie o extrato da conta ou a fatura do cartão que o banco entrega (OFX ou CSV, até 5 MB). Você confere a
        prévia antes de lançar; o que já foi importado aparece como já lançado.
      </Typography>

      {accounts.length === 0 ? (
        <Typography color="text.secondary">Crie uma conta antes de importar.</Typography>
      ) : (
        <RowForm onSubmit={() => void handleUpload()} dataCy="import-form">
          <SelectField
            label="Conta"
            dataCy="import-account"
            value={accountId}
            onChange={(v) => {
              setAccountId(v);
              setCreditCardId('');
            }}
            options={accounts.map((a) => ({ value: a.id, label: `${a.name} (${a.scope === 'Business' ? 'PJ' : 'PF'})` }))}
          />
          <SelectField label="Tipo" dataCy="import-card" value={creditCardId} onChange={setCreditCardId}>
            <option value="">Extrato da conta</option>
            {accountCards.map((c) => (
              <option key={c.id} value={c.id}>
                Fatura: {c.label} •••• {c.lastFourDigits}
              </option>
            ))}
          </SelectField>
          {!creditCardId && (
            <SelectField
              label="Meio padrão"
              dataCy="import-payment-method"
              value={defaultMethod}
              onChange={setDefaultMethod}
              options={statementMethods.map((m) => ({ value: m.code, label: m.name }))}
            />
          )}
          <Button
            variant="outlined"
            color="inherit"
            component="label"
            startIcon={<UploadFile />}
            sx={{ height: 40, position: 'relative', maxWidth: 280, justifyContent: 'flex-start' }}
          >
            <Box component="span" sx={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
              {file ? file.name : 'Arquivo (OFX ou CSV)'}
            </Box>
            {/* Continua um input de arquivo de verdade, transparente sobre o botão: os testes usam `selectFile`. */}
            <Box
              component="input"
              key={fileInputKey}
              data-cy="import-file"
              type="file"
              accept={ACCEPTED_IMPORT_EXTENSIONS.join(',')}
              required
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => chooseFile(e.target.files?.[0] ?? null)}
              sx={{ position: 'absolute', inset: 0, width: '100%', height: '100%', opacity: 0, cursor: 'pointer' }}
            />
          </Button>
          <Button variant="contained" data-cy="import-submit" type="submit" disabled={!file || !accountId || busy} sx={{ height: 40 }}>
            {busy ? 'Lendo…' : 'Ver prévia'}
          </Button>
        </RowForm>
      )}

      {errorNotice}
    </section>
  );
}

function messageOf(err: unknown): string {
  if (err instanceof ApiError) {
    const friendly: Record<string, string> = {
      'import.card_required': 'Este arquivo é uma fatura de cartão: escolha o cartão em "Tipo".',
      'import.card_not_allowed': 'Este arquivo é um extrato de conta: escolha "Extrato da conta" em "Tipo".',
      'import.categories_required': 'Toda linha incluída precisa de categoria. As linhas pendentes estão destacadas.',
      'import.mappings_limit': 'Você já tem 50 mapeamentos salvos. Exclua algum para salvar outro.',
    };
    return friendly[err.code] ?? err.message;
  }
  return err instanceof Error ? err.message : 'Erro inesperado.';
}
