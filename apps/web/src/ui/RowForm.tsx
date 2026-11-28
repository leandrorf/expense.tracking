import Box from '@mui/material/Box';
import type { FormEvent, ReactNode } from 'react';

/** Formulário em linha: os campos quebram de linha no celular. */
export function RowForm({ onSubmit, children, dataCy }: { onSubmit: () => void; children: ReactNode; dataCy?: string }) {
  return (
    <Box
      component="form"
      data-cy={dataCy}
      onSubmit={(e: FormEvent) => {
        e.preventDefault();
        onSubmit();
      }}
      sx={{
        display: 'flex',
        flexWrap: 'wrap',
        gap: 1,
        alignItems: 'flex-start',
        mb: 2,
        '& > .MuiTextField-root': { flex: '1 1 140px' },
      }}
    >
      {children}
    </Box>
  );
}
