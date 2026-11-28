import { useEffect, useState } from 'react';
import ChevronLeft from '@mui/icons-material/ChevronLeft';
import ChevronRight from '@mui/icons-material/ChevronRight';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { ApiError } from '../api/client';
import { invoicesApi, type CardInvoices, type Invoice, type InvoiceStatus } from '../api/reports';
import { cardPaymentsApi } from '../api/transactions';
import type { Account, CreditCard } from '../api/types';
import { InvoiceReconciliationPanel } from './InvoiceReconciliationPanel';
import { DateField, Field, Notice, RowForm, SelectField, Stat, StatRow, StatusChip, TableBox, TextAction, type StatusTone } from '../ui';

const STATUS: Record<InvoiceStatus, { label: string; tone: StatusTone }> = {
  Open: { label: 'Aberta', tone: 'info' },
  Closed: { label: 'Fechada', tone: 'attention' },
  PartiallyPaid: { label: 'Parcialmente paga', tone: 'attention' },
  Overdue: { label: 'Vencida', tone: 'outside' },
  Paid: { label: 'Paga', tone: 'ok' },
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
  /** RCN-6: atalho para editar um lançamento (aba Lançamentos). */
  onEditTransaction?: (transactionId: string) => void;
}

/**
 * INV-7 (ADR-0005): faturas de um cartão. Abre na fatura atual e navega pelas anteriores e
 * próximas; registra e exclui pagamentos (INV-6), que abatem a fatura sem virar gasto.
 */
export function InvoicesView({ card, accounts, onBack, initialDueMonth, onEditTransaction }: Props) {
  const [data, setData] = useState<CardInvoices | null>(null);
  const [dueMonth, setDueMonth] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [paying, setPaying] = useState(false);
  const [busy, setBusy] = useState(false);
  // RCN-6: painel de conferência com a fatura do banco.
  const [reconciling, setReconciling] = useState(false);

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

  async function handlePay() {
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
      <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 1 }}>
        <Button variant="outlined" color="inherit" data-cy="invoices-back" onClick={onBack} startIcon={<ChevronLeft />}>
          Cartões
        </Button>
        <Typography variant="h2" component="h2">
          Faturas de {card.label}{' '}
          <Box component="span" sx={{ color: 'text.secondary', fontWeight: 400 }}>
            •••• {card.lastFourDigits}
          </Box>
        </Typography>
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        Fecha dia {card.closingDay} e vence dia {card.dueDay}. Compras até o fechamento entram na fatura que vence no
        mesmo ciclo; parcelas caem uma em cada fatura.
      </Typography>
      {data && (
        <Typography variant="body2" data-cy="invoice-limit" sx={{ mb: 2 }}>
          Limite {money(data.creditLimit, currency)} · em aberto {money(data.outstanding, currency)} (com parcelas futuras) ·{' '}
          <Box
            component="strong"
            data-cy="invoice-available"
            sx={{ color: data.availableLimit < 0 ? 'error.main' : 'success.main' }}
          >
            disponível {money(data.availableLimit, currency)}
          </Box>
        </Typography>
      )}

      {error && <Notice severity="error">{error}</Notice>}
      {loading ? (
        <Typography>Carregando…</Typography>
      ) : !invoice ? (
        <Typography color="text.secondary">Nenhuma fatura.</Typography>
      ) : (
        <>
          <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 1.5 }}>
            <IconButton
              data-cy="invoice-prev"
              aria-label="Fatura anterior"
              disabled={index <= 0}
              onClick={() => {
                setPaying(false);
                setDueMonth(invoices[index - 1].dueMonth);
              }}
            >
              <ChevronLeft />
            </IconButton>
            <Typography variant="h3" component="h3" data-cy="invoice-month" aria-live="polite">
              Fatura de {monthLabel(invoice.dueMonth)}
              {invoice.dueMonth === data?.currentDueMonth && (
                <Box component="span" sx={{ color: 'text.secondary', fontWeight: 400 }}>
                  {' '}
                  (atual)
                </Box>
              )}
            </Typography>
            <IconButton
              data-cy="invoice-next"
              aria-label="Próxima fatura"
              disabled={index >= invoices.length - 1}
              onClick={() => {
                setPaying(false);
                setDueMonth(invoices[index + 1].dueMonth);
              }}
            >
              <ChevronRight />
            </IconButton>
            <StatusChip
              tone={STATUS[invoice.status].tone}
              label={STATUS[invoice.status].label}
              dataCy="invoice-status"
              data={{ 'data-status': invoice.status }}
            />
            {invoice.reconciliation &&
              (invoice.reconciliation.changedSince ? (
                <StatusChip
                  tone="attention"
                  dataCy="invoice-reconciled"
                  data={{ 'data-changed': 'true' }}
                  title={`Conferida com ${money(invoice.reconciliation.appTotal, currency)}; o total agora é ${money(invoice.total, currency)}`}
                  label="Alterada depois da conferência"
                />
              ) : (
                <StatusChip
                  tone="ok"
                  dataCy="invoice-reconciled"
                  title={`Total do banco ${money(invoice.reconciliation.bankTotal, currency)}`}
                  label={`Conferida${invoice.reconciliation.difference !== 0 ? ` (dif. ${money(invoice.reconciliation.difference, currency)})` : ''}`}
                />
              ))}
          </Stack>

          <StatRow dataCy="invoice-figures">
            <Stat label="Total" value={money(invoice.total, currency)} tone="expense" valueDataCy="invoice-total" />
            <Stat label="Pago" value={money(invoice.paid, currency)} tone="income" valueDataCy="invoice-paid" />
            <Stat label="Falta pagar" value={money(invoice.remaining, currency)} valueDataCy="invoice-remaining" />
            <Stat
              label="Fechamento / vencimento"
              value={`${day(invoice.closingDate)} / ${day(invoice.dueDate)}`}
              small
              valueDataCy="invoice-dates"
            />
          </StatRow>

          <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mb: 2 }}>
            {invoice.remaining > 0 && !paying && (
              <Button
                variant="contained"
                data-cy="invoice-pay"
                disabled={busy || activeAccounts.length === 0}
                onClick={() => openPayment(invoice)}
              >
                Registrar pagamento
              </Button>
            )}
            <Button
              variant="outlined"
              color="inherit"
              data-cy="invoice-reconcile"
              aria-expanded={reconciling}
              onClick={() => setReconciling((open) => !open)}
            >
              {reconciling ? 'Fechar conferência' : 'Conferir com o banco'}
            </Button>
          </Stack>

          {reconciling && (
            <InvoiceReconciliationPanel
              card={card}
              dueMonth={invoice.dueMonth}
              currency={currency}
              onChanged={load}
              onEditTransaction={onEditTransaction}
              onShowInvoice={(month) => {
                if (invoices.some((i) => i.dueMonth === month)) setDueMonth(month);
              }}
            />
          )}

          {paying && (
            <RowForm onSubmit={() => void handlePay()} dataCy="payment-form">
              <SelectField
                dataCy="payment-account"
                label="Conta de origem"
                value={accountId}
                onChange={setAccountId}
                required
                options={activeAccounts.map((a) => ({ value: a.id, label: a.name }))}
              />
              <Field
                dataCy="payment-amount"
                label="Valor"
                type="number"
                value={amount}
                onChange={setAmount}
                required
                slotProps={{ htmlInput: { step: '0.01', min: '0.01' } }}
              />
              <DateField dataCy="payment-date" label="Data" value={paidOn} onChange={setPaidOn} required />
              <Button data-cy="payment-submit" variant="contained" type="submit" disabled={busy} sx={{ height: 40 }}>
                {busy ? 'Registrando…' : 'Registrar'}
              </Button>
              <Button variant="outlined" color="inherit" type="button" onClick={() => setPaying(false)} disabled={busy} sx={{ height: 40 }}>
                Cancelar
              </Button>
            </RowForm>
          )}

          {invoice.payments.length > 0 && (
            <>
              <Typography variant="h3" component="h3" sx={{ mb: 1 }}>
                Pagamentos
              </Typography>
              <TableBox>
                <Table data-cy="invoice-payments">
                  <TableHead>
                    <TableRow>
                      <TableCell>Data</TableCell>
                      <TableCell>Conta</TableCell>
                      <TableCell align="right">Valor</TableCell>
                      <TableCell />
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {invoice.payments.map((p) => (
                      <TableRow key={p.paymentId} data-cy="invoice-payment-row">
                        <TableCell>{day(p.paidOn)}</TableCell>
                        <TableCell>{accountName(p.accountId)}</TableCell>
                        <TableCell align="right">{money(p.amount, currency)}</TableCell>
                        <TableCell align="right">
                          <TextAction
                            danger
                            data-cy="invoice-payment-delete"
                            disabled={busy}
                            onClick={() => void handleDeletePayment(p.paymentId)}
                          >
                            excluir
                          </TextAction>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableBox>
            </>
          )}

          <Typography variant="h3" component="h3" sx={{ mb: 1 }}>
            Compras
          </Typography>
          {invoice.items.length === 0 ? (
            <Typography color="text.secondary">Nenhuma compra nesta fatura.</Typography>
          ) : (
            <TableBox>
              <Table data-cy="invoice-items" sx={{ minWidth: 560 }}>
                <TableHead>
                  <TableRow>
                    <TableCell>Data</TableCell>
                    <TableCell>Descrição</TableCell>
                    <TableCell>Categoria</TableCell>
                    <TableCell>Parcela</TableCell>
                    <TableCell align="right">Valor</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {invoice.items.map((item) => (
                    <TableRow key={`${item.transactionId}-${item.installment}`} data-cy="invoice-item">
                      <TableCell>{day(item.purchaseDate)}</TableCell>
                      <TableCell>{item.description ?? '—'}</TableCell>
                      <TableCell>{item.category}</TableCell>
                      <TableCell data-cy="invoice-item-installment">
                        {item.installments > 1 ? `${item.installment}/${item.installments}` : 'à vista'}
                      </TableCell>
                      <TableCell align="right" sx={{ color: item.amount < 0 ? 'success.main' : undefined }}>
                        {money(item.amount, currency)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableBox>
          )}
        </>
      )}
    </section>
  );
}
