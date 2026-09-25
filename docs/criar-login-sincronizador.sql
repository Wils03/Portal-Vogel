-- SUBSTITUÍDO por docs/implantacao/02-criar-logins.sql (logins do sincronizador e do portal).
-- Login próprio do SincronizadorLV (no lugar do sa), com o mínimo de permissão:
--   BASE   (ERP): só leitura (db_datareader). Não pode alterar nada.
--   BASELV       : só as 3 tabelas do portal (LV_Dre, LV_Precificacao, LV_Sincronizacao): ler, inserir, apagar e atualizar.
--                  Não enxerga nem mexe nas outras tabelas da BASELV.
-- Rodar UMA vez no SSMS do servidor do cliente, conectado como administrador (ex.: sa).
-- Antes de rodar, troque DEFINA_UMA_SENHA_FORTE pela senha escolhida. Guarde-a só no appsettings.json do sincronizador.

USE master;
GO
CREATE LOGIN vogel_sincronizador
    WITH PASSWORD = 'DEFINA_UMA_SENHA_FORTE',
         CHECK_POLICY = ON,
         DEFAULT_DATABASE = BASELV;
GO

USE BASE;
GO
CREATE USER vogel_sincronizador FOR LOGIN vogel_sincronizador;
EXEC sp_addrolemember 'db_datareader', 'vogel_sincronizador';  -- funciona em qualquer nível de compatibilidade (a BASE é 100)
GO

USE BASELV;
GO
CREATE USER vogel_sincronizador FOR LOGIN vogel_sincronizador;
GRANT SELECT, INSERT, DELETE ON dbo.LV_Dre TO vogel_sincronizador;
GRANT SELECT, INSERT, DELETE ON dbo.LV_Precificacao TO vogel_sincronizador;
GRANT SELECT, INSERT, UPDATE ON dbo.LV_Sincronizacao TO vogel_sincronizador;
GO

-- Conferência: deve listar as permissões acima
SELECT USER_NAME(p.grantee_principal_id) AS Usuario, OBJECT_NAME(p.major_id) AS Tabela, p.permission_name AS Permissao
FROM BASELV.sys.database_permissions p
WHERE USER_NAME(p.grantee_principal_id) = 'vogel_sincronizador' AND p.major_id > 0
ORDER BY 2, 3;
