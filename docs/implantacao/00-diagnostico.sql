-- =====================================================================================
-- PASSO 1 — DIAGNÓSTICO DO SERVIDOR DO CLIENTE (somente leitura; não cria nem altera nada)
-- Rodar no SSMS conectado ao servidor do cliente. Ajuste @BaseErp se o banco do ERP não se chamar BASE.
-- Mostra só nomes de objetos, contagens e datas — nenhum dado de produto, cliente ou valor.
-- Guia: docs/implantacao/GUIA.md
-- =====================================================================================
SET NOCOUNT ON;
DECLARE @BaseErp sysname = N'BASE';

-- 1) Servidor
SELECT @@SERVERNAME AS Servidor,
       CONVERT(varchar(20), SERVERPROPERTY('ProductVersion')) AS Versao,
       CONVERT(varchar(60), SERVERPROPERTY('Edition')) AS Edicao,
       CONVERT(sysname, SERVERPROPERTY('Collation')) AS CollationDoServidor;

-- 2) Bancos
SELECT d.name AS Banco, d.collation_name AS Collation, d.compatibility_level AS Compatibilidade, d.state_desc AS Estado,
       CASE WHEN d.name = @BaseErp THEN 'ERP (origem)' ELSE 'BASELV (destino)' END AS Papel
FROM sys.databases d
WHERE d.name IN (@BaseErp, N'BASELV');
IF DB_ID(@BaseErp) IS NULL RAISERROR('Banco do ERP "%s" não encontrado neste servidor. Ajuste @BaseErp.', 16, 1, @BaseErp);
IF DB_ID(N'BASELV') IS NULL PRINT 'BASELV ainda não existe: o passo 2 (01-criar-baselv.sql) vai criá-la.';

-- 3) Tabelas do ERP que o portal/sincronizador usam
IF DB_ID(@BaseErp) IS NOT NULL
BEGIN
    SELECT t.Uso, t.Tabela,
           CASE WHEN OBJECT_ID(QUOTENAME(@BaseErp) + N'.dbo.' + QUOTENAME(t.Tabela)) IS NULL THEN 'FALTANDO' ELSE 'ok' END AS Situacao
    FROM (VALUES
        ('DRE / Precificação', N'Documentos Fiscais'), ('DRE / Precificação', N'Itens dos Documentos Fisc'),
        ('DRE', N'Titulos do Contas a Pagar'), ('DRE', N'Centro de custo'),
        ('DRE (regra de faturamento)', N'Condicoes de Faturamento'), ('DRE (regra de faturamento)', N'Tipos de Documentos Fisca'),
        ('DRE (regra de faturamento)', N'Descricao dos Codigos Fis'),
        ('Precificação', N'Produtos'), ('Precificação', N'Produtos_dados'), ('Precificação', N'Precos'),
        ('Precificação', N'Grupos formacao preco'), ('Precificação', N'Grupo form preco itens'), ('Comercial (vendedores)', N'Estrutura de Vendas'), ('Comercial (metas)', N'Cotas de Vendas'), ('Comercial (dias úteis)', N'Feriados fixos'), ('Comercial (permuta)', N'Locais de Pagamento'), ('Comercial (dias úteis)', N'Feriados moveis'),
        ('Rotinas', N'Nfe_download'), ('Rotinas', N'Nfe_status'), ('Rotinas', N'Log de whatsapp'), ('Rotinas', N'Entradas')
    ) t (Uso, Tabela)
    ORDER BY CASE WHEN OBJECT_ID(QUOTENAME(@BaseErp) + N'.dbo.' + QUOTENAME(t.Tabela)) IS NULL THEN 0 ELSE 1 END, t.Uso, t.Tabela;

    -- 4) Filiais com venda nos últimos 90 dias (para a configuração "Filiais" do sincronizador)
    DECLARE @sql nvarchar(max) = N'
        SELECT d.Filial, COUNT(*) AS DocumentosUltimos90Dias, MAX(d.[Data de Emissao]) AS UltimaVenda
        FROM ' + QUOTENAME(@BaseErp) + N'.dbo.[Documentos Fiscais] d WITH (NOLOCK)
        WHERE d.Situacao = ''G'' AND d.[Codigo do tipo de documen] IN (''N'', ''C'', ''V'', ''VA'')
          AND d.[Data de Emissao] >= DATEADD(day, -90, GETDATE())
        GROUP BY d.Filial ORDER BY d.Filial';
    IF OBJECT_ID(QUOTENAME(@BaseErp) + N'.dbo.[Documentos Fiscais]') IS NOT NULL EXEC (@sql);
END

-- 5) BASELV: tabelas do portal e logins da Vogel
IF DB_ID(N'BASELV') IS NOT NULL
    SELECT t.Tabela, CASE WHEN OBJECT_ID(N'BASELV.dbo.' + t.Tabela) IS NULL THEN 'não existe (passo 2 cria)' ELSE 'ok' END AS Situacao
    FROM (VALUES (N'LV_Dre'), (N'LV_Precificacao'), (N'LV_Comercial'), (N'LV_Cota'), (N'LV_Feriado'), (N'LV_Rotina'), (N'LV_Pedido'), (N'LV_Sincronizacao')) t (Tabela);

SELECT n.Login, CASE WHEN SUSER_ID(n.Login) IS NULL THEN 'não existe (passo 3 cria)' ELSE 'ok' END AS Situacao
FROM (VALUES (N'vogel_sincronizador'), (N'vogel_portal')) n (Login);
