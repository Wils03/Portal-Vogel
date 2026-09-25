-- =====================================================================================
-- PASSO 2 — CRIAR A BASELV E AS TABELAS DO PORTAL
-- Pode rodar mais de uma vez: só cria o que falta. NÃO mexe em tabelas que já existam na BASELV
-- (ex.: no cliente piloto já havia tabelas antigas de outro projeto) e NÃO toca na BASE do ERP.
-- Ajuste @BaseErp se o banco do ERP não se chamar BASE. Rodar no SSMS como administrador (ex.: sa).
-- Guia: docs/implantacao/GUIA.md
-- =====================================================================================
USE master;
GO
SET NOCOUNT ON;
DECLARE @BaseErp sysname = N'BASE';
DECLARE @collation sysname = CONVERT(sysname, DATABASEPROPERTYEX(@BaseErp, 'Collation'));

IF @collation IS NULL
BEGIN
    RAISERROR('Banco do ERP "%s" não encontrado. Ajuste @BaseErp. Nada foi criado.', 16, 1, @BaseErp);
    SET NOEXEC ON;
END
ELSE IF DB_ID(N'BASELV') IS NULL
BEGIN
    -- Mesma collation do ERP: evita "collation conflict" ao comparar textos entre os dois bancos
    DECLARE @sql nvarchar(400) = N'CREATE DATABASE [BASELV] COLLATE ' + @collation;
    EXEC (@sql);
    PRINT 'BASELV criada com a collation ' + @collation + '.';
END
ELSE IF CONVERT(sysname, DATABASEPROPERTYEX(N'BASELV', 'Collation')) <> @collation
BEGIN
    RAISERROR('A BASELV já existe com collation diferente da BASE. Nada foi criado: fale com a Vogel.', 16, 1);
    SET NOEXEC ON;
END
ELSE
    PRINT 'BASELV já existia (collation igual à do ERP). Só as tabelas que faltarem serão criadas.';
GO

USE BASELV;
GO

-- DRE: uma linha por mês, grupo e conta (resultado do SQL da DRE). O sincronizador recalcula o ano atual e o anterior.
IF OBJECT_ID(N'dbo.LV_Dre') IS NULL
BEGIN
    CREATE TABLE dbo.[LV_Dre] (
        [Competencia]  date          NOT NULL,  -- 1º dia do mês
        [Grupo]        varchar(30)   NOT NULL,  -- ReceitaBruta, Deducoes, Cmv, DespesasOperacionais, DespesasFinanceiras...
        [Conta]        varchar(150)  NOT NULL,
        [Valor]        decimal(18,4) NOT NULL,
        [AtualizadoEm] datetime      NOT NULL,
        CONSTRAINT [PK_LV_Dre] PRIMARY KEY ([Competencia], [Grupo], [Conta])
    );
    PRINT 'Criada: LV_Dre';
END

-- Precificação: uma linha por filial e produto (resultado do SQL da Precificação).
IF OBJECT_ID(N'dbo.LV_Precificacao') IS NULL
BEGIN
    CREATE TABLE dbo.[LV_Precificacao] (
        [Filial]           int           NOT NULL,
        [Codigo]           varchar(60)   NOT NULL,
        [Descricao]        varchar(255)  NULL,
        [Custo]            decimal(18,4) NULL,
        [Preco]            decimal(18,4) NULL,
        [QtdMes]           decimal(18,4) NULL,  -- média mensal vendida nos últimos 90 dias
        [ImpostosPct]      decimal(18,4) NULL,
        [GrupoPreco]       varchar(255)  NULL,
        [LucroDesejadoPct] decimal(18,4) NULL,
        [LucroSobre]       varchar(20)   NULL,  -- como vem do ERP ("Custo" / "Preço de Venda")
        [AtualizadoEm]     datetime      NOT NULL,
        CONSTRAINT [PK_LV_Precificacao] PRIMARY KEY ([Filial], [Codigo])
    );
    PRINT 'Criada: LV_Precificacao';
END

-- Comercial: uma linha por DIA e vendedor (estrutura de venda). O sincronizador recalcula o ano atual e o anterior.
IF OBJECT_ID(N'dbo.LV_Comercial') IS NULL
BEGIN
    CREATE TABLE dbo.[LV_Comercial] (
        [Data]         date          NOT NULL,
        [Vendedor]     varchar(20)   NOT NULL,  -- código da estrutura de venda do documento
        [NomeVendedor] varchar(100)  NULL,
        [Gerente]      varchar(20)   NULL,      -- estrutura "pai" do vendedor
        [NomeGerente]  varchar(100)  NULL,
        [VendaBruta]   decimal(18,4) NOT NULL,
        [Devolucoes]   decimal(18,4) NOT NULL,
        [Cmv]          decimal(18,4) NOT NULL,  -- quantidade × "Custo total" do item
        [Documentos]   int           NOT NULL,  -- número de vendas (documentos)
        [Itens]        int           NOT NULL,  -- itens vendidos (linhas)
        [AtualizadoEm] datetime      NOT NULL,
        [Permuta]      bit           NOT NULL CONSTRAINT [DF_LV_Comercial_Permuta] DEFAULT 0, -- local de pagamento PERMUTA
        CONSTRAINT [PK_LV_Comercial] PRIMARY KEY ([Data], [Vendedor], [Permuta])
    );
    PRINT 'Criada: LV_Comercial';
END
ELSE IF COL_LENGTH(N'dbo.LV_Comercial', N'Permuta') IS NULL
BEGIN
    -- BASELV criada antes da coluna Permuta: acrescenta a coluna e refaz a chave (os dados ficam; a próxima sincronização completa)
    ALTER TABLE dbo.[LV_Comercial] ADD [Permuta] bit NOT NULL CONSTRAINT [DF_LV_Comercial_Permuta] DEFAULT 0;
    ALTER TABLE dbo.[LV_Comercial] DROP CONSTRAINT [PK_LV_Comercial];
    EXEC (N'ALTER TABLE dbo.[LV_Comercial] ADD CONSTRAINT [PK_LV_Comercial] PRIMARY KEY ([Data], [Vendedor], [Permuta])');
    PRINT 'Atualizada: LV_Comercial (coluna Permuta)';
END

-- Metas (cotas) de venda por vendedor e mês, da tabela Cotas de Vendas do ERP.
IF OBJECT_ID(N'dbo.LV_Cota') IS NULL
BEGIN
    CREATE TABLE dbo.[LV_Cota] (
        [Competencia]  date          NOT NULL,  -- 1º dia do mês
        [Vendedor]     varchar(20)   NOT NULL,
        [Meta]         decimal(18,4) NOT NULL,
        [AtualizadoEm] datetime      NOT NULL,
        CONSTRAINT [PK_LV_Cota] PRIMARY KEY ([Competencia], [Vendedor])
    );
    PRINT 'Criada: LV_Cota';
END

-- Feriados do ERP (fixos já transformados em datas): dias úteis do Comercial = segunda a sábado, menos feriados.
IF OBJECT_ID(N'dbo.LV_Feriado') IS NULL
BEGIN
    CREATE TABLE dbo.[LV_Feriado] (
        [Data]      date         NOT NULL,
        [Descricao] varchar(100) NULL,
        CONSTRAINT [PK_LV_Feriado] PRIMARY KEY ([Data])
    );
    PRINT 'Criada: LV_Feriado';
END

-- Rotinas/alertas: uma linha por rotina do portal (RotinaId = Id da rotina no portal) com o último resultado do SQL dela.
-- O portal lê daqui (clientes com "Rotinas pela BASELV") em vez de consultar a BASE.
IF OBJECT_ID(N'dbo.LV_Rotina') IS NULL
BEGIN
    CREATE TABLE dbo.[LV_Rotina] (
        [RotinaId]    int            NOT NULL,
        [Nome]        varchar(150)   NULL,
        [ExecutadoEm] datetime       NOT NULL,
        [Quantidade]  decimal(18,4)  NULL,      -- 1×1 numérico (ex.: COUNT) = o próprio número; senão, quantidade de linhas
        [Linhas]      int            NULL,
        [Amostra]     nvarchar(max)  NULL,      -- até 50 linhas em JSON (o mesmo detalhe que o alerta mostra)
        [Erro]        varchar(1000)  NULL,      -- preenchido se o SQL da rotina falhou no ERP
        CONSTRAINT [PK_LV_Rotina] PRIMARY KEY ([RotinaId])
    );
    PRINT 'Criada: LV_Rotina';
END

-- Pedidos de "Sincronizar agora" feitos no portal. O portal só insere aqui (única gravação dele no servidor do cliente);
-- o SincronizadorLV confere a cada poucos minutos, roda e marca como concluído.
IF OBJECT_ID(N'dbo.LV_Pedido') IS NULL
BEGIN
    CREATE TABLE dbo.[LV_Pedido] (
        [Id]          int IDENTITY(1,1) NOT NULL,
        [PedidoEm]    datetime      NOT NULL CONSTRAINT [DF_LV_Pedido_PedidoEm] DEFAULT (GETDATE()),
        [PedidoPor]   varchar(256)  NULL,
        [Status]      varchar(20)   NOT NULL CONSTRAINT [DF_LV_Pedido_Status] DEFAULT ('Pendente'),  -- Pendente, Executando, Concluido, Erro
        [IniciadoEm]  datetime      NULL,
        [ConcluidoEm] datetime      NULL,
        [Erro]        varchar(1000) NULL,
        CONSTRAINT [PK_LV_Pedido] PRIMARY KEY ([Id])
    );
    PRINT 'Criada: LV_Pedido';
END

-- Controle: uma linha por item sincronizado (Dre, Precificacao, Rotinas): última execução, linhas e erro.
IF OBJECT_ID(N'dbo.LV_Sincronizacao') IS NULL
BEGIN
    CREATE TABLE dbo.[LV_Sincronizacao] (
        [Tabela]        varchar(60)   NOT NULL,
        [UltimoInicio]  datetime      NULL,
        [UltimoFim]     datetime      NULL,
        [Linhas]        int           NULL,
        [Erro]          varchar(1000) NULL,
        CONSTRAINT [PK_LV_Sincronizacao] PRIMARY KEY ([Tabela])
    );
    PRINT 'Criada: LV_Sincronizacao';
END
GO

SET NOEXEC OFF;
GO
-- Resultado esperado: as 8 tabelas listadas.
SELECT name AS TabelaDoPortal, create_date AS CriadaEm
FROM BASELV.sys.tables WHERE name IN (N'LV_Dre', N'LV_Precificacao', N'LV_Comercial', N'LV_Cota', N'LV_Feriado', N'LV_Rotina', N'LV_Pedido', N'LV_Sincronizacao') ORDER BY name;
