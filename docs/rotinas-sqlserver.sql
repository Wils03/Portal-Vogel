-- Rotinas do Portal Vogel para o banco do sistema dos clientes (SQL Server, compatibilidade 100).
-- Tabelas: Nfe_download (NF-e de fornecedores), Nfe_status (situação das notas emitidas),
--          [Documentos Fiscais] (notas emitidas), [Log de whatsapp] (envios do próprio sistema).
-- Nfe_status.Codigosituacao: 0/1 aguardando retorno, 2/3 autorizada, 4 cancelada, 5 rejeitada, 6 inutilizada.
-- Nfe_download.Importado = 0 -> nota sem entrada lançada (conferido contra Entradas.Numeronfe = 'NFe' + chave).
-- No portal cada consulta é colada sem ';' final.

-- ===== Notas de fornecedores não lançadas =====
-- NF-e de fornecedores baixadas da SEFAZ (Nfe_download), autorizadas, há mais de 3 dias sem entrada lançada no sistema.
-- Severidade: Crítico | Alerta quando > 0 | Horário: 07:00
-- Mensagem: Olá {cliente}! Identificamos {quantidade} nota(s) de fornecedor autorizada(s) na SEFAZ que ainda não foram lançadas no sistema. Favor revisar as entradas pendentes. Qualquer dúvida estamos à disposição. Equipe Vogel
SELECT d.Filial, d.Nome AS Fornecedor, d.Cnpj, d.[Data de Emissao] AS Emissao,
       DATEDIFF(day, d.[Data de Emissao], GETDATE()) AS Dias, d.[Valor nf] AS Valor, d.Chavenota AS Chave
FROM Nfe_download d
WHERE d.Tipo <> ''
  AND d.Situacao_nfe = 1
  AND d.Importado = 0
  AND d.[Data de Emissao] >= DATEADD(day, -60, GETDATE())
  AND d.[Data de Emissao] <  DATEADD(day, -3, GETDATE())
ORDER BY d.[Data de Emissao];

-- ===== Notas emitidas rejeitadas pela SEFAZ =====
-- NF-e/NFC-e emitidas cuja situação atual é rejeitada (Nfe_status.Codigosituacao = 5) nos últimos 30 dias.
-- Severidade: Crítico | Alerta quando > 0 | Horário: 07:00
-- Mensagem: Olá {cliente}! Existem {quantidade} nota(s) emitida(s) rejeitada(s) pela SEFAZ. Elas precisam ser corrigidas e transmitidas novamente. Favor revisar. Equipe Vogel
SELECT s.Filial, CASE s.[Codigo do tipo de documen] WHEN 'C' THEN 'NFC-e' WHEN 'N' THEN 'NF-e' ELSE s.[Codigo do tipo de documen] END AS Tipo,
       s.[Numero do documento] AS Documento, s.Datasituacao AS Data, s.Cstat, CAST(s.Xmotivo AS varchar(300)) AS Motivo
FROM Nfe_status s
WHERE s.Codigosituacao = 5
  AND COALESCE(s.Datasituacao, s.Rdatainclusao, s.Rdata) >= DATEADD(day, -30, GETDATE())
ORDER BY s.Datasituacao DESC;

-- ===== Notas emitidas aguardando retorno da SEFAZ =====
-- NF-e/NFC-e enviadas sem retorno (Codigosituacao 0 ou 1) há mais de 2 horas, nos últimos 30 dias.
-- Severidade: Atenção | Alerta quando > 0 | Horário: 08:00
-- Mensagem: Olá {cliente}! Há {quantidade} nota(s) enviada(s) à SEFAZ ainda aguardando retorno. Favor consultar o status no sistema. Equipe Vogel
SELECT s.Filial, CASE s.[Codigo do tipo de documen] WHEN 'C' THEN 'NFC-e' WHEN 'N' THEN 'NF-e' ELSE s.[Codigo do tipo de documen] END AS Tipo,
       s.[Numero do documento] AS Documento, COALESCE(s.Datasituacao, s.Rdatainclusao, s.Rdata) AS Data, CAST(s.Xmotivo AS varchar(300)) AS Motivo
FROM Nfe_status s
WHERE s.Codigosituacao IN (0, 1)
  AND COALESCE(s.Datasituacao, s.Rdatainclusao, s.Rdata) >= DATEADD(day, -30, GETDATE())
  AND COALESCE(s.Datasituacao, s.Rdatainclusao, s.Rdata) <  DATEADD(hour, -2, GETDATE());

-- ===== Notas emitidas não transmitidas =====
-- Documentos fiscais NF-e/NFC-e gerados (Situacao = 'G') até ontem, nos últimos 30 dias, sem nenhum registro de transmissão em Nfe_status.
-- Severidade: Crítico | Alerta quando > 0 | Horário: 07:00
-- Mensagem: Olá {cliente}! Encontramos {quantidade} nota(s) emitida(s) que não foram transmitidas para a SEFAZ. Favor verificar e transmitir. Equipe Vogel
SELECT df.Filial, CASE df.[Codigo do tipo de documen] WHEN 'C' THEN 'NFC-e' ELSE 'NF-e' END AS Tipo,
       df.[Numero do documento] AS Documento, df.[Data de Emissao] AS Emissao, df.[Valor Total] AS Valor
FROM [Documentos Fiscais] df
WHERE df.[Codigo do tipo de documen] IN ('N', 'C')
  AND df.Situacao = 'G'
  AND df.[Data de Emissao] >= DATEADD(day, -30, GETDATE())
  AND df.[Data de Emissao] <  CAST(CONVERT(varchar(10), GETDATE(), 112) AS datetime)
  AND NOT EXISTS (SELECT 1 FROM Nfe_status s
                  WHERE s.Filial = df.Filial
                    AND s.[Codigo do tipo de documen] = df.[Codigo do tipo de documen]
                    AND s.[Numero do documento] = df.[Numero do documento])
ORDER BY df.[Data de Emissao];

-- ===== WhatsApp do sistema não enviado =====
-- Mensagens do próprio sistema do cliente (Log de whatsapp) pendentes ou não enviadas nos últimos 7 dias.
-- Severidade: Informativo | Alerta quando > 5 | Horário: 09:00
-- Mensagem: Olá {cliente}! O sistema tem {quantidade} mensagem(ns) de WhatsApp pendente(s) ou não enviada(s) nos últimos dias. Vale conferir a conexão do WhatsApp. Equipe Vogel
SELECT w.Data, w.[Status envio] AS Status, w.[Origem contato] AS Origem, w.[Celular destinatario] AS Celular
FROM [Log de whatsapp] w
WHERE w.[Status envio] <> 'Enviado'
  AND w.Data >= DATEADD(day, -7, GETDATE())
ORDER BY w.Data DESC;
