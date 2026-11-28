# ADR-0014: Material UI na SPA, com campos nativos, componentes do projeto e migração tela a tela (épico MUI)

- **Status:** Aceito
- **Data:** 2026-11-28
- **Decisores:** dono do produto/repositório; análise preparada pelo time de desenvolvimento
- **Relacionados:** épico MUI (história MUI-1), `docs/backlog.md`, épico WEB (SPA React), ADR-0007 (edição
  de lançamentos e receita na SPA)

## Contexto

A SPA (`apps/web`) cresceu para 18 telas com um CSS próprio de cerca de 1.100 linhas, escrito tela a
tela. Cada épico novo reinventa botões, campos, selos, tabelas e avisos: os espaçamentos, as alturas e o
foco variam, a acessibilidade não tem padrão, o celular é difícil de usar e cada tela nova custa CSS. O
dono do produto pediu para adotar o Material UI.

### Achados no código

- **Sem biblioteca de UI.** React 18.3 + Vite 5 + TypeScript; só `react` e `react-dom` como dependências.
  As telas somam cerca de 6.400 linhas em `src/views/`. O tema é escuro, com as cores fixas no `styles.css`.
- **Os testes dependem de elementos nativos.** São 427 `data-cy` em 29 specs Cypress, com 175 `.select()`,
  324 `.type()` e 8 `.check()` (o refinamento contou 9; a contagem na MUI-1 deu 8).
  - O `Select` padrão do MUI desenha uma lista própria, não um `<select>`.
  - O `TextField` repassa atributos desconhecidos à `div` de fora, não ao `<input>`.
  - O `Checkbox` deixa o `<input>` com opacidade zero, e o Cypress recusa marcar elemento invisível.
- **Há 19 campos de data e de mês nativos**, preenchidos nos testes com `.type('2026-11-28')`.
- **As telas repetem as mesmas peças:**
  - campos: valor em reais, escopo PF/PJ;
  - navegação e estado: navegação de mês, selo de situação;
  - conteúdo: lista de números (`dl`), aviso, formulário em linha e tabela.
- **O sino** (`RemindersBell`) é um painel posicionado à mão.
- **Bundle de hoje:** 84 KB gzip de JS e 3,3 KB de CSS.
- **Licenças:** o projeto evita dependências pagas para uso comercial (por isso ficaram de fora
  FluentAssertions e MediatR).

## Opções consideradas

### 1. Biblioteca

- **(a) Material UI (`@mui/material`)**, MIT. É o pedido do dono do produto, com componentes acessíveis e
  tema central.
- **(b) Continuar com CSS próprio e organizar um design system à mão:** custa mais e não resolve a
  acessibilidade.
- **(c) Outra biblioteca** (Chakra, Mantine, shadcn): também atenderia, mas não foi o pedido.

### 2. Selects

- **(a) `TextField select` em modo nativo:** continua sendo um `<select>`, então os testes não mudam e o
  celular abre o seletor do sistema.
- **(b) `Select` do MUI:** exige um comando `cy.selectMui` e a reescrita das 175 chamadas.

### 3. Datas e meses

- **(a) `TextField` com `type="date"`/`type="month"`:** nativo, sem dependência nova.
- **(b) Date Pickers do MUI X:** acrescenta um pacote e um adaptador de datas, e muda como os testes
  preenchem os campos. As versões Pro/Premium são pagas.

### 4. Checkbox

- **(a) `Checkbox` do MUI com `.check({ force: true })`** nas chamadas existentes.
- **(b) Clicar no rótulo** nos testes.
- **(c) Manter o `<input type="checkbox">` nativo:** fica fora do tema.

### 5. Migração

- **(a) Gradual, uma história por grupo de telas**, com os dois visuais convivendo e o Cypress verde a
  cada história.
- **(b) Tudo de uma vez:** um PR enorme, difícil de revisar, com a suíte quebrada até o fim.

## Decisão

Ficam as recomendações do refinamento, com os detalhes abaixo.

1. **Pacotes.**
   - Entram `@mui/material` v7, `@emotion/react`, `@emotion/styled` e `@mui/icons-material`, todos MIT, com
     as versões fixadas no `package.json` (como as demais).
   - **O MUI X não entra**: o Data Grid e os Date Pickers Pro/Premium são pagos, e as versões grátis trazem
     peso e um adaptador de datas que os campos nativos dispensam.
   - As tabelas usam a `Table` do `@mui/material`.
   - Os ícones são importados um a um (`@mui/icons-material/Notifications`), nunca pelo índice do pacote,
     para o bundle levar só o que a tela usa.
2. **Selects são nativos.** O `SelectField` do projeto é um `TextField` com `select` e
   `slotProps.select.native = true`, com `<option>` dentro. Os 175 `.select()` não mudam.
3. **Datas e meses são nativos.** `DateField` e `MonthField` são `TextField` com `type="date"` e
   `type="month"` e o rótulo sempre encolhido (`slotProps.inputLabel.shrink`), porque o navegador desenha o
   formato dentro do campo. Os testes continuam com `.type('2026-11-28')` e `.type('2026-11')`.
4. **O contrato do `data-cy` não muda.** Os componentes do projeto recebem `dataCy` e o põem no elemento
   que o teste usa:
   - no `<input>` ou no `<select>`, via `slotProps.htmlInput`;
   - no próprio botão;
   - na linha da tabela (`TableRow`).

   Contêineres (cartão, seção, lista) recebem o `data-cy` na raiz. Uma história que precisar trocar um
   `data-cy` explica a troca no PR.
5. **Checkbox do MUI**, com o `data-cy` no `<input>` (`slotProps.input`) e `.check({ force: true })` /
   `.uncheck({ force: true })` nos testes que marcam. A troca é feita na história de cada tela. O clique de
   verdade cai no ícone do MUI, que cobre o `<input>`; o `force` só pula a checagem de visibilidade.
6. **Tema** (`src/theme.ts`):
   - modo escuro, com a paleta tirada das cores de hoje: `success` para receita e "dentro", `error` para gasto
     e "fora", `warning` para atenção;
   - fundo e superfícies iguais aos do `styles.css`, para as telas ainda não migradas não destoarem;
   - fonte do sistema (`system-ui, -apple-system, "Segoe UI", Roboto, sans-serif`), sem Roboto nem Google
     Fonts no bundle ou na rede;
   - densidade compacta: `size="small"` como padrão de `TextField`, `Button`, `Table` e `IconButton`, nas
     `defaultProps` do tema;
   - valores com algarismos de largura fixa (`font-variant-numeric: tabular-nums`) nas tabelas e nos números.

   Alternar entre claro e escuro fica para a MUI-12 (Could).
7. **Componentes do projeto em `src/ui/`**, só o que se repete:
   - campos: `Field`, `MoneyField`, `DateField`, `MonthField`, `SelectField`;
   - navegação e estado: `ScopeSelect`, `MonthNav`, `StatusChip`;
   - layout: `Figures`, `Notice`, `Section`, `RowForm`.

   Regras de uso:
   - `MoneyField` mantém a digitação de hoje (texto com vírgula, convertido pela função atual), sem máscara
     nova;
   - `StatusChip` mapeia as situações que já existem (dentro/atenção/fora, no caminho/atrasado/alcançado) às
     cores do tema;
   - as telas usam esses componentes e, do MUI direto, só layout (`Stack`, `Grid`, `Box`, `Paper`) e peças
     sem repetição (`Dialog`, `Popover`, `Tabs`);
   - nenhuma tela ganha CSS novo: o estilo vem do tema ou do `sx`.
8. **Migração gradual.**
   - A MUI-2 instala os pacotes, cria o tema, envolve o app no `ThemeProvider` com `CssBaseline` e migra a
     barra do topo e o login.
   - Cada história seguinte migra um grupo de telas e apaga do `styles.css` as regras que só aquelas telas
     usavam. A MUI-11 apaga o que sobrar e o arquivo.
   - Enquanto as telas convivem, o `CssBaseline` usa o mesmo fundo e a mesma cor de texto do `styles.css`, e
     as regras globais do `styles.css` (`body`, `button`, `input`) continuam até a última tela migrar. Se uma
     regra global atrapalhar um componente do MUI, ela é restrita às telas antigas (por exemplo, prefixada
     por uma classe do contêiner) na história que encontrar o conflito.
   - A ordem segue o backlog: MUI-1 → MUI-2 → MUI-3 → MUI-6 → MUI-7 → MUI-4 → MUI-5 → MUI-8 → MUI-9 → MUI-10.
   - Uma história só fecha com a suíte Cypress inteira verde e o bundle medido (meta: no máximo +150 KB gzip
     sobre os 84 KB de hoje). Se a meta estourar, a história registra a medida e o motivo antes de seguir.
9. **Escopo: só aparência e componentes.**
   - Fluxos, textos, regras e chamadas à API continuam iguais.
   - Sem roteador novo, sem i18n e sem tela nova.
   - Melhorias de fluxo que aparecerem viram histórias de outros épicos.

## Consequências

**Positivas**
- As telas ficam consistentes, com foco visível e semântica acessível (rótulos ligados aos campos, `aria`
  dos componentes do MUI), e o layout responde ao celular.
- Telas novas saem mais rápido, montadas com `src/ui/` e sem CSS.
- Os testes E2E continuam valendo durante toda a migração: selects e datas nativos e o `data-cy` no mesmo
  elemento de hoje.

**Negativas / riscos**
- **Bundle maior:** o MUI e o Emotion somam algumas dezenas de KB gzip. Mitigação: ícones importados um a
  um e a meta medida em cada história.
- **Dois visuais convivendo** até a MUI-10, com risco de conflito entre o CSS global e os componentes do MUI.
  Mitigação: tema com as mesmas cores e regras globais restritas quando houver conflito.
- **Selects e datas nativos** não têm o visual do MUI ao abrir a lista ou o calendário, porque o navegador
  desenha. Fica aceito em troca dos testes estáveis e do melhor uso no celular.
- **`force: true` nos checkboxes** dispensa a checagem de visibilidade do Cypress nesses 8 testes.
- **Dependência nova a manter:** atualizações de versão maior do MUI (v7 → v8) viram histórias próprias.

**O que muda nas histórias**
- A MUI-2 cria também `src/ui/` com os componentes da decisão 7. Os testes desses componentes são os
  próprios E2E das telas que os usam; não entra outro framework de teste.
- As histórias de tela trocam `.check()` por `.check({ force: true })` só nos specs das telas que migram.
