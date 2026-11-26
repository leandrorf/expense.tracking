import { useEffect, useState } from 'react';
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

  function handleReconcile(e: React.FormEvent) {
    e.preventDefault();
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
    <div className="reconciliation" data-cy="reconciliation">
      <h3 className="section-title">Conferir com a fatura do banco</h3>
      {error && <p className="error" role="alert">{error}</p>}
      {notice && (
        <p className="notice" data-cy="reconciliation-notice">
          {notice}
        </p>
      )}

      {view?.reconciliation && (
        <p className="notice" data-cy="reconciliation-done">
          Conferida em {new Date(view.reconciliation.reconciledAtUtc).toLocaleDateString('pt-BR')} com o total do banco de{' '}
          {money(view.reconciliation.bankTotal)}
          {view.reconciliation.difference !== 0 && ` (diferença aceita de ${money(view.reconciliation.difference)})`}.{' '}
          {view.reconciliation.changedSince && (
            <strong data-cy="reconciliation-changed">
              A fatura mudou depois: o total do app era {money(view.reconciliation.appTotal)} e agora é {money(view.appTotal)}.{' '}
            </strong>
          )}
          <button className="link" type="button" data-cy="reconciliation-undo" disabled={busy} onClick={handleUndo}>
            desfazer
          </button>
        </p>
      )}

      <form className="row-form wrap" onSubmit={handleReconcile} data-cy="reconciliation-form">
        <label className="field">
          <span>Total da fatura do banco</span>
          <input
            data-cy="reconciliation-bank-total"
            type="number"
            step="0.01"
            value={bankTotal}
            onChange={(e) => setBankTotal(e.target.value)}
            required
          />
        </label>
        <span className="muted" data-cy="reconciliation-difference">
          App: {view ? money(view.appTotal) : '…'}
          {typedDifference !== null && ` · diferença ${money(typedDifference)}`}
        </span>
        <button className="primary" type="submit" data-cy="reconciliation-mark" disabled={busy || !view}>
          Marcar como conferida
        </button>
        <label className="field">
          <span>Arquivo da fatura (OFX)</span>
          <input
            data-cy="reconciliation-file"
            type="file"
            accept=".ofx"
            disabled={busy}
            onChange={(e) => handleFile(e.target.files?.[0])}
          />
        </label>
      </form>

      {detail && (
        <>
          <div className="cards-row" data-cy="reconciliation-totals">
            <div className="stat">
              <span className="stat-label">Banco (arquivo)</span>
              <span className="stat-value" data-cy="reconciliation-file-total">{money(detail.fileTotal)}</span>
            </div>
            <div className="stat">
              <span className="stat-label">App</span>
              <span className="stat-value">{money(view.appTotal)}</span>
            </div>
            <div className="stat">
              <span className="stat-label">Diferença</span>
              <span className={`stat-value ${detail.difference === 0 ? 'income' : 'expense'}`} data-cy="reconciliation-file-difference">
                {money(detail.difference)}
              </span>
            </div>
          </div>
          {detail.suggestedDueMonth && detail.suggestedDueMonth !== dueMonth && (
            <p className="notice warning" data-cy="reconciliation-suggested">
              O arquivo parece ser da fatura de {detail.suggestedDueMonth.split('-').reverse().join('/')}.{' '}
              <button className="link" type="button" onClick={() => onShowInvoice(detail.suggestedDueMonth!)}>
                ver essa fatura
              </button>
            </p>
          )}
          {detail.explanation.unexplained !== 0 && (
            <p className="muted" data-cy="reconciliation-unexplained">
              O total declarado pelo banco difere da soma das linhas do arquivo em {money(detail.explanation.unexplained)}.
            </p>
          )}

          {ORDER.map((group) => {
            const entries = detail.entries.filter((e) => e.group === group);
            if (entries.length === 0) return null;
            return (
              <div key={group} data-cy="reconciliation-group" data-group={group}>
                <h4>
                  {GROUPS[group].title} ({entries.length})
                </h4>
                <p className="muted">{GROUPS[group].hint}</p>
                <table className="data">
                  <tbody>
                    {entries.map((e) => (
                      <tr key={`${e.lineId ?? ''}:${e.transactionId ?? ''}`} data-cy="reconciliation-entry" data-group={group}>
                        <td>{day(e.bankDate ?? e.appDate)}</td>
                        <td>
                          {e.bankDescription ?? e.appDescription ?? '—'}
                          {e.charge && (
                            <span className="sub" data-cy="reconciliation-charge">
                              encargo do banco
                            </span>
                          )}
                          {e.bankDescription && e.appDescription && <span className="sub">no app: {e.appDescription}</span>}
                          {e.installments && e.installments > 1 && (
                            <span className="sub">
                              parcela {e.installment}/{e.installments}
                            </span>
                          )}
                        </td>
                        <td className="right">
                          {money(e.bankAmount ?? e.appAmount ?? 0)}
                          {group === 'DifferentAmount' && <span className="sub">no app: {money(e.appAmount ?? 0)}</span>}
                        </td>
                        <td className="row-actions">
                          {(group === 'OnlyInApp' || group === 'DifferentAmount') && e.transactionId && (
                            <>
                              {onEditTransaction && (
                                <button
                                  className="link"
                                  type="button"
                                  data-cy="reconciliation-edit"
                                  onClick={() => onEditTransaction(e.transactionId!)}
                                >
                                  editar
                                </button>
                              )}
                              <button
                                className="link-danger"
                                type="button"
                                data-cy="reconciliation-delete"
                                disabled={busy}
                                onClick={() => handleDelete(e)}
                              >
                                excluir
                              </button>
                            </>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {group === 'OnlyInBank' && batch?.status === 'Pending' && (
                  <div className="row-form">
                    <label className="field">
                      <span>Categoria (sem sugestão)</span>
                      <input data-cy="reconciliation-category" value={category} onChange={(ev) => setCategory(ev.target.value)} required />
                    </label>
                    <button className="primary" type="button" data-cy="reconciliation-import" disabled={busy} onClick={handleImportMissing}>
                      Importar o que falta
                    </button>
                  </div>
                )}
              </div>
            );
          })}
        </>
      )}
    </div>
  );
}
