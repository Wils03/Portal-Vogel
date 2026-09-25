# Portal Vogel — Ideias e etapas do projeto

> Arquivo vivo: anote aqui as ideias, decisões e o que falta em cada etapa.
> Marque `[x]` quando concluir. Data de início: 24/09/2026.

## Visão

Um portal web (C# / ASP.NET Core Blazor) onde a Vogel:

- Conecta nos bancos de dados de todos os clientes (hoje as conexões estão no **Admin**);
- Roda **rotinas diárias** de verificação (ex.: notas com erro / não processadas) no lugar do relatório em Word;
- Gera **alertas** e dispara **mensagens de WhatsApp** (via **Pagebot** / Vobô) para os clientes;
- Ajuda na **precificação correta** dos produtos e no **ponto de equilíbrio**;
- Mostra um **dashboard tipo DRE / Power BI** com dados do banco do cliente ou importados por planilha;
- No futuro, é operado por **outra pessoa** (sem depender do Wilson no operacional).

---

## Etapa 1 — Base do sistema (MVP) ✅ feito nesta primeira versão

- [x] Projeto Blazor Server .NET 10 em `src/Dashborad` com login (ASP.NET Identity)
- [x] Cadastro público bloqueado: só o 1º usuário se cadastra sozinho; os próximos são cadastrados por quem já está logado
- [x] Identidade visual Vogel (paleta, fonte Montserrat, logo, Vobô) — cores centralizadas em `wwwroot/css/tema.css`
- [x] Cadastro de **clientes** com conexão para SQL Server, PostgreSQL, MySQL e Firebird
- [x] String de conexão **criptografada** (Data Protection) + botão **Testar conexão**
- [x] **Rotinas** = SELECT configurável + limite + severidade + horário + modelo de mensagem
- [x] Trava de segurança: só aceita SELECT/WITH e roda dentro de transação desfeita (rollback)
- [x] Agendador interno: roda cada rotina 1x por dia no horário configurado
- [x] **Alertas** com status (Novo → Notificado → Resolvido / Erro), amostra dos registros e exportação Excel
- [x] **Mensagens**: modo manual (abre WhatsApp com texto pronto) e modo Pagebot (API configurável), com histórico
- [x] **Precificação**: markup, preço sugerido, margem de contribuição, margem líquida, ponto de equilíbrio
- [x] **DRE** mensal com análise vertical, KPIs e gráfico; importação CSV/XLSX ou SQL; modelo de planilha e exportação
- [x] Autoteste das regras: `dotnet run -- --self-test`

## Etapa 2 — Conectar com a realidade (próximo passo)

- [x] Banco dos clientes: **SQL Server** (base de teste `BASE`, compatibilidade 100). Tabelas mapeadas: `Nfe_download`, `Nfe_status`, `Documentos Fiscais`, `Entradas`, `Log de whatsapp`
- [ ] Criar um **usuário somente leitura** em cada banco de cliente para o portal (hoje o teste usa `sa`) → script pronto: login `vogel_portal` em [docs/implantacao/02-criar-logins.sql](docs/implantacao/02-criar-logins.sql); falta aplicar no cliente piloto
- [x] SQL das rotinas de notas (não lançadas, rejeitadas, aguardando SEFAZ, não transmitidas, WhatsApp do sistema) — ver `docs/rotinas-sqlserver.sql`
- [ ] Validar com um cliente real (a base de teste só tem dados até 29/05/2025)
- [x] DRE ligada ao banco (vendas, devoluções, CMV pelo custo, contas a pagar por centro de custo) + ponto de equilíbrio calculado pela DRE — ver `docs/dre-sqlserver.md`
- [ ] Conferir as regras da DRE com o contador (itens em "Pontos para confirmar")
- [x] Precificação ligada ao banco: custo atual, preço lista 1, grupo de formação de preço (impostos), lucro desejado (margem/markup), venda 90 dias — ver `docs/precificacao-sqlserver.md`
- [ ] Precificação: considerar arredondamento de preço do ERP e clientes que descontam crédito de ICMS (custo líquido)
- [ ] **Regra**: nada é criado/alterado no banco do cliente; view de apoio só com aprovação prévia
- [ ] Próximas rotinas: manifestação do destinatário pendente, títulos a receber vencidos, produtos com preço abaixo do custo
- [ ] Transformar os itens do **relatório em Word** atual em rotinas (uma rotina por item do relatório)
- [ ] Importar as conexões do **Admin** (ver se dá para ler direto do banco do Admin ou exportar em planilha)
- [ ] Definir se o portal roda **na nuvem** ou em um **servidor da Vogel** com acesso (VPN) aos bancos dos clientes

## Etapa 3 — WhatsApp / Pagebot (Vobô)

- [x] Documentação da API do Pagebot: `POST /core/v2/api/chats/send-text`, cabeçalho `access-token` (https://api.pagebot.com.br/swagger/index.html) — já configurado
- [ ] Colocar o **token do canal** (user-secrets) e trocar `Modo` para `Pagebot`
- [ ] Ver se o canal é WhatsApp Web ou **WhatsApp Cloud** (Cloud exige **template aprovado** para iniciar conversa → `send-template`)
- [x] Envio automático por rotina: 1 mensagem por ocorrência, alerta atualizado enquanto continuar, resolvido sozinho quando zerar, novo envio se voltar; só em horário comercial; até 3 tentativas
- [ ] Publicar o portal num servidor 24h com acesso aos bancos dos clientes (senão o agendador só roda com o PC ligado)
- [ ] Receber respostas do cliente (webhook do Pagebot) e registrar no histórico
- [ ] Horário comercial / limite de mensagens por cliente por dia (para não virar spam)

## Etapa 4 — Operação por outra pessoa

- [x] Perfis de acesso: **Administrador** (conexões, SQL) × **Operador** (alertas, mensagens) — ver Etapa 4b
- [x] Tela de **gestão de usuários** (listar, criar, desativar/reativar, redefinir senha — sem envio de e-mail, "esqueci a senha" depende do administrador ou do gestor da empresa)
- [ ] Checklist diário do operador (alertas do dia, quem foi avisado, quem respondeu)
- [ ] Registro de **anotações/conversas** por cliente
- [ ] Log de auditoria (quem editou conexão, rotina, quem enviou mensagem)
- [x] Cliente em foco no topo da tela (vale para todas as telas; cliente vê só a própria empresa)
- [x] Excluir cliente do portal (além de desativar): apaga conexão, produtos, DRE, alertas e programação; desativa usuários; mantém o histórico de mensagens

## Etapa 4b — Portal do cliente (clientes da Vogel usando o portal)

Ideia (24/09/2026): o cliente recebe um link próprio + usuário e senha e vê **só a empresa dele**.

- [x] **Perfis:** Vogel Administrador · Vogel Operador · Cliente Gestor · Cliente Usuário
- [x] **Usuário vinculado a um cliente**; todas as telas e consultas filtradas pela empresa do usuário (filtro no servidor, não só no menu)
- [ ] **Link por empresa** (`portal.../acesso/nome-da-empresa`): a tela de login mostra logo e nome da empresa; o link só identifica, quem dá acesso é o usuário+senha
- [x] Cadastro de usuários só pela tela Usuários (administrador Vogel ou gestor da empresa); o cadastro livre só existe para o 1º usuário
- [x] O que o cliente vê: Painel, Alertas, Precificação, DRE, histórico de Mensagens dele (rotinas por cliente: item abaixo)
- [x] O que fica só com a Vogel: Clientes (conexões), SQL das rotinas, Configurações do Pagebot (token, limites), dados de outros clientes
- [ ] Segurança: ~~bloqueio após 5 senhas erradas (15 min)~~ feito; "esqueci a senha" por e-mail, 2FA opcional, log de acessos (LGPD)
- [ ] **Versão online sem acesso direto à base do cliente**: os dados chegam por sincronização (um agente pequeno no cliente roda os SELECTs e envia só o resultado, ou importação de arquivo). Hoje, local, só a Vogel consulta o banco
- [x] Campo `Impressao` no cliente (SHA-256 dos dígitos do CNPJ), preenchido ao salvar
- [ ] **Identificação da empresa no estilo da REV_Base (REV ENTRADAS)**: guardar só a *impressão digital* (hash SHA-256) do CNPJ/CGC, não o CNPJ aberto; o agente manda a impressão e o portal sabe de qual cliente são os dados. Todas as tabelas ligadas pelo Id do cliente (como o `BaseId`)
- [x] Rotinas por empresa: o gestor liga/desliga, escolhe o horário e o aviso automático (se a Vogel permitir); SQL, limites e horário comercial continuam com a Vogel
- [ ] Texto da mensagem por empresa (hoje o texto é o da rotina)

## Documentação do portal (manual de uso)

Pedido em 24/09/2026: um manual com **prints de cada tela**, explicando para que serve e como usar.

- [ ] Manual por tela: Painel, Alertas, Rotinas, Clientes, Precificação, DRE, Mensagens, Configurações, Usuários, Login
- [ ] Em cada tela: print, para que serve, passo a passo, o que significa cada coluna/botão (aproveitar os textos das dicas), cuidados
- [ ] Duas versões: **equipe Vogel** (tudo) e **cliente** (só o que o perfil de cliente vê)
- [ ] Prints gerados automaticamente com dados de exemplo (sem dados reais de clientes) para atualizar fácil quando a tela mudar
- [ ] Publicar junto com o portal (página "Ajuda" ou PDF) e atualizar a cada mudança de tela

## Etapa 5 — Inteligência e dashboards

Tudo a partir da **base de dados de cada cliente**, apresentado de forma acessível. Sem vínculo com outros projetos (LVAdderi, NCM/Tributações).


- [ ] Biblioteca de rotinas prontas por tipo de sistema/banco (reaproveitar entre clientes)
- [ ] DRE comparativo (ano x ano, realizado x orçado) e plano de contas configurável por cliente
- [x] DRE do cliente piloto: receita pela `viewFaturamentoLV` do ERP (bate com a DRE/E-Diretor); CMV pela nossa regra (reposição médio). SQL em `docs/dre-viewFaturamentoLV-sqlserver.sql`
- [ ] CMV do E-Diretor (tela de indicadores do ERP) — calculado no programa, não nas visões; perguntar ao ERP qual custo usa (retomar depois)
- [ ] Avaliar DRE pela `viewDRE` (estrutura de centros de custo do cliente; filtro Compõe ≠ CC e "Não soma")
- [ ] Curva ABC de produtos, giro de estoque, produtos abaixo do custo
- [x] **Módulo Comercial** (25/09/2026), tela **Comercial** no menu (Vogel e Cliente Gestor):
  - indicadores por vendedor (estrutura de venda do ERP), agrupados por gerente (estrutura "pai"): venda líquida, número de vendas, ticket médio, itens/venda e margem;
  - **meta por vendedor e mês**, guardada no portal: % atingido, projeção do mês em andamento e "Copiar metas do mês anterior";
  - dados: `ModeloComercialSqlServer.sql`, **direto nas tabelas do ERP** (sem a `viewFaturamentoLV`, só com as regras dela) → `LV_Comercial` → portal, com importação automática diária;
  - conferência: na base de teste, o SQL das tabelas deu venda, número de vendas e itens iguais aos da view em todos os meses de 2025. A view só serviu de referência nessa conferência.
- [x] Comercial no formato do relatório **"Metas"** do ERP (25/09/2026):
  - blocos **Diário** (data final), **Acumulado** (dia 1 até a data), **Acumulado ano anterior** (mesmo período, com crescimento) e **Meta geral** (mês inteiro), com a coluna **Diferença**: faltam ou passou;
  - meta do dia = meta do mês ÷ dias úteis; meta acumulada proporcional aos dias úteis até a data;
  - dias úteis **como o ERP** (fDiasUteis): segunda a sexta 1, sábado ½, menos feriados, arredondado (ago/2026: 23,5 → 24), com ajuste manual por mês;
  - **metas = cotas do ERP** (tabela Cotas de Vendas → `LV_Cota`), e a meta digitada no portal só vale onde o ERP não tem cota;
  - **sem vendas em permuta** (local 25), como o relatório;
  - CMV pelo "Custo total", como o ERP;
  - dados por dia e vendedor. Conferido na base de teste: venda, CMV e número de vendas iguais aos das tabelas do ERP.
- [x] Comercial: interruptor "Desconsiderar permutas" (padrão ligado). A permuta vem separada (coluna `Permuta` na `LV_Comercial`); o relatório "Metas" do ERP tira a permuta no mês, mas não no ano anterior. Aparece quem tem meta ou vendeu no mês; totais somam todos
- [ ] Comercial: meta no Painel; curva ABC de clientes e produtos; comparação com o mesmo mês do ano anterior por vendedor; conferir no cliente piloto com o relatório "Metas" do ERP
- [x] **Tela Comercial (indicadores de gestão comercial)** (primeira versão feita: ver "Módulo Comercial" acima) — mesmo esquema da DRE: SQL cadastrado pela Vogel, resultado guardado no portal, gestor atualiza, cliente só consulta
  - Vendas: faturamento (mês, ano anterior, acumulado), ticket médio, nº de vendas, vendas por vendedor/filial/dia da semana — `Documentos Fiscais` + itens
  - Rentabilidade: margem bruta por produto/grupo/vendedor, desconto médio concedido, curva ABC — itens × custo (já usado na DRE e na precificação)
  - Clientes: clientes ativos, novos, que pararam de comprar (inativos há 60/90 dias), recompra, concentração (quanto os 10 maiores representam)
  - Recebimento: prazo médio de recebimento, inadimplência (títulos vencidos em aberto), vencidos por faixa de atraso — `Titulos do Contas a Receb`
  - Conversão: orçamentos × vendas (taxa de conversão) — `Tiposdf orcamentos` / documentos do tipo orçamento (confirmar)
  - Devoluções: % devolvido sobre as vendas (códigos fiscais 2 e 4, já usados na DRE)
  - Comissões: comissão por vendedor — `Lancamentos Comissoes` / `Titulos comissoes`
  - Metas: realizado × meta (a meta pode ser digitada no portal; `Centro de custo metas` quase vazio)
  - Rotinas/alertas ligadas: cliente grande que parou de comprar, inadimplência acima de X%, venda abaixo do ponto de equilíbrio no mês
- [ ] Simulador de preço (e se eu mudar a margem / imposto?)
- [x] Precificação: marcar produtos e avisar um responsável pelo WhatsApp para revisão de preços (gestor e Vogel)
- [ ] Aviso de preços automático por regra (ex.: toda segunda, produtos com venda abaixo da regra do ERP)
- [ ] Grupo de formação de preço: considerar Operação (Débito/Crédito) e "Aplica sobre" (preço de venda × custo) dos itens — hoje soma todos (no BASE todos são Débito sobre o preço de venda)
- [ ] **Reforma tributária (CBS/IBS)** — ⏸ **aguardando o regramento** (regulamentação da LC 214/2025 e como o ERP vai tratar CBS/IBS nos grupos de formação de preço); revisar quando sair. 2027: CBS no lugar de PIS/COFINS; 2029–2032: ICMS/ISS caem e o IBS sobe; 2033: só IBS/CBS
  - Precificação: CBS/IBS são cobrados "por fora" (somam em cima do preço, não entram no divisor como PIS/COFINS/ICMS) e dão crédito amplo na compra (custo líquido de crédito)
  - Ler os novos tipos de custo dos grupos de formação de preço do ERP quando existirem (depende do item acima: Operação e "Aplica sobre")
  - Transição 2027–2032 com os dois modelos juntos (ICMS + IBS) no mesmo produto
  - DRE: CBS/IBS nas deduções, créditos reduzindo o CMV; comparar margem antes × depois
  - Simulador "preço hoje × preço na regra nova" por produto, para o gestor se preparar
- [ ] IA para resumir a situação do cliente e sugerir a mensagem (Claude)
- [ ] Portal do cliente: ver **Etapa 4b**

## Etapa 6 — Publicação

- [x] Banco do portal no SQL Server **PORTAL_VG** (login próprio `portal_vogel`; antes chamado BASELV), dados copiados do SQLite em 24/09/2026; empresas com chave Id + impressão do CNPJ (estilo REV_Base)
- [x] **BASELV no servidor de cada cliente** (opção B, 25/09/2026): só os **resultados** que o portal usa: `LV_Dre`, `LV_Precificacao` e `LV_Sincronizacao` ([docs/criar-baselv-resultados.sql](docs/criar-baselv-resultados.sql)). A cópia das tabelas brutas (opção C) foi descartada por duplicar dados.
- [x] Programa separado **SincronizadorLV** ([src/SincronizadorLV](src/SincronizadorLV/README.md)): roda na BASE os mesmos SQLs do portal (só SELECT) e grava o resultado; CSV opcional para o "Importar CSV/XLSX"; `agendar-tarefa.ps1` para a virada do dia (00:05)
- [x] Portal: modelos "Usar BASELV (recomendado)" na DRE e na Precificação; cliente "teste" já lê da BASELV
- [x] cliente piloto: tabelas criadas na BASELV existente do servidor do cliente (as 5 tabelas antigas não foram tocadas)
- [x] DRE do cliente piloto direto nas tabelas, sem a `viewFaturamentoLV`, que se desconfigura nas atualizações do ERP. Segue as mesmas regras da view ([docs/dre-propria-tabelas-sqlserver.sql](docs/dre-propria-tabelas-sqlserver.sql)); receita, deduções e CMV de 2025 e 2026 iguais aos da view, centavo por centavo
- [x] cliente piloto: primeira sincronização em 25/09/2026 (agosto/2026 bate com o ERP) e cadastro lendo da BASELV
- [x] **Kit de implantação** ([docs/implantacao/GUIA.md](docs/implantacao/GUIA.md)): passo a passo com resultados esperados e testes; scripts `00-diagnostico`, `01-criar-baselv`, `02-criar-logins` (sincronizador + portal, permissão mínima), `03-conferencia` e `gerar-pacote.ps1`
- [ ] **Na implantação em cada cliente** (fica para quando formos implantar de verdade): criar o login próprio do sincronizador (passo 3 do [guia](docs/implantacao/GUIA.md): só leitura na BASE e só as 3 tabelas `LV_` na BASELV) e agendar a virada do dia (`agendar-tarefa.ps1`)
- [x] **Rotinas e alertas pela BASELV** (25/09/2026):
  - o sincronizador roda os SQLs das rotinas (exportados do portal para o pacote) e grava o resultado em `LV_Rotina`;
  - o portal, com "Rotinas e alertas pela BASELV" marcado no cadastro, lê daí em vez de consultar a BASE;
  - SQL com erro no cliente, resultado com mais de 36 h ou rotina ainda não sincronizada viram alerta de falha;
  - `agendar-tarefa.ps1 -RotinasACadaHoras 2` roda as rotinas várias vezes ao dia;
  - testado localmente: a rotina 1 deu 54 na BASELV e 54 direto na BASE.
- [x] **Botão "Sincronizar agora"** no Painel (25/09/2026):
  - o portal grava um pedido em `BASELV.dbo.LV_Pedido`. É a única gravação dele no servidor do cliente, com SQL fixo, e o login `vogel_portal` só tem INSERT nessa tabela;
  - `SincronizadorLV --pedidos` (tarefa a cada 5 min) atende o pedido, e o Painel acompanha, importa e recarrega sozinho;
  - evita repetição: 10 min entre pedidos, e nenhum pedido novo enquanto o anterior está pendente ou executando;
  - avisa quando o servidor do cliente não responde em 20 min.
  - Na versão online, o pedido passa a ir pela API.
- [x] Painel com a empresa escolhida (25/09/2026):
  - venda líquida do mês, com projeção quando o mês está em andamento e comparação com o mês anterior e com o mesmo mês do ano passado;
  - barra do ponto de equilíbrio (faturamento bruto do mês contra o PE dos 12 meses anteriores);
  - "Dados atualizados" da DRE e da Precificação, com o selo "desatualizado" depois de 36 h. A data vem da `LV_Sincronizacao` quando o SQL lê a BASELV.
- [x] Painel (25/09/2026):
  - margem bruta do mês e resultado do último mês fechado;
  - "Preços que precisam de atenção", com link para a Precificação já filtrada (`?problemas=1`; mesma regra do "Só problemas", agora em `CalculoPreco.TemProblema`);
  - visão "Todas as empresas" com uma tabela por cliente: venda, variação, equilíbrio, preços, alertas e data dos dados;
  - verificação por linha de comando: `dotnet run -- --painel <id>`.
- [x] Importação automática da BASELV para o portal (25/09/2026): todo dia a partir das 06:00, só quando há sincronização nova; sem conexão, não apaga nada e tenta a cada hora. O código de gravação é o mesmo do botão e da planilha (`ImportacaoDados`)
- [ ] HTTPS, domínio (ex.: portal.vogel...), backup do banco **e da pasta `Data/keys`** (sem as chaves as conexões não podem ser lidas)
- [ ] Monitorar o agendador (avisar se a rotina diária não rodou)

---

## Ideias soltas / anotações

- Mensagem modelo: "Olá {cliente}! Identificamos {quantidade} nota(s) com erro ou não processada(s) em {data}. Favor revisar."
- Vobô pode ser o "remetente" das mensagens automáticas.
-
- Futuro (mais para frente): talvez juntar o projeto revisor de tributação ao portal. Por ora, foco no trabalho atual.
