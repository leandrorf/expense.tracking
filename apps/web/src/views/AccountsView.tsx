import { useEffect, useState } from 'react';
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
import type { Account } from '../api/types';
import { DateField, Field, Notice, RowForm, ScopeSelect, TableBox, TextAction } from '../ui';

export function AccountsView() {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [name, setName] = useState('');
  const [scope, setScope] = useState<'Personal' | 'Business'>('Personal');
  const [taxId, setTaxId] = useState('');
  const [submitting, setSubmitting] = useState(false);
  // INV-13: uma conta por vez tem o saldo em edicao, na propria linha.
  const [balanceFor, setBalanceFor] = useState<string | null>(null);
  const [balanceAmount, setBalanceAmount] = useState('');
  const [balanceDate, setBalanceDate] = useState(today());

  async function load() {
    setLoading(true);
    setError(null);
    try {
      setAccounts(await accountsApi.list());
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  async function handleCreate() {
    setSubmitting(true);
    setError(null);
    try {
      await accountsApi.create({
        name,
        scope,
        taxId: taxId.trim() ? taxId.trim() : undefined,
      });
      setName('');
      setTaxId('');
      await load();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setSubmitting(false);
    }
  }

  function startBalance(a: Account) {
    setBalanceFor(a.id);
    setBalanceAmount(a.openingBalance === null ? '' : String(a.openingBalance));
    setBalanceDate(a.openingBalanceOn ?? today());
    setError(null);
  }

  async function handleSaveBalance() {
    if (!balanceFor) return;
    setError(null);
    try {
      await accountsApi.setOpeningBalance(balanceFor, Number(balanceAmount), balanceDate);
      setBalanceFor(null);
      await load();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  async function handleClearBalance(id: string) {
    setError(null);
    try {
      await accountsApi.clearOpeningBalance(id);
      setBalanceFor(null);
      await load();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  async function handleRemove(id: string) {
    try {
      await accountsApi.remove(id);
      await load();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  return (
    <section>
      <Typography variant="h2" component="h2" sx={{ mb: 2 }}>
        Contas
      </Typography>

      <RowForm onSubmit={() => void handleCreate()} dataCy="account-form">
        <Field dataCy="account-name" label="Nome da conta" value={name} onChange={setName} required />
        <ScopeSelect dataCy="account-scope" value={scope} onChange={(v) => setScope(v as 'Personal' | 'Business')} />
        <Field
          dataCy="account-taxid"
          label={scope === 'Business' ? 'CNPJ (opcional)' : 'CPF (opcional)'}
          value={taxId}
          onChange={setTaxId}
        />
        <Button data-cy="account-submit" variant="contained" type="submit" disabled={submitting} sx={{ height: 40 }}>
          {submitting ? 'Criando…' : 'Adicionar'}
        </Button>
      </RowForm>

      {error && <Notice severity="error">{error}</Notice>}
      {loading ? (
        <Typography>Carregando…</Typography>
      ) : accounts.length === 0 ? (
        <Typography color="text.secondary">Nenhuma conta ainda.</Typography>
      ) : (
        <TableBox>
          <Table data-cy="accounts-table" sx={{ minWidth: 640 }}>
            <TableHead>
              <TableRow>
                <TableCell>Nome</TableCell>
                <TableCell>Escopo</TableCell>
                <TableCell>Documento</TableCell>
                <TableCell>Moeda</TableCell>
                <TableCell title="Ponto de partida do saldo projetado na Previsão">Saldo</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {accounts.map((a) => (
                <TableRow key={a.id} data-cy="account-row" sx={{ opacity: a.isActive ? 1 : 0.5 }}>
                  <TableCell>{a.name}</TableCell>
                  <TableCell>{a.scope === 'Business' ? 'PJ' : 'PF'}</TableCell>
                  <TableCell>{a.taxId ?? '—'}</TableCell>
                  <TableCell>{a.currency}</TableCell>
                  <TableCell data-cy="account-balance">
                    {balanceFor === a.id ? (
                      <Box
                        component="form"
                        data-cy="account-balance-form"
                        onSubmit={(e: React.FormEvent) => {
                          e.preventDefault();
                          void handleSaveBalance();
                        }}
                        sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, alignItems: 'center' }}
                      >
                        <Field
                          dataCy="account-balance-amount"
                          label="Saldo"
                          type="number"
                          value={balanceAmount}
                          onChange={setBalanceAmount}
                          required
                          slotProps={{ htmlInput: { step: '0.01' } }}
                          sx={{ width: 130 }}
                        />
                        <DateField
                          dataCy="account-balance-date"
                          label="No fim do dia"
                          title="Saldo no fim deste dia; o que vier depois entra na projeção"
                          value={balanceDate}
                          onChange={setBalanceDate}
                          required
                          slotProps={{ htmlInput: { max: today() } }}
                          sx={{ width: 160 }}
                        />
                        <Button data-cy="account-balance-save" variant="contained" type="submit">
                          Salvar
                        </Button>
                        <Button variant="outlined" color="inherit" type="button" onClick={() => setBalanceFor(null)}>
                          Cancelar
                        </Button>
                      </Box>
                    ) : (
                      <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 0.5 }}>
                        {a.openingBalance === null ? (
                          <Box component="span" sx={{ color: 'text.secondary' }}>
                            —
                          </Box>
                        ) : (
                          <span data-cy="account-balance-value">
                            {a.openingBalance.toLocaleString('pt-BR', { style: 'currency', currency: a.currency })}
                            <Box component="span" sx={{ display: 'block', fontSize: '0.75rem', color: 'text.secondary' }}>
                              em {dateLabel(a.openingBalanceOn!)}
                            </Box>
                          </span>
                        )}
                        <TextAction data-cy="account-balance-edit" onClick={() => startBalance(a)}>
                          {a.openingBalance === null ? 'informar' : 'alterar'}
                        </TextAction>
                        {a.openingBalance !== null && (
                          <TextAction danger data-cy="account-balance-clear" onClick={() => void handleClearBalance(a.id)}>
                            remover
                          </TextAction>
                        )}
                      </Box>
                    )}
                  </TableCell>
                  <TableCell align="right">
                    {a.isActive && (
                      <TextAction danger onClick={() => void handleRemove(a.id)}>
                        desativar
                      </TextAction>
                    )}
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

/** Hoje no fuso do navegador, `yyyy-MM-dd`. */
function today(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** `2026-11-10` → `10/11/2026`. */
function dateLabel(iso: string): string {
  const [y, m, d] = iso.split('-');
  return `${d}/${m}/${y}`;
}

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}
