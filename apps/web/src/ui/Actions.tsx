import Button, { type ButtonProps } from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import TableContainer from '@mui/material/TableContainer';
import type { ReactNode } from 'react';

/**
 * MUI-6: ação discreta dentro de uma linha ("editar", "alterar"); `danger` para as que desfazem ou
 * desativam ("remover", "desativar"). Substitui os botões `.link` e `.link-danger`.
 */
export function TextAction({ danger, sx, ...rest }: ButtonProps & { danger?: boolean }) {
  return (
    <Button
      variant="text"
      color={danger ? 'error' : 'primary'}
      {...rest}
      sx={[{ minWidth: 0, px: 0.75, py: 0.25 }, ...(Array.isArray(sx) ? sx : [sx])]}
    />
  );
}

/** Tabela numa superfície que rola na horizontal dentro do próprio contêiner (celular). */
export function TableBox({ children }: { children: ReactNode }) {
  return (
    <TableContainer component={Paper} variant="outlined" sx={{ mb: 2 }}>
      {children}
    </TableContainer>
  );
}
