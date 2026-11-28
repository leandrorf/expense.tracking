import { useCallback, useEffect, useRef, useState } from 'react';
import Notifications from '@mui/icons-material/Notifications';
import Badge from '@mui/material/Badge';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import ButtonBase from '@mui/material/ButtonBase';
import Checkbox from '@mui/material/Checkbox';
import ClickAwayListener from '@mui/material/ClickAwayListener';
import FormControlLabel from '@mui/material/FormControlLabel';
import IconButton from '@mui/material/IconButton';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { ApiError } from '../api/client';
import { remindersApi, type Reminder, type ReminderEmail, type ReminderSettings, type Reminders } from '../api/reminders';
import { Field, Notice, SelectField, TextAction } from '../ui';

/** `2026-11-23` → `23/11`. */
function shortDate(iso: string): string {
  const [, m, d] = iso.split('-');
  return `${d}/${m}`;
}

function detailOf(r: Reminder, money: (v: number) => string): string {
  switch (r.kind) {
    case 'InvoiceOverdue':
      return `venceu em ${shortDate(r.dueOn)} · falta ${money(r.amount)}`;
    case 'InvoiceDue':
      return `${r.urgency === 'Today' ? 'vence hoje' : `vence em ${shortDate(r.dueOn)}`} · falta ${money(r.amount)}`;
    case 'RecurrenceDue':
      return `${r.urgency === 'Today' ? 'vence hoje' : `vence em ${shortDate(r.dueOn)}`} · ${money(r.amount)}`;
    case 'RecurrencesToLaunch':
      return `pendentes desde ${shortDate(r.dueOn)}: lance ou pule`;
    case 'RecurrenceAdjustment': {
      // RJT-9: "R$ 2.000,00 → R$ 2.084,00 (IGP-M 4,2%)", ou aguardando o índice.
      const index = r.index === 'IGPM' ? 'IGP-M' : r.index;
      if (r.rate === null) return `aguardando o ${index ?? 'índice'}: confirme na recorrência`;
      const rate = `${r.rate.toLocaleString('pt-BR', { maximumFractionDigits: 4 })}%`;
      return `${money(r.previousAmount ?? r.amount)} → ${money(r.amount)} (${index ? `${index} ` : ''}${rate})`;
    }
    case 'Budget':
      return r.urgency === 'Overdue'
        ? `estourou: ${money(r.amount)} de ${money(r.limit)}`
        : `${Math.round((r.amount / r.limit) * 100)}% de ${money(r.limit)} gastos`;
    case 'MonthlyGoal':
      // MET-9: meta do mês fora (no teto, `amount` é o que saiu; nas outras, o resultado).
      return `fora da meta: alvo de ${money(r.limit)}, mês em ${money(r.amount)}`;
    case 'SavingsObjective':
      return `atrasado: precisa de ${money(r.amount)} por mês, ritmo de ${money(r.limit)}`;
    default:
      return shortDate(r.dueOn);
  }
}

interface Props {
  /** Muda a cada troca de aba: os avisos são relidos (algo pode ter sido resolvido). */
  refreshKey: unknown;
  onOpen: (reminder: Reminder) => void;
}

/**
 * NTF-3 (ADR-0009, decisões 1 e 7): sino com o número de avisos (vencidos em destaque) e o painel, do
 * mais urgente para o menos. Relê ao montar, ao trocar de aba, ao abrir o painel e quando a janela volta
 * ao foco. Falha ao ler os avisos não atrapalha o app: o sino só fica sem número.
 */
export function RemindersBell({ refreshKey, onOpen }: Props) {
  const [reminders, setReminders] = useState<Reminders | null>(null);
  const [open, setOpen] = useState(false);
  // NTF-6: preferências editadas dentro do painel.
  const [settings, setSettings] = useState<ReminderSettings | null>(null);
  const [settingsError, setSettingsError] = useState<string | null>(null);
  // O campo guarda o texto digitado: apagar para redigitar nao vira "0" no meio do caminho.
  const [leadDaysText, setLeadDaysText] = useState('');
  // NTF-8: resumo por e-mail (o endereço gravado, para saber se mudou).
  const [email, setEmail] = useState<ReminderEmail | null>(null);
  const [emailText, setEmailText] = useState('');
  const [emailNotice, setEmailNotice] = useState<string | null>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const bellRef = useRef<HTMLButtonElement>(null);

  const load = useCallback(() => {
    remindersApi
      .list()
      .then(setReminders)
      .catch(() => undefined);
  }, []);

  useEffect(() => {
    load();
  }, [load, refreshKey]);

  useEffect(() => {
    const onFocus = () => load();
    window.addEventListener('focus', onFocus);
    return () => window.removeEventListener('focus', onFocus);
  }, [load]);

  // MUI-10: ao abrir, o foco vai para o painel (Tab navega pelos avisos; Esc fecha e volta ao sino).
  useEffect(() => {
    if (!open) return;
    const id = requestAnimationFrame(() => panelRef.current?.focus());
    return () => cancelAnimationFrame(id);
  }, [open]);

  /** NTF-5: some na hora (otimista) e relê; se falhar, a releitura o traz de volta. */
  async function dismiss(key: string) {
    setReminders((current) =>
      current ? { ...current, items: current.items.filter((i) => i.key !== key) } : current,
    );
    try {
      await remindersApi.dismiss(key);
    } finally {
      load();
    }
  }

  async function toggleSettings() {
    setSettingsError(null);
    if (settings) {
      setSettings(null);
      return;
    }
    try {
      const { email: currentEmail, ...current } = await remindersApi.settings();
      setSettings(current);
      setLeadDaysText(String(current.leadDays));
      setEmail(currentEmail);
      setEmailText(currentEmail.email ?? '');
      setEmailNotice(null);
    } catch (err) {
      setSettingsError(err instanceof Error ? err.message : 'Erro inesperado.');
    }
  }

  async function saveSettings(e: React.FormEvent) {
    e.preventDefault();
    if (!settings) return;
    setSettingsError(null);
    try {
      await remindersApi.saveSettings({ ...settings, leadDays: Number(leadDaysText) });
      if (email) {
        const typed = emailText.trim() || null;
        const saved = await remindersApi.saveEmail({ email: typed, enabled: email.enabled, sendHour: email.sendHour });
        if (typed && !saved.emailConfirmed && typed.toLowerCase() !== (email.email ?? '')) {
          // Fica aberto para mostrar o aviso: o link foi para o endereço novo.
          setEmail(saved);
          setEmailNotice(`Enviamos um link de confirmação para ${typed}. O resumo só sai depois de confirmar.`);
          load();
          return;
        }
      }
      setSettings(null);
      load();
    } catch (err) {
      setSettingsError(err instanceof ApiError || err instanceof Error ? err.message : 'Erro inesperado.');
    }
  }

  const items = reminders?.items ?? [];
  const money = (value: number) => value.toLocaleString('pt-BR', { style: 'currency', currency: reminders?.currency ?? 'BRL' });
  const hasOverdue = !!reminders && reminders.overdue > 0;

  function close(returnFocus = true) {
    setOpen(false);
    if (returnFocus) bellRef.current?.focus();
  }

  return (
    <Box sx={{ position: 'relative', display: 'inline-flex' }}>
      <IconButton
        ref={bellRef}
        className={`bell${hasOverdue ? ' has-overdue' : ''}`}
        data-cy="reminders-bell"
        aria-label={items.length === 0 ? 'Avisos: nenhum' : `Avisos: ${items.length}`}
        aria-expanded={open}
        aria-haspopup="dialog"
        onClick={() => {
          if (!open) load();
          setOpen(!open);
        }}
        sx={{ border: 1, borderColor: 'divider', borderRadius: 2 }}
      >
        <Badge
          badgeContent={items.length}
          color={hasOverdue ? 'error' : 'info'}
          max={99}
          slotProps={{ badge: (items.length > 0 ? { 'data-cy': 'reminders-count' } : {}) as object }}
        >
          <Notifications fontSize="small" />
        </Badge>
      </IconButton>
      {/*
        MUI-10: painel não modal logo abaixo do sino, em vez de Popover. O Popover cobre a página com um fundo
        invisível, e o sino e as abas deixariam de ser clicáveis com o painel aberto (os testes fecham o
        painel clicando de novo no sino); o Popper traria +12 KB gzip só para posicionar. O painel recebe o
        foco ao abrir e fecha com Esc ou clique fora.
      */}
      {open && (
        <ClickAwayListener
          onClickAway={(e) => {
            if (bellRef.current?.contains(e.target as Node)) return;
            close(false);
          }}
        >
          <Paper
            ref={panelRef}
            elevation={8}
            data-cy="reminders-panel"
            role="dialog"
            aria-label="Avisos"
            tabIndex={-1}
            onKeyDown={(e) => {
              if (e.key === 'Escape') {
                e.stopPropagation();
                close();
              }
            }}
            sx={{
              // No celular o sino fica longe da borda direita: o painel vai para o canto da janela.
              position: { xs: 'fixed', sm: 'absolute' },
              top: { xs: 52, sm: '100%' },
              right: { xs: 8, sm: 0 },
              zIndex: 'modal',
              mt: 1,
              p: 1.5,
              width: 'min(22rem, 90vw)',
              maxHeight: '70vh',
              overflowY: 'auto',
              outline: 'none',
              color: 'text.primary',
              whiteSpace: 'normal',
            }}
          >
            <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
              <Typography component="strong" sx={{ fontWeight: 700 }}>
                Avisos
              </Typography>
              <TextAction data-cy="reminders-settings-toggle" onClick={() => void toggleSettings()}>
                {settings ? 'fechar preferências' : 'preferências'}
              </TextAction>
            </Stack>
            {settingsError && <Notice severity="error">{settingsError}</Notice>}
            {settings && (
              <Box
                component="form"
                data-cy="reminders-settings"
                onSubmit={saveSettings}
                sx={{ display: 'flex', flexDirection: 'column', gap: 0.5, mb: 1.5, pb: 1.5, borderBottom: 1, borderColor: 'divider' }}
              >
                <Field
                  dataCy="reminders-lead-days"
                  label="Avisar com quantos dias de antecedência"
                  type="number"
                  value={leadDaysText}
                  onChange={setLeadDaysText}
                  required
                  slotProps={{ htmlInput: { min: 0, max: 15 } }}
                  sx={{ mt: 1, mb: 0.5 }}
                />
                <KindCheck
                  dataCy="reminders-kind-invoices"
                  label="faturas"
                  checked={settings.invoices}
                  onChange={(checked) => setSettings({ ...settings, invoices: checked })}
                />
                <KindCheck
                  dataCy="reminders-kind-recurrences"
                  label="recorrências"
                  checked={settings.recurrences}
                  onChange={(checked) => setSettings({ ...settings, recurrences: checked })}
                />
                <KindCheck
                  dataCy="reminders-kind-budgets"
                  label="orçamentos e metas"
                  checked={settings.budgets}
                  onChange={(checked) => setSettings({ ...settings, budgets: checked })}
                />
                <Box
                  component="fieldset"
                  data-cy="reminders-email-section"
                  sx={{ border: 1, borderColor: 'divider', borderRadius: 1, p: 1.5, m: 0, mt: 1, display: 'flex', flexDirection: 'column', gap: 1 }}
                >
                  <Box component="legend" sx={{ px: 0.5, fontSize: '0.8rem', color: 'text.secondary' }}>
                    Resumo diário por e-mail
                  </Box>
                  <Field
                    dataCy="reminders-email"
                    label="E-mail para o resumo"
                    type="email"
                    placeholder="seu@email.com"
                    value={emailText}
                    onChange={setEmailText}
                  />
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <KindCheck
                      dataCy="reminders-email-enabled"
                      label="receber às"
                      checked={email?.enabled ?? false}
                      onChange={(checked) => email && setEmail({ ...email, enabled: checked })}
                    />
                    <SelectField
                      dataCy="reminders-email-hour"
                      label="Hora do resumo"
                      value={String(email?.sendHour ?? 8)}
                      onChange={(v) => email && setEmail({ ...email, sendHour: Number(v) })}
                      options={Array.from({ length: 24 }, (_, h) => ({ value: String(h), label: `${String(h).padStart(2, '0')}h` }))}
                      sx={{ width: 110 }}
                    />
                  </Stack>
                  {email?.email && (
                    <Box
                      component="span"
                      data-cy="reminders-email-status"
                      data-confirmed={email.emailConfirmed}
                      sx={{ fontSize: '0.75rem', color: email.emailConfirmed ? 'success.main' : 'text.secondary' }}
                    >
                      {email.emailConfirmed ? (
                        'e-mail confirmado'
                      ) : (
                        <>
                          aguardando confirmação: abra o link enviado para {email.email}{' '}
                          <TextAction
                            type="button"
                            data-cy="reminders-email-resend"
                            onClick={() =>
                              void remindersApi
                                .resendEmail()
                                .then(() => setEmailNotice(`Link reenviado para ${email.email}.`))
                                .catch((err: unknown) => setSettingsError(err instanceof Error ? err.message : 'Erro inesperado.'))
                            }
                          >
                            reenviar link
                          </TextAction>
                        </>
                      )}
                    </Box>
                  )}
                  {emailNotice && (
                    <Box component="span" data-cy="reminders-email-notice" sx={{ fontSize: '0.75rem', color: 'info.main' }}>
                      {emailNotice}
                    </Box>
                  )}
                </Box>
                <Button type="submit" variant="contained" data-cy="reminders-settings-save" sx={{ alignSelf: 'flex-start', mt: 1 }}>
                  Salvar
                </Button>
              </Box>
            )}
            {!reminders ? (
              <Typography variant="body2" color="text.secondary" data-cy="reminders-loading">
                Carregando…
              </Typography>
            ) : items.length === 0 ? (
              <Typography variant="body2" color="text.secondary" data-cy="reminders-empty">
                Nada vencendo nos próximos {reminders?.leadDays ?? 3} dias.
              </Typography>
            ) : (
              <Box component="ul" sx={{ listStyle: 'none', p: 0, m: 0, display: 'grid', gap: 0.75 }}>
                {items.map((r) => (
                  <Box component="li" key={r.key} data-cy="reminder-row" sx={{ display: 'flex', alignItems: 'stretch', gap: 0.5 }}>
                    <ButtonBase
                      className={`reminder ${r.urgency.toLowerCase()}`}
                      data-cy="reminder-item"
                      data-kind={r.kind}
                      data-urgency={r.urgency}
                      onClick={() => {
                        close(false);
                        onOpen(r);
                      }}
                      sx={{
                        flex: 1,
                        display: 'block',
                        textAlign: 'left',
                        p: 1,
                        borderRadius: 1,
                        bgcolor: 'background.default',
                        borderLeft: 3,
                        borderLeftColor: r.urgency === 'Overdue' ? 'error.main' : r.urgency === 'Today' ? 'warning.main' : 'info.main',
                        '&:hover, &.Mui-focusVisible': { bgcolor: 'action.hover' },
                      }}
                    >
                      <Box component="span" sx={{ display: 'block', fontWeight: 600, fontSize: '0.875rem' }}>
                        {r.title}
                      </Box>
                      <Box component="span" sx={{ display: 'block', fontSize: '0.75rem', color: 'text.secondary' }}>
                        {detailOf(r, money)}
                      </Box>
                    </ButtonBase>
                    <TextAction
                      type="button"
                      data-cy="reminder-dismiss"
                      title={r.kind === 'InvoiceDue' ? 'Dispensar (volta se a fatura vencer sem pagamento)' : 'Dispensar este aviso'}
                      aria-label={`Dispensar: ${r.title}`}
                      onClick={() => void dismiss(r.key)}
                    >
                      dispensar
                    </TextAction>
                  </Box>
                ))}
              </Box>
            )}
          </Paper>
        </ClickAwayListener>
      )}
    </Box>
  );
}

/** Tipo de aviso (ou o e-mail) ligado/desligado: Checkbox do MUI com o `data-cy` no `<input>`. */
function KindCheck({
  dataCy,
  label,
  checked,
  onChange,
}: {
  dataCy: string;
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <FormControlLabel
      label={label}
      sx={{ m: 0, '& .MuiFormControlLabel-label': { fontSize: '0.875rem' } }}
      control={
        <Checkbox
          size="small"
          checked={checked}
          onChange={(e) => onChange(e.target.checked)}
          slotProps={{ input: { 'data-cy': dataCy } as object }}
        />
      }
    />
  );
}
