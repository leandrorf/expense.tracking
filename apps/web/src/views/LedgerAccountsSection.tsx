import { useEffect, useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { ledgerAccountsApi, type LedgerAccount } from '../api/accountingExport';
import { ApiError } from '../api/client';
import { Field, Notice, TableBox } from '../ui';

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}

interface RowProps {
  ledger: LedgerAccount;
  onChanged: () => void;
}

function LedgerRow({ ledger, onChanged }: RowProps) {
  const [code, setCode] = useState(ledger.code ?? '');
  const [name, setName] = useState(ledger.name ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const dirty = code.trim() !== (ledger.code ?? '') || name.trim() !== (ledger.name ?? '');

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      onChanged();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <TableRow data-cy="ledger-row" data-category={ledger.category} sx={{ verticalAlign: 'top' }}>
      <TableCell>{ledger.category}</TableCell>
      <TableCell align="right">{ledger.lines}</TableCell>
      <TableCell>
        <Field
          dataCy="ledger-code"
          placeholder="ex.: 3.1.02"
          value={code}
          onChange={setCode}
          slotProps={{ htmlInput: { maxLength: 20, 'aria-label': `Código da conta contábil de ${ledger.category}` } }}
          sx={{ width: 140 }}
        />
      </TableCell>
      <TableCell>
        <Field
          dataCy="ledger-name"
          placeholder="opcional"
          value={name}
          onChange={setName}
          slotProps={{ htmlInput: { maxLength: 100, 'aria-label': `Nome da conta contábil de ${ledger.category}` } }}
          sx={{ minWidth: 180 }}
        />
        {error && (
          <Box component="span" sx={{ display: 'block', fontSize: '0.75rem', color: 'error.main', mt: 0.5 }}>
            {error}
          </Box>
        )}
      </TableCell>
      <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
        <Button
          type="button"
          variant="contained"
          data-cy="ledger-save"
          disabled={busy || !dirty || !code.trim()}
          onClick={() => void run(() => ledgerAccountsApi.set(ledger.category, code.trim(), name.trim()))}
        >
          Salvar
        </Button>
        {ledger.code && (
          <Button
            type="button"
            variant="outlined"
            color="inherit"
            data-cy="ledger-remove"
            disabled={busy}
            onClick={() => void run(() => ledgerAccountsApi.remove(ledger.category))}
            sx={{ ml: 1 }}
          >
            Remover
          </Button>
        )}
      </TableCell>
    </TableRow>
  );
}

/**
 * CTB-5 (ADR-0006, decisão 7): plano de contas. Cada categoria das contas PJ pode ganhar o código
 * da conta no plano do contador, que sai na coluna "Conta contábil" da exportação. Sem código, a
 * coluna sai vazia e a exportação continua.
 */
export function LedgerAccountsSection({ onChanged }: { onChanged: () => void }) {
  const [ledgers, setLedgers] = useState<LedgerAccount[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    try {
      setLedgers(await ledgerAccountsApi.list());
    } catch (err) {
      setError(messageOf(err));
    }
  }

  useEffect(() => {
    void load();
  }, []);

  function changed() {
    void load();
    onChanged();
  }

  return (
    <section data-cy="ledger-accounts">
      <Typography variant="h3" component="h3" sx={{ mb: 0.5 }}>
        Plano de contas
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
        Código da conta no plano do seu contador para cada categoria PJ. Ele sai na coluna “Conta contábil” das
        planilhas; a categoria vale como foi digitada.
      </Typography>
      {error && <Notice severity="error">{error}</Notice>}
      {ledgers === null ? (
        !error && <Typography>Carregando…</Typography>
      ) : ledgers.length === 0 ? (
        <Typography color="text.secondary" data-cy="ledger-empty">
          Nenhuma categoria lançada em conta PJ ainda.
        </Typography>
      ) : (
        <TableBox>
          <Table data-cy="ledger-table" sx={{ minWidth: 640 }}>
            <TableHead>
              <TableRow>
                <TableCell>Categoria</TableCell>
                <TableCell align="right">Lançamentos</TableCell>
                <TableCell>Código</TableCell>
                <TableCell>Nome</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {ledgers.map((l) => (
                <LedgerRow key={`${l.category}|${l.code ?? ''}|${l.name ?? ''}`} ledger={l} onChanged={changed} />
              ))}
            </TableBody>
          </Table>
        </TableBox>
      )}
    </section>
  );
}
