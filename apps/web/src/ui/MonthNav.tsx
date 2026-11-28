import ChevronLeft from '@mui/icons-material/ChevronLeft';
import ChevronRight from '@mui/icons-material/ChevronRight';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import type { ReactNode } from 'react';

/** Navegação ‹ mês ›. `dataCy` é o prefixo de `-prev`, `-next` e `-label`, como nas telas de hoje. */
export function MonthNav({
  label,
  onPrev,
  onNext,
  dataCy,
}: {
  label: ReactNode;
  onPrev: () => void;
  onNext: () => void;
  dataCy?: string;
}) {
  return (
    <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1.5 }}>
      <IconButton aria-label="Mês anterior" data-cy={dataCy && `${dataCy}-prev`} onClick={onPrev}>
        <ChevronLeft />
      </IconButton>
      <Typography variant="h3" component="h3" aria-live="polite" data-cy={dataCy && `${dataCy}-label`}>
        {label}
      </Typography>
      <IconButton aria-label="Próximo mês" data-cy={dataCy && `${dataCy}-next`} onClick={onNext}>
        <ChevronRight />
      </IconButton>
    </Stack>
  );
}
