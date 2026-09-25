-- DESCARTADO em 25/09/2026 (opção C: cópia das tabelas brutas). NÃO RODAR. Use docs/criar-baselv-resultados.sql.
-- BASELV: cópia diária, só leitura, das tabelas e colunas da BASE do ERP que o Portal Vogel usa.
-- Desenho: docs/baselv-tabelas.md. Nomes de tabelas e colunas iguais aos do ERP, para as consultas do portal servirem nos dois.
-- Rodar UMA vez no SSMS, conectado como administrador, com o banco BASELV já criado e vazio.
-- Não toca na BASE do ERP.

USE master;
GO
-- Mesma collation da BASE do ERP (evita erro de "collation conflict" ao comparar textos entre os dois bancos)
ALTER DATABASE BASELV COLLATE SQL_Latin1_General_CP1_CI_AS;
GO

USE BASELV;
GO

-- ===================== Vendas (DRE e Precificação) =====================

CREATE TABLE [Documentos Fiscais] (
    [Filial]                     int           NOT NULL,
    [Codigo do tipo de documen]  varchar(3)    NOT NULL,
    [Numero do documento]        int           NOT NULL,
    [Data de Emissao]            datetime      NULL,
    [Data competencia]           datetime      NULL,
    [Situacao]                   varchar(3)    NULL,
    [Valor Total]                decimal(18,4) NULL,
    [Valor do desconto]          decimal(16,2) NULL,
    [Valor Frete]                decimal(18,2) NULL,
    [Valor despesas acessorias]  decimal(14,2) NULL,
    [Numero da Lista]            int           NULL,
    [Condicao]                   int           NULL,
    CONSTRAINT [PK_Documentos Fiscais] PRIMARY KEY ([Filial], [Codigo do tipo de documen], [Numero do documento])
);
CREATE INDEX [IX_Documentos Fiscais_Data de Emissao] ON [Documentos Fiscais] ([Data de Emissao]);

CREATE TABLE [Itens dos Documentos Fisc] (
    [Filial]                     int           NOT NULL,
    [Codigo do tipo de documen]  varchar(3)    NOT NULL,
    [Numero do documento]        int           NOT NULL,
    [Sequencia]                  int           NOT NULL,
    [Codigo do Produto]          varchar(15)   NULL,
    [Codigo Fiscal]              int           NULL,
    [CIOF]                       varchar(4)    NULL,
    [Quantidade]                 decimal(15,4) NULL,
    [Preco Final]                decimal(18,2) NULL,
    [Preco com IPI]              decimal(18,2) NULL,
    [Subst Tributaria Valor]     decimal(18,2) NULL,
    [Valor Frete]                decimal(18,2) NULL,
    [Valor despesas acessorias]  decimal(14,2) NULL,
    [Custo Atual]                decimal(18,4) NULL,
    [Custo reposicao medio]      decimal(18,4) NULL,
    CONSTRAINT [PK_Itens dos Documentos Fisc] PRIMARY KEY ([Filial], [Codigo do tipo de documen], [Numero do documento], [Sequencia])
);
CREATE INDEX [IX_Itens dos Documentos Fisc_Codigo do Produto] ON [Itens dos Documentos Fisc] ([Codigo do Produto]);

CREATE TABLE [Titulos do Contas a Pagar] (
    [Filial]            int           NOT NULL,
    [Id]                bigint        NOT NULL,
    [Codigo do cc]      varchar(20)   NULL,
    [Data de Emissao]   datetime      NULL,
    [Data competencia]  datetime      NULL,
    [Valor Nominal]     decimal(18,4) NULL,
    [Eprevisao]         bit           NULL,
    CONSTRAINT [PK_Titulos do Contas a Pagar] PRIMARY KEY ([Filial], [Id])
);
CREATE INDEX [IX_Titulos do Contas a Pagar_Datas] ON [Titulos do Contas a Pagar] ([Data competencia], [Data de Emissao]);

CREATE TABLE [Centro de custo] (
    [Filial]           int         NOT NULL,
    [Codigo do cc]     varchar(20) NOT NULL,
    [Descricao do cc]  varchar(50) NULL,
    CONSTRAINT [PK_Centro de custo] PRIMARY KEY ([Filial], [Codigo do cc])
);

-- Regras de faturamento (as mesmas que a viewFaturamentoLV do ERP usa)
CREATE TABLE [Condicoes de Faturamento] (
    [Filial]                     int NOT NULL,
    [Numero da Lista]            int NOT NULL,
    [Condicao]                   int NOT NULL,
    [Nao considera faturamento]  bit NULL,
    CONSTRAINT [PK_Condicoes de Faturamento] PRIMARY KEY ([Filial], [Numero da Lista], [Condicao])
);

CREATE TABLE [Descricao dos Codigos Fis] (
    [Codigo da descricao ciof]  varchar(4)  NOT NULL,
    [Devolucao]                 varchar(19) NULL,
    CONSTRAINT [PK_Descricao dos Codigos Fis] PRIMARY KEY ([Codigo da descricao ciof])
);

CREATE TABLE [Tipos de Documentos Fisca] (
    [Filial]                     int         NOT NULL,
    [Codigo do tipo de documen]  varchar(3)  NOT NULL,
    [Tipodocfiscal]              varchar(22) NULL,
    CONSTRAINT [PK_Tipos de Documentos Fisca] PRIMARY KEY ([Filial], [Codigo do tipo de documen])
);

-- ===================== Precificação (cadastro) =====================

CREATE TABLE [Produtos] (
    [Codigo do Produto]  varchar(15)  NOT NULL,
    [Descricao]          varchar(255) NULL,
    [Desativado]         bit          NULL,
    CONSTRAINT [PK_Produtos] PRIMARY KEY ([Codigo do Produto])
);

CREATE TABLE [Produtos_dados] (
    [Filial]                   int           NOT NULL,
    [Codigo do Produto]        varchar(15)   NOT NULL,
    [Custo Atual]              decimal(18,4) NULL,
    [Codigo grupo form preco]  int           NULL,
    CONSTRAINT [PK_Produtos_dados] PRIMARY KEY ([Filial], [Codigo do Produto])
);

CREATE TABLE [Precos] (
    [Filial]                int           NOT NULL,
    [Numero da Lista]       int           NOT NULL,
    [Codigo do Produto]     varchar(15)   NOT NULL,
    [Preco1]                decimal(18,4) NULL,
    [Lucro Desejado]        decimal(8,3)  NULL,
    [Lucro desejado sobre]  varchar(14)   NULL,
    CONSTRAINT [PK_Precos] PRIMARY KEY ([Filial], [Numero da Lista], [Codigo do Produto])
);

CREATE TABLE [Grupos formacao preco] (
    [Codigo grupo form preco]  int          NOT NULL,
    [Descricao]                varchar(255) NULL,
    CONSTRAINT [PK_Grupos formacao preco] PRIMARY KEY ([Codigo grupo form preco])
);

CREATE TABLE [Grupo form preco itens] (
    [Codigo grupo form preco]  int          NOT NULL,
    [Sequencia]                int          NOT NULL,
    [Percentual]               decimal(8,4) NULL,
    CONSTRAINT [PK_Grupo form preco itens] PRIMARY KEY ([Codigo grupo form preco], [Sequencia])
);

-- ===================== Rotinas (alertas) =====================

CREATE TABLE [Nfe_download] (
    [Filial]           int           NOT NULL,
    [Nsu]              bigint        NOT NULL,
    [Nome]             varchar(150)  NULL,
    [Cnpj]             varchar(18)   NULL,
    [Data de Emissao]  datetime      NULL,
    [Valor nf]         decimal(18,4) NULL,
    [Chavenota]        varchar(50)   NULL,
    [Tipo]             varchar(7)    NULL,
    [Situacao_nfe]     smallint      NULL,
    [Importado]        bit           NULL,
    CONSTRAINT [PK_Nfe_download] PRIMARY KEY ([Filial], [Nsu])
);

CREATE TABLE [Nfe_status] (
    [Filial]                     int          NOT NULL,
    [Codigo do tipo de documen]  varchar(3)   NOT NULL,
    [Numero do documento]        int          NOT NULL,
    [Datasituacao]               datetime     NULL,
    [Rdatainclusao]              datetime     NULL,
    [Rdata]                      datetime     NULL,
    [Cstat]                      varchar(50)  NULL,
    [Xmotivo]                    varchar(300) NULL,  -- no ERP é "text"; a rotina só usa os 300 primeiros caracteres
    [Codigosituacao]             int          NULL,
    CONSTRAINT [PK_Nfe_status] PRIMARY KEY ([Filial], [Codigo do tipo de documen], [Numero do documento])
);

CREATE TABLE [Log de whatsapp] (
    [Id_bd]                 bigint       NOT NULL,
    [Data]                  datetime     NULL,
    [Status envio]          varchar(11)  NULL,
    [Origem contato]        varchar(255) NULL,
    [Celular destinatario]  varchar(15)  NULL,
    CONSTRAINT [PK_Log de whatsapp] PRIMARY KEY ([Id_bd])
);

CREATE TABLE [Entradas] (
    [Filial]                int      NOT NULL,
    [Data da Entrada]       datetime NOT NULL,
    [Sequencia da Entrada]  int      NOT NULL,
    [Lancado]               bit      NULL,
    CONSTRAINT [PK_Entradas] PRIMARY KEY ([Filial], [Data da Entrada], [Sequencia da Entrada])
);

-- ===================== Controle da sincronização (não existe no ERP) =====================

-- Uma linha por tabela: quando foi a última cópia, quantas linhas vieram e se deu erro.
-- O portal usa para mostrar "dados de dd/mm hh:mm" e avisar se a cópia parou.
CREATE TABLE [LV_Sincronizacao] (
    [Tabela]        varchar(60)   NOT NULL,
    [UltimoInicio]  datetime      NULL,
    [UltimoFim]     datetime      NULL,
    [Linhas]        int           NULL,
    [Erro]          varchar(1000) NULL,
    CONSTRAINT [PK_LV_Sincronizacao] PRIMARY KEY ([Tabela])
);
GO
