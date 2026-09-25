# Vogel SincronizadorLV

Programa que roda no servidor do cliente. Todo dia, na virada da data, ele roda na **BASE do ERP** os mesmos SQLs que o Portal Vogel usa (DRE e Precificação) e grava **só o resultado** na **BASELV**:

| Tabela na BASELV | Conteúdo |
|---|---|
| `LV_Dre` | uma linha por mês, grupo e conta. O programa recalcula o ano atual e o anterior (`DreAnos`). |
| `LV_Precificacao` | uma linha por filial e produto (`Filiais`) |
| `LV_Comercial` | uma linha por **dia** e vendedor (estrutura de venda do documento): venda bruta, devoluções, CMV (quantidade × "Custo total"), número de vendas e itens. O portal usa no relatório de metas (tela Comercial) |
| `LV_Cota` | metas (cotas) de venda por vendedor e mês, da tabela Cotas de Vendas do ERP |
| `LV_Feriado` | feriados do ERP (fixos dd/mm já em datas, de 3 anos atrás até o ano que vem, mais os móveis): dias úteis = segunda a sábado, menos feriados |
| `LV_Rotina` | uma linha por rotina do portal (`consultas/rotinas/rotina-<Id>.sql`, exportadas pelo portal): quantidade, amostra de até 50 linhas e erro. O portal lê daqui para clientes com "Rotinas e alertas pela BASELV" |
| `LV_Sincronizacao` | controle: última execução, quantidade de linhas e erro de cada item |

- Na BASE, só roda `SELECT`, dentro de uma transação que sempre é desfeita, com a mesma trava de leitura do portal. Grava apenas na BASELV.
- Se origem e destino forem o mesmo banco, ele se recusa a rodar.
- **Implantação num cliente:** siga o [guia passo a passo](../../docs/implantacao/GUIA.md). Os scripts de diagnóstico, criação da BASELV, logins e conferência, e o gerador de pacote, ficam em `docs/implantacao/`.
- O portal lê esses resultados com os modelos "Usar BASELV (recomendado)" nas telas DRE e Precificação: `SELECT ... FROM BASELV.dbo.LV_Dre / LV_Precificacao`.

## Configuração (`appsettings.json`)
- `ConnectionStrings:Origem` é a BASE do ERP, e `ConnectionStrings:Destino` é a BASELV.
- Pode usar o login do Windows (`Integrated Security=True`) ou um login SQL (`User Id=...;Password=...`). A senha fica só nesse arquivo, no servidor do cliente.
- `Sincronizacao:ConsultaDre` e `ConsultaPrecificacao` apontam para os arquivos em `consultas/`.
  - O padrão são os modelos do portal.
  - Cliente com SQL próprio: copie o SQL salvo no cadastro dele. Por exemplo, o cliente piloto usa [docs/dre-propria-tabelas-sqlserver.sql](../../docs/dre-propria-tabelas-sqlserver.sql), que segue as regras da `viewFaturamentoLV` direto nas tabelas.
- `DreAnos` (padrão 2), `Filiais` (padrão `[1]`), `TempoLimiteSegundos`, `PastaCsv` (vazio = não gera CSV).

## Executar
```
SincronizadorLV.exe                          DRE + Precificação
SincronizadorLV.exe --item Dre               só a DRE (ou --item Precificacao, --item Comercial, --item Rotinas)
SincronizadorLV.exe --item Dre --ano 2024    recalcula a DRE de um ano
SincronizadorLV.exe --csv C:\Vogel\csv       também gera CSVs para o "Importar CSV/XLSX" do portal
SincronizadorLV.exe --pedidos                atende o botão "Sincronizar agora" do portal (LV_Pedido); sem pedido, sai sem fazer nada
```
Saída do programa: 0 = tudo certo; 1 = erro em algum item; 2 = não rodou (configuração ou conexão). O log fica em `logs/sincronizador-AAAAMMDD.log`.

## Instalar no servidor do cliente
1. Gerar o pacote. O servidor não precisa ter o .NET instalado.
   ```
   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publicar
   ```
2. Copiar a pasta para o servidor (ex.: `C:\Vogel\SincronizadorLV`) e ajustar o `appsettings.json` e a pasta `consultas/`.
3. Rodar `SincronizadorLV.exe` uma vez à mão e conferir o log.
4. Agendar na virada do dia (PowerShell como administrador, dentro da pasta):
   ```
   powershell -ExecutionPolicy Bypass -File .\agendar-tarefa.ps1
   ```
   O padrão é às 00:05, com o usuário SYSTEM. Com login do Windows, use `-Usuario "DOMINIO\usuario"`. Para outro horário, use `-Horario 00:30`.

## Teste em 25/09/2026 (BASE de teste → BASELV local)
- Em uns 7 segundos: 343 linhas de DRE (2025) e 8.354 produtos (filial 1).
- Receita, CMV e Precificação iguais aos do portal e aos calculados direto na BASE.
