> **Descartado em 25/09/2026 (opção C).** A BASELV guarda só os resultados: veja [criar-baselv-resultados.sql](criar-baselv-resultados.sql).

# BASELV — tabelas e colunas copiadas da BASE do ERP

Desenho da **BASELV** (aprovado e criado em 25/09/2026), que ficará no servidor de cada cliente. Ela recebe uma cópia diária, só com as colunas que o portal usa.
Levantado em 25/09/2026 a partir das consultas do portal (rotinas, DRE e Precificação) e da `viewFaturamentoLV`, usando a BASE de teste.
Os nomes de tabelas e colunas são iguais aos do ERP, e os tipos, iguais aos da BASE (nível de compatibilidade 100).

Legenda: **PK** = chave primária do ERP, necessária para a sincronização saber se uma linha é nova ou foi alterada.

## Vendas (DRE e Precificação)

### Documentos Fiscais — 800 mil linhas / 3,0 GB no ERP
| Coluna | Tipo | |
|---|---|---|
| Filial | int | PK |
| Codigo do tipo de documen | varchar(3) | PK |
| Numero do documento | int | PK |
| Data de Emissao | datetime | |
| Data competencia | datetime | |
| Situacao | varchar(3) | |
| Valor Total | decimal(18,4) | |
| Valor do desconto | decimal(16,2) | |
| Valor Frete | decimal(18,2) | |
| Valor despesas acessorias | decimal(14,2) | |
| Numero da Lista | int | |
| Condicao | int | |

### Itens dos Documentos Fisc — 2,1 milhões de linhas / 5,3 GB no ERP
| Coluna | Tipo | |
|---|---|---|
| Filial | int | PK |
| Codigo do tipo de documen | varchar(3) | PK |
| Numero do documento | int | PK |
| Sequencia | int | PK |
| Codigo do Produto | varchar(15) | |
| Codigo Fiscal | int | |
| CIOF | varchar(4) | |
| Quantidade | decimal(15,4) | |
| Preco Final | decimal(18,2) | |
| Preco com IPI | decimal(18,2) | |
| Subst Tributaria Valor | decimal(18,2) | |
| Valor Frete | decimal(18,2) | |
| Valor despesas acessorias | decimal(14,2) | |
| Custo Atual | decimal(18,4) | |
| Custo reposicao medio | decimal(18,4) | |

### Titulos do Contas a Pagar — 95 mil linhas / 56 MB no ERP
| Coluna | Tipo | |
|---|---|---|
| Filial | int | PK |
| Id | bigint | PK |
| Codigo do cc | varchar(20) | |
| Data de Emissao | datetime | |
| Data competencia | datetime | |
| Valor Nominal | decimal(18,4) | |
| Eprevisao | bit | |

### Centro de custo — 478 linhas
Filial int PK · Codigo do cc varchar(20) PK · Descricao do cc varchar(50)

### Regras de faturamento (substituem a `viewFaturamentoLV` na DRE do cliente piloto)
- **Condicoes de Faturamento** (156): Filial int PK · Numero da Lista int PK · Condicao int PK · Nao considera faturamento bit
- **Descricao dos Codigos Fis** (98): Codigo da descricao ciof varchar(4) PK · Devolucao varchar(19)
- **Tipos de Documentos Fisca** (20): Filial int PK · Codigo do tipo de documen varchar(3) PK · Tipodocfiscal varchar(22)

A DRE só usa da visão: eDev, ValorTotal, Quantidade, CRMedio, CA, as datas e o tipo de documento. Tudo isso vem das 5 tabelas acima (Documentos, Itens e as 3 de regras).
Pessoas, Estruturas, Locais de Pagamento, Depto compras e a função `DIASEMANA` servem apenas para as descrições da visão. **Não precisam ser copiadas.**

## Precificação (cadastro)
- **Produtos** (20 mil): Codigo do Produto varchar(15) PK · Descricao varchar(255) · Desativado bit
- **Produtos_dados** (41 mil): Filial int PK · Codigo do Produto varchar(15) PK · Custo Atual decimal(18,4) · Codigo grupo form preco int
- **Precos** (41 mil): Filial int PK · Numero da Lista int PK · Codigo do Produto varchar(15) PK · Preco1 decimal(18,4) · Lucro Desejado decimal(8,3) · Lucro desejado sobre varchar(14)
- **Grupos formacao preco** (9): Codigo grupo form preco int PK · Descricao varchar(255)
- **Grupo form preco itens** (16): Codigo grupo form preco int PK · Sequencia int PK · Percentual decimal(8,4)
  - Quando formos tratar Operação e Aplica sobre (roadmap), essas duas colunas também entram.

## Rotinas (alertas)
- **Nfe_download** (116 mil): Filial int PK · Nsu bigint PK · Nome varchar(150) · Cnpj varchar(18) · Data de Emissao datetime · Valor nf decimal(18,4) · Chavenota varchar(50) · Tipo varchar(7) · Situacao_nfe smallint · Importado bit
- **Nfe_status** (289 mil): Filial int PK · Codigo do tipo de documen varchar(3) PK · Numero do documento int PK · Datasituacao datetime · Rdatainclusao datetime · Rdata datetime · Cstat varchar(50) · Xmotivo text (copiar como varchar(300)) · Codigosituacao int
- **Log de whatsapp** (30 mil): Id_bd bigint PK · Data datetime · Status envio varchar(11) · Origem contato varchar(255) · Celular destinatario varchar(15)
- **Entradas** (34 mil): Filial int PK · Data da Entrada datetime PK · Sequencia da Entrada int PK · Lancado bit

## Quanto copiar (proposta)
| Grupo | Período |
|---|---|
| Cadastros pequenos (centro de custo, regras, produtos, preços, grupos) | tudo, todo dia |
| Documentos, Itens, Títulos | carga inicial de 24 meses; depois, todo dia, recopiar os últimos 60 dias (notas canceladas e títulos alterados depois) |
| Nfe_status, Nfe_download, Log de whatsapp | últimos 60 dias |
| Entradas | só as não lançadas (`Lancado <> 1`) |

Não dá para confiar no `Rdata` do ERP para saber o que mudou. Ele vem sem hora, e na BASE de teste a data mais recente é de 2025. Por isso a proposta é recopiar uma janela de dias, em vez de trazer "só o que mudou".
