import { useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormLabel from '@mui/material/FormLabel';
import Radio from '@mui/material/Radio';
import RadioGroup from '@mui/material/RadioGroup';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import type { CsvColumnMapping, CsvMappingProposal, CsvMappingToSave, SavedCsvMapping } from '../api/imports';
import { Field, Notice, SelectField, TableBox, TextAction } from '../ui';

type AmountMode = 'single' | 'split';

/** Limites do servidor para o cabeçalho guardado com o mapeamento (IMP-11). */
const MAX_SAVED_HEADER_LENGTH = 100;

const modeOf = (mapping: CsvColumnMapping): AmountMode => (mapping.amountColumn === null ? 'split' : 'single');

const sameHeaders = (a: string[], b: string[] | null) =>
  !!b && a.length > 0 && a.length === b.length && a.every((h, i) => h.trim().toLowerCase() === b[i].trim().toLowerCase());

/** IMP-11: o mapeamento salvo de um arquivo com o mesmo cabeçalho já vem escolhido. */
export function matchingSaved(saved: SavedCsvMapping[], proposal: CsvMappingProposal): SavedCsvMapping | undefined {
  return saved.find((s) => sameHeaders(s.headers, proposal.headers));
}

/** Ponto de partida quando o servidor não reconheceu as colunas: as três primeiras, na ordem comum. */
function fallbackMapping(proposal: CsvMappingProposal, isCard: boolean): CsvColumnMapping {
  return {
    dateColumn: 0,
    descriptionColumn: Math.min(1, proposal.columnCount - 1),
    amountColumn: Math.min(2, proposal.columnCount - 1),
    debitColumn: null,
    creditColumn: null,
    hasHeader: proposal.headers !== null,
    decimalSeparator: ',',
    invertSign: isCard,
  };
}

/** O que falta ou conflita no mapeamento; vazio quando dá para ver a prévia. */
export function mappingProblems(mapping: CsvColumnMapping, mode: AmountMode, columnCount: number): string[] {
  const used =
    mode === 'single'
      ? [mapping.dateColumn, mapping.descriptionColumn, mapping.amountColumn]
      : [mapping.dateColumn, mapping.descriptionColumn, mapping.debitColumn, mapping.creditColumn];
  const problems: string[] = [];
  if (used.some((c) => c === null || c < 0)) problems.push('Escolha uma coluna para cada campo.');
  else if (used.some((c) => c !== null && c >= columnCount)) problems.push('O mapeamento usa colunas que este arquivo não tem.');
  else if (new Set(used).size !== used.length) problems.push('Cada campo precisa de uma coluna diferente.');
  return problems;
}

/**
 * IMP-8: antes da prévia de um CSV, mostra as primeiras linhas do arquivo e deixa o usuário
 * confirmar (ou escolher) qual coluna é data, descrição e valor (ou débito e crédito), se a
 * primeira linha é cabeçalho, o separador decimal e se o sinal do valor deve ser invertido.
 * Parte da proposta do servidor (IMP-3); sem proposta, parte das três primeiras colunas.
 * IMP-11: dá para partir de um mapeamento salvo (já escolhido quando o cabeçalho é o mesmo) e
 * salvar o mapeamento com um nome para as próximas importações.
 */
export function CsvMappingStep({
  proposal,
  isCard,
  busy,
  saved,
  onSubmit,
  onRemoveSaved,
  onCancel,
}: {
  proposal: CsvMappingProposal;
  isCard: boolean;
  busy: boolean;
  saved: SavedCsvMapping[];
  onSubmit: (mapping: CsvColumnMapping, save?: CsvMappingToSave) => void;
  onRemoveSaved: (id: string) => void;
  onCancel: () => void;
}) {
  const [initialSaved] = useState(() => matchingSaved(saved, proposal));
  const [selectedId, setSelectedId] = useState(initialSaved?.id ?? '');
  const [saveName, setSaveName] = useState('');
  const [mapping, setMapping] = useState<CsvColumnMapping>(() =>
    initialSaved
      ? initialSaved.mapping
      : proposal.mapping
        ? // Fatura em CSV costuma trazer a compra positiva: começa com o sinal invertido.
          { ...proposal.mapping, invertSign: isCard }
        : fallbackMapping(proposal, isCard),
  );
  const [mode, setMode] = useState<AmountMode>(() =>
    initialSaved ? modeOf(initialSaved.mapping) : proposal.mapping && proposal.mapping.amountColumn === null ? 'split' : 'single',
  );
  const selected = saved.find((s) => s.id === selectedId);

  function chooseSaved(id: string) {
    setSelectedId(id);
    const chosen = saved.find((s) => s.id === id);
    if (chosen) {
      setMapping(chosen.mapping);
      setMode(modeOf(chosen.mapping));
    }
  }

  const columns = Array.from({ length: proposal.columnCount }, (_, i) => i);
  const columnName = (i: number) => {
    const header = mapping.hasHeader ? proposal.headers?.[i]?.trim() : '';
    return header ? `${i + 1}. ${header}` : `Coluna ${i + 1}`;
  };
  const roleOf = (i: number) =>
    i === mapping.dateColumn
      ? 'Data'
      : i === mapping.descriptionColumn
        ? 'Descrição'
        : mode === 'single' && i === mapping.amountColumn
          ? 'Valor'
          : mode === 'split' && i === mapping.debitColumn
            ? 'Débito'
            : mode === 'split' && i === mapping.creditColumn
              ? 'Crédito'
              : null;

  // Sem cabeçalho, a linha que o servidor tomou por cabeçalho também é lançamento: entra na amostra.
  const sampleRows = !mapping.hasHeader && proposal.headers ? [{ lineNumber: 1, cells: proposal.headers }, ...proposal.preview] : proposal.preview;
  const problems = mappingProblems(mapping, mode, proposal.columnCount);
  const set = (change: Partial<CsvColumnMapping>) => setMapping((current) => ({ ...current, ...change }));

  function changeMode(next: AmountMode) {
    setMode(next);
    if (next === 'single') {
      set({ amountColumn: mapping.amountColumn ?? mapping.debitColumn ?? 2, debitColumn: null, creditColumn: null });
    } else {
      const free = columns.filter((c) => c !== mapping.dateColumn && c !== mapping.descriptionColumn);
      set({ amountColumn: null, debitColumn: mapping.debitColumn ?? free[0] ?? null, creditColumn: mapping.creditColumn ?? free[1] ?? null });
    }
  }

  const columnSelect = (label: string, cy: string, value: number | null, onChange: (column: number) => void) => (
    <SelectField label={label} dataCy={cy} value={value === null ? '' : String(value)} onChange={(v) => onChange(Number(v))} sx={{ minWidth: 170 }}>
      <option value="" disabled>
        Escolha…
      </option>
      {columns.map((c) => (
        <option key={c} value={c}>
          {columnName(c)}
        </option>
      ))}
    </SelectField>
  );

  // Coluna usada no mapeamento: destaque na amostra.
  const mappedSx = (c: number) => (roleOf(c) ? { bgcolor: 'action.selected' } : undefined);

  return (
    <Box data-cy="csv-mapping">
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        {selected && selected.id === initialSaved?.id
          ? `Usamos o mapeamento salvo "${selected.name}", do arquivo com as mesmas colunas. Confira antes de ver a prévia.`
          : proposal.mapping
            ? 'Confira as colunas que reconhecemos no arquivo antes de ver a prévia.'
            : 'Não reconhecemos as colunas deste arquivo: indique qual é cada uma.'}
      </Typography>

      {saved.length > 0 && (
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center', mb: 2 }}>
          <SelectField label="Mapeamento salvo" dataCy="csv-saved" value={selectedId} onChange={chooseSaved} sx={{ minWidth: 220 }}>
            <option value="">Nenhum</option>
            {saved.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </SelectField>
          {selected && (
            <TextAction
              danger
              data-cy="csv-saved-remove"
              type="button"
              onClick={() => {
                setSelectedId('');
                onRemoveSaved(selected.id);
              }}
            >
              excluir "{selected.name}"
            </TextAction>
          )}
        </Stack>
      )}

      <Stack direction="row" spacing={1.5} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center', mb: 2 }}>
        {columnSelect('Data', 'csv-map-date', mapping.dateColumn, (c) => set({ dateColumn: c }))}
        {columnSelect('Descrição', 'csv-map-description', mapping.descriptionColumn, (c) => set({ descriptionColumn: c }))}

        <FormControl component="fieldset" sx={{ px: 1 }}>
          <FormLabel component="legend" sx={{ fontSize: '0.75rem' }}>
            Valor
          </FormLabel>
          <RadioGroup row name="amount-mode" value={mode} onChange={(e) => changeMode(e.target.value as AmountMode)}>
            <FormControlLabel
              value="single"
              control={<Radio size="small" slotProps={{ input: { 'data-cy': 'csv-map-mode-single' } as object }} />}
              label="uma coluna (negativo = gasto)"
            />
            <FormControlLabel
              value="split"
              control={<Radio size="small" slotProps={{ input: { 'data-cy': 'csv-map-mode-split' } as object }} />}
              label="débito e crédito separados"
            />
          </RadioGroup>
        </FormControl>

        {mode === 'single' ? (
          columnSelect('Coluna do valor', 'csv-map-amount', mapping.amountColumn, (c) => set({ amountColumn: c }))
        ) : (
          <>
            {columnSelect('Débito', 'csv-map-debit', mapping.debitColumn, (c) => set({ debitColumn: c }))}
            {columnSelect('Crédito', 'csv-map-credit', mapping.creditColumn, (c) => set({ creditColumn: c }))}
          </>
        )}

        <SelectField
          label="Decimal"
          dataCy="csv-map-decimal"
          value={mapping.decimalSeparator}
          onChange={(v) => set({ decimalSeparator: v })}
          options={[
            { value: ',', label: 'vírgula (1.234,56)' },
            { value: '.', label: 'ponto (1,234.56)' },
          ]}
          sx={{ minWidth: 170 }}
        />
        <FormControlLabel
          control={
            <Checkbox
              size="small"
              checked={mapping.hasHeader}
              onChange={(e) => set({ hasHeader: e.target.checked })}
              slotProps={{ input: { 'data-cy': 'csv-map-header' } as object }}
            />
          }
          label="primeira linha é cabeçalho"
        />
        {mode === 'single' && (
          <FormControlLabel
            control={
              <Checkbox
                size="small"
                checked={mapping.invertSign}
                onChange={(e) => set({ invertSign: e.target.checked })}
                slotProps={{ input: { 'data-cy': 'csv-map-invert' } as object }}
              />
            }
            label="inverter sinal (o arquivo traz gasto positivo)"
          />
        )}
      </Stack>

      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        Primeiras linhas do arquivo
      </Typography>
      <TableBox>
        <Table data-cy="csv-sample">
          <TableHead>
            <TableRow>
              <TableCell>Linha</TableCell>
              {columns.map((c) => (
                <TableCell key={c} sx={mappedSx(c)}>
                  {columnName(c)}
                  {roleOf(c) && (
                    <Box
                      component="span"
                      data-cy="csv-role"
                      sx={{ display: 'block', fontSize: '0.7rem', fontWeight: 700, color: 'primary.main', textTransform: 'uppercase' }}
                    >
                      {roleOf(c)}
                    </Box>
                  )}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {sampleRows.map((row) => (
              <TableRow key={row.lineNumber}>
                <TableCell>{row.lineNumber}</TableCell>
                {columns.map((c) => (
                  <TableCell key={c} sx={mappedSx(c)}>
                    {row.cells[c] ?? ''}
                  </TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableBox>

      {problems.length > 0 && (
        <Notice severity="error" dataCy="csv-map-problem" role="alert">
          {problems.join(' ')}
        </Notice>
      )}
      <Field
        label="Salvar como (opcional)"
        dataCy="csv-save-name"
        placeholder="ex.: Banco X — fatura"
        value={saveName}
        onChange={setSaveName}
        slotProps={{ htmlInput: { maxLength: 60 } }}
        sx={{ minWidth: 280, mb: 2 }}
      />
      <Stack direction="row" spacing={1}>
        <Button
          variant="contained"
          data-cy="csv-map-submit"
          disabled={problems.length > 0 || busy}
          onClick={() =>
            onSubmit(
              mode === 'single'
                ? { ...mapping, debitColumn: null, creditColumn: null }
                : { ...mapping, amountColumn: null, invertSign: false },
              saveName.trim()
                ? {
                    name: saveName.trim(),
                    // Só o cabeçalho, para reconhecer o mesmo layout depois; nunca as linhas do arquivo.
                    headers: mapping.hasHeader ? (proposal.headers ?? []).map((h) => h.trim().slice(0, MAX_SAVED_HEADER_LENGTH)) : [],
                  }
                : undefined,
            )
          }
        >
          {busy ? 'Lendo…' : 'Ver prévia'}
        </Button>
        <Button variant="outlined" color="inherit" data-cy="csv-map-cancel" onClick={onCancel} disabled={busy}>
          Cancelar
        </Button>
      </Stack>
    </Box>
  );
}
