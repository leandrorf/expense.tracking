import { useEffect, useMemo, useState } from 'react';
import { accountsApi } from '../api/accounts';
import { ApiError } from '../api/client';
import { transactionsApi } from '../api/transactions';
import type { Account, CreditCard, PaymentMethod, Transaction } from '../api/types';
import { CategoryDatalist, useCategorySuggestions } from './useCategorySuggestions';

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

export function TransactionsView() {
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
    setTransactions(await transactionsApi.list(filterAccount ? { accountId: filterAccount } : {}));
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
      {transactions.length === 0 ? (
        <p className="muted">Nenhum lançamento.</p>
      ) : (
        <table className="data" data-cy="tx-table">
          <thead>
            <tr>
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
              <tr key={t.id} data-cy="tx-row" data-type={t.type}>
                <td>{t.occurredOn}</td>
                <td>
                  {t.description}
                  {t.notes && (
                    <span className="sub" data-cy="tx-notes-label">
                      {t.notes}
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

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}
