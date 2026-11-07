import { useEffect, useMemo, useState } from 'react';
import { accountsApi } from '../api/accounts';
import { ApiError } from '../api/client';
import { transactionsApi } from '../api/transactions';
import type { Account, CreditCard, Transaction } from '../api/types';

// Valores = nomes do enum do servico transactions (o catalogo do accounts usa codigos snake_case).
const PAYMENT_METHODS: { value: string; label: string; requiresCard: boolean }[] = [
  { value: 'Cash', label: 'Dinheiro', requiresCard: false },
  { value: 'Pix', label: 'Pix', requiresCard: false },
  { value: 'Boleto', label: 'Boleto', requiresCard: false },
  { value: 'CreditCard', label: 'Cartão de crédito', requiresCard: true },
  { value: 'DebitCard', label: 'Cartão de débito', requiresCard: true },
  { value: 'BankTransfer', label: 'Transferência', requiresCard: false },
];

function today(): string {
  return new Date().toISOString().slice(0, 10);
}

export function TransactionsView() {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [cards, setCards] = useState<CreditCard[]>([]);
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [filterAccount, setFilterAccount] = useState('');

  const [accountId, setAccountId] = useState('');
  const [paymentMethod, setPaymentMethod] = useState('Pix');
  const [creditCardId, setCreditCardId] = useState('');
  const [description, setDescription] = useState('');
  const [category, setCategory] = useState('');
  const [amount, setAmount] = useState('');
  const [occurredOn, setOccurredOn] = useState(today());
  const [submitting, setSubmitting] = useState(false);

  const requiresCard = useMemo(
    () => PAYMENT_METHODS.find((m) => m.value === paymentMethod)?.requiresCard ?? false,
    [paymentMethod],
  );

  async function loadRefData() {
    const [accs, crds] = await Promise.all([accountsApi.list(), accountsApi.listCards()]);
    setAccounts(accs);
    setCards(crds);
    if (!accountId && accs.length > 0) setAccountId(accs[0].id);
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

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await transactionsApi.create({
        accountId,
        type: 'Expense',
        paymentMethod,
        creditCardId: requiresCard && creditCardId ? creditCardId : undefined,
        description,
        category,
        amount: Number(amount),
        occurredOn,
      });
      setDescription('');
      setCategory('');
      setAmount('');
      await loadTransactions();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setSubmitting(false);
    }
  }

  async function handleRemove(id: string) {
    try {
      await transactionsApi.remove(id);
      await loadTransactions();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  const accountName = (id: string) => accounts.find((a) => a.id === id)?.name ?? '—';
  const accountCards = cards.filter((c) => c.accountId === accountId && c.isActive);

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
        <form className="row-form wrap" onSubmit={handleCreate}>
          <select value={accountId} onChange={(e) => setAccountId(e.target.value)}>
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </select>
          <select value={paymentMethod} onChange={(e) => setPaymentMethod(e.target.value)}>
            {PAYMENT_METHODS.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label}
              </option>
            ))}
          </select>
          {requiresCard && (
            <select value={creditCardId} onChange={(e) => setCreditCardId(e.target.value)} required>
              <option value="">Selecione o cartão…</option>
              {accountCards.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.label} •••• {c.lastFourDigits}
                </option>
              ))}
            </select>
          )}
          <input placeholder="Descrição" value={description} onChange={(e) => setDescription(e.target.value)} required />
          <input placeholder="Categoria" value={category} onChange={(e) => setCategory(e.target.value)} required />
          <input placeholder="Valor" type="number" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} required />
          <input type="date" value={occurredOn} onChange={(e) => setOccurredOn(e.target.value)} required />
          <button className="primary" type="submit" disabled={submitting}>
            {submitting ? 'Salvando…' : 'Registrar'}
          </button>
        </form>
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
        <table className="data">
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
              <tr key={t.id}>
                <td>{t.occurredOn}</td>
                <td>{t.description}</td>
                <td>{t.category}</td>
                <td>{PAYMENT_METHODS.find((m) => m.value === t.paymentMethod)?.label ?? t.paymentMethod}</td>
                <td>{accountName(t.accountId)}</td>
                <td className="right">
                  {t.amount.toLocaleString('pt-BR', { style: 'currency', currency: t.currency })}
                </td>
                <td>
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
