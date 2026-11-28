import Chip from '@mui/material/Chip';
import type { ReactNode } from 'react';

/** Tons das situações que já existem: dentro/no caminho, atenção, fora/atrasado, em aberto. */
export type StatusTone = 'ok' | 'attention' | 'outside' | 'info' | 'neutral';

const COLOR = { ok: 'success', attention: 'warning', outside: 'error', info: 'info', neutral: 'default' } as const;

export function StatusChip({
  tone,
  label,
  dataCy,
  data,
}: {
  tone: StatusTone;
  label: ReactNode;
  dataCy?: string;
  /** Atributos `data-*` extras que os testes leem (ex.: `{ 'data-status': 'Open' }`). */
  data?: Record<`data-${string}`, string>;
}) {
  return <Chip size="small" variant="outlined" color={COLOR[tone]} label={label} data-cy={dataCy} {...data} sx={{ fontWeight: 600 }} />;
}
