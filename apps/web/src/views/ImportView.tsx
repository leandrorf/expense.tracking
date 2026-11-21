import { useEffect, useMemo, useState } from 'react';
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

const STATUS_LABELS: Record<ImportLineStatus, string> = {
  New: 'Nova',
  Duplicate: 'Já lançada',
  Invalid: 'Inválida',
  ExcludedByDefault: 'Fora por padrão',
  Imported: 'Importada',
};

/** O que o usuário decidiu na prévia; só vai ao servidor na confirmação (IMP-5). */
interface LineDraft {
  included: boolean;
  category: string;
  paymentMethod: string | null;
}

const canInclude = (line: ImportLine) => line.status === 'New' || line.status === 'ExcludedByDefault';

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

  async function handleUpload(e: React.FormEvent) {
    e.preventDefault();
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
      setDrafts(
        Object.fromEntries(
          preview.lines.map((l) => [l.id, { included: l.included, category: l.category ?? '', paymentMethod: l.paymentMethod }]),
        ),
      );
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
  const missingCategory = includedLines.filter((l) => !drafts[l.id]?.category.trim());
  const canConfirm = includedLines.length > 0 && missingCategory.length === 0 && !busy;

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
        })),
      );
      includedLines.forEach((l) => remember(drafts[l.id].category));
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

  if (loading) {
    return (
      <section>
        <h2>Importar</h2>
        <p>Carregando…</p>
      </section>
    );
  }

  if (result) {
    const ignored = result.summary.total - result.summary.imported - result.summary.duplicate;
    const undone = result.status === 'Undone';
    return (
      <section>
        <h2>Importar</h2>
        <div className="import-result" data-cy="import-result" data-status={result.status} role="status">
          <h3>{undone ? 'Importação desfeita' : 'Importação concluída'}</h3>
          <dl className="import-result-figures">
            <div>
              <dt>Importados</dt>
              <dd data-cy="import-result-imported">{result.summary.imported}</dd>
            </div>
            <div>
              <dt>Já lançados</dt>
              <dd data-cy="import-result-duplicate">{result.summary.duplicate}</dd>
            </div>
            <div>
              <dt>Ignorados</dt>
              <dd data-cy="import-result-ignored">{ignored}</dd>
            </div>
          </dl>
          <p className="muted">
            {undone
              ? 'Os lançamentos criados por esta importação foram excluídos. O Resumo e os orçamentos deixam de contá-los em alguns segundos.'
              : 'O Resumo e os orçamentos passam a contar os importados em alguns segundos.'}
          </p>
          {error && (
            <p className="error" data-cy="import-error" role="alert">
              {error}
            </p>
          )}
          {confirmingUndo ? (
            <div className="import-undo-confirm" data-cy="import-undo-confirm" role="alertdialog" aria-label="Desfazer importação">
              <p>
                Excluir os {result.summary.imported} lançamento(s) criados por esta importação? Os que você já excluiu
                ficam como estão.
              </p>
              <div className="actions">
                <button data-cy="import-undo-yes" className="danger" onClick={() => void handleUndo()} disabled={busy}>
                  Desfazer importação
                </button>
                <button data-cy="import-undo-no" className="ghost" onClick={() => setConfirmingUndo(false)} disabled={busy}>
                  Manter
                </button>
              </div>
            </div>
          ) : (
            <div className="actions">
              <button data-cy="import-go-transactions" className="primary" onClick={onShowTransactions}>
                Ver lançamentos
              </button>
              <button data-cy="import-new" className="ghost" onClick={startOver}>
                Nova importação
              </button>
              {!undone && result.summary.imported > 0 && (
                <button data-cy="import-undo" className="link-danger" onClick={() => setConfirmingUndo(true)}>
                  Desfazer importação
                </button>
              )}
            </div>
          )}
        </div>
      </section>
    );
  }

  if (csvProposal && file) {
    return (
      <section>
        <h2>Importar — colunas do CSV</h2>
        <CsvMappingStep
          proposal={csvProposal}
          isCard={!!creditCardId}
          busy={busy}
          saved={savedMappings}
          onSubmit={(mapping, save) => void sendPreview(file, mapping, save)}
          onRemoveSaved={(id) => void removeSavedMapping(id)}
          onCancel={startOver}
        />
        {error && (
          <p className="error" data-cy="import-error" role="alert">
            {error}
          </p>
        )}
      </section>
    );
  }

  if (batch) {
    const isCard = batch.kind === 'CreditCard';
    return (
      <section>
        <h2>Importar — prévia</h2>
        <p className="muted" data-cy="import-preview-info">
          {isCard ? 'Fatura de cartão' : 'Extrato de conta'} · {batch.summary.total} linhas ·{' '}
          {accounts.find((a) => a.id === batch.accountId)?.name ?? ''}
        </p>

        <div className="row-form wrap import-bulk">
          <label className="inline-label">
            Categoria para as incluídas
            <input
              data-cy="import-bulk-category"
              list="import-category-suggestions"
              autoComplete="off"
              value={bulkCategory}
              onChange={(e) => setBulkCategory(e.target.value)}
            />
          </label>
          <button data-cy="import-bulk-apply-empty" type="button" onClick={() => applyBulkCategory(true)} disabled={!bulkCategory.trim()}>
            Aplicar às sem categoria
          </button>
          <button data-cy="import-bulk-apply-all" type="button" onClick={() => applyBulkCategory(false)} disabled={!bulkCategory.trim()}>
            Aplicar a todas as incluídas
          </button>
        </div>
        <CategoryDatalist id="import-category-suggestions" categories={categories} />

        <table className="data import-preview" data-cy="import-preview">
          <thead>
            <tr>
              <th>Incluir</th>
              <th>#</th>
              <th>Data</th>
              <th>Descrição</th>
              <th className="right">Valor</th>
              <th>Meio</th>
              <th>Categoria</th>
              <th>Situação</th>
            </tr>
          </thead>
          <tbody>
            {batch.lines.map((line) => {
              const draft = drafts[line.id];
              const includable = canInclude(line);
              const needsCategory = draft.included && !draft.category.trim();
              const flagged = flaggedLines.has(line.number) && needsCategory;
              return (
                <tr
                  key={line.id}
                  data-cy="import-row"
                  data-status={line.status}
                  className={`import-row ${line.status.toLowerCase()}${draft.included ? '' : ' excluded'}${flagged ? ' flagged' : ''}`}
                >
                  <td>
                    <input
                      type="checkbox"
                      data-cy="import-row-include"
                      aria-label={`Incluir linha ${line.number}`}
                      checked={draft.included}
                      disabled={!includable}
                      onChange={(e) => updateDraft(line.id, { included: e.target.checked })}
                    />
                  </td>
                  <td>{line.number}</td>
                  <td>{line.occurredOn ?? '—'}</td>
                  <td>{line.description || '—'}</td>
                  <td className={`right ${line.type === 'Income' ? 'income' : 'expense'}`}>
                    {line.amount === null ? '—' : `${line.type === 'Expense' ? '−' : '+'}${money(line.amount, batch.currency)}`}
                  </td>
                  <td>
                    {isCard || !includable ? (
                      methodName(line.paymentMethod)
                    ) : (
                      <select
                        data-cy="import-row-method"
                        aria-label={`Meio de pagamento da linha ${line.number}`}
                        value={draft.paymentMethod ?? ''}
                        onChange={(e) => updateDraft(line.id, { paymentMethod: e.target.value })}
                      >
                        {statementMethods.map((m) => (
                          <option key={m.code} value={m.code}>
                            {m.name}
                          </option>
                        ))}
                      </select>
                    )}
                  </td>
                  <td>
                    {includable && (
                      <input
                        data-cy="import-row-category"
                        aria-label={`Categoria da linha ${line.number}`}
                        aria-invalid={needsCategory}
                        list="import-category-suggestions"
                        autoComplete="off"
                        placeholder={draft.included ? 'Obrigatória' : ''}
                        value={draft.category}
                        onChange={(e) => updateDraft(line.id, { category: e.target.value })}
                      />
                    )}
                  </td>
                  <td>
                    <span className={`import-status ${line.status.toLowerCase()}`} title={line.problem ?? undefined}>
                      {STATUS_LABELS[line.status]}
                    </span>
                    {line.problem && <div className="hint">{line.problem}</div>}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>

        {error && (
          <p className="error" data-cy="import-error" role="alert">
            {error}
          </p>
        )}
        <div className="actions import-actions">
          <span data-cy="import-pending-count" aria-live="polite">
            {includedLines.length} incluída(s)
            {missingCategory.length > 0 && ` · ${missingCategory.length} sem categoria`}
          </span>
          <button data-cy="import-confirm" className="primary" onClick={() => void handleConfirm()} disabled={!canConfirm}>
            {busy ? 'Importando…' : `Importar ${includedLines.length} lançamento(s)`}
          </button>
          <button data-cy="import-cancel" className="ghost" onClick={startOver} disabled={busy}>
            Cancelar
          </button>
        </div>
      </section>
    );
  }

  return (
    <section>
      <h2>Importar</h2>
      <p className="muted">
        Envie o extrato da conta ou a fatura do cartão que o banco entrega (OFX ou CSV, até 5 MB). Você confere a
        prévia antes de lançar; o que já foi importado aparece como já lançado.
      </p>

      {accounts.length === 0 ? (
        <p className="muted">Crie uma conta antes de importar.</p>
      ) : (
        <form className="row-form wrap" onSubmit={handleUpload} data-cy="import-form">
          <label className="inline-label">
            Conta
            <select
              data-cy="import-account"
              value={accountId}
              onChange={(e) => {
                setAccountId(e.target.value);
                setCreditCardId('');
              }}
            >
              {accounts.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.name} ({a.scope === 'Business' ? 'PJ' : 'PF'})
                </option>
              ))}
            </select>
          </label>
          <label className="inline-label">
            Tipo
            <select data-cy="import-card" value={creditCardId} onChange={(e) => setCreditCardId(e.target.value)}>
              <option value="">Extrato da conta</option>
              {accountCards.map((c) => (
                <option key={c.id} value={c.id}>
                  Fatura: {c.label} •••• {c.lastFourDigits}
                </option>
              ))}
            </select>
          </label>
          {!creditCardId && (
            <label className="inline-label">
              Meio padrão
              <select data-cy="import-payment-method" value={defaultMethod} onChange={(e) => setDefaultMethod(e.target.value)}>
                {statementMethods.map((m) => (
                  <option key={m.code} value={m.code}>
                    {m.name}
                  </option>
                ))}
              </select>
            </label>
          )}
          <label className="inline-label">
            Arquivo
            <input
              key={fileInputKey}
              data-cy="import-file"
              type="file"
              accept={ACCEPTED_IMPORT_EXTENSIONS.join(',')}
              onChange={(e) => chooseFile(e.target.files?.[0] ?? null)}
              required
            />
          </label>
          <button data-cy="import-submit" className="primary" type="submit" disabled={!file || !accountId || busy}>
            {busy ? 'Lendo…' : 'Ver prévia'}
          </button>
        </form>
      )}

      {error && (
        <p className="error" data-cy="import-error" role="alert">
          {error}
        </p>
      )}
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
