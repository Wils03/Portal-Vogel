-- Banco do Portal Vogel no SQL Server local + login próprio do portal.
-- Rodar UMA vez no SQL Server Management Studio, conectado como administrador (ex.: sa).
-- Antes de rodar, troque DEFINA_UMA_SENHA_FORTE pela senha que você escolher (e guarde num lugar seguro).
-- O login só enxerga o PORTAL_VG: não tem acesso ao BASE nem a outros bancos.

CREATE DATABASE PORTAL_VG;
GO

CREATE LOGIN portal_vogel
    WITH PASSWORD = 'DEFINA_UMA_SENHA_FORTE',
         CHECK_POLICY = ON,
         DEFAULT_DATABASE = PORTAL_VG;
GO

USE PORTAL_VG;
GO

CREATE USER portal_vogel FOR LOGIN portal_vogel;
-- db_owner só dentro do PORTAL_VG: o portal cria e atualiza as próprias tabelas (migrations)
ALTER ROLE db_owner ADD MEMBER portal_vogel;
GO
