import TextField, { type TextFieldProps } from '@mui/material/TextField';
import type { ReactNode } from 'react';

/**
 * MUI-2 (ADR-0014, decisões 2 a 4): campos do projeto. O `dataCy` vai no próprio `<input>`/`<select>`,
 * onde os testes digitam e escolhem; selects, datas e meses continuam nativos.
 */
export type FieldProps = Omit<TextFieldProps, 'onChange' | 'value'> & {
  value: string;
  onChange: (value: string) => void;
  dataCy?: string;
};

export function Field({ dataCy, onChange, slotProps, ...rest }: FieldProps) {
  return (
    <TextField
      {...rest}
      onChange={(e) => onChange(e.target.value)}
      slotProps={{
        ...slotProps,
        htmlInput: { ...(slotProps?.htmlInput as object), 'data-cy': dataCy },
      }}
    />
  );
}

/** Valor em reais como texto (aceita vírgula); a conversão continua com a tela. */
export function MoneyField({ slotProps, ...rest }: FieldProps) {
  return (
    <Field
      placeholder="0,00"
      {...rest}
      slotProps={{ ...slotProps, htmlInput: { inputMode: 'decimal', ...(slotProps?.htmlInput as object) } }}
    />
  );
}

/** O navegador desenha o formato dentro do campo: o rótulo fica sempre encolhido. */
function NativeDateLike({ type, slotProps, ...rest }: FieldProps & { type: 'date' | 'month' }) {
  return (
    <Field {...rest} type={type} slotProps={{ ...slotProps, inputLabel: { shrink: true, ...(slotProps?.inputLabel as object) } }} />
  );
}

export function DateField(props: FieldProps) {
  return <NativeDateLike {...props} type="date" />;
}

export function MonthField(props: FieldProps) {
  return <NativeDateLike {...props} type="month" />;
}

export type SelectOption = { value: string; label: ReactNode; disabled?: boolean };

/** Select nativo do MUI: continua um `<select>` (os testes usam `.select()`). */
export function SelectField({ options, slotProps, children, ...rest }: FieldProps & { options?: SelectOption[] }) {
  return (
    <Field
      {...rest}
      select
      slotProps={{
        ...slotProps,
        select: { native: true, ...(slotProps?.select as object) },
        inputLabel: { shrink: true, ...(slotProps?.inputLabel as object) },
      }}
    >
      {options?.map((o) => (
        <option key={o.value} value={o.value} disabled={o.disabled}>
          {o.label}
        </option>
      ))}
      {children}
    </Field>
  );
}
