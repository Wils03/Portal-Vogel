-- =====================================================================================
-- PASSO 3 — LOGINS DA VOGEL NO SQL DO CLIENTE (no lugar do sa), com o mínimo de permissão
--   vogel_sincronizador → usado pelo SincronizadorLV (no servidor do cliente)
--       BASE (ERP): só leitura.            BASELV: só LV_Dre / LV_Precificacao / LV_Rotina / LV_Sincronizacao.
--   vogel_portal        → usado pelo Portal Vogel (string de conexão do cadastro do cliente)
--       BASE (ERP): só leitura (para clientes com rotinas ainda direto na BASE).  BASELV: só leitura das tabelas LV_.
-- Pode rodar mais de uma vez: só cria o que falta e reaplica as permissões. Não mexe em mais nada.
-- ANTES DE RODAR: troque as duas senhas abaixo (e guarde num lugar seguro). Ajuste @BaseErp se preciso.
-- Rodar no SSMS como administrador (ex.: sa), DEPOIS do passo 2. Guia: docs/implantacao/GUIA.md
-- =====================================================================================
USE master;
GO
SET NOCOUNT ON;
DECLARE @BaseErp sysname = N'BASE';
DECLARE @SenhaSincronizador nvarchar(128) = N'DEFINA_UMA_SENHA_FORTE_1';
DECLARE @SenhaPortal        nvarchar(128) = N'DEFINA_UMA_SENHA_FORTE_2';
DECLARE @CriarLoginPortal   bit = 1;   -- 0 = não criar o login do portal

IF @SenhaSincronizador LIKE N'DEFINA_UMA_SENHA%' OR (@CriarLoginPortal = 1 AND @SenhaPortal LIKE N'DEFINA_UMA_SENHA%')
BEGIN
    RAISERROR('Troque as senhas no início do script antes de rodar. Nada foi criado.', 16, 1);
    RETURN;
END
IF DB_ID(@BaseErp) IS NULL OR OBJECT_ID(N'BASELV.dbo.LV_Sincronizacao') IS NULL OR OBJECT_ID(N'BASELV.dbo.LV_Rotina') IS NULL OR OBJECT_ID(N'BASELV.dbo.LV_Pedido') IS NULL OR OBJECT_ID(N'BASELV.dbo.LV_Comercial') IS NULL OR OBJECT_ID(N'BASELV.dbo.LV_Feriado') IS NULL OR OBJECT_ID(N'BASELV.dbo.LV_Cota') IS NULL
BEGIN
    RAISERROR('Rode antes o passo 2 (01-criar-baselv.sql) e confira @BaseErp. Nada foi criado.', 16, 1);
    RETURN;
END

DECLARE @sql nvarchar(max);

-- ---------- vogel_sincronizador ----------
IF SUSER_ID(N'vogel_sincronizador') IS NULL
BEGIN
    SET @sql = N'CREATE LOGIN vogel_sincronizador WITH PASSWORD = N''' + REPLACE(@SenhaSincronizador, N'''', N'''''') + N''', CHECK_POLICY = ON, DEFAULT_DATABASE = BASELV;';
    EXEC (@sql);
    PRINT 'Login criado: vogel_sincronizador';
END
SET @sql = N'USE ' + QUOTENAME(@BaseErp) + N';
    IF USER_ID(N''vogel_sincronizador'') IS NULL CREATE USER vogel_sincronizador FOR LOGIN vogel_sincronizador;
    EXEC sp_addrolemember N''db_datareader'', N''vogel_sincronizador'';';
EXEC (@sql);
SET @sql = N'USE BASELV;
    IF USER_ID(N''vogel_sincronizador'') IS NULL CREATE USER vogel_sincronizador FOR LOGIN vogel_sincronizador;
    GRANT SELECT, INSERT, DELETE ON dbo.LV_Dre TO vogel_sincronizador;
    GRANT SELECT, INSERT, DELETE ON dbo.LV_Precificacao TO vogel_sincronizador;
    GRANT SELECT, INSERT, DELETE ON dbo.LV_Rotina TO vogel_sincronizador;
    GRANT SELECT, INSERT, DELETE ON dbo.LV_Comercial TO vogel_sincronizador;
    GRANT SELECT, INSERT, DELETE ON dbo.LV_Feriado TO vogel_sincronizador;
    GRANT SELECT, INSERT, DELETE ON dbo.LV_Cota TO vogel_sincronizador;
    GRANT SELECT, UPDATE ON dbo.LV_Pedido TO vogel_sincronizador;
    GRANT SELECT, INSERT, UPDATE ON dbo.LV_Sincronizacao TO vogel_sincronizador;';
EXEC (@sql);

-- ---------- vogel_portal ----------
IF @CriarLoginPortal = 1
BEGIN
    IF SUSER_ID(N'vogel_portal') IS NULL
    BEGIN
        SET @sql = N'CREATE LOGIN vogel_portal WITH PASSWORD = N''' + REPLACE(@SenhaPortal, N'''', N'''''') + N''', CHECK_POLICY = ON, DEFAULT_DATABASE = ' + QUOTENAME(@BaseErp) + N';';
        EXEC (@sql);
        PRINT 'Login criado: vogel_portal';
    END
    SET @sql = N'USE ' + QUOTENAME(@BaseErp) + N';
        IF USER_ID(N''vogel_portal'') IS NULL CREATE USER vogel_portal FOR LOGIN vogel_portal;
        EXEC sp_addrolemember N''db_datareader'', N''vogel_portal'';';
    EXEC (@sql);
    SET @sql = N'USE BASELV;
        IF USER_ID(N''vogel_portal'') IS NULL CREATE USER vogel_portal FOR LOGIN vogel_portal;
        GRANT SELECT ON dbo.LV_Dre TO vogel_portal;
        GRANT SELECT ON dbo.LV_Precificacao TO vogel_portal;
        GRANT SELECT ON dbo.LV_Rotina TO vogel_portal;
        GRANT SELECT ON dbo.LV_Comercial TO vogel_portal;
        GRANT SELECT ON dbo.LV_Feriado TO vogel_portal;
        GRANT SELECT ON dbo.LV_Cota TO vogel_portal;
        GRANT SELECT, INSERT ON dbo.LV_Pedido TO vogel_portal;   -- botão "Sincronizar agora": só registra o pedido
        GRANT SELECT ON dbo.LV_Sincronizacao TO vogel_portal;';
    EXEC (@sql);
END

-- Resultado esperado: vogel_sincronizador com SELECT/INSERT/DELETE (e UPDATE na LV_Sincronizacao);
-- vogel_portal só com SELECT (e INSERT na LV_Pedido). Nenhuma outra tabela da BASELV aparece.
SELECT dp.name AS Usuario, o.name AS TabelaBASELV, p.permission_name AS Permissao
FROM BASELV.sys.database_permissions p
JOIN BASELV.sys.database_principals dp ON dp.principal_id = p.grantee_principal_id
JOIN BASELV.sys.objects o ON o.object_id = p.major_id
WHERE dp.name IN (N'vogel_sincronizador', N'vogel_portal')
ORDER BY 1, 2, 3;
GO
