# Mapa do banco BASE (sistema dos clientes)

SQL Server, nível de compatibilidade 100 (SQL 2008): não usar `STRING_AGG`, `TRY_CONVERT`, `IIF`, `CONCAT_WS`.
550 tabelas, 236 com dados. A base de teste tem dados até 29/05/2025.

> As regras de "pendência" e "problema" ficam no programa (ERP), não gravadas no banco.
> As rotinas do portal recriam essas regras em SQL; confirmar cada regra com o comportamento das telas do sistema.

## Por assunto (tabelas com mais dados)

| Assunto | Tabelas principais | Uso no portal |
|---|---|---|
| Fiscal (NF-e/NFC-e) | `Documentos Fiscais` (800 mil), `Itens dos Documentos Fisc` (2,1 mi), `Nfe_status`, `Nfe_download`, `Documentos fiscais bloqueios`, `Itens dos df cancelados` | Rotinas de notas (feitas) |
| Entradas / compras | `Entradas`, `Entradasitens`, `Entradas processos`, `Ordens de Compra`, `Ordens de compra itens`, `Produtos_fornecedores` | Notas não lançadas (feito), OC em aberto |
| Estoque / produtos | `Produtos`, `Produtos_dados`, `Movimento de Produtos` (1,3 mi), `Inventario`, `Produtoscustosantigos`, `Produtosprecosantigos` | Estoque negativo, giro, curva ABC |
| Preços | `Precos`, `Precos sugeridos`, `Precos promocao`, `Tipo custo formacao preco` | **Precificação** (custo x preço) |
| Financeiro | `Titulos do Contas a Receb` (826 mil), `Lancamento titulos cr` (2,2 mi), `Titulos do Contas a Pagar`, `Plano de Contas`, `Encerramento financeiro` | Inadimplência, **DRE** |
| Custos fixos | `Despesas mensais`, `Despesas mensais valores`, `Centro de custo` | **Ponto de equilíbrio** |
| Caixa / vendas | `Saldos do Caixa`, `Lancamentos do Caixa`, `Lancamentos Comissoes`, `Cotas de Vendas` | Faturamento, metas |
| Pessoas / CRM | `Pessoas`, `Pessoascontatos`, `Pessoas limite de credito` | Contatos para mensagens |
| Comunicação | `Log de whatsapp`, `Log de email`, `Mensagens Padroes` | Rotina WhatsApp (feita) |
| Logística | `Controle entregas`, `Controle entregas romaneio`, `Controle entregas movimento` (1,1 mi cada) | Entregas atrasadas |
| Configuração / logs | `Configuracoes da Empresa`, `Usuarios`, `Sessao`, `Logsql` (4,6 mi), `Versoes bd` | Versão do sistema do cliente |

## Códigos já conhecidos

- `Nfe_status.Codigosituacao`: 0/1 aguardando retorno · 2/3 autorizada · 4 cancelada · 5 rejeitada · 6 inutilizada
- `[Codigo do tipo de documen]`: `N` NF-e · `C` NFC-e
- `Documentos Fiscais.Situacao`: `G` gerada · `C` cancelada · `F` (a confirmar)
- `Nfe_download.Importado = 0`: sem entrada lançada (chave em `Entradas.Numeronfe` = `'NFe' + Chavenota`)
- `Nfe_download.Situacao_nfe`: 1 autorizada · 3 cancelada; `Tipo = ''` são eventos, não notas
