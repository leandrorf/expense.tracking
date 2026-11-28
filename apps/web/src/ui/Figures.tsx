import Box from '@mui/material/Box';
import type { ReactNode } from 'react';

export type Figure = { label: ReactNode; value: ReactNode; dataCy?: string };

/** Lista de números (`dl`): rótulo e valor, com algarismos de largura fixa. */
export function Figures({ items, dataCy }: { items: Figure[]; dataCy?: string }) {
  return (
    <Box
      component="dl"
      data-cy={dataCy}
      sx={{ display: 'grid', gridTemplateColumns: 'auto 1fr', columnGap: 2, rowGap: 0.5, m: 0, fontSize: '0.875rem' }}
    >
      {items.map((f, i) => (
        <Box key={i} sx={{ display: 'contents' }}>
          <Box component="dt" sx={{ color: 'text.secondary' }}>
            {f.label}
          </Box>
          <Box component="dd" data-cy={f.dataCy} sx={{ m: 0, fontVariantNumeric: 'tabular-nums' }}>
            {f.value}
          </Box>
        </Box>
      ))}
    </Box>
  );
}
