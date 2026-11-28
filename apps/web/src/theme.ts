import { createTheme } from '@mui/material/styles';

/** Contorno do foco por teclado (MUI-11): visível em qualquer superfície do tema escuro. */
const focusRing = { outline: '2px solid #a5b4fc', outlineOffset: 2 };

/**
 * MUI-2 (ADR-0014, decisão 6): tema escuro com as cores de antes, fonte do sistema e densidade compacta.
 * MUI-11: o índigo de destaque é o `#818cf8` (contraste AA como texto: 6,0:1 no fundo e 4,9:1 nos cartões; o
 * `#6366f1` de antes dava 3,3:1), com texto escuro nos botões cheios (6,0:1).
 */
export const theme = createTheme({
  palette: {
    mode: 'dark',
    primary: { main: '#818cf8', light: '#a5b4fc', dark: '#6366f1', contrastText: '#0f172a' },
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
    // MUI-11: foco por teclado explícito em botões, abas, ícones e itens clicáveis (não só o efeito do clique).
    MuiButtonBase: { styleOverrides: { root: { '&.Mui-focusVisible': focusRing } } },
    MuiCssBaseline: { styleOverrides: { 'summary:focus-visible': focusRing } },
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
