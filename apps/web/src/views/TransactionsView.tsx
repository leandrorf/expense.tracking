import { useEffect, useMemo, useState } from 'react';
import { accountsApi } from '../api/accounts';
import { ApiError } from '../api/client';
import { transactionsApi } from '../api/transactions';
import type { Account, CreditCard, PaymentMethod, Transaction } from '../api/types';
import { CategoryDatalist, useCategorySuggestions } from './useCategorySuggestions';

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

/** REC-10: `onRepeat` abre a aba Recorrências com o lançamento como modelo ("repetir todo mês"). */
export function TransactionsView({
  onRepeat,
  editId = null,
  onEditOpened,
}: {
  onRepeat?: (t: Transaction) => void;
  /** RCN-6: abre este lançamento em edição (atalho da conferência da fatura). */
  editId?: string | null;
  onEditOpened?: () => void;
} = {}) {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [cards, setCards] = useState<CreditCard[]>([]);
  const [paymentMethods, setPaymentMethods] = useState<PaymentMethod[]>([]);
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [filterAccount, setFilterAccount] = useState('');

  const [accountId, setAccountId] = useState('');
  // LNC-2 (ADR-0007, decisao 7): gasto ou receita; receita nunca e parcelada.
  const [type, setType] = useState<'Expense' | 'Income'>('Expense');
  const [paymentMethod, setPaymentMethod] = useState('');
  const [creditCardId, setCreditCardId] = useState('');
  const [description, setDescription] = useState('');
  const [category, setCategory] = useState('');
  const [amount, setAmount] = useState('');
  // INV-5: parcelas so no cartao de credito (o debito tambem exige cartao, mas e sempre a vista).
  const [installments, setInstallments] = useState(1);
  const [occurredOn, setOccurredOn] = useState(today());
  const [notes, setNotes] = useState('');
  const [submitting, setSubmitting] = useState(false);
  // LNC-5 (ADR-0007): lancamento em edicao (o formulario e o mesmo da criacao) e o aviso de conflito.
  const [editing, setEditing] = useState<Transaction | null>(null);
  const [conflict, setConflict] = useState(false);
  // LNC-7: lancamentos marcados na lista para recategorizar de uma vez.
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [bulkCategory, setBulkCategory] = useState('');
  const [bulkSubmitting, setBulkSubmitting] = useState(false);
  const [bulkOutcome, setBulkOutcome] = useState<BulkOutcome | null>(null);
  const { categories, remember } = useCategorySuggestions();

  const requiresCard = useMemo(
    () => paymentMethods.find((m) => m.code === paymentMethod)?.requiresCard ?? false,
    [paymentMethods, paymentMethod],
  );

  async function loadRefData() {
    const [accs, crds, methods] = await Promise.all([
      accountsApi.list(),
      accountsApi.listCards(),
      accountsApi.paymentMethods(),
    ]);
    setAccounts(accs);
    setCards(crds);
    setPaymentMethods(methods);
    if (!accountId && accs.length > 0) setAccountId(accs[0].id);
    setPaymentMethod((current) => current || methods[0]?.code || '');
  }

  async function loadTransactions() {
    const items = await transactionsApi.list(filterAccount ? { accountId: filterAccount } : {});
    setTransactions(items);
    // A selecao so guarda o que continua na lista (filtro trocado, excluido...).
    const visible = new Set(items.map((t) => t.id));
    setSelected((current) => new Set([...current].filter((id) => visible.has(id))));
  }

  async function loadAll() {
    setLoading(true);
    setError(null);
    try {
      await loadRefData();
      await loadTransactions();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadAll();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    void loadTransactions().catch((err) => setError(messageOf(err)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filterAccount]);

  // RCN-6: o atalho da conferência abre o lançamento em edição (depois de carregar contas e cartões).
  useEffect(() => {
    if (!editId || loading) return;
    transactionsApi
      .get(editId)
      .then(startEdit)
      .catch((err) => setError(messageOf(err)))
      .finally(() => onEditOpened?.());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editId, loading]);

  const canSplit = paymentMethod === 'CreditCard' && type === 'Expense';

  function resetForm() {
    setEditing(null);
    setType('Expense');
    setDescription('');
    setNotes('');
    setCategory('');
    setAmount('');
    setInstallments(1);
    setCreditCardId('');
    setOccurredOn(today());
  }

  /** Abre o formulario com o lancamento (a conta fica travada: ela nao muda). */
  function startEdit(t: Transaction) {
    setEditing(t);
    setAccountId(t.accountId);
    setType(t.type);
    setPaymentMethod(t.paymentMethod);
    setCreditCardId(t.creditCardId ?? '');
    setDescription(t.description);
    setCategory(t.category);
    setAmount(String(t.amount));
    setInstallments(t.installments);
    setOccurredOn(t.occurredOn);
    setNotes(t.notes ?? '');
    setError(null);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  function cancelEdit() {
    setConflict(false);
    resetForm();
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    setConflict(false);
    const input = {
      accountId,
      type,
      paymentMethod,
      creditCardId: requiresCard && creditCardId ? creditCardId : undefined,
      description,
      category,
      amount: Number(amount),
      occurredOn,
      installments: canSplit ? installments : undefined,
      notes: notes.trim() || undefined,
    };
    try {
      if (editing) {
        await transactionsApi.update(editing.id, { ...input, version: editing.version });
      } else {
        await transactionsApi.create(input);
      }
      remember(category);
      resetForm();
      await loadTransactions();
    } catch (err) {
      if (editing && err instanceof ApiError && err.code === 'transaction.concurrency_conflict') {
        // Outra aba salvou antes: mostra o que esta gravado agora, sem sobrescrever.
        try {
          startEdit(await transactionsApi.get(editing.id));
          setConflict(true);
        } catch (reloadErr) {
          setError(messageOf(reloadErr));
        }
      } else {
        setError(messageOf(err));
      }
    } finally {
      setSubmitting(false);
    }
  }

  async function handleRemove(id: string) {
    try {
      await transactionsApi.remove(id);
      if (editing?.id === id) cancelEdit();
      await loadTransactions();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  function toggleSelected(id: string) {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  const allSelected = transactions.length > 0 && transactions.every((t) => selected.has(t.id));

  function toggleAll() {
    setSelected(allSelected ? new Set() : new Set(transactions.map((t) => t.id)));
  }

  /** LNC-7: manda a versao que a lista leu; o que mudou em outra aba volta como conflito. */
  async function handleRecategorize(e: React.FormEvent) {
    e.preventDefault();
    const items = transactions.filter((t) => selected.has(t.id)).map((t) => ({ id: t.id, version: t.version }));
    if (items.length === 0) return;
    setBulkSubmitting(true);
    setError(null);
    setBulkOutcome(null);
    try {
      const result = await transactionsApi.recategorize(bulkCategory, items);
      remember(bulkCategory);
      setBulkOutcome({
        category: bulkCategory.trim(),
        updated: result.updated.length,
        unchanged: result.unchanged.length,
        conflicts: result.conflicts,
        notFound: result.notFound.length,
        rejected: result.rejected.length,
      });
      // Os que mudaram em outra aba continuam marcados: a lista recarrega com o valor atual e o
      // usuario decide se aplica de novo.
      setSelected(new Set(result.conflicts.map((t) => t.id)));
      setBulkCategory('');
      await loadTransactions();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBulkSubmitting(false);
    }
  }

  const accountName = (id: string) => accounts.find((a) => a.id === id)?.name ?? '—';
  // O cartao do lancamento em edicao aparece mesmo se foi desativado depois.
  const accountCards = cards.filter((c) => c.accountId === accountId && (c.isActive || c.id === editing?.creditCardId));

  if (loading) {
    return (
      <section>
        <h2>Lançamentos</h2>
        <p>Carregando…</p>
      </section>
    );
  }

  return (
    <section>
      <h2>Lançamentos</h2>

      {accounts.length === 0 ? (
        <p className="muted">Crie uma conta antes de registrar lançamentos.</p>
      ) : (
        <>
        {editing && (
          <div className="editing-banner" data-cy="tx-editing">
            <strong>Editando: {editing.description}</strong>
            {editing.imported && (
              <p className="muted" data-cy="tx-imported-notice">
                Este lançamento veio de uma importação; desfazer a importação ainda o apaga.
              </p>
            )}
            {conflict && (
              <p className="notice warning" data-cy="tx-conflict">
                Este lançamento mudou enquanto você editava. O formulário mostra agora o que está gravado; refaça a
                alteração e salve de novo.
              </p>
            )}
          </div>
        )}
        <form className="row-form wrap" onSubmit={handleSubmit} data-cy="tx-form">
          <select
            data-cy="tx-account"
            value={accountId}
            disabled={editing !== null}
            title={editing ? 'A conta de um lançamento não muda: exclua e lance de novo na outra conta.' : undefined}
            onChange={(e) => setAccountId(e.target.value)}
          >
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </select>
          <select
            data-cy="tx-type"
            aria-label="Tipo"
            value={type}
            onChange={(e) => setType(e.target.value as 'Expense' | 'Income')}
          >
            <option value="Expense">Gasto</option>
            <option value="Income">Receita</option>
          </select>
          <select data-cy="tx-payment-method" value={paymentMethod} onChange={(e) => setPaymentMethod(e.target.value)}>
            {paymentMethods.map((m) => (
              <option key={m.code} value={m.code}>
                {m.name}
              </option>
            ))}
          </select>
          {requiresCard && (
            <select data-cy="tx-card" value={creditCardId} onChange={(e) => setCreditCardId(e.target.value)} required>
              <option value="">Selecione o cartão…</option>
              {accountCards.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.label} •••• {c.lastFourDigits}
                </option>
              ))}
            </select>
          )}
          <input data-cy="tx-description" placeholder="Descrição" value={description} onChange={(e) => setDescription(e.target.value)} required />
          <input
            data-cy="tx-category"
            placeholder="Categoria"
            list="tx-category-suggestions"
            autoComplete="off"
            value={category}
            onChange={(e) => setCategory(e.target.value)}
            required
          />
          <CategoryDatalist id="tx-category-suggestions" categories={categories} />
          <input data-cy="tx-amount" placeholder="Valor" type="number" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} required />
          {canSplit && (
            <select
              data-cy="tx-installments"
              aria-label="Parcelas"
              title="O valor é o total da compra; cada parcela cai numa fatura"
              value={installments}
              onChange={(e) => setInstallments(Number(e.target.value))}
            >
              {Array.from({ length: 24 }, (_, i) => i + 1).map((n) => (
                <option key={n} value={n}>
                  {n === 1 ? 'À vista' : `${n}x`}
                </option>
              ))}
            </select>
          )}
          <input data-cy="tx-date" type="date" value={occurredOn} onChange={(e) => setOccurredOn(e.target.value)} required />
          <input
            data-cy="tx-notes"
            placeholder="Observações (opcional)"
            maxLength={500}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
          />
          <button data-cy="tx-submit" className="primary" type="submit" disabled={submitting}>
            {submitting ? 'Salvando…' : editing ? 'Salvar alteração' : 'Registrar'}
          </button>
          {editing && (
            <button data-cy="tx-cancel" className="ghost" type="button" onClick={cancelEdit}>
              Cancelar
            </button>
          )}
        </form>
        </>
      )}

      <div className="filters">
        <label>Filtrar por conta:</label>
        <select value={filterAccount} onChange={(e) => setFilterAccount(e.target.value)}>
          <option value="">Todas</option>
          {accounts.map((a) => (
            <option key={a.id} value={a.id}>
              {a.name}
            </option>
          ))}
        </select>
      </div>

      {error && <p className="error">{error}</p>}
      {bulkOutcome && <BulkOutcomeNotice outcome={bulkOutcome} onClose={() => setBulkOutcome(null)} />}
      {selected.size > 0 && (
        <form className="row-form wrap bulk-bar" onSubmit={handleRecategorize} data-cy="tx-bulk">
          <strong data-cy="tx-bulk-count">
            {selected.size} {selected.size === 1 ? 'selecionado' : 'selecionados'}
          </strong>
          <input
            data-cy="tx-bulk-category"
            placeholder="Nova categoria"
            aria-label="Nova categoria para os selecionados"
            list="tx-category-suggestions"
            autoComplete="off"
            maxLength={60}
            value={bulkCategory}
            onChange={(e) => setBulkCategory(e.target.value)}
            required
          />
          <button data-cy="tx-bulk-apply" className="primary" type="submit" disabled={bulkSubmitting}>
            {bulkSubmitting ? 'Recategorizando…' : 'Recategorizar'}
          </button>
          <button data-cy="tx-bulk-clear" className="ghost" type="button" onClick={() => setSelected(new Set())}>
            Limpar seleção
          </button>
        </form>
      )}
      {transactions.length === 0 ? (
        <p className="muted">Nenhum lançamento.</p>
      ) : (
        <table className="data" data-cy="tx-table">
          <thead>
            <tr>
              <th className="select-cell">
                <input
                  type="checkbox"
                  data-cy="tx-select-all"
                  aria-label="Selecionar todos"
                  checked={allSelected}
                  onChange={toggleAll}
                />
              </th>
              <th>Data</th>
              <th>Descrição</th>
              <th>Categoria</th>
              <th>Meio</th>
              <th>Conta</th>
              <th className="right">Valor</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {transactions.map((t) => (
              <tr key={t.id} data-cy="tx-row" data-type={t.type} className={selected.has(t.id) ? 'selected' : undefined}>
                <td className="select-cell">
                  <input
                    type="checkbox"
                    data-cy="tx-select"
                    aria-label={`Selecionar ${t.description}`}
                    checked={selected.has(t.id)}
                    onChange={() => toggleSelected(t.id)}
                  />
                </td>
                <td>{t.occurredOn}</td>
                <td>
                  {t.description}
                  {t.notes && (
                    <span className="sub" data-cy="tx-notes-label">
                      {t.notes}
                    </span>
                  )}
                  {t.recurrenceId && (
                    <span className="sub" data-cy="tx-recurring-label" title="Lançado a partir de uma recorrência">
                      recorrência
                    </span>
                  )}
                </td>
                <td>{t.category}</td>
                <td>{paymentMethods.find((m) => m.code === t.paymentMethod)?.name ?? t.paymentMethod}</td>
                <td>{accountName(t.accountId)}</td>
                <td className={`right ${t.type === 'Income' ? 'positive' : 'negative'}`} data-cy="tx-value">
                  {t.type === 'Income' ? '+' : '−'}
                  {t.amount.toLocaleString('pt-BR', { style: 'currency', currency: t.currency })}
                  {t.installments > 1 && (
                    <span className="sub" data-cy="tx-installments-label">
                      em {t.installments}x
                    </span>
                  )}
                </td>
                <td className="row-actions">
                  <button className="link" data-cy="tx-edit" onClick={() => startEdit(t)}>
                    editar
                  </button>
                  {onRepeat && !t.recurrenceId && t.installments === 1 && (
                    <button
                      className="link"
                      data-cy="tx-repeat"
                      title="Criar uma recorrência a partir deste lançamento"
                      onClick={() => onRepeat(t)}
                    >
                      repetir
                    </button>
                  )}
                  <button className="link-danger" onClick={() => void handleRemove(t.id)}>
                    excluir
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

interface BulkOutcome {
  category: string;
  updated: number;
  unchanged: number;
  conflicts: Transaction[];
  notFound: number;
  rejected: number;
}

function plural(n: number, one: string, many: string): string {
  return `${n} ${n === 1 ? one : many}`;
}

function BulkOutcomeNotice({ outcome, onClose }: { outcome: BulkOutcome; onClose: () => void }) {
  const done = outcome.updated + outcome.unchanged;
  return (
    <div className="notice" data-cy="tx-bulk-result">
      <p>
        {plural(done, 'lançamento ficou', 'lançamentos ficaram')} em <strong>{outcome.category}</strong>.
      </p>
      {outcome.conflicts.length > 0 && (
        <div className="warning" data-cy="tx-bulk-conflicts">
          <p>
            {plural(outcome.conflicts.length, 'lançamento mudou', 'lançamentos mudaram')} em outra aba e não{' '}
            {outcome.conflicts.length === 1 ? 'foi alterado' : 'foram alterados'}. A lista mostra o que está gravado
            agora e {outcome.conflicts.length === 1 ? 'ele continua marcado' : 'eles continuam marcados'}:
          </p>
          <ul>
            {outcome.conflicts.map((t) => (
              <li key={t.id} data-cy="tx-bulk-conflict">
                {t.description} — {t.category}
              </li>
            ))}
          </ul>
        </div>
      )}
      {outcome.notFound > 0 && (
        <p className="muted" data-cy="tx-bulk-not-found">
          {plural(outcome.notFound, 'lançamento já tinha sido excluído', 'lançamentos já tinham sido excluídos')}.
        </p>
      )}
      {outcome.rejected > 0 && (
        <p className="muted" data-cy="tx-bulk-rejected">
          {plural(outcome.rejected, 'lançamento não pôde', 'lançamentos não puderam')} ser recategorizado(s); abra a
          edição para corrigi-lo(s).
        </p>
      )}
      <button className="link" type="button" onClick={onClose}>
        fechar
      </button>
    </div>
  );
}

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}
