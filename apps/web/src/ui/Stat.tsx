import Grid from '@mui/material/Grid';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import type { ReactNode } from 'react';

/**
 * Cartão de um número (Resumo, Faturas, Conciliação): rótulo pequeno e valor grande. `tone` pinta o valor
 * (gasto em vermelho, receita em verde). O valor leva a classe `stat-value`, que alguns testes leem.
 */
export function Stat({
  label,
  value,
  tone,
  small,
  dataCy,
  valueDataCy,
  size = { xs: 12, sm: 6, md: 3 },
}: {
  label: ReactNode;
  value: ReactNode;
  tone?: 'expense' | 'income';
  /** Valor em fonte menor (datas). */
  small?: boolean;
  /** No cartão. */
  dataCy?: string;
  /** No valor. */
  valueDataCy?: string;
  size?: { xs?: number; sm?: number; md?: number };
}) {
  return (
    <Grid size={size}>
      <Paper variant="outlined" data-cy={dataCy} sx={{ p: 2, height: '100%' }}>
        <Typography variant="overline" color="text.secondary" sx={{ display: 'block', lineHeight: 1.6 }}>
          {label}
        </Typography>
        <Typography
          className="stat-value"
          data-cy={valueDataCy}
          sx={{
            fontSize: small ? '1rem' : '1.35rem',
            fontWeight: 700,
            fontVariantNumeric: 'tabular-nums',
            color: tone === 'expense' ? 'error.main' : tone === 'income' ? 'success.main' : 'text.primary',
          }}
        >
          {value}
        </Typography>
      </Paper>
    </Grid>
  );
}

/** Linha de cartões `Stat`, que se empilham no celular. */
export function StatRow({ children, dataCy }: { children: ReactNode; dataCy?: string }) {
  return (
    <Grid container spacing={1.5} data-cy={dataCy} sx={{ mb: 2 }}>
      {children}
    </Grid>
  );
}
