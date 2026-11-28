import { Fragment, useEffect, useState } from 'react';
import { accountsApi } from '../api/accounts';
import { ApiError } from '../api/client';
import { invoicesApi, type CardLimit } from '../api/reports';
import type { Account, CreditCard } from '../api/types';
import { monthLabel, pendingChanges, todayIso } from '../cardCycle';
import { CardEditRow } from './CardEditRow';
import { InvoicesView } from './InvoicesView';

/**
 * `openInvoice` (NTF-3): fatura que um aviso pediu para abrir; a aba vai direto para ela depois de
 * carregar os cartões.
 */
export function CardsView({
  openInvoice = null,
  onInvoiceOpened,
  onEditTransaction,
}: {
  openInvoice?: { cardId: string; dueMonth: string } | null;
  onInvoiceOpened?: () => void;
  /** RCN-6: atalho da conferência para editar um lançamento. */
  onEditTransaction?: (transactionId: string) => void;
} = {}) {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [cards, setCards] = useState<CreditCard[]>([]);
  // INV-10: limite disponível por cartão (vem do reporting; sem ele, a lista continua funcionando).
  const [limits, setLimits] = useState<Record<string, CardLimit>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [accountId, setAccountId] = useState('');
  const [label, setLabel] = useState('');
  const [brand, setBrand] = useState('');
  const [lastFour, setLastFour] = useState('');
  const [limit, setLimit] = useState('');
  const [closingDay, setClosingDay] = useState('10');
  const [dueDay, setDueDay] = useState('17');
  const [submitting, setSubmitting] = useState(false);
  // CYC-5: cartão em edição (dados e ciclo) e o recado da última alteração.
  const [editingId, setEditingId] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  // INV-7: cartão cujas faturas estão abertas (a aba mostra a lista ou as faturas).
  const [invoicesOf, setInvoicesOf] = useState<CreditCard | null>(null);
  const [initialDueMonth, setInitialDueMonth] = useState<string | undefined>(undefined);

  useEffect(() => {
    if (!openInvoice || cards.length === 0) return;
    const card = cards.find((c) => c.id === openInvoice.cardId);
    if (card) {
      setInitialDueMonth(openInvoice.dueMonth);
      setInvoicesOf(card);
    }
    onInvoiceOpened?.();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openInvoice, cards]);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const [accs, crds] = await Promise.all([accountsApi.list(), accountsApi.listCards()]);
      setAccounts(accs);
      setCards(crds);
      invoicesApi
        .limits()
        .then((list) => setLimits(Object.fromEntries(list.map((l) => [l.cardId, l]))))
        .catch(() => setLimits({}));
      if (!accountId && accs.length > 0) {
        setAccountId(accs[0].id);
      }
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await accountsApi.createCard({
        accountId,
        label,
        brand: brand.trim() ? brand.trim() : undefined,
        lastFourDigits: lastFour,
        creditLimit: Number(limit),
        closingDay: Number(closingDay),
        dueDay: Number(dueDay),
      });
      setLabel('');
      setBrand('');
      setLastFour('');
      setLimit('');
      await load();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setSubmitting(false);
    }
  }

  const accountName = (id: string) => accounts.find((a) => a.id === id)?.name ?? '—';

  if (invoicesOf) {
    // Ao voltar, recarrega: um pagamento feito na fatura muda o limite disponível.
    return (
      <InvoicesView
        key={`${invoicesOf.id}:${initialDueMonth ?? ''}`}
        card={invoicesOf}
        accounts={accounts}
        initialDueMonth={initialDueMonth}
        onEditTransaction={onEditTransaction}
        onBack={() => {
          setInvoicesOf(null);
          setInitialDueMonth(undefined);
          void load();
        }}
      />
    );
  }

  return (
    <section>
      <h2>Cartões de crédito</h2>

      {accounts.length === 0 ? (
        <p className="muted">Crie uma conta antes de cadastrar cartões.</p>
      ) : (
        <form className="row-form wrap" onSubmit={handleCreate} data-cy="card-form">
          <select data-cy="card-account" value={accountId} onChange={(e) => setAccountId(e.target.value)}>
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </select>
          <input data-cy="card-label" placeholder="Apelido" value={label} onChange={(e) => setLabel(e.target.value)} required />
          <input data-cy="card-brand" placeholder="Bandeira" value={brand} onChange={(e) => setBrand(e.target.value)} />
          <input
            data-cy="card-lastfour"
            placeholder="4 dígitos"
            value={lastFour}
            maxLength={4}
            onChange={(e) => setLastFour(e.target.value)}
            required
          />
          <input data-cy="card-limit" placeholder="Limite" type="number" step="0.01" value={limit} onChange={(e) => setLimit(e.target.value)} required />
          <input data-cy="card-closing" placeholder="Fecha" type="number" min={1} max={28} value={closingDay} onChange={(e) => setClosingDay(e.target.value)} />
          <input data-cy="card-due" placeholder="Vence" type="number" min={1} max={28} value={dueDay} onChange={(e) => setDueDay(e.target.value)} />
          <button data-cy="card-submit" className="primary" type="submit" disabled={submitting}>
            {submitting ? 'Criando…' : 'Adicionar'}
          </button>
        </form>
      )}

      {error && <p className="error">{error}</p>}
      {notice && (
        <p className="notice" data-cy="card-notice">
          {notice}
        </p>
      )}
      {loading ? (
        <p>Carregando…</p>
      ) : cards.length === 0 ? (
        <p className="muted">Nenhum cartão ainda.</p>
      ) : (
        <table className="data" data-cy="cards-table">
          <thead>
            <tr>
              <th>Apelido</th>
              <th>Bandeira</th>
              <th>Final</th>
              <th>Limite</th>
              <th>Disponível</th>
              <th>Fecha/Vence</th>
              <th>Conta</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {cards.map((c) => {
              const today = todayIso();
              const pending = pendingChanges(c.cycles, c.closingDay, c.dueDay, today);
              return (
                <Fragment key={c.id}>
                  <tr className={c.isActive ? '' : 'inactive'} data-cy="card-row">
                    <td>{c.label}</td>
                    <td>{c.brand}</td>
                    <td>•••• {c.lastFourDigits}</td>
                    <td>{c.creditLimit.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}</td>
                    <td
                      data-cy="card-available"
                      className={limits[c.id]?.availableLimit < 0 ? 'negative' : ''}
                      title={limits[c.id] ? `Falta pagar ${limits[c.id].outstanding.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}, com parcelas futuras` : undefined}
                    >
                      {limits[c.id] ? limits[c.id].availableLimit.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) : '—'}
                    </td>
                    <td data-cy="card-cycle">
                      {c.closingDay}/{c.dueDay}
                      {pending.map((p) => (
                        <span key={p.fromDueMonth} className="sub" data-cy="card-cycle-pending">
                          a partir de {monthLabel(p.fromDueMonth!)}: {p.closingDay}/{p.dueDay}
                        </span>
                      ))}
                    </td>
                    <td>{accountName(c.accountId)}</td>
                    <td>
                      <button className="ghost small" data-cy="card-invoices" onClick={() => setInvoicesOf(c)}>
                        Faturas
                      </button>
                      {c.isActive && (
                        <button
                          className="link"
                          data-cy="card-edit-open"
                          aria-expanded={editingId === c.id}
                          onClick={() => setEditingId(editingId === c.id ? null : c.id)}
                        >
                          editar
                        </button>
                      )}
                      {c.isActive && (
                        <button className="link-danger" onClick={() => void removeCard(c.id, load, setError)}>
                          desativar
                        </button>
                      )}
                    </td>
                  </tr>
                  {editingId === c.id && (
                    <CardEditRow
                      key={`${c.id}:${JSON.stringify(c.cycles)}:${c.creditLimit}:${c.label}`}
                      card={c}
                      colSpan={8}
                      onClose={() => setEditingId(null)}
                      onSaved={async (message) => {
                        setNotice(message);
                        await load();
                      }}
                    />
                  )}
                </Fragment>
              );
            })}
          </tbody>
        </table>
      )}
    </section>
  );
}

async function removeCard(id: string, reload: () => Promise<void>, setError: (m: string) => void) {
  try {
    await accountsApi.removeCard(id);
    await reload();
  } catch (err) {
    setError(messageOf(err));
  }
}

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}
