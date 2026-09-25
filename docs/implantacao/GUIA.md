# Guia de implantação do Portal Vogel num cliente

Passo a passo para configurar um cliente novo pela primeira vez. Siga na ordem: cada passo diz **o que fazer**, **o resultado esperado** e **o que conferir** antes de ir para o próximo.

## Como funciona

```
Servidor do cliente                                           Máquina do portal (Vogel)
┌────────────────────────────────────────────────┐           ┌──────────────────────────┐
│  BASE (ERP)  ──SELECT──▶  SincronizadorLV  ──▶  BASELV     │           │  Portal Vogel            │
│  (nunca é alterada)       (todo dia, 00:05)     LV_Dre     │ ◀─leitura─│  (DRE, Precificação,     │
│                                                 LV_Precif. │           │   Comercial, rotinas,    │
│                                                 LV_Comerc. │           │   WhatsApp)              │
│                                                 LV_Rotina  │           │  banco próprio PORTAL_VG │
│                                                 LV_Pedido ◀┼─ pedido ──│  (botão Sincronizar      │
│                                                 LV_Sincr.  │           │   agora)                 │
└────────────────────────────────────────────────┘           └──────────────────────────┘
```

- **BASE**: banco do ERP. O portal e o sincronizador **só leem** esse banco.
- **BASELV**: banco da Vogel no servidor do cliente. Guarda **só os resultados** que o portal mostra (DRE, Precificação e o resultado de cada rotina), nunca cópias das tabelas do ERP.
- **SincronizadorLV**: programa que roda no servidor do cliente. Ele executa na BASE os mesmos SQLs do portal (DRE, Precificação e rotinas) e grava o resultado na BASELV.
- **Portal**: lê a BASELV. Com a opção **"Rotinas e alertas pela BASELV"** marcada no cadastro, o portal não consulta mais a BASE do ERP. A **única gravação** do portal no servidor do cliente é o pedido do botão **"Sincronizar agora"**, na tabela `LV_Pedido` da BASELV.

## Arquivos do kit (`docs/implantacao/`)

| Arquivo | Passo | Onde roda | Altera algo? |
|---|---|---|---|
| `00-diagnostico.sql` | 1 | SSMS, servidor do cliente | Não (só leitura) |
| `01-criar-baselv.sql` | 2 | SSMS, servidor do cliente | Cria a BASELV e 8 tabelas (só o que falta) |
| `02-criar-logins.sql` | 3 | SSMS, servidor do cliente | Cria 2 logins com permissão mínima |
| `gerar-pacote.ps1` | 4 | PowerShell, máquina da Vogel | Gera a pasta do sincronizador |
| `03-conferencia.sql` | 6 | SSMS, servidor do cliente | Não (só leitura) |
| `agendar-tarefa.ps1` (vem no pacote) | 8 | PowerShell (admin), servidor do cliente | Cria a tarefa agendada |

---

## Antes de começar: o que levantar

- [ ] **Nome do servidor SQL** do cliente, com a instância se houver (ex.: `SRV-CLIENTE` ou `SRV01\SQLEXPRESS`).
- [ ] **Nome do banco do ERP**. Normalmente é `BASE`; se for outro, ajuste `@BaseErp` nos scripts.
- [ ] **Acesso de administrador ao SQL** (ex.: `sa`), só para os passos 2 e 3.
- [ ] **Quem vai rodar o sincronizador:** em qual máquina (o próprio servidor ou outra da rede do cliente) e com qual autenticação. O padrão é o login SQL `vogel_sincronizador`, do passo 3.
- [ ] **A DRE do cliente usa o modelo padrão ou um SQL próprio?** Exemplo de SQL próprio: `docs/dre-propria-tabelas-sqlserver.sql`, do cliente piloto, que usa as regras de faturamento da `viewFaturamentoLV`.
- [ ] **Tela de indicadores do ERP de um mês fechado** (print), para conferir os números no passo 6.
- [ ] **A máquina do portal alcança o servidor do cliente** (rede ou VPN)?

---

## Passo 1 — Diagnóstico (`00-diagnostico.sql`)

1. No SSMS, conecte no servidor do cliente e abra uma **nova consulta** (confira o servidor na barra de status).
2. Abra o arquivo. Se o banco do ERP não se chamar `BASE`, ajuste `@BaseErp`.
3. Execute com **F5, sem nada selecionado**.

**Resultado esperado:**
- **Servidor e versão:** anote a versão (ex.: 16.x = SQL Server 2022).
- **Bancos:** a BASE aparece como "ERP (origem)". A BASELV pode não aparecer ainda.
- **Tabelas do ERP:** todas com **ok**. Qualquer **FALTANDO** aparece no topo da lista.
  - Se faltar tabela de DRE ou de Precificação, **pare e fale com a Vogel**: o ERP pode ser de outra versão.
  - Se faltar só tabela de Rotinas, dá para seguir; aquela rotina fica desligada para esse cliente.
- **Filiais com venda nos últimos 90 dias:** anote as filiais, que vão para o `-Filiais` do passo 4.
- **LV_ e logins:** "não existe" é normal num cliente novo.

## Passo 2 — Criar a BASELV (`01-criar-baselv.sql`)

1. Mesma conexão de administrador. Ajuste `@BaseErp` se precisar.
2. Execute com F5.

**Resultado esperado:** a lista final mostra **LV_Comercial, LV_Cota, LV_Feriado, LV_Dre, LV_Pedido, LV_Precificacao, LV_Rotina e LV_Sincronizacao**. Numa BASELV criada antes dessas tabelas novas (como a do cliente piloto), basta rodar o script de novo: ele cria só as que faltam. Na aba "Mensagens" aparece:
- *"BASELV criada com a collation …"*, num cliente novo; ou
- *"BASELV já existia…"*, quando o cliente já tem uma BASELV (como o cliente piloto). Nesse caso, as tabelas antigas não são tocadas.

O script pode ser rodado de novo sem problema: ele só cria o que estiver faltando.

**Se der erro:**

| Mensagem | Causa | O que fazer |
|---|---|---|
| `Cannot create file '...\BASELV.mdf' because it already exists` | Existe um arquivo com esse nome, geralmente de um banco que foi renomeado | Veja no SSMS de qual banco é o arquivo. **Não apague o arquivo.** Renomeie os arquivos do outro banco (`ALTER DATABASE ... MODIFY FILE`) ou crie a BASELV com outro nome de arquivo |
| `A BASELV já existe com collation diferente da BASE` | A BASELV foi criada à mão com outra collation | Fale com a Vogel antes de mexer |
| `Banco do ERP "BASE" não encontrado` | O banco do ERP tem outro nome | Ajuste `@BaseErp` |

## Passo 3 — Logins (`02-criar-logins.sql`)

1. No início do script, **troque as duas senhas** (`DEFINA_UMA_SENHA_FORTE_1` e `_2`). Guarde-as num lugar seguro:
   - `vogel_sincronizador`: vai para o `appsettings.json` do sincronizador, no passo 5;
   - `vogel_portal`: vai para a string de conexão do cliente no portal, no passo 7.
2. Execute com F5.

**Resultado esperado:** a tabela final mostra:
- `vogel_sincronizador`: SELECT, INSERT e DELETE em LV_Dre, LV_Precificacao, LV_Comercial e LV_Rotina; SELECT, INSERT e UPDATE em LV_Sincronizacao; SELECT e UPDATE em LV_Pedido;
- `vogel_portal`: só SELECT nas tabelas `LV_`, e INSERT só na LV_Pedido (o botão "Sincronizar agora");
- nenhuma outra tabela da BASELV aparece.

Na BASE, os dois logins ficam **só com leitura** (`db_datareader`).

Se aparecer *"Troque as senhas…"*, nada foi criado: ajuste as senhas e rode de novo.

## Passo 4 — Gerar o pacote do sincronizador (`gerar-pacote.ps1`)

Na máquina da Vogel, no PowerShell, na raiz do projeto `TesteWil`:
```
.\docs\implantacao\gerar-pacote.ps1 -Cliente "Nome do Cliente" -Servidor "SERVIDOR" -Filiais 1
```
Opções:
- `-BaseErp` quando o ERP não for `BASE`;
- `-Filiais 1,2` para mais de uma filial;
- `-ConsultaDre .\docs\dre-propria-tabelas-sqlserver.sql` para uma DRE própria;
- `-Autenticacao Windows` para usar o usuário do Windows em vez do login SQL.

**Resultado esperado:** a pasta `publicar\SincronizadorLV-Nome-do-Cliente` com `SincronizadorLV.exe`, `appsettings.json`, `consultas\` (inclusive `consultas\rotinas\rotina-<Id>.sql`, exportadas do portal) e `agendar-tarefa.ps1`. O resumo no fim mostra quantas rotinas entraram.

Mudou o SQL de uma rotina no portal? Gere o pacote de novo e troque a pasta `consultas\rotinas` no servidor do cliente.

## Passo 5 — Instalar e rodar a primeira sincronização

1. Copie a pasta para o servidor do cliente, por exemplo `C:\Vogel\SincronizadorLV`.
2. No `appsettings.json`, troque `DIGITE_A_SENHA_NO_SERVIDOR` pela senha do `vogel_sincronizador`, **nas duas linhas**. A senha fica só nesse arquivo, no servidor.
3. Abra um prompt de comando na pasta e rode `SincronizadorLV.exe`.

**Resultado esperado** (os números variam por cliente; exemplo):
```
Origem: SERVIDOR/BASE  →  Destino: SERVIDOR/BASELV
  DRE 2026: N linha(s)
  DRE 2025: N linha(s)
Dre: ok, N linha(s) em 00:01
  Precificação filial 1: N produto(s)
Precificacao: ok, N linha(s) em 00:00
  Rotina 1 (Notas com erro / não processadas): ...
Rotinas: ok, 6 linha(s) em 00:03
Fim: tudo gravado.
```
A saída termina em "Fim: tudo gravado." O log fica em `logs\sincronizador-AAAAMMDD.log`.

**Se der erro:**

| Mensagem | O que fazer |
|---|---|
| `Falha de logon ... domínio não confiável` | O login do Windows não vale nesse servidor: use o login SQL (`-Autenticacao Sql`) |
| `Login failed for user 'vogel_sincronizador'` | A senha no `appsettings.json` está errada, ou o passo 3 não foi rodado |
| `O destino não tem as tabelas LV_...` | Rode o passo 2 |
| `O SQL não devolveu a(s) coluna(s)...` | O SQL da DRE ou da Precificação desse cliente está diferente do esperado: fale com a Vogel |
| `Origem e destino são o mesmo banco` | O `appsettings.json` está com `Database=BASELV` na Origem |

## Passo 6 — Conferir os números (`03-conferencia.sql`)

Rode no SSMS. É só leitura e mostra só totais.

**Resultado esperado e testes:**
1. **Sincronização:** Dre e Precificacao com `Situacao = ok` e `Erro` vazio.
2. **DRE por ano:** o ano atual e o anterior, com ReceitaBruta, Deducoes, Cmv e despesas.
3. **Teste com o ERP:** para um mês fechado, a coluna **VendaLiquida** tem que bater **exatamente** com a **"Venda"** da tela de indicadores do ERP.
   - O **CMV pode ser diferente** do ERP. O portal usa quantidade × custo de reposição médio; o ERP calcula o dele no próprio programa. É esperado.
4. **Precificação:** quantidade de produtos por filial maior que zero. "SemCusto" deve ser pequeno.
5. **Rotinas:** uma linha por rotina, com `ExecutadoEm` de hoje e `Erro` vazio. Se alguma tiver erro, a tabela do ERP que ela usa pode não existir nesse cliente (veja o passo 1); a rotina aparece no portal como falha.

**Comercial (3b):** "Vendas" e "Itens" têm que bater com **"Número de Vendas"** e **"Itens Vendidos"** da tela de indicadores do ERP, e a VendaLiquida com a **"Venda"**. Na base de teste, bateram exatamente em todos os meses de 2025.

**Se a VendaLiquida não bater:** não siga para o passo 7. Anote o mês e os dois valores e revise o SQL da DRE do cliente.

## Passo 7 — Cadastrar o cliente no portal

Entre no portal com o perfil **Vogel Administrador**.

1. **Clientes → Novo:**
   - Nome, CNPJ, contato e WhatsApp;
   - **Banco de dados:** SQL Server;
   - **String de conexão:** `Server=SERVIDOR;Database=BASE;User Id=vogel_portal;Password=SENHA;TrustServerCertificate=True`;
   - Clique em **Testar conexão**, que deve dar sucesso, e salve. A string fica criptografada no portal.
2. **Escolha o cliente no topo da tela.**
3. **DRE:**
   - Abra **SQL do banco**, clique em **Usar BASELV (recomendado)** e depois em **Salvar SQL no cliente**.
   - Clique em **Atualizar [ano] do banco** para o ano atual e para o anterior. Isso só é preciso na primeira vez: depois, o portal importa sozinho todo dia (veja abaixo).
   - **Teste:** a linha **"= Venda líquida"** de um mês fechado bate com a "Venda" do ERP.
4. **Precificação:**
   - Em **SQL do banco**, use **Usar BASELV (recomendado)** e **Salvar SQL no cliente**.
   - Informe a filial e clique em **Atualizar do banco**.
   - **Teste:** a quantidade de produtos é igual à do passo 6.
5. **Comercial:**
   - Abra **SQL do banco**, clique em **Usar BASELV (recomendado)** e depois em **Salvar SQL no cliente**.
   - Clique em **Atualizar do banco** para o ano atual e para o anterior.
   - **Metas:** vêm das **cotas do ERP** (tabela Cotas de Vendas) e aparecem com o selo "ERP". Só onde o ERP não tiver cota, o gestor digita na coluna **Meta geral** e salva.
   - **Dias úteis:** como o ERP (funções fDiasUteis/fDiasUteisMes). Segunda a sexta contam 1, sábado conta ½, e os feriados do ERP ficam de fora. O número é arredondado como no relatório: ago/2026 dá 23,5, que vira 24. Se precisar, o gestor ajusta no cabeçalho.
   - **Vendas em permuta** (local de pagamento PERMUTA, o 25 do ERP) não entram, como no relatório.
   - **Teste:** compare com o relatório **"Metas"** do ERP na mesma data final. Venda líq., CMV (Custo Total), margem e ticket do Diário, do Acumulado e do Ano anterior têm que bater.
6. **Rotinas:** em **Clientes**, marque **"Rotinas e alertas pela BASELV"** e salve. Depois, na tela **Rotinas**, ligue as rotinas que o cliente vai usar e defina o horário e o envio automático. O horário da rotina no portal deve ser **depois** da sincronização, que roda às 00:05 ou, com `-RotinasACadaHoras`, das 07:00 às 19:00.
7. **Usuários:** crie o **Cliente Gestor** e, se precisar, os **Cliente Usuário**, ligados à empresa.

**Se os cliques não fazem nada** depois que o portal foi reiniciado: aperte **F5**, porque a conexão da tela caiu.

### Importação automática (depois do passo 7)
Todo dia, a partir das **06:00**, o portal busca sozinho na BASELV a DRE (ano atual e anterior) e a Precificação das empresas que usam **"Usar BASELV"**. Ele só importa quando a sincronização do cliente tem dados mais novos do que os do portal.

Na Precificação, a importação usa a filial da última atualização feita pela tela.

Se o servidor do cliente estiver sem conexão, nada é apagado: o portal tenta de novo a cada hora, e o Painel marca os dados como **desatualizado** depois de 36 horas.

Para mudar o horário e o intervalo, altere `ImportacaoBaseLv:Hora` e `IntervaloMinutos` no `appsettings.json` do portal. Para importar na hora, rode `dotnet run -- --importar-baselv [id do cliente]`.

## Passo 8 — Agendar na virada do dia

No servidor do cliente, abra o PowerShell **como administrador**, entre na pasta do sincronizador e rode:
```
powershell -ExecutionPolicy Bypass -File .\agendar-tarefa.ps1
```
O padrão é às 00:05, com o usuário SYSTEM, que funciona com o login SQL. Use `-Horario 00:30` para outro horário. Com login do Windows, use `-Usuario "DOMINIO\usuario"`.

O script também cria a tarefa **"Vogel SincronizadorLV Pedidos"**, que confere a cada 5 minutos se alguém clicou em **"Sincronizar agora"** no portal. Sem pedido, ela sai na hora, sem gravar log. Use `-PedidosACadaMinutos 0` para não criar essa tarefa.

**Alertas mais frescos durante o dia** (recomendado): acrescente `-RotinasACadaHoras 2`. O script cria uma segunda tarefa, "Vogel SincronizadorLV Rotinas", que roda só as rotinas das 07:00 às 19:00, a cada 2 horas. A DRE e a Precificação continuam uma vez por dia.

**Teste:** rode `Start-ScheduledTask -TaskName 'Vogel SincronizadorLV'`, espere alguns segundos e rode o passo 6 de novo. O `UltimoFim` deve ter mudado para agora.

**Teste do botão:**
1. No portal, com a empresa escolhida, clique em **Sincronizar agora**, na linha "Dados:" do Painel.
2. A mensagem muda para "aguardando o servidor do cliente…" e, em até 5 minutos, para "Sincronizando…". Depois vira "concluída às HH:mm", e o Painel recarrega sozinho com os dados novos.
3. Se aparecer "sem resposta", confira se a tarefa "Vogel SincronizadorLV Pedidos" existe e está habilitada no servidor do cliente.

---

## Checklist final

- [ ] Passo 1 sem nenhuma tabela FALTANDO de DRE ou Precificação
- [ ] Passos 2 e 3 com os resultados esperados
- [ ] Primeira sincronização terminou com "Fim: tudo gravado."
- [ ] VendaLiquida de um mês fechado = Venda do ERP
- [ ] Portal: DRE (2 anos) e Precificação atualizadas a partir da BASELV
- [ ] Tarefa agendada e testada
- [ ] Senhas guardadas em local seguro; nenhuma senha em e-mail ou chat

## Implantações feitas

O registro de cada implantação (cliente, data, servidor, pendências) fica em `docs/privado/implantacoes.md`, que **não vai para o GitHub**. Colunas:

| Cliente | Data | Servidor | DRE | Observações |
|---|---|---|---|---|

