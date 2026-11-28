import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import type { ReactNode } from 'react';

/** Bloco de uma tela: título, ações à direita e conteúdo numa superfície. */
export function Section({
  title,
  actions,
  children,
  dataCy,
}: {
  title?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  dataCy?: string;
}) {
  return (
    <Paper component="section" variant="outlined" data-cy={dataCy} sx={{ p: 2, mb: 2 }}>
      {(title || actions) && (
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1.5, flexWrap: 'wrap' }}>
          {title && (
            <Typography variant="h2" component="h2">
              {title}
            </Typography>
          )}
          {actions}
        </Stack>
      )}
      {children}
    </Paper>
  );
}
