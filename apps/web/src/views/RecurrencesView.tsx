import { useEffect, useMemo, useState } from 'react';
import { accountsApi } from '../api/accounts';
import { ApiError } from '../api/client';
import { recurrencesApi, type DueOccurrence, type Frequency, type Recurrence, type RecurrenceInput } from '../api/recurrences';
import type { Account, CreditCard, PaymentMethod, Transaction } from '../api/types';
import { CategoryDatalist, useCategorySuggestions } from './useCategorySuggestions';

const MONTHS = ['jan', 'fev', 'mar', 'abr', 'mai', 'jun', 'jul', 'ago', 'set', 'out', 'nov', 'dez'];

function today(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** `2026-12-10` → `10/12/2026`. */
function dateLabel(iso: string): string {
  const [y, m, d] = iso.split('-');
  return `${d}/${m}/${y}`;
}

/** `2026-12` → `dez/2026`; `2027` → `2027`. */
function periodLabel(period: string): string {
  const [y, m] = period.split('-');
  return m ? `${MONTHS[Number(m) - 1]}/${y}` : y;
}

/** "todo dia 10" ou "todo ano em 15/03" (mês sem o dia usa o último). */
function scheduleLabel(r: Pick<Recurrence, 'frequency' | 'startsOn'>): string {
  const [, m, d] = r.startsOn.split('-');
  return r.frequency === 'Yearly' ? `todo ano em ${d}/${m}` : `todo dia ${Number(d)}`;
}

function messageOf(err: unknown): string {
  return err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Erro inesperado.';
}

interface LaunchDraft {
  key: string;
  recurrenceId: string;
  period: string;
  amount: string;
  occurredOn: string;
}

/**
 * REC-5 (ADR-0008): regras de recorrência e o que há "a lançar". A regra só entra na previsão; vira
 * lançamento quando o usuário lança a ocorrência (com o valor real) ou some quando a pula.
 */
/**
 * `template` (REC-10): lançamento escolhido em "repetir todo mês". O formulário abre com os dados dele, a
 * agenda ancorada na data dele, e ele vira a ocorrência do próprio período (a próxima é a do mês seguinte).
 */
export function RecurrencesView({
  template = null,
  onTemplateUsed,
}: { template?: Transaction | null; onTemplateUsed?: () => void } = {}) {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [cards, setCards] = useState<CreditCard[]>([]);
  const [paymentMethods, setPaymentMethods] = useState<PaymentMethod[]>([]);
  const [rules, setRules] = useState<Recurrence[]>([]);
  const [due, setDue] = useState<DueOccurrence[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const [editing, setEditing] = useState<Recurrence | null>(null);
  const [conflict, setConflict] = useState(false);
  const [accountId, setAccountId] = useState('');
  const [type, setType] = useState<'Expense' | 'Income'>('Expense');
  const [paymentMethod, setPaymentMethod] = useState('');
  const [creditCardId, setCreditCardId] = useState('');
  const [description, setDescription] = useState('');
  const [category, setCategory] = useState('');
  const [amount, setAmount] = useState('');
  const [frequency, setFrequency] = useState<Frequency>('Monthly');
  const [startsOn, setStartsOn] = useState(today());
  const [endsOn, setEndsOn] = useState('');
  const [notes, setNotes] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const [launch, setLaunch] = useState<LaunchDraft | null>(null);
  const [fromTransaction, setFromTransaction] = useState<Transaction | null>(null);
  const { categories, remember } = useCategorySuggestions();

  const requiresCard = useMemo(
    () => paymentMethods.find((m) => m.code === paymentMethod)?.requiresCard ?? false,
    [paymentMethods, paymentMethod],
  );

  async function loadRules() {
    const [list, pending] = await Promise.all([recurrencesApi.list(), recurrencesApi.due()]);
    setRules(list);
    setDue(pending);
  }

  async function loadAll() {
    setLoading(true);
    setError(null);
    try {
      const [accs, crds, methods] = await Promise.all([
        accountsApi.list(),
        accountsApi.listCards(),
        accountsApi.paymentMethods(),
      ]);
      setAccounts(accs);
      setCards(crds);
      setPaymentMethods(methods);
      setAccountId((current) => current || accs.find((a) => a.isActive)?.id || accs[0]?.id || '');
      setPaymentMethod((current) => current || methods[0]?.code || '');
      await loadRules();
    } catch (err) {
      setError(messageOf(err));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadAll();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // REC-10: preenche o formulário com o lançamento de origem (uma vez, depois de carregar as contas).
  useEffect(() => {
    if (!template || loading) return;
    resetForm();
    setFromTransaction(template);
    setAccountId(template.accountId);
    setType(template.type);
    setPaymentMethod(template.paymentMethod);
    setCreditCardId(template.creditCardId ?? '');
    setDescription(template.description);
    setCategory(template.category);
    setAmount(String(template.amount));
    setFrequency('Monthly');
    setStartsOn(template.occurredOn);
    setNotes(template.notes ?? '');
    onTemplateUsed?.();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [template, loading]);

  function resetForm() {
    setEditing(null);
    setFromTransaction(null);
    setConflict(false);
    setType('Expense');
    setCreditCardId('');
    setDescription('');
    setCategory('');
    setAmount('');
    setFrequency('Monthly');
    setStartsOn(today());
    setEndsOn('');
    setNotes('');
  }

  function startEdit(r: Recurrence) {
    setEditing(r);
    setAccountId(r.accountId);
    setType(r.type);
    setPaymentMethod(r.paymentMethod);
    setCreditCardId(r.creditCardId ?? '');
    setDescription(r.description);
    setCategory(r.category);
    setAmount(String(r.amount));
    setFrequency(r.frequency);
    setStartsOn(r.startsOn);
    setEndsOn(r.endsOn ?? '');
    setNotes(r.notes ?? '');
    setError(null);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    setNotice(null);
    setConflict(false);
    const input: RecurrenceInput = {
      accountId,
      type,
      paymentMethod,
      creditCardId: requiresCard && creditCardId ? creditCardId : undefined,
      description,
      category,
      amount: Number(amount),
      frequency,
      startsOn,
      endsOn: endsOn || undefined,
      notes: notes.trim() || undefined,
      fromTransactionId: !editing && fromTransaction ? fromTransaction.id : undefined,
    };
    try {
      if (editing) {
        await recurrencesApi.update(editing.id, { ...input, version: editing.version });
      } else {
        await recurrencesApi.create(input);
      }
      remember(category);
      resetForm();
      await loadRules();
    } catch (err) {
      if (editing && err instanceof ApiError && err.code === 'recurrence.concurrency_conflict') {
        const current = (await recurrencesApi.list()).find((r) => r.id === editing.id);
        if (current) startEdit(current);
        setConflict(true);
      } else {
        setError(messageOf(err));
      }
    } finally {
      setSubmitting(false);
    }
  }

  async function handleRemove(r: Recurrence) {
    if (!window.confirm(`Excluir a recorrência "${r.description}"? Os lançamentos já feitos a partir dela continuam.`)) return;
    try {
      await recurrencesApi.remove(r.id);
      if (editing?.id === r.id) resetForm();
      await loadRules();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  function openLaunch(recurrenceId: string, period: string, estimated: number, scheduledOn: string) {
    setLaunch({ key: `${recurrenceId}:${period}`, recurrenceId, period, amount: String(estimated), occurredOn: scheduledOn });
    setError(null);
  }

  async function handleLaunch(e: React.FormEvent) {
    e.preventDefault();
    if (!launch) return;
    setError(null);
    try {
      const tx = await recurrencesApi.launch(launch.recurrenceId, launch.period, {
        amount: Number(launch.amount),
        occurredOn: launch.occurredOn,
      });
      setNotice(`Lançado: ${tx.description} em ${dateLabel(tx.occurredOn)}.`);
      setLaunch(null);
      await loadRules();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  async function handleSkip(recurrenceId: string, period: string, label: string) {
    setError(null);
    try {
      await recurrencesApi.skip(recurrenceId, period);
      setNotice(`Pulado: ${label} de ${periodLabel(period)}.`);
      await loadRules();
    } catch (err) {
      setError(messageOf(err));
    }
  }

  const accountName = (id: string) => accounts.find((a) => a.id === id)?.name ?? '—';
  const accountCards = cards.filter((c) => c.accountId === accountId && (c.isActive || c.id === editing?.creditCardId));
  const money = (value: number, currency = 'BRL') => value.toLocaleString('pt-BR', { style: 'currency', currency });
  const signed = (t: 'Expense' | 'Income', value: number, currency?: string) =>
    `${t === 'Income' ? '+' : '−'}${money(value, currency)}`;

  const launchForm = (key: string) =>
    launch?.key === key && (
      <form className="row-form inline" onSubmit={handleLaunch} data-cy="rec-launch-form">
        <input
          data-cy="rec-launch-amount"
          aria-label="Valor real"
          type="number"
          step="0.01"
          value={launch.amount}
          onChange={(e) => setLaunch({ ...launch, amount: e.target.value })}
          required
        />
        <input
          data-cy="rec-launch-date"
          aria-label="Data"
          type="date"
          value={launch.occurredOn}
          onChange={(e) => setLaunch({ ...launch, occurredOn: e.target.value })}
          required
        />
        <button data-cy="rec-launch-save" className="primary small" type="submit">
          Lançar
        </button>
        <button className="ghost small" type="button" onClick={() => setLaunch(null)}>
          Cancelar
        </button>
      </form>
    );

  if (loading) {
    return (
      <section>
        <h2>Recorrências</h2>
        <p>Carregando…</p>
      </section>
    );
  }

  return (
    <section data-cy="recurrences">
      <h2>Recorrências</h2>
      <p className="notice">
        Salário, aluguel e assinaturas cadastrados aqui entram na Previsão até você lançar ou pular cada ocorrência. Eles
        não aparecem no Resumo, nos orçamentos nem na exportação antes de serem lançados.
      </p>

      {error && <p className="error">{error}</p>}
      {notice && (
        <p className="notice" data-cy="rec-notice">
          {notice}
        </p>
      )}

      <h3>A lançar</h3>
      {due.length === 0 ? (
        <p className="muted" data-cy="rec-due-empty">
          Nada pendente dos últimos 60 dias.
        </p>
      ) : (
        <table className="data" data-cy="rec-due">
          <thead>
            <tr>
              <th>Data</th>
              <th>Descrição</th>
              <th>Conta</th>
              <th className="right">Valor estimado</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {due.map((d) => {
              const key = `${d.recurrenceId}:${d.period}`;
              return (
                <tr key={key} data-cy="rec-due-row" data-period={d.period}>
                  <td>{dateLabel(d.scheduledOn)}</td>
                  <td>
                    {d.description}
                    <span className="sub">{d.category}</span>
                  </td>
                  <td>{accountName(d.accountId)}</td>
                  <td className={`right ${d.type === 'Income' ? 'positive' : 'negative'}`}>{signed(d.type, d.amount, d.currency)}</td>
                  <td className="row-actions">
                    {launchForm(key) || (
                      <>
                        <button
                          className="link"
                          data-cy="rec-due-launch"
                          onClick={() => openLaunch(d.recurrenceId, d.period, d.amount, d.scheduledOn)}
                        >
                          lançar
                        </button>
                        <button
                          className="link"
                          data-cy="rec-due-skip"
                          onClick={() => void handleSkip(d.recurrenceId, d.period, d.description)}
                        >
                          pular
                        </button>
                      </>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}

      <h3>{editing ? `Editando: ${editing.description}` : 'Nova recorrência'}</h3>
      {accounts.length === 0 ? (
        <p className="muted">Crie uma conta antes de cadastrar recorrências.</p>
      ) : (
        <>
          {fromTransaction && !editing && (
            <p className="notice" data-cy="rec-from-transaction">
              Repetindo <strong>{fromTransaction.description}</strong>: o lançamento de {dateLabel(fromTransaction.occurredOn)}{' '}
              conta como a ocorrência daquele mês, e a próxima fica para o mês seguinte.{' '}
              <button className="link" type="button" onClick={resetForm}>
                desistir
              </button>
            </p>
          )}
          {conflict && (
            <p className="notice warning" data-cy="rec-conflict">
              Esta recorrência mudou enquanto você editava. O formulário mostra agora o que está gravado; refaça a
              alteração e salve de novo.
            </p>
          )}
          <form className="row-form wrap" onSubmit={handleSubmit} data-cy="rec-form">
            <select
              data-cy="rec-account"
              value={accountId}
              disabled={editing !== null}
              title={editing ? 'A conta de uma recorrência não muda: exclua e crie de novo na outra conta.' : undefined}
              onChange={(e) => setAccountId(e.target.value)}
            >
              {accounts
                .filter((a) => a.isActive || a.id === accountId)
                .map((a) => (
                  <option key={a.id} value={a.id}>
                    {a.name}
                  </option>
                ))}
            </select>
            <select data-cy="rec-type" aria-label="Tipo" value={type} onChange={(e) => setType(e.target.value as 'Expense' | 'Income')}>
              <option value="Expense">Gasto</option>
              <option value="Income">Receita</option>
            </select>
            <select data-cy="rec-payment-method" value={paymentMethod} onChange={(e) => setPaymentMethod(e.target.value)}>
              {paymentMethods.map((m) => (
                <option key={m.code} value={m.code}>
                  {m.name}
                </option>
              ))}
            </select>
            {requiresCard && (
              <select data-cy="rec-card" value={creditCardId} onChange={(e) => setCreditCardId(e.target.value)} required>
                <option value="">Selecione o cartão…</option>
                {accountCards.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.label} •••• {c.lastFourDigits}
                  </option>
                ))}
              </select>
            )}
            <input data-cy="rec-description" placeholder="Descrição" value={description} onChange={(e) => setDescription(e.target.value)} required />
            <input
              data-cy="rec-category"
              placeholder="Categoria"
              list="rec-category-suggestions"
              autoComplete="off"
              value={category}
              onChange={(e) => setCategory(e.target.value)}
              required
            />
            <CategoryDatalist id="rec-category-suggestions" categories={categories} />
            <input
              data-cy="rec-amount"
              placeholder="Valor estimado"
              type="number"
              step="0.01"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              required
            />
            <select data-cy="rec-frequency" aria-label="Frequência" value={frequency} onChange={(e) => setFrequency(e.target.value as Frequency)}>
              <option value="Monthly">Todo mês</option>
              <option value="Yearly">Todo ano</option>
            </select>
            <label className="inline-label">
              a partir de
              <input
                data-cy="rec-starts-on"
                type="date"
                title="O dia (e, na anual, o mês) desta data define quando a recorrência se repete"
                value={startsOn}
                onChange={(e) => setStartsOn(e.target.value)}
                required
              />
            </label>
            <label className="inline-label">
              até (opcional)
              <input data-cy="rec-ends-on" type="date" min={startsOn} value={endsOn} onChange={(e) => setEndsOn(e.target.value)} />
            </label>
            <input data-cy="rec-notes" placeholder="Observações (opcional)" maxLength={500} value={notes} onChange={(e) => setNotes(e.target.value)} />
            <button data-cy="rec-submit" className="primary" type="submit" disabled={submitting}>
              {submitting ? 'Salvando…' : editing ? 'Salvar alteração' : 'Cadastrar'}
            </button>
            {editing && (
              <button data-cy="rec-cancel" className="ghost" type="button" onClick={resetForm}>
                Cancelar
              </button>
            )}
          </form>
        </>
      )}

      <h3>Cadastradas</h3>
      {rules.length === 0 ? (
        <p className="muted">Nenhuma recorrência.</p>
      ) : (
        <table className="data" data-cy="rec-table">
          <thead>
            <tr>
              <th>Descrição</th>
              <th>Quando</th>
              <th>Conta</th>
              <th className="right">Valor estimado</th>
              <th>Próxima</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {rules.map((r) => {
              const next = r.nextOccurrence;
              const key = next ? `${r.id}:${next.period}` : r.id;
              return (
                <tr key={r.id} data-cy="rec-row" data-type={r.type}>
                  <td>
                    {r.description}
                    <span className="sub">{r.category}</span>
                  </td>
                  <td>
                    {scheduleLabel(r)}
                    {r.endsOn && <span className="sub">até {dateLabel(r.endsOn)}</span>}
                  </td>
                  <td>{accountName(r.accountId)}</td>
                  <td className={`right ${r.type === 'Income' ? 'positive' : 'negative'}`} data-cy="rec-value">
                    {signed(r.type, r.amount, r.currency)}
                  </td>
                  <td data-cy="rec-next">{next ? dateLabel(next.scheduledOn) : <span className="muted">encerrada</span>}</td>
                  <td className="row-actions">
                    {(next && launchForm(key)) || (
                      <>
                        {next && (
                          <>
                            <button
                              className="link"
                              data-cy="rec-launch-next"
                              title={`Lançar a ocorrência de ${periodLabel(next.period)}`}
                              onClick={() => openLaunch(r.id, next.period, r.amount, next.scheduledOn)}
                            >
                              lançar {periodLabel(next.period)}
                            </button>
                            <button
                              className="link"
                              data-cy="rec-skip-next"
                              onClick={() => void handleSkip(r.id, next.period, r.description)}
                            >
                              pular
                            </button>
                          </>
                        )}
                        <button className="link" data-cy="rec-edit" onClick={() => startEdit(r)}>
                          editar
                        </button>
                        <button className="link-danger" data-cy="rec-remove" onClick={() => void handleRemove(r)}>
                          excluir
                        </button>
                      </>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </section>
  );
}
