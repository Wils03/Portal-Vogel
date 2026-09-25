-- SUBSTITUÍDO por docs/implantacao/01-criar-baselv.sql (mesmas tabelas; cria a BASELV se faltar e pode rodar mais de uma vez).
-- BASELV (opção B): só os RESULTADOS que o Portal Vogel usa, no mesmo formato que o portal já grava.
-- O SincronizadorLV roda na BASE os mesmos SQLs do portal (só SELECT) e grava aqui o resultado.
-- Serve para BASELV nova ou já existente (ex.: cliente piloto): cria só estas 3 tabelas, não mexe em nada que já exista
-- e não muda a collation. Não toca na BASE do ERP.
-- Rodar UMA vez no SSMS, conectado ao servidor do cliente.

USE BASELV;
GO

-- Conferência: se alguma já existir, para sem criar nada.
DECLARE @existentes varchar(200) = '';
SELECT @existentes = @existentes + t.n + '; '
FROM (VALUES ('LV_Dre'), ('LV_Precificacao'), ('LV_Sincronizacao')) t(n)
WHERE OBJECT_ID(QUOTENAME(t.n)) IS NOT NULL;
IF @existentes <> ''
BEGIN
    RAISERROR('Estas tabelas ja existem na BASELV: %s Nada foi criado.', 16, 1, @existentes);
    SET NOEXEC ON;
END
GO

-- DRE: uma linha por mês, grupo e conta (o que o SQL da DRE do portal devolve).
-- O sincronizador recalcula o ano atual e o anterior e troca essas linhas.
CREATE TABLE [LV_Dre] (
    [Competencia]  date          NOT NULL,  -- 1º dia do mês
    [Grupo]        varchar(30)   NOT NULL,  -- ReceitaBruta, Deducoes, Cmv, DespesasOperacionais, DespesasFinanceiras...
    [Conta]        varchar(150)  NOT NULL,
    [Valor]        decimal(18,4) NOT NULL,
    [AtualizadoEm] datetime      NOT NULL,
    CONSTRAINT [PK_LV_Dre] PRIMARY KEY ([Competencia], [Grupo], [Conta])
);

-- Precificação: uma linha por produto e filial (o que o SQL da Precificação do portal devolve).
CREATE TABLE [LV_Precificacao] (
    [Filial]           int           NOT NULL,
    [Codigo]           varchar(60)   NOT NULL,
    [Descricao]        varchar(255)  NULL,
    [Custo]            decimal(18,4) NULL,
    [Preco]            decimal(18,4) NULL,
    [QtdMes]           decimal(18,4) NULL,  -- média mensal vendida nos últimos 90 dias
    [ImpostosPct]      decimal(18,4) NULL,
    [GrupoPreco]       varchar(255)  NULL,
    [LucroDesejadoPct] decimal(18,4) NULL,
    [LucroSobre]       varchar(20)   NULL,  -- como vem do ERP ("Custo" / "Preço de venda")
    [AtualizadoEm]     datetime      NOT NULL,
    CONSTRAINT [PK_LV_Precificacao] PRIMARY KEY ([Filial], [Codigo])
);

-- Controle: uma linha por item sincronizado (Dre, Precificacao...): última execução, linhas e erro.
CREATE TABLE [LV_Sincronizacao] (
    [Tabela]        varchar(60)   NOT NULL,
    [UltimoInicio]  datetime      NULL,
    [UltimoFim]     datetime      NULL,
    [Linhas]        int           NULL,
    [Erro]          varchar(1000) NULL,
    CONSTRAINT [PK_LV_Sincronizacao] PRIMARY KEY ([Tabela])
);
GO

SET NOEXEC OFF;
GO
SELECT name AS TabelaCriada FROM sys.tables WHERE name IN ('LV_Dre', 'LV_Precificacao', 'LV_Sincronizacao') ORDER BY name;
