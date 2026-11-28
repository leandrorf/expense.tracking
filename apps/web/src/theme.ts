import { createTheme } from '@mui/material/styles';

/**
 * MUI-2 (ADR-0014, decisão 6): tema escuro com as cores do `styles.css`, fonte do sistema e densidade
 * compacta. Fundo e texto iguais aos de hoje, para as telas ainda não migradas não destoarem.
 */
export const theme = createTheme({
  palette: {
    mode: 'dark',
    primary: { main: '#6366f1', dark: '#4f46e5' },
    success: { main: '#4ade80' },
    error: { main: '#f87171' },
    warning: { main: '#fbbf24' },
    background: { default: '#0f172a', paper: '#1e293b' },
    text: { primary: '#e2e8f0', secondary: '#94a3b8' },
    divider: '#334155',
  },
  shape: { borderRadius: 8 },
  typography: {
    fontFamily: "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif",
    button: { textTransform: 'none', fontWeight: 600 },
    h1: { fontSize: '1.4rem', fontWeight: 700 },
    h2: { fontSize: '1.15rem', fontWeight: 700 },
    h3: { fontSize: '1rem', fontWeight: 700 },
  },
  components: {
    MuiTextField: { defaultProps: { size: 'small' } },
    MuiButton: { defaultProps: { size: 'small', disableElevation: true } },
    MuiIconButton: { defaultProps: { size: 'small' } },
    MuiTable: { defaultProps: { size: 'small' } },
    MuiPaper: { styleOverrides: { root: { backgroundImage: 'none' } } },
    MuiTableCell: { styleOverrides: { root: { fontVariantNumeric: 'tabular-nums' } } },
    MuiTab: { styleOverrides: { root: { minHeight: 40, textTransform: 'none', fontWeight: 600 } } },
    MuiTabs: { styleOverrides: { root: { minHeight: 40 } } },
  },
});
