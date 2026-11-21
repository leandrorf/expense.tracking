import { useState } from 'react';
import type { CsvColumnMapping, CsvMappingProposal } from '../api/imports';

type AmountMode = 'single' | 'split';

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
export function mappingProblems(mapping: CsvColumnMapping, mode: AmountMode): string[] {
  const used =
    mode === 'single'
      ? [mapping.dateColumn, mapping.descriptionColumn, mapping.amountColumn]
      : [mapping.dateColumn, mapping.descriptionColumn, mapping.debitColumn, mapping.creditColumn];
  const problems: string[] = [];
  if (used.some((c) => c === null || c < 0)) problems.push('Escolha uma coluna para cada campo.');
  else if (new Set(used).size !== used.length) problems.push('Cada campo precisa de uma coluna diferente.');
  return problems;
}

/**
 * IMP-8: antes da prévia de um CSV, mostra as primeiras linhas do arquivo e deixa o usuário
 * confirmar (ou escolher) qual coluna é data, descrição e valor (ou débito e crédito), se a
 * primeira linha é cabeçalho, o separador decimal e se o sinal do valor deve ser invertido.
 * Parte da proposta do servidor (IMP-3); sem proposta, parte das três primeiras colunas.
 */
export function CsvMappingStep({
  proposal,
  isCard,
  busy,
  onSubmit,
  onCancel,
}: {
  proposal: CsvMappingProposal;
  isCard: boolean;
  busy: boolean;
  onSubmit: (mapping: CsvColumnMapping) => void;
  onCancel: () => void;
}) {
  const [mapping, setMapping] = useState<CsvColumnMapping>(() =>
    proposal.mapping
      ? // Fatura em CSV costuma trazer a compra positiva: começa com o sinal invertido.
        { ...proposal.mapping, invertSign: isCard }
      : fallbackMapping(proposal, isCard),
  );
  const [mode, setMode] = useState<AmountMode>(proposal.mapping && proposal.mapping.amountColumn === null ? 'split' : 'single');

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
  const problems = mappingProblems(mapping, mode);
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
    <label className="inline-label">
      {label}
      <select data-cy={cy} value={value ?? ''} onChange={(e) => onChange(Number(e.target.value))}>
        <option value="" disabled>
          Escolha…
        </option>
        {columns.map((c) => (
          <option key={c} value={c}>
            {columnName(c)}
          </option>
        ))}
      </select>
    </label>
  );

  return (
    <div data-cy="csv-mapping">
      <p className="muted">
        {proposal.mapping
          ? 'Confira as colunas que reconhecemos no arquivo antes de ver a prévia.'
          : 'Não reconhecemos as colunas deste arquivo: indique qual é cada uma.'}
      </p>

      <div className="row-form wrap csv-mapping-fields">
        {columnSelect('Data', 'csv-map-date', mapping.dateColumn, (c) => set({ dateColumn: c }))}
        {columnSelect('Descrição', 'csv-map-description', mapping.descriptionColumn, (c) => set({ descriptionColumn: c }))}

        <fieldset className="csv-amount-mode">
          <legend>Valor</legend>
          <label>
            <input type="radio" name="amount-mode" data-cy="csv-map-mode-single" checked={mode === 'single'} onChange={() => changeMode('single')} />
            uma coluna (negativo = gasto)
          </label>
          <label>
            <input type="radio" name="amount-mode" data-cy="csv-map-mode-split" checked={mode === 'split'} onChange={() => changeMode('split')} />
            débito e crédito separados
          </label>
        </fieldset>

        {mode === 'single'
          ? columnSelect('Coluna do valor', 'csv-map-amount', mapping.amountColumn, (c) => set({ amountColumn: c }))
          : (
            <>
              {columnSelect('Débito', 'csv-map-debit', mapping.debitColumn, (c) => set({ debitColumn: c }))}
              {columnSelect('Crédito', 'csv-map-credit', mapping.creditColumn, (c) => set({ creditColumn: c }))}
            </>
          )}

        <label className="inline-label">
          Decimal
          <select data-cy="csv-map-decimal" value={mapping.decimalSeparator} onChange={(e) => set({ decimalSeparator: e.target.value })}>
            <option value=",">vírgula (1.234,56)</option>
            <option value=".">ponto (1,234.56)</option>
          </select>
        </label>
        <label className="checkbox-label">
          <input type="checkbox" data-cy="csv-map-header" checked={mapping.hasHeader} onChange={(e) => set({ hasHeader: e.target.checked })} />
          primeira linha é cabeçalho
        </label>
        {mode === 'single' && (
          <label className="checkbox-label">
            <input type="checkbox" data-cy="csv-map-invert" checked={mapping.invertSign} onChange={(e) => set({ invertSign: e.target.checked })} />
            inverter sinal (o arquivo traz gasto positivo)
          </label>
        )}
      </div>

      <table className="data csv-sample" data-cy="csv-sample">
        <caption className="muted">Primeiras linhas do arquivo</caption>
        <thead>
          <tr>
            <th>Linha</th>
            {columns.map((c) => (
              <th key={c} className={roleOf(c) ? 'mapped' : ''}>
                {columnName(c)}
                {roleOf(c) && <span className="csv-role" data-cy="csv-role">{roleOf(c)}</span>}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {sampleRows.map((row) => (
            <tr key={row.lineNumber}>
              <td>{row.lineNumber}</td>
              {columns.map((c) => (
                <td key={c} className={roleOf(c) ? 'mapped' : ''}>
                  {row.cells[c] ?? ''}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>

      {problems.length > 0 && (
        <p className="error" data-cy="csv-map-problem" role="alert">
          {problems.join(' ')}
        </p>
      )}
      <div className="actions">
        <button
          data-cy="csv-map-submit"
          className="primary"
          disabled={problems.length > 0 || busy}
          onClick={() =>
            onSubmit(
              mode === 'single'
                ? { ...mapping, debitColumn: null, creditColumn: null }
                : { ...mapping, amountColumn: null, invertSign: false },
            )
          }
        >
          {busy ? 'Lendo…' : 'Ver prévia'}
        </button>
        <button data-cy="csv-map-cancel" className="ghost" onClick={onCancel} disabled={busy}>
          Cancelar
        </button>
      </div>
    </div>
  );
}
