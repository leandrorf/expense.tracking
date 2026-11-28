import { SelectField, type FieldProps } from './Field';

/** Escopo PF/PJ. Os valores variam por API (`Personal`/`personal`), então vêm de quem usa. */
export function ScopeSelect({
  personal = 'Personal',
  business = 'Business',
  label = 'Escopo',
  ...rest
}: FieldProps & { personal?: string; business?: string }) {
  return (
    <SelectField
      label={label}
      {...rest}
      options={[
        { value: personal, label: 'Pessoal (PF)' },
        { value: business, label: 'Empresarial (PJ)' },
      ]}
    />
  );
}
