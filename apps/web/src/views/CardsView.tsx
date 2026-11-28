import { Fragment, useEffect, useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { accountsApi } from '../api/accounts';
import { ApiError } from '../api/client';
import { invoicesApi, type CardLimit } from '../api/reports';
import type { Account, CreditCard } from '../api/types';
import { monthLabel, pendingChanges, todayIso } from '../cardCycle';
import { CardEditRow } from './CardEditRow';
import { InvoicesView } from './InvoicesView';
import { Field, Notice, RowForm, SelectField, TableBox, TextAction } from '../ui';

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

  async function handleCreate() {
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
      <Typography variant="h2" component="h2" sx={{ mb: 2 }}>
        Cartões de crédito
      </Typography>

      {accounts.length === 0 ? (
        <Typography color="text.secondary" sx={{ mb: 2 }}>
          Crie uma conta antes de cadastrar cartões.
        </Typography>
      ) : (
        <RowForm onSubmit={() => void handleCreate()} dataCy="card-form">
          <SelectField
            dataCy="card-account"
            label="Conta"
            value={accountId}
            onChange={setAccountId}
            options={accounts.map((a) => ({ value: a.id, label: a.name }))}
          />
          <Field dataCy="card-label" label="Apelido" value={label} onChange={setLabel} required />
          <Field dataCy="card-brand" label="Bandeira" value={brand} onChange={setBrand} />
          <Field
            dataCy="card-lastfour"
            label="4 dígitos"
            value={lastFour}
            onChange={setLastFour}
            required
            slotProps={{ htmlInput: { maxLength: 4, inputMode: 'numeric' } }}
          />
          <Field dataCy="card-limit" label="Limite" type="number" value={limit} onChange={setLimit} required slotProps={{ htmlInput: { step: '0.01' } }} />
          <Field dataCy="card-closing" label="Fecha" type="number" value={closingDay} onChange={setClosingDay} slotProps={{ htmlInput: { min: 1, max: 28 } }} />
          <Field dataCy="card-due" label="Vence" type="number" value={dueDay} onChange={setDueDay} slotProps={{ htmlInput: { min: 1, max: 28 } }} />
          <Button data-cy="card-submit" variant="contained" type="submit" disabled={submitting} sx={{ height: 40 }}>
            {submitting ? 'Criando…' : 'Adicionar'}
          </Button>
        </RowForm>
      )}

      {error && <Notice severity="error">{error}</Notice>}
      {notice && (
        <Notice severity="success" dataCy="card-notice">
          {notice}
        </Notice>
      )}
      {loading ? (
        <Typography>Carregando…</Typography>
      ) : cards.length === 0 ? (
        <Typography color="text.secondary">Nenhum cartão ainda.</Typography>
      ) : (
        <TableBox>
          <Table data-cy="cards-table" sx={{ minWidth: 820 }}>
            <TableHead>
              <TableRow>
                <TableCell>Apelido</TableCell>
                <TableCell>Bandeira</TableCell>
                <TableCell>Final</TableCell>
                <TableCell align="right">Limite</TableCell>
                <TableCell align="right">Disponível</TableCell>
                <TableCell>Fecha/Vence</TableCell>
                <TableCell>Conta</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {cards.map((c) => {
                const today = todayIso();
                const pending = pendingChanges(c.cycles, c.closingDay, c.dueDay, today);
                return (
                  <Fragment key={c.id}>
                    <TableRow data-cy="card-row" sx={{ opacity: c.isActive ? 1 : 0.5 }}>
                      <TableCell>{c.label}</TableCell>
                      <TableCell>{c.brand}</TableCell>
                      <TableCell sx={{ whiteSpace: 'nowrap' }}>•••• {c.lastFourDigits}</TableCell>
                      <TableCell align="right">{c.creditLimit.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}</TableCell>
                      <TableCell
                        align="right"
                        data-cy="card-available"
                        sx={{ color: limits[c.id]?.availableLimit < 0 ? 'error.main' : undefined }}
                        title={limits[c.id] ? `Falta pagar ${limits[c.id].outstanding.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}, com parcelas futuras` : undefined}
                      >
                        {limits[c.id] ? limits[c.id].availableLimit.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) : '—'}
                      </TableCell>
                      <TableCell data-cy="card-cycle">
                        {c.closingDay}/{c.dueDay}
                        {pending.map((p) => (
                          <Box
                            component="span"
                            key={p.fromDueMonth}
                            data-cy="card-cycle-pending"
                            sx={{ display: 'block', fontSize: '0.75rem', color: 'text.secondary' }}
                          >
                            a partir de {monthLabel(p.fromDueMonth!)}: {p.closingDay}/{p.dueDay}
                          </Box>
                        ))}
                      </TableCell>
                      <TableCell>{accountName(c.accountId)}</TableCell>
                      <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                        <Button variant="outlined" color="inherit" data-cy="card-invoices" onClick={() => setInvoicesOf(c)}>
                          Faturas
                        </Button>
                        {c.isActive && (
                          <TextAction
                            data-cy="card-edit-open"
                            aria-expanded={editingId === c.id}
                            onClick={() => setEditingId(editingId === c.id ? null : c.id)}
                            sx={{ ml: 0.5 }}
                          >
                            editar
                          </TextAction>
                        )}
                        {c.isActive && (
                          <TextAction danger onClick={() => void removeCard(c.id, load, setError)}>
                            desativar
                          </TextAction>
                        )}
                      </TableCell>
                    </TableRow>
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
            </TableBody>
          </Table>
        </TableBox>
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
