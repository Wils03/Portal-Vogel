# Portal Vogel

Portal web (ASP.NET Core Blazor Server, .NET 10) para monitorar os bancos dos clientes, gerar alertas, enviar mensagens de WhatsApp, precificar produtos e acompanhar a DRE.

Etapas e ideias: [`ROADMAP.md`](../../ROADMAP.md).

## Executar

```powershell
dotnet run --launch-profile http
```

Antes da 1ª vez: criar o banco e o login com [docs/criar-portal-vg.sql](../../docs/criar-portal-vg.sql) e guardar a conexão nos user-secrets:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=PORTAL_VG;User Id=portal_vogel;Password=SUA_SENHA;TrustServerCertificate=True"
```

Abra http://localhost:5250. As tabelas são criadas sozinhas (migrations). O **primeiro usuário** se cadastra em *Cadastrar*; depois disso, usuários são criados na tela Usuários.

Ferramentas de linha de comando:
- `dotnet run -- --consulta <id do cliente> arquivo.sql` — conferência só de leitura no banco de um cliente, com a conexão salva (sem expor a senha).
- `dotnet run -- --copiar-sqlite Data/app.db` — cópia única do banco antigo (SQLite) para o PORTAL_VG, com o destino vazio.

## Validar

```powershell
dotnet run -- --self-test
```

## Módulos

| Menu | O que faz |
|---|---|
| Painel | Alertas em aberto, falhas, clientes com mais pendências, rotinas |
| Clientes | Cadastro, WhatsApp, tipo de banco, string de conexão (criptografada) e parâmetros de precificação |
| Rotinas | SELECT executado em cada cliente; gera alerta quando o resultado passa do limite. Roda no horário configurado ou em *Executar agora* |
| Alertas | Revisar, editar a mensagem, enviar por WhatsApp/Pagebot, marcar como resolvido, exportar Excel |
| Precificação | Markup, preço sugerido, margem de contribuição e ponto de equilíbrio. Importa CSV/XLSX ou SQL |
| DRE | DRE mensal com AV%, gráfico e KPIs. Importa CSV/XLSX (baixe o modelo) ou SQL |
| Mensagens | Histórico de envios |

### Rotinas

O SQL precisa começar com `SELECT` ou `WITH`, ter um único comando e não conter comandos de alteração. Ele roda numa transação que é sempre desfeita. Mesmo assim, use um **usuário de banco somente leitura** para cada cliente.

- Se retornar **1 linha e 1 coluna numérica** (ex.: `COUNT(*)`), esse número é a quantidade.
- Se retornar **linhas**, a quantidade é o número de linhas e até 50 delas ficam salvas como amostra no alerta.

Variáveis da mensagem: `{cliente}` (contato), `{empresa}`, `{quantidade}`, `{rotina}`, `{data}`.

### Alertas e mensagens automáticas

Um problema gera **um** alerta por cliente e rotina:

1. A rotina encontra ocorrências (quantidade > limite) → **alerta novo**. Se a rotina tiver *Enviar automaticamente*, a mensagem vai **uma vez** pelo Pagebot para o WhatsApp do cliente.
2. Nos dias seguintes, se continuar → o mesmo alerta é **atualizado** (sem nova mensagem).
3. Quando zerar → o alerta é **resolvido automaticamente**.
4. Se voltar → alerta novo e nova mensagem.

Proteções do número (seção `Mensagens` do `appsettings.json`; veja e simule na tela **Mensagens**):

| Chave | Padrão | O que faz |
|---|---|---|
| `ModoTeste` / `NumeroTeste` | ligado / vazio | Toda mensagem do Pagebot vai para o número de teste com "[TESTE → cliente]". Sem número, nada sai. |
| `LimiteDiario` | 20 | Máximo de envios pelo Pagebot por dia (todos os clientes). |
| `LimitePorClienteDia` | 1 | Máximo de mensagens automáticas por cliente/dia; vários alertas do cliente vão juntos numa só. |
| `IntervaloSegundos` | 60 | Espera mínima entre dois envios automáticos (um por vez, nunca em rajada). |
| `HorarioInicio` / `HorarioFim` | 8 / 18 | Envio só em horário comercial, seg–sex (`EnviarSabado`/`EnviarDomingo`). Fora dele, fica na fila. |
| `TentativasMaximas` | 3 | Tentativas por alerta se o Pagebot falhar. |

`forceSend` (tela Configurações): **ligado** envia mesmo com atendimento aberto no Pagebot (a mensagem entra na conversa); **desligado** o Pagebot recusa (`chat_03`) e o portal tenta de novo em 2 h, sem gastar tentativa nem contar no limite do dia.

### WhatsApp / Pagebot

API: `POST https://api.pagebot.com.br/core/v2/api/chats/send-text` com o token do **canal** no cabeçalho `access-token` ([documentação](https://api.pagebot.com.br/swagger/index.html)). Já vem configurado no `appsettings.json`; falta só:

Tudo é configurado na tela **Configurações** do portal (menu lateral), sem CMD:

1. Pegar o **token do canal** de WhatsApp na plataforma Pagebot e colar no campo *Token do canal Pagebot* (fica criptografado no banco do portal e nunca é exibido de novo).
2. Escolher *Modo de envio* = **Pagebot**, manter **Modo teste** ligado com o seu número e clicar em **Salvar** (vale na hora, sem reiniciar).
3. Clicar em **Enviar 1 mensagem de teste** e conferir no WhatsApp.

Enquanto ninguém salvar pela tela, vale a seção `Mensagens` do `appsettings.json` (e o token de `dotnet user-secrets` ou da variável `Mensagens__Pagebot__Token`).
O token salvo pela tela depende das chaves em `Data/keys`: sem elas, é preciso informar o token de novo.

Com `Modo = Manual` (padrão), o botão só abre o WhatsApp com o texto pronto e nada é enviado sozinho.
Se o canal do Pagebot for **WhatsApp Cloud (API oficial)**, o WhatsApp só aceita texto livre para quem conversou nas últimas 24h; para iniciar conversa é preciso **template aprovado** (`send-template`), a implementar.

**Para rodar sozinho todo dia**, o portal precisa ficar ligado 24h num servidor (IIS/serviço do Windows ou nuvem) com acesso aos bancos dos clientes.

### Importação de planilhas

- Produtos: `Codigo; Descricao; Custo; Preco; QtdMes`
- DRE: `Competencia (mm/aaaa); Grupo; Conta; Valor` com grupos `ReceitaBruta, Deducoes, Cmv, DespesasOperacionais, ReceitasFinanceiras, DespesasFinanceiras, ImpostosSobreLucro`

CSV aceita `;` ou `,` e números no formato brasileiro (`1.234,56`).

## Perfis e usuários

| Perfil | Vê | Pode |
|---|---|---|
| **Vogel · Administrador** | todos os clientes | tudo: clientes e conexões, SQL, Configurações (Pagebot), usuários |
| **Vogel · Operador** | todos os clientes | alertas, enviar mensagens, executar rotinas, importar/atualizar precificação e DRE (sem SQL, conexões ou configurações) |
| **Cliente · Gestor** | só a própria empresa | painel, alertas (marcar resolvido), precificação e DRE (salvar parâmetros, importar planilha, "Atualizar do banco" com o SQL que a Vogel cadastrou — sem ver o SQL), mensagens recebidas, usuários da empresa, **programa as rotinas** da empresa (liga/desliga, horário, aviso automático — sem ver o SQL) |
| **Cliente · Usuário** | só a própria empresa | vê painel, alertas, precificação e mensagens (sem DRE e rotinas); exporta |

- Usuários são criados na tela **Usuários**. O cadastro livre (`Account/Register`) só existe para o **primeiro** usuário, que vira administrador.
- O filtro por empresa é feito no servidor em cada tela (`Services/Acesso.cs`), não só no menu. O perfil é relido do banco a cada tela.
- "Testar SQL" e "Executar rotina" são só da Vogel. "Atualizar do banco" (DRE e precificação) também é do gestor, sempre com o SQL salvo no cadastro. Na versão online vira sincronização.
- Desativar usuário: não entra mais e cai em até 1 minuto. 5 senhas erradas bloqueiam por 15 minutos.
- Sempre fica pelo menos um administrador ativo.
- `Cliente.Impressao`: SHA-256 dos dígitos do CNPJ (no estilo da REV_Base), para identificar a empresa na sincronização futura.

## Visual

Todas as cores ficam em `wwwroot/css/tema.css` (paleta Vogel). Logo e Vobô em `wwwroot/img`.

## Dados e backup

- Banco do portal: **SQL Server `PORTAL_VG`** (localhost), login próprio `portal_vogel` (só acessa o PORTAL_VG). Cada empresa é uma linha de `Clientes` (Id + `Impressao` = SHA-256 do CNPJ, como a REV_Base); os dados dela ficam ligados pelo `ClienteId`.
- `Data/app.db` (SQLite) é o banco antigo, copiado em 24/09/2026 para o banco do portal (hoje PORTAL_VG); guardado só como histórico. Os testes (`--self-test`) usam SQLite em memória.
- Chaves que criptografam as conexões: `Data/keys`. **Faça backup junto com o banco**; sem elas as conexões salvas não podem ser lidas.
