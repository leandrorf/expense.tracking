import { useEffect, useState } from 'react';
import Download from '@mui/icons-material/Download';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { accountingExportApi, type AccountingExportPreview } from '../api/accountingExport';
import { accountsApi } from '../api/accounts';
import { ApiError } from '../api/client';
import type { Account } from '../api/types';
import { LedgerAccountsSection } from './LedgerAccountsSection';
import { MonthField, Notice, SelectField, TableBox } from '../ui';

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}

/** Mês anterior ao de hoje, `yyyy-MM`: é o que se manda ao contador. */
export function previousMonth(today = new Date()): string {
  const d = new Date(today.getFullYear(), today.getMonth() - 1, 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}

/**
 * CTB-4 (ADR-0006): exportação para o contador. Mostra quantos lançamentos e pagamentos de fatura
 * entram e os totais (os mesmos do resumo.csv) antes de baixar o ZIP do mês. Só contas PJ.
 */
export function AccountingExportView() {
  const [accounts, setAccounts] = useState<Account[] | null>(null);
  const [month, setMonth] = useState(previousMonth());
  const [accountId, setAccountId] = useState('');
  const [preview, setPreview] = useState<AccountingExportPreview | null>(null);
  const [loading, setLoading] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Muda quando o plano de contas muda (CTB-5) ou depois de baixar (CTB-6), para recarregar a prévia.
  const [refreshKey, setRefreshKey] = useState(0);

  const business = (accounts ?? []).filter((a) => a.scope === 'Business');

  useEffect(() => {
    accountsApi.list().then(setAccounts, (err) => setError(messageOf(err)));
  }, []);

  useEffect(() => {
    if (business.length === 0 || !month) return;
    let current = true;
    setLoading(true);
    setError(null);
    accountingExportApi
      .preview(month, accountId || undefined)
      .then(
        (p) => current && setPreview(p),
        (err) => {
          if (!current) return;
          setPreview(null);
          setError(messageOf(err));
        },
      )
      .finally(() => current && setLoading(false));
    return () => {
      current = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [accounts, month, accountId, refreshKey]);

  async function download() {
    if (!preview) return;
    setDownloading(true);
    setError(null);
    try {
      const blob = await accountingExportApi.download(month, accountId || undefined);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = preview.fileName;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
      setRefreshKey((k) => k + 1);
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setDownloading(false);
    }
  }

  const money = (value: number, currency: string) => value.toLocaleString('pt-BR', { style: 'currency', currency });
  const when = (utc: string) =>
    new Date(utc).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });

  const heading = (
    <Typography variant="h2" component="h2" sx={{ mb: 2 }}>
      Exportar para o contador
    </Typography>
  );

  if (accounts === null) {
    return (
      <section data-cy="accounting-export">
        {heading}
        {error ? <Notice severity="error">{error}</Notice> : <Typography>Carregando…</Typography>}
      </section>
    );
  }

  return (
    <section data-cy="accounting-export">
      {heading}

      {business.length === 0 ? (
        <Notice dataCy="accounting-no-business">
          A exportação contábil é só para contas PJ. Cadastre uma conta empresarial (com CNPJ) em Contas para usá-la.
        </Notice>
      ) : (
        <>
          <Notice>
            Um ZIP com três planilhas que abrem no Excel: lançamentos, pagamentos de fatura e resumo por categoria. Só
            entram contas PJ; os lançamentos contam pela data, e a compra parcelada entra inteira.
          </Notice>

          <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center', mb: 2 }}>
            <MonthField id="accounting-month" dataCy="accounting-month" label="Mês" value={month} onChange={setMonth} sx={{ width: 190 }} />
            {business.length > 1 && (
              <SelectField
                id="accounting-account"
                dataCy="accounting-account"
                label="Conta"
                value={accountId}
                onChange={setAccountId}
                sx={{ minWidth: 220 }}
              >
                <option value="">Todas as contas PJ</option>
                {business.map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.name}
                    {a.isActive ? '' : ' (inativa)'}
                  </option>
                ))}
              </SelectField>
            )}
          </Stack>

          {error && <Notice severity="error">{error}</Notice>}
          {loading && !preview && <Typography>Carregando…</Typography>}

          {preview && (
            <Box data-cy="accounting-preview" sx={{ mb: 3 }}>
              <Typography sx={{ mb: 1.5 }}>
                <span data-cy="accounting-lines">{preview.lines}</span> lançamento(s) e{' '}
                <span data-cy="accounting-payments">{preview.payments}</span> pagamento(s) de fatura em{' '}
                {preview.accounts} conta(s) PJ.
              </Typography>
              {preview.lastExport &&
                (preview.lastExport.changed ? (
                  <Notice severity="warning" dataCy="accounting-changed">
                    Mudou desde a exportação de {when(preview.lastExport.exportedAtUtc)}: eram{' '}
                    {preview.lastExport.lines} lançamento(s) e {preview.lastExport.payments} pagamento(s)
                    {preview.lastExport.totals.map((t) => `, saídas de ${money(t.expense, t.currency)}`).join('')}. Baixe
                    de novo e reenvie ao contador.
                  </Notice>
                ) : (
                  <Typography variant="body2" color="text.secondary" data-cy="accounting-unchanged" sx={{ mb: 1.5 }}>
                    Exportado em {when(preview.lastExport.exportedAtUtc)}, sem mudanças desde então.
                  </Typography>
                ))}
              {preview.unmappedLines > 0 && (
                <Notice dataCy="accounting-unmapped">
                  {preview.unmappedLines} lançamento(s) sem conta contábil: a coluna sai vazia. Associe as categorias no
                  plano de contas abaixo, se o contador pedir.
                </Notice>
              )}
              {preview.totals.length > 0 && (
                <TableBox>
                  <Table data-cy="accounting-totals" sx={{ minWidth: 480 }}>
                    <TableHead>
                      <TableRow>
                        <TableCell>Moeda</TableCell>
                        <TableCell align="right">Entradas</TableCell>
                        <TableCell align="right">Saídas</TableCell>
                        <TableCell align="right">Saldo</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {preview.totals.map((t) => (
                        <TableRow key={t.currency} data-cy="accounting-total" data-currency={t.currency}>
                          <TableCell>{t.currency}</TableCell>
                          <TableCell align="right" data-cy="accounting-income">
                            {money(t.income, t.currency)}
                          </TableCell>
                          <TableCell align="right" data-cy="accounting-expense">
                            {money(t.expense, t.currency)}
                          </TableCell>
                          <TableCell
                            align="right"
                            sx={{ fontWeight: 600, color: t.balance < 0 ? 'error.main' : t.balance > 0 ? 'success.main' : undefined }}
                          >
                            {money(t.balance, t.currency)}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </TableBox>
              )}
              <Button
                variant="contained"
                data-cy="accounting-download"
                disabled={downloading || loading}
                onClick={() => void download()}
                startIcon={<Download />}
              >
                {downloading ? 'Gerando…' : `Baixar ${preview.fileName}`}
              </Button>
            </Box>
          )}

          <LedgerAccountsSection onChanged={() => setRefreshKey((k) => k + 1)} />
        </>
      )}
    </section>
  );
}
