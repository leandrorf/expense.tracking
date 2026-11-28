import Alert, { type AlertProps } from '@mui/material/Alert';

/** Aviso ("notice"): informação por padrão, `warning` quando pede ação. */
export function Notice({ dataCy, severity = 'info', sx, ...rest }: AlertProps & { dataCy?: string }) {
  return <Alert variant="outlined" severity={severity} data-cy={dataCy} {...rest} sx={[{ mb: 2 }, ...(Array.isArray(sx) ? sx : [sx])]} />;
}
