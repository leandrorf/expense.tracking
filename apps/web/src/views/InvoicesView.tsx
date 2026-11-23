import { useEffect, useState } from 'react';
import { ApiError } from '../api/client';
import { invoicesApi, type CardInvoices, type Invoice, type InvoiceStatus } from '../api/reports';
import { cardPaymentsApi } from '../api/transactions';
import type { Account, CreditCard } from '../api/types';

const STATUS: Record<InvoiceStatus, { label: string; tone: string }> = {
  Open: { label: 'Aberta', tone: 'open' },
  Closed: { label: 'Fechada', tone: 'warning' },
  PartiallyPaid: { label: 'Parcialmente paga', tone: 'warning' },
  Overdue: { label: 'Vencida', tone: 'exceeded' },
  Paid: { label: 'Paga', tone: 'paid' },
};

/** A projeção do reporting recebe o pagamento alguns segundos depois (feed do transactions). */
const PROJECTION_WAIT_MS = 20_000;

const money = (value: number, currency: string) =>
  value.toLocaleString('pt-BR', { style: 'currency', currency: currency || 'BRL' });

/** `2026-12-17` → `17/12/2026`, sem passar por Date (fuso). */
const day = (iso: string) => iso.split('-').reverse().join('/');

/** `2026-12` → `12/2026`. */
function monthLabel(month: string): string {
  const [year, m] = month.split('-');
  return `${m}/${year}`;
}

function today(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}

interface Props {
  card: CreditCard;
  accounts: Account[];
  onBack: () => void;
  /** NTF-3: abre nesta fatura (`yyyy-MM`) em vez da atual, se ela existir. */
  initialDueMonth?: string;
}

/**
 * INV-7 (ADR-0005): faturas de um cartão. Abre na fatura atual e navega pelas anteriores e
 * próximas; registra e exclui pagamentos (INV-6), que abatem a fatura sem virar gasto.
 */
export function InvoicesView({ card, accounts, onBack, initialDueMonth }: Props) {
  const [data, setData] = useState<CardInvoices | null>(null);
  const [dueMonth, setDueMonth] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [paying, setPaying] = useState(false);
  const [busy, setBusy] = useState(false);

  const activeAccounts = accounts.filter((a) => a.isActive);
  const [accountId, setAccountId] = useState(card.accountId);
  const [amount, setAmount] = useState('');
  const [paidOn, setPaidOn] = useState(today());

  async function load(): Promise<CardInvoices | null> {
    setError(null);
    try {
      const result = await invoicesApi.list(card.id);
      setData(result);
      setDueMonth(
        (current) =>
          current ??
          (initialDueMonth && result.invoices.some((i) => i.dueMonth === initialDueMonth) ? initialDueMonth : result.currentDueMonth),
      );
      return result;
    } catch (err) {
      setError(messageOf(err));
      return null;
    } finally {
      setLoading(false);
    }
  }

  /** Recarrega até a fatura refletir o pagamento gravado (ou desistir depois de alguns segundos). */
  async function reloadUntil(month: string, paid: (invoice: Invoice | undefined) => boolean) {
    const started = Date.now();
    for (;;) {
      const result = await load();
      if (!result || paid(result.invoices.find((i) => i.dueMonth === month)) || Date.now() - started > PROJECTION_WAIT_MS) {
        return;
      }
      await new Promise((resolve) => setTimeout(resolve, 1_000));
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [card.id]);

  const invoices = data?.invoices ?? [];
  const index = invoices.findIndex((i) => i.dueMonth === dueMonth);
  const invoice = index >= 0 ? invoices[index] : undefined;
  const currency = data?.currency ?? 'BRL';

  function openPayment(target: Invoice) {
    setAmount(target.remaining.toFixed(2));
    setPaidOn(today());
    setAccountId(activeAccounts.some((a) => a.id === card.accountId) ? card.accountId : activeAccounts[0]?.id ?? '');
    setPaying(true);
  }

  async function handlePay(e: React.FormEvent) {
    e.preventDefault();
    if (!invoice) return;
    setBusy(true);
    setError(null);
    try {
      const before = invoice.paid;
      await cardPaymentsApi.register({
        creditCardId: card.id,
        invoiceDueMonth: invoice.dueMonth,
        accountId,
        amount: Number(amount),
        paidOn,
      });
      setPaying(false);
      await reloadUntil(invoice.dueMonth, (i) => (i?.paid ?? 0) > before);
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  async function handleDeletePayment(paymentId: string) {
    if (!invoice) return;
    setBusy(true);
    setError(null);
    try {
      await cardPaymentsApi.remove(paymentId);
      await reloadUntil(invoice.dueMonth, (i) => !i?.payments.some((p) => p.paymentId === paymentId));
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  const accountName = (id: string) => accounts.find((a) => a.id === id)?.name ?? '—';

  return (
    <section data-cy="invoices">
      <div className="invoices-head">
        <button type="button" className="ghost small" data-cy="invoices-back" onClick={onBack}>
          ‹ Cartões
        </button>
        <h2>
          Faturas de {card.label} <span className="muted">•••• {card.lastFourDigits}</span>
        </h2>
      </div>
      <p className="muted">
        Fecha dia {card.closingDay} e vence dia {card.dueDay}. Compras até o fechamento entram na fatura que vence no
        mesmo ciclo; parcelas caem uma em cada fatura.
      </p>
      {data && (
        <p className="limit-line" data-cy="invoice-limit">
          Limite {money(data.creditLimit, currency)} · em aberto {money(data.outstanding, currency)} (com parcelas futuras) ·{' '}
          <strong className={data.availableLimit < 0 ? 'negative' : 'positive'} data-cy="invoice-available">
            disponível {money(data.availableLimit, currency)}
          </strong>
        </p>
      )}

      {error && <p className="error">{error}</p>}
      {loading ? (
        <p>Carregando…</p>
      ) : !invoice ? (
        <p className="muted">Nenhuma fatura.</p>
      ) : (
        <>
          <div className="month-nav">
            <button
              type="button"
              className="ghost small"
              data-cy="invoice-prev"
              aria-label="Fatura anterior"
              disabled={index <= 0}
              onClick={() => {
                setPaying(false);
                setDueMonth(invoices[index - 1].dueMonth);
              }}
            >
              ‹
            </button>
            <h3 data-cy="invoice-month" aria-live="polite">
              Fatura de {monthLabel(invoice.dueMonth)}
              {invoice.dueMonth === data?.currentDueMonth && <span className="muted"> (atual)</span>}
            </h3>
            <button
              type="button"
              className="ghost small"
              data-cy="invoice-next"
              aria-label="Próxima fatura"
              disabled={index >= invoices.length - 1}
              onClick={() => {
                setPaying(false);
                setDueMonth(invoices[index + 1].dueMonth);
              }}
            >
              ›
            </button>
            <span className={`health-badge ${STATUS[invoice.status].tone}`} data-cy="invoice-status" data-status={invoice.status}>
              {STATUS[invoice.status].label}
            </span>
          </div>

          <div className="cards-row" data-cy="invoice-figures">
            <div className="stat">
              <span className="stat-label">Total</span>
              <span className="stat-value expense" data-cy="invoice-total">{money(invoice.total, currency)}</span>
            </div>
            <div className="stat">
              <span className="stat-label">Pago</span>
              <span className="stat-value income" data-cy="invoice-paid">{money(invoice.paid, currency)}</span>
            </div>
            <div className="stat">
              <span className="stat-label">Falta pagar</span>
              <span className="stat-value" data-cy="invoice-remaining">{money(invoice.remaining, currency)}</span>
            </div>
            <div className="stat">
              <span className="stat-label">Fechamento / vencimento</span>
              <span className="stat-value small-value" data-cy="invoice-dates">
                {day(invoice.closingDate)} / {day(invoice.dueDate)}
              </span>
            </div>
          </div>

          {invoice.remaining > 0 && !paying && (
            <button
              type="button"
              className="primary"
              data-cy="invoice-pay"
              disabled={busy || activeAccounts.length === 0}
              onClick={() => openPayment(invoice)}
            >
              Registrar pagamento
            </button>
          )}

          {paying && (
            <form className="row-form wrap" onSubmit={handlePay} data-cy="payment-form">
              <label className="field">
                <span>Conta de origem</span>
                <select data-cy="payment-account" value={accountId} onChange={(e) => setAccountId(e.target.value)} required>
                  {activeAccounts.map((a) => (
                    <option key={a.id} value={a.id}>
                      {a.name}
                    </option>
                  ))}
                </select>
              </label>
              <label className="field">
                <span>Valor</span>
                <input
                  data-cy="payment-amount"
                  type="number"
                  step="0.01"
                  min="0.01"
                  value={amount}
                  onChange={(e) => setAmount(e.target.value)}
                  required
                />
              </label>
              <label className="field">
                <span>Data</span>
                <input data-cy="payment-date" type="date" value={paidOn} onChange={(e) => setPaidOn(e.target.value)} required />
              </label>
              <button data-cy="payment-submit" className="primary" type="submit" disabled={busy}>
                {busy ? 'Registrando…' : 'Registrar'}
              </button>
              <button type="button" className="ghost" onClick={() => setPaying(false)} disabled={busy}>
                Cancelar
              </button>
            </form>
          )}

          {invoice.payments.length > 0 && (
            <>
              <h3 className="section-title">Pagamentos</h3>
              <table className="data" data-cy="invoice-payments">
                <thead>
                  <tr>
                    <th>Data</th>
                    <th>Conta</th>
                    <th>Valor</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {invoice.payments.map((p) => (
                    <tr key={p.paymentId} data-cy="invoice-payment-row">
                      <td>{day(p.paidOn)}</td>
                      <td>{accountName(p.accountId)}</td>
                      <td>{money(p.amount, currency)}</td>
                      <td>
                        <button
                          className="link-danger"
                          data-cy="invoice-payment-delete"
                          disabled={busy}
                          onClick={() => void handleDeletePayment(p.paymentId)}
                        >
                          excluir
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}

          <h3 className="section-title">Compras</h3>
          {invoice.items.length === 0 ? (
            <p className="muted">Nenhuma compra nesta fatura.</p>
          ) : (
            <table className="data" data-cy="invoice-items">
              <thead>
                <tr>
                  <th>Data</th>
                  <th>Descrição</th>
                  <th>Categoria</th>
                  <th>Parcela</th>
                  <th>Valor</th>
                </tr>
              </thead>
              <tbody>
                {invoice.items.map((item) => (
                  <tr key={`${item.transactionId}-${item.installment}`} data-cy="invoice-item">
                    <td>{day(item.purchaseDate)}</td>
                    <td>{item.description ?? '—'}</td>
                    <td>{item.category}</td>
                    <td data-cy="invoice-item-installment">
                      {item.installments > 1 ? `${item.installment}/${item.installments}` : 'à vista'}
                    </td>
                    <td className={item.amount < 0 ? 'income' : ''}>{money(item.amount, currency)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </section>
  );
}
