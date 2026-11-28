import { useEffect, useMemo, useState, type ReactNode } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Paper from '@mui/material/Paper';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { accountsApi } from '../api/accounts';
import { ApiError } from '../api/client';
import { transactionsApi } from '../api/transactions';
import type { Account, CreditCard, PaymentMethod, Transaction } from '../api/types';
import { CategoryDatalist, useCategorySuggestions } from './useCategorySuggestions';
import { DateField, Field, Notice, RowForm, SelectField, TableBox, TextAction } from '../ui';

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

  async function handleSubmit() {
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
  async function handleRecategorize() {
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

  const title = (
    <Typography variant="h2" component="h2" sx={{ mb: 2 }}>
      Lançamentos
    </Typography>
  );

  if (loading) {
    return (
      <section>
        {title}
        <Typography>Carregando…</Typography>
      </section>
    );
  }

  return (
    <section>
      {title}

      {accounts.length === 0 ? (
        <Typography color="text.secondary" sx={{ mb: 2 }}>
          Crie uma conta antes de registrar lançamentos.
        </Typography>
      ) : (
        <>
          {editing && (
            <Notice severity="info" dataCy="tx-editing" icon={false}>
              <strong>Editando: {editing.description}</strong>
              {editing.imported && (
                <Typography variant="body2" color="text.secondary" data-cy="tx-imported-notice" sx={{ mt: 0.5 }}>
                  Este lançamento veio de uma importação; desfazer a importação ainda o apaga.
                </Typography>
              )}
              {conflict && (
                <Typography variant="body2" color="warning.main" data-cy="tx-conflict" sx={{ mt: 0.5 }}>
                  Este lançamento mudou enquanto você editava. O formulário mostra agora o que está gravado; refaça a
                  alteração e salve de novo.
                </Typography>
              )}
            </Notice>
          )}
          <RowForm onSubmit={() => void handleSubmit()} dataCy="tx-form">
            <SelectField
              dataCy="tx-account"
              label="Conta"
              value={accountId}
              disabled={editing !== null}
              title={editing ? 'A conta de um lançamento não muda: exclua e lance de novo na outra conta.' : undefined}
              onChange={setAccountId}
              options={accounts.map((a) => ({ value: a.id, label: a.name }))}
            />
            <SelectField
              dataCy="tx-type"
              label="Tipo"
              value={type}
              onChange={(v) => setType(v as 'Expense' | 'Income')}
              options={[
                { value: 'Expense', label: 'Gasto' },
                { value: 'Income', label: 'Receita' },
              ]}
            />
            <SelectField
              dataCy="tx-payment-method"
              label="Meio"
              value={paymentMethod}
              onChange={setPaymentMethod}
              options={paymentMethods.map((m) => ({ value: m.code, label: m.name }))}
            />
            {requiresCard && (
              <SelectField dataCy="tx-card" label="Cartão" value={creditCardId} onChange={setCreditCardId} required>
                <option value="">Selecione o cartão…</option>
                {accountCards.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.label} •••• {c.lastFourDigits}
                  </option>
                ))}
              </SelectField>
            )}
            <Field dataCy="tx-description" label="Descrição" value={description} onChange={setDescription} required />
            <Field
              dataCy="tx-category"
              label="Categoria"
              value={category}
              onChange={setCategory}
              required
              slotProps={{ htmlInput: { list: 'tx-category-suggestions', autoComplete: 'off' } }}
            />
            <CategoryDatalist id="tx-category-suggestions" categories={categories} />
            <Field
              dataCy="tx-amount"
              label="Valor"
              type="number"
              value={amount}
              onChange={setAmount}
              required
              slotProps={{ htmlInput: { step: '0.01' } }}
            />
            {canSplit && (
              <SelectField
                dataCy="tx-installments"
                label="Parcelas"
                title="O valor é o total da compra; cada parcela cai numa fatura"
                value={String(installments)}
                onChange={(v) => setInstallments(Number(v))}
                options={Array.from({ length: 24 }, (_, i) => i + 1).map((n) => ({
                  value: String(n),
                  label: n === 1 ? 'À vista' : `${n}x`,
                }))}
              />
            )}
            <DateField dataCy="tx-date" label="Data" value={occurredOn} onChange={setOccurredOn} required />
            <Field
              dataCy="tx-notes"
              label="Observações (opcional)"
              value={notes}
              onChange={setNotes}
              slotProps={{ htmlInput: { maxLength: 500 } }}
            />
            <Button data-cy="tx-submit" variant="contained" type="submit" disabled={submitting} sx={{ height: 40 }}>
              {submitting ? 'Salvando…' : editing ? 'Salvar alteração' : 'Registrar'}
            </Button>
            {editing && (
              <Button data-cy="tx-cancel" variant="outlined" color="inherit" type="button" onClick={cancelEdit} sx={{ height: 40 }}>
                Cancelar
              </Button>
            )}
          </RowForm>
        </>
      )}

      <SelectField
        label="Filtrar por conta"
        value={filterAccount}
        onChange={setFilterAccount}
        sx={{ minWidth: 220, mb: 2 }}
        options={[{ value: '', label: 'Todas' }, ...accounts.map((a) => ({ value: a.id, label: a.name }))]}
      />

      {error && <Notice severity="error">{error}</Notice>}
      {bulkOutcome && <BulkOutcomeNotice outcome={bulkOutcome} onClose={() => setBulkOutcome(null)} />}
      {selected.size > 0 && (
        <Paper variant="outlined" sx={{ p: 1.5, mb: 2, borderColor: 'primary.main' }}>
          <RowForm onSubmit={() => void handleRecategorize()} dataCy="tx-bulk">
            <Typography component="strong" data-cy="tx-bulk-count" sx={{ fontWeight: 700, alignSelf: 'center' }}>
              {selected.size} {selected.size === 1 ? 'selecionado' : 'selecionados'}
            </Typography>
            <Field
              dataCy="tx-bulk-category"
              label="Nova categoria"
              value={bulkCategory}
              onChange={setBulkCategory}
              required
              slotProps={{
                htmlInput: {
                  list: 'tx-category-suggestions',
                  autoComplete: 'off',
                  maxLength: 60,
                  'aria-label': 'Nova categoria para os selecionados',
                },
              }}
            />
            <Button data-cy="tx-bulk-apply" variant="contained" type="submit" disabled={bulkSubmitting} sx={{ height: 40 }}>
              {bulkSubmitting ? 'Recategorizando…' : 'Recategorizar'}
            </Button>
            <Button
              data-cy="tx-bulk-clear"
              variant="outlined"
              color="inherit"
              type="button"
              onClick={() => setSelected(new Set())}
              sx={{ height: 40 }}
            >
              Limpar seleção
            </Button>
          </RowForm>
        </Paper>
      )}
      {transactions.length === 0 ? (
        <Typography color="text.secondary">Nenhum lançamento.</Typography>
      ) : (
        <TableBox>
          <Table data-cy="tx-table" sx={{ minWidth: 860 }}>
            <TableHead>
              <TableRow>
                <TableCell padding="checkbox">
                  <Checkbox
                    size="small"
                    checked={allSelected}
                    indeterminate={selected.size > 0 && !allSelected}
                    onChange={toggleAll}
                    slotProps={{ input: { 'aria-label': 'Selecionar todos', 'data-cy': 'tx-select-all' } as object }}
                  />
                </TableCell>
                <TableCell>Data</TableCell>
                <TableCell>Descrição</TableCell>
                <TableCell>Categoria</TableCell>
                <TableCell>Meio</TableCell>
                <TableCell>Conta</TableCell>
                <TableCell align="right">Valor</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {transactions.map((t) => (
                <TableRow key={t.id} data-cy="tx-row" data-type={t.type} selected={selected.has(t.id)} sx={{ verticalAlign: 'top' }}>
                  <TableCell padding="checkbox">
                    <Checkbox
                      size="small"
                      checked={selected.has(t.id)}
                      onChange={() => toggleSelected(t.id)}
                      slotProps={{ input: { 'aria-label': `Selecionar ${t.description}`, 'data-cy': 'tx-select' } as object }}
                    />
                  </TableCell>
                  <TableCell sx={{ whiteSpace: 'nowrap' }}>{t.occurredOn}</TableCell>
                  <TableCell>
                    {t.description}
                    {t.notes && <Sub dataCy="tx-notes-label">{t.notes}</Sub>}
                    {t.recurrenceId && (
                      <Sub dataCy="tx-recurring-label" title="Lançado a partir de uma recorrência">
                        recorrência
                      </Sub>
                    )}
                  </TableCell>
                  <TableCell>{t.category}</TableCell>
                  <TableCell>{paymentMethods.find((m) => m.code === t.paymentMethod)?.name ?? t.paymentMethod}</TableCell>
                  <TableCell>{accountName(t.accountId)}</TableCell>
                  <TableCell
                    align="right"
                    data-cy="tx-value"
                    // `positive`/`negative` sao lidas pelos testes (transaction-income).
                    className={t.type === 'Income' ? 'positive' : 'negative'}
                    sx={{ whiteSpace: 'nowrap', color: t.type === 'Income' ? 'success.main' : 'error.main' }}
                  >
                    {t.type === 'Income' ? '+' : '−'}
                    {t.amount.toLocaleString('pt-BR', { style: 'currency', currency: t.currency })}
                    {t.installments > 1 && <Sub dataCy="tx-installments-label">em {t.installments}x</Sub>}
                  </TableCell>
                  <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                    <TextAction data-cy="tx-edit" onClick={() => startEdit(t)}>
                      editar
                    </TextAction>
                    {onRepeat && !t.recurrenceId && t.installments === 1 && (
                      <TextAction data-cy="tx-repeat" title="Criar uma recorrência a partir deste lançamento" onClick={() => onRepeat(t)}>
                        repetir
                      </TextAction>
                    )}
                    <TextAction danger onClick={() => void handleRemove(t.id)}>
                      excluir
                    </TextAction>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableBox>
      )}
    </section>
  );
}

/** Linha menor dentro da célula (observação, recorrência, parcelas). */
function Sub({ children, dataCy, title }: { children: ReactNode; dataCy?: string; title?: string }) {
  return (
    <Box component="span" data-cy={dataCy} title={title} sx={{ display: 'block', fontSize: '0.75rem', color: 'text.secondary' }}>
      {children}
    </Box>
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
    <Notice
      severity={outcome.conflicts.length > 0 ? 'warning' : 'success'}
      dataCy="tx-bulk-result"
      onClose={onClose}
      slotProps={{ closeButton: { title: 'fechar' } }}
    >
      <div>
        {plural(done, 'lançamento ficou', 'lançamentos ficaram')} em <strong>{outcome.category}</strong>.
      </div>
      {outcome.conflicts.length > 0 && (
        <Box data-cy="tx-bulk-conflicts" sx={{ mt: 1 }}>
          <div>
            {plural(outcome.conflicts.length, 'lançamento mudou', 'lançamentos mudaram')} em outra aba e não{' '}
            {outcome.conflicts.length === 1 ? 'foi alterado' : 'foram alterados'}. A lista mostra o que está gravado
            agora e {outcome.conflicts.length === 1 ? 'ele continua marcado' : 'eles continuam marcados'}:
          </div>
          <Box component="ul" sx={{ my: 0.5, pl: 2.5 }}>
            {outcome.conflicts.map((t) => (
              <li key={t.id} data-cy="tx-bulk-conflict">
                {t.description} — {t.category}
              </li>
            ))}
          </Box>
        </Box>
      )}
      {outcome.notFound > 0 && (
        <Typography variant="body2" color="text.secondary" data-cy="tx-bulk-not-found">
          {plural(outcome.notFound, 'lançamento já tinha sido excluído', 'lançamentos já tinham sido excluídos')}.
        </Typography>
      )}
      {outcome.rejected > 0 && (
        <Typography variant="body2" color="text.secondary" data-cy="tx-bulk-rejected">
          {plural(outcome.rejected, 'lançamento não pôde', 'lançamentos não puderam')} ser recategorizado(s); abra a
          edição para corrigi-lo(s).
        </Typography>
      )}
    </Notice>
  );
}

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}
