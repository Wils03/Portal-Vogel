-- DRE lida da BASELV (resultado pronto, gravado todo dia pelo SincronizadorLV no servidor do cliente).
-- Não consulta as tabelas do ERP: só a tabela LV_Dre. A conexão do cliente continua apontando para o servidor dele.
SELECT Competencia, Grupo, Conta, Valor
FROM BASELV.dbo.LV_Dre
WHERE Competencia >= {inicio} AND Competencia < {fim}
