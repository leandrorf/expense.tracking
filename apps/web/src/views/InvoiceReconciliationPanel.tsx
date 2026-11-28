import { useEffect, useState, type ReactNode } from 'react';
import UploadFile from '@mui/icons-material/UploadFile';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { ApiError } from '../api/client';
import { importsApi, type ImportBatch } from '../api/imports';
import {
  invoicesApi,
  type InvoiceReconciliationView,
  type ReconciliationEntry,
  type ReconciliationGroup,
} from '../api/reports';
import { transactionsApi } from '../api/transactions';
import type { CreditCard } from '../api/types';
import { Field, Notice, RowForm, Stat, StatRow, TableBox, TextAction } from '../ui';

/** A projeção do reporting recebe os lançamentos novos alguns segundos depois. */
const PROJECTION_WAIT_MS = 20_000;

const GROUPS: Record<ReconciliationGroup, { title: string; hint: string }> = {
  OnlyInBank: { title: 'Só no banco', hint: 'Estão na fatura do banco e não foram lançadas: importe o que falta.' },
  OnlyInApp: { title: 'Só no app', hint: 'Lançadas aqui, mas não estão na fatura do banco: data no ciclo errado, outro cartão ou em dobro.' },
  DifferentAmount: { title: 'Valor diferente', hint: 'A mesma compra, com valor diferente no banco.' },
  Matched: { title: 'Conferidas', hint: 'Batem com a fatura do banco.' },
};
const ORDER: ReconciliationGroup[] = ['OnlyInBank', 'OnlyInApp', 'DifferentAmount', 'Matched'];

const day = (iso: string | null) => (iso ? iso.split('-').reverse().join('/') : '—');

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}

interface Props {
  card: CreditCard;
  dueMonth: string;
  currency: string;
  onChanged: () => Promise<unknown>;
  onEditTransaction?: (transactionId: string) => void;
  onShowInvoice: (dueMonth: string) => void;
}

/**
 * RCN-6 (ADR-0011): conferir a fatura com a do banco. Pelo total (marca como conferida, com a diferença aceita)
 * ou pelo arquivo OFX da fatura: mostra o que casou, o que falta lançar e o que sobra, importa o que falta pelo
 * próprio lote e leva à edição ou à exclusão do que está só no app.
 */
export function InvoiceReconciliationPanel({ card, dueMonth, currency, onChanged, onEditTransaction, onShowInvoice }: Props) {
  const [view, setView] = useState<InvoiceReconciliationView | null>(null);
  const [batch, setBatch] = useState<ImportBatch | null>(null);
  const [bankTotal, setBankTotal] = useState('');
  const [category, setCategory] = useState('Cartão de crédito');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const money = (value: number) => value.toLocaleString('pt-BR', { style: 'currency', currency: currency || 'BRL' });

  async function load(batchId = batch?.id) {
    const result = await invoicesApi.reconciliation(card.id, dueMonth, batchId);
    setView(result);
    setBankTotal((current) =>
      current !== '' ? current : String(result.detail?.fileTotal ?? result.reconciliation?.bankTotal ?? result.appTotal),
    );
    return result;
  }

  useEffect(() => {
    setBatch(null);
    setBankTotal('');
    setNotice(null);
    setError(null);
    load(undefined).catch((err) => setError(messageOf(err)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [card.id, dueMonth]);

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await action();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  /** Recarrega até a conciliação refletir a mudança (a projeção leva um instante). */
  async function reloadUntil(done: (v: InvoiceReconciliationView) => boolean, batchId = batch?.id) {
    const started = Date.now();
    for (;;) {
      const result = await load(batchId);
      if (done(result) || Date.now() - started > PROJECTION_WAIT_MS) return;
      await new Promise((resolve) => setTimeout(resolve, 1_000));
    }
  }

  function handleFile(file: File | undefined) {
    if (!file) return;
    void run(async () => {
      const uploaded = await importsApi.preview({ file, accountId: card.accountId, creditCardId: card.id });
      setBatch(uploaded);
      setBankTotal('');
      const result = await load(uploaded.id);
      if (result.detail) setBankTotal(String(result.detail.fileTotal));
    });
  }

  const onlyInBank = view?.detail?.entries.filter((e) => e.group === 'OnlyInBank') ?? [];

  /** RCN-5: confirma o lote com só as linhas "só no banco" incluídas; as casadas ficam de fora. */
  function handleImportMissing() {
    if (!batch || onlyInBank.length === 0) return;
    const missing = new Set(onlyInBank.map((e) => e.lineId));
    void run(async () => {
      await importsApi.confirm(
        batch.id,
        batch.lines
          .filter((l) => l.status === 'New' || l.status === 'ExcludedByDefault')
          .map((l) => ({
            id: l.id,
            included: missing.has(l.id),
            category: missing.has(l.id) ? l.category ?? category : null,
            paymentMethod: null,
          })),
      );
      setBatch(await importsApi.get(batch.id));
      await reloadUntil((v) => !v.detail?.entries.some((e) => e.group === 'OnlyInBank' && missing.has(e.lineId)));
      await onChanged();
      setNotice(`${missing.size === 1 ? '1 compra importada' : `${missing.size} compras importadas`} para a fatura.`);
    });
  }

  function handleDelete(entry: ReconciliationEntry) {
    if (!entry.transactionId) return;
    if (!window.confirm(`Excluir "${entry.appDescription ?? 'lançamento'}" de ${money(entry.appAmount ?? 0)}?`)) return;
    const id = entry.transactionId;
    void run(async () => {
      await transactionsApi.remove(id);
      await reloadUntil((v) => !v.detail?.entries.some((e) => e.transactionId === id) && v.appTotal !== view?.appTotal);
      await onChanged();
      setNotice('Lançamento excluído.');
    });
  }

  function handleReconcile() {
    void run(async () => {
      const result = await invoicesApi.reconcile(card.id, dueMonth, Number(bankTotal));
      setView((current) => ({ ...result, detail: current?.detail ?? null }));
      await onChanged();
      setNotice(result.difference === 0 ? 'Fatura conferida.' : `Fatura conferida com diferença de ${money(result.difference ?? 0)}.`);
    });
  }

  function handleUndo() {
    void run(async () => {
      await invoicesApi.undoReconciliation(card.id, dueMonth);
      await load();
      await onChanged();
      setNotice('Conferência desfeita.');
    });
  }

  const detail = view?.detail;
  const typedDifference = bankTotal !== '' && view ? Number(bankTotal) - view.appTotal : null;

  return (
    <Paper variant="outlined" data-cy="reconciliation" sx={{ p: 2, mb: 2 }}>
      <Typography variant="h3" component="h3" sx={{ mb: 1.5 }}>
        Conferir com a fatura do banco
      </Typography>
      {error && (
        <Notice severity="error" role="alert">
          {error}
        </Notice>
      )}
      {notice && (
        <Notice severity="success" dataCy="reconciliation-notice">
          {notice}
        </Notice>
      )}

      {view?.reconciliation && (
        <Notice
          severity={view.reconciliation.changedSince ? 'warning' : 'success'}
          dataCy="reconciliation-done"
          action={
            <TextAction type="button" data-cy="reconciliation-undo" disabled={busy} onClick={handleUndo}>
              desfazer
            </TextAction>
          }
        >
          Conferida em {new Date(view.reconciliation.reconciledAtUtc).toLocaleDateString('pt-BR')} com o total do banco de{' '}
          {money(view.reconciliation.bankTotal)}
          {view.reconciliation.difference !== 0 && ` (diferença aceita de ${money(view.reconciliation.difference)})`}.{' '}
          {view.reconciliation.changedSince && (
            <strong data-cy="reconciliation-changed">
              A fatura mudou depois: o total do app era {money(view.reconciliation.appTotal)} e agora é {money(view.appTotal)}.
            </strong>
          )}
        </Notice>
      )}

      <RowForm onSubmit={handleReconcile} dataCy="reconciliation-form">
        <Field
          dataCy="reconciliation-bank-total"
          label="Total da fatura do banco"
          type="number"
          value={bankTotal}
          onChange={setBankTotal}
          required
          slotProps={{ htmlInput: { step: '0.01' } }}
          sx={{ maxWidth: 220 }}
        />
        <Button variant="contained" type="submit" data-cy="reconciliation-mark" disabled={busy || !view} sx={{ height: 40 }}>
          Marcar como conferida
        </Button>
        <Button variant="outlined" color="inherit" component="label" disabled={busy} startIcon={<UploadFile />} sx={{ height: 40, position: 'relative' }}>
          Arquivo da fatura (OFX)
          {/* Continua um input de arquivo de verdade, transparente sobre o botão: os testes usam `selectFile`. */}
          <Box
            component="input"
            sx={{ position: 'absolute', inset: 0, width: '100%', height: '100%', opacity: 0, cursor: 'pointer' }}
            data-cy="reconciliation-file"
            type="file"
            accept=".ofx"
            disabled={busy}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => handleFile(e.target.files?.[0])}
          />
        </Button>
        <Typography variant="body2" color="text.secondary" data-cy="reconciliation-difference" sx={{ alignSelf: 'center', flexBasis: '100%' }}>
          App: {view ? money(view.appTotal) : '…'}
          {typedDifference !== null && ` · diferença ${money(typedDifference)}`}
        </Typography>
      </RowForm>

      {detail && (
        <>
          <StatRow dataCy="reconciliation-totals">
            <Stat size={{ xs: 12, sm: 4 }} label="Banco (arquivo)" value={money(detail.fileTotal)} valueDataCy="reconciliation-file-total" />
            <Stat size={{ xs: 12, sm: 4 }} label="App" value={money(view.appTotal)} />
            <Stat
              size={{ xs: 12, sm: 4 }}
              label="Diferença"
              value={money(detail.difference)}
              tone={detail.difference === 0 ? 'income' : 'expense'}
              valueDataCy="reconciliation-file-difference"
            />
          </StatRow>
          {detail.suggestedDueMonth && detail.suggestedDueMonth !== dueMonth && (
            <Notice
              severity="warning"
              dataCy="reconciliation-suggested"
              action={
                <TextAction type="button" onClick={() => onShowInvoice(detail.suggestedDueMonth!)}>
                  ver essa fatura
                </TextAction>
              }
            >
              O arquivo parece ser da fatura de {detail.suggestedDueMonth.split('-').reverse().join('/')}.
            </Notice>
          )}
          {detail.explanation.unexplained !== 0 && (
            <Typography variant="body2" color="text.secondary" data-cy="reconciliation-unexplained" sx={{ mb: 2 }}>
              O total declarado pelo banco difere da soma das linhas do arquivo em {money(detail.explanation.unexplained)}.
            </Typography>
          )}

          {ORDER.map((group) => {
            const entries = detail.entries.filter((e) => e.group === group);
            if (entries.length === 0) return null;
            return (
              <Box key={group} data-cy="reconciliation-group" data-group={group} sx={{ mb: 2 }}>
                <Typography variant="subtitle2" component="h4" sx={{ fontWeight: 700 }}>
                  {GROUPS[group].title} ({entries.length})
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                  {GROUPS[group].hint}
                </Typography>
                <TableBox>
                  <Table sx={{ minWidth: 520 }}>
                    <TableBody>
                      {entries.map((e) => (
                        <TableRow key={`${e.lineId ?? ''}:${e.transactionId ?? ''}`} data-cy="reconciliation-entry" data-group={group} sx={{ verticalAlign: 'top' }}>
                          <TableCell sx={{ whiteSpace: 'nowrap' }}>{day(e.bankDate ?? e.appDate)}</TableCell>
                          <TableCell>
                            {e.bankDescription ?? e.appDescription ?? '—'}
                            {e.charge && <Sub dataCy="reconciliation-charge">encargo do banco</Sub>}
                            {e.bankDescription && e.appDescription && <Sub>no app: {e.appDescription}</Sub>}
                            {e.installments && e.installments > 1 && (
                              <Sub>
                                parcela {e.installment}/{e.installments}
                              </Sub>
                            )}
                          </TableCell>
                          <TableCell align="right">
                            {money(e.bankAmount ?? e.appAmount ?? 0)}
                            {group === 'DifferentAmount' && <Sub>no app: {money(e.appAmount ?? 0)}</Sub>}
                          </TableCell>
                          <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                            {(group === 'OnlyInApp' || group === 'DifferentAmount') && e.transactionId && (
                              <>
                                {onEditTransaction && (
                                  <TextAction type="button" data-cy="reconciliation-edit" onClick={() => onEditTransaction(e.transactionId!)}>
                                    editar
                                  </TextAction>
                                )}
                                <TextAction danger type="button" data-cy="reconciliation-delete" disabled={busy} onClick={() => handleDelete(e)}>
                                  excluir
                                </TextAction>
                              </>
                            )}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </TableBox>
                {group === 'OnlyInBank' && batch?.status === 'Pending' && (
                  <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'flex-start' }}>
                    <Field
                      dataCy="reconciliation-category"
                      label="Categoria (sem sugestão)"
                      value={category}
                      onChange={setCategory}
                      required
                    />
                    <Button variant="contained" type="button" data-cy="reconciliation-import" disabled={busy} onClick={handleImportMissing} sx={{ height: 40 }}>
                      Importar o que falta
                    </Button>
                  </Stack>
                )}
              </Box>
            );
          })}
        </>
      )}
    </Paper>
  );
}

/** Linha menor dentro da célula (encargo, descrição no app, parcela). */
function Sub({ children, dataCy }: { children: ReactNode; dataCy?: string }) {
  return (
    <Box component="span" data-cy={dataCy} sx={{ display: 'block', fontSize: '0.75rem', color: 'text.secondary' }}>
      {children}
    </Box>
  );
}
