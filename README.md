# Portal Vogel

Portal web da **Vogel Gestão e Tecnologia** para os clientes acompanharem a gestão da loja (vendas, metas, DRE, ponto de equilíbrio, projeção, preços) e para a Vogel monitorar rotinas e alertas de todos os clientes.

- **Portal**: ASP.NET Core Blazor Server, .NET 10, EF Core, banco próprio em SQL Server (`PORTAL_VG`). Detalhes em [src/Dashborad/README.md](src/Dashborad/README.md).
- **SincronizadorLV**: programa de console que roda no servidor de cada cliente e grava os resultados na `BASELV`. Detalhes em [src/SincronizadorLV/README.md](src/SincronizadorLV/README.md).
- **Implantação num cliente**: [docs/implantacao/GUIA.md](docs/implantacao/GUIA.md) (passo a passo, scripts e conferências) e o roteiro curto [docs/implantacao/COMANDOS-NOVO-CLIENTE.md](docs/implantacao/COMANDOS-NOVO-CLIENTE.md).
- **Etapas e próximos passos**: [ROADMAP.md](ROADMAP.md).

## Como os dados chegam ao portal

```
Servidor do cliente                                         Máquina/servidor da Vogel
┌──────────────────────────┐                              ┌───────────────────────────┐
│ BASE (ERP)  — só leitura │                              │ Portal Vogel (Blazor)     │
│        │ SELECT          │                              │   lê BASELV.dbo.LV_*      │
│        ▼                 │                              │   e guarda no PORTAL_VG   │
│ SincronizadorLV          │  ◄── pedido (LV_Pedido) ──── │   ("Sincronizar agora")   │
│  (tarefa diária 00:05)   │                              │                           │
│        │ grava           │ ───── resultados ──────────► │ PORTAL_VG (banco próprio) │
│        ▼                 │                              └───────────────────────────┘
│ BASELV  (tabelas LV_*)   │
└──────────────────────────┘
```

1. **BASE do ERP**: banco do cliente. **Nunca é alterado**: o portal e o sincronizador só rodam `SELECT`, dentro de transação sempre desfeita e com uma trava que recusa qualquer comando de alteração.
2. **SincronizadorLV**: roda na virada do dia (e a cada 5 min para atender o botão "Sincronizar agora"), executa os SQLs do portal na BASE e grava **só o resultado** na BASELV.
3. **BASELV**: banco da Vogel no servidor do cliente, só com as tabelas `LV_*` (resultados prontos, sem cópia de tabelas do ERP).
4. **Portal**: importa da BASELV todo dia a partir das 6h (e quando o sincronizador termina um pedido) e guarda no `PORTAL_VG`. A única escrita do portal no servidor do cliente é o `INSERT` em `BASELV.dbo.LV_Pedido`.

## Telas

| Área | Telas |
|---|---|
| **Painel** | Venda do mês e projeção, meta de vendas, ponto de equilíbrio, margem, preços que precisam de atenção, pendências; visão "Empresas" para a Vogel |
| **Comercial** | Metas por vendedor (igual ao relatório "Metas" do ERP, com interruptor de permutas) · Precificação · *Vendas e Curva ABC (em breve)* |
| **Financeiro** | DRE gerencial (mês a mês e comparativo, margens bruta/contribuição/EBITDA/líquida, carga tributária, tributos atuais x reforma tributária — CBS/IBS/IS, contas por linha, classificação de despesas) · *Fluxo de caixa e Contas a receber e a pagar (em breve)* |
| **Gestão** | Ponto de equilíbrio · Projeção (ano, cenários e simulador "e se") · *Rentabilidade (em breve)* |
| **Acompanhamento** | Alertas, Mensagens (WhatsApp/Pagebot), Rotinas |
| **Administração** | Clientes, Configurações, Usuários |

## Arquivos do repositório

| Caminho | O que é |
|---|---|
| `src/Dashborad/` | Portal (Blazor Server). Páginas em `Components/Pages`, regras em `Services`, entidades e migrations em `Data` |
| `src/Dashborad/Services/Modelo*SqlServer.sql` | SQLs padrão que leem **direto as tabelas do ERP** (DRE, Precificação, Comercial, Cotas, Feriados) |
| `src/Dashborad/Services/Modelo*BaseLv.sql` | SQLs que leem o resultado pronto da **BASELV** (o padrão recomendado) |
| `src/SincronizadorLV/` | Programa do servidor do cliente; `agendar-tarefa.ps1` cria as tarefas agendadas do Windows |
| `docs/implantacao/` | Kit de implantação: `00-diagnostico.sql`, `01-criar-baselv.sql`, `02-criar-logins.sql`, `03-conferencia.sql`, `gerar-pacote.ps1` e o `GUIA.md` |
| `docs/criar-portal-vg.sql` | Cria o banco `PORTAL_VG` e o login do portal |
| `docs/dre-propria-tabelas-sqlserver.sql` | Exemplo de DRE própria de um cliente, direto nas tabelas do ERP |
| `docs/rotinas-sqlserver.sql` | SQLs das rotinas de notas (não lançadas, rejeitadas, aguardando SEFAZ, WhatsApp do sistema) |
| `docs/mapa-banco-base.md`, `docs/baselv-tabelas.md`, `docs/dre-sqlserver.md`, `docs/precificacao-sqlserver.md` | Mapeamento do banco do ERP e regras de cada cálculo |
| `docs/criar-baselv-*.sql`, `docs/dre-viewFaturamentoLV-sqlserver.sql` | Versões antigas, marcadas como DESCARTADO/SUBSTITUÍDO (só histórico) |
| `docs/privado/` | Registro das implantações por cliente. **Não vai para o GitHub** (`.gitignore`) |
| `publicar/` | Pacotes gerados do SincronizadorLV por cliente. **Não vai para o GitHub** |

## Tabelas e consultas SQL necessárias

### 1. Tabelas do ERP lidas (BASE, só `SELECT`)

| Uso | Tabelas |
|---|---|
| **DRE** (receita, devoluções, impostos, CMV) | `[Documentos Fiscais]`, `[Itens dos Documentos Fisc]`, `[Tipos de Documentos Fisca]`, `[Condicoes de Faturamento]`, `[Descricao dos Codigos Fis]` |
| **DRE** (despesas) | `[Titulos do Contas a Pagar]`, `[Centro de custo]` |
| **Precificação** | `Produtos`, `Produtos_dados`, `Precos`, `[Grupos formacao preco]`, `[Grupo form preco itens]`, `[Documentos Fiscais]`, `[Itens dos Documentos Fisc]` |
| **Comercial** (vendas por dia e vendedor) | as da DRE (receita) + `[Estrutura de Vendas]` (vendedor e gerente "pai") + `[Locais de Pagamento]` (permuta) |
| **Metas** | `[Cotas de Vendas]` |
| **Dias úteis** | `[Feriados fixos]`, `[Feriados moveis]` |
| **Rotinas de notas** | `Nfe_download`, `Nfe_status`, `[Documentos Fiscais]`, `[Log de whatsapp]` |

### 2. Visões (views) e funções do ERP: **não são usadas**

Os SQLs do portal leem **sempre as tabelas**, nunca as views do ERP, porque elas se desconfiguram nas atualizações do sistema. As views abaixo servem **só de referência** para conferir as regras e os números:

| View / função do ERP | Usada como referência para |
|---|---|
| `viewFaturamentoLV` | regras de faturamento da DRE e do Comercial (o que entra e o que sai da receita) |
| `view_FaturamentoItensCotas` | relatório "Metas" do ERP (venda sem permuta, CMV pelo "Custo total", cotas) |
| `fDiasUteis` / `fDiasUteisMes` | dias úteis (segunda a sexta = 1, sábado = ½, menos feriados; o relatório arredonda) |

**Reforma tributária:** os impostos sobre vendas da DRE vêm das guias (contas a pagar por centro de custo) e são separados em tributos atuais e da reforma (CBS, IBS, Imposto Seletivo) **pelo nome da conta**. O ERP já tem a tabela `[Classtrib ibscbs]` e o campo `Cclasstrib` nos itens das notas, ainda sem os valores de IBS/CBS; quando tiver, a DRE pode ler direto das notas.

**Nenhuma view, tabela ou função é criada na BASE do ERP.**

### 3. Tabelas da BASELV (servidor do cliente)

Criadas pelo [`docs/implantacao/01-criar-baselv.sql`](docs/implantacao/01-criar-baselv.sql) (idempotente: só cria o que falta e atualiza tabelas antigas). Gravadas pelo SincronizadorLV.

| Tabela | Conteúdo |
|---|---|
| `LV_Dre` | DRE por mês, grupo e conta (ano atual e anterior) |
| `LV_Precificacao` | produtos por filial: custo, preço, regra do ERP, venda média |
| `LV_Comercial` | vendas por **dia e vendedor**: venda bruta, devoluções, CMV, nº de vendas, itens; coluna `Permuta` separa as vendas em permuta |
| `LV_Cota` | metas (cotas) de venda por vendedor e mês |
| `LV_Feriado` | feriados fixos e móveis já em datas |
| `LV_Rotina` | resultado de cada rotina (quantidade, amostra, erro) |
| `LV_Pedido` | pedidos do botão "Sincronizar agora" (único lugar onde o portal escreve) |
| `LV_Sincronizacao` | controle: última execução, linhas e erro de cada item |

Logins de permissão mínima (`vogel_sincronizador` e `vogel_portal`): [`docs/implantacao/02-criar-logins.sql`](docs/implantacao/02-criar-logins.sql).

### 4. Tabelas do portal (PORTAL_VG)

Criadas sozinhas pelas migrations do EF Core ao iniciar o portal (`src/Dashborad/Data/Migrations`).

| Tabela | Conteúdo |
|---|---|
| `Clientes` | empresas: conexão (criptografada), SQLs cadastrados, parâmetros, datas dos dados |
| `AspNetUsers` e demais `AspNet*` | usuários, perfis e login |
| `LancamentosDre` | DRE importada (mês, grupo, conta, valor) |
| `ClassificacoesDre` | cada conta de despesa como fixa, variável ou depreciação |
| `Produtos` | precificação |
| `VendasVendedores`, `MetasVendedores`, `Feriados`, `DiasUteis` | Comercial (vendas por dia, metas do ERP ou do portal, calendário) |
| `Rotinas`, `RotinasClientes`, `Alertas`, `Mensagens`, `ConfiguracoesMensagens` | rotinas, alertas e envios de WhatsApp |

## Executar e testar

```powershell
cd src\Dashborad
dotnet run                    # portal em http://localhost:5250
dotnet run -- --self-test     # testes automáticos
```

Ferramentas de linha de comando (rodam antes de abrir o portal, sem expor senhas):

| Comando | O que faz |
|---|---|
| `dotnet run -- --consulta <id> arquivo.sql` | consulta só de leitura no banco de um cliente, com a conexão salva |
| `dotnet run -- --painel <id>` | imprime os números do Painel, da DRE e da projeção (só totais) |
| `dotnet run -- --importar-baselv <id> [--ano N]` | força a importação da BASELV de um cliente |
| `dotnet run -- --exportar-rotinas <pasta>` | exporta os SQLs das rotinas para o pacote do sincronizador |

## Segurança

- Banco do ERP **somente leitura**, sempre. Nada é criado nele.
- Senhas nunca ficam no repositório: conexões dos clientes ficam criptografadas no `PORTAL_VG` (chaves em `src/Dashborad/Data/keys`, fora do Git); a senha do sincronizador fica só no `appsettings.json` do servidor do cliente.
- Cada usuário de cliente vê só a própria empresa (filtro no servidor, em todas as telas).
