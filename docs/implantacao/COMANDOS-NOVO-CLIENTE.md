# Novo cliente: comandos em ordem

Roteiro curto para ligar um cliente novo ao Portal Vogel. O passo a passo completo, com os erros comuns, está no [GUIA.md](GUIA.md).

> O banco do ERP (`BASE`) **nunca é alterado**. Tudo o que é criado fica na `BASELV`.
> Senhas: não guarde no repositório, em e-mail ou em chat.

---

## 1. No SSMS, conectado ao servidor do cliente

Confira na barra de status do SSMS se está **no servidor certo**. Rode cada arquivo com **F5, sem nada selecionado**.
Se o banco do ERP não se chamar `BASE`, troque `@BaseErp` no início de cada script.

| Ordem | Arquivo | O que faz | Resultado esperado |
|---|---|---|---|
| 1 | [`00-diagnostico.sql`](00-diagnostico.sql) | Confere servidor, ERP e as tabelas que o portal usa. **Só leitura** | Tabelas do ERP marcadas como existentes |
| 2 | [`01-criar-baselv.sql`](01-criar-baselv.sql) | Cria a **BASELV** (mesma collation do ERP) e as 8 tabelas `LV_`. Pode rodar de novo | Lista com as **8 tabelas**: LV_Comercial, LV_Cota, LV_Dre, LV_Feriado, LV_Pedido, LV_Precificacao, LV_Rotina, LV_Sincronizacao |
| 3 | [`02-criar-logins.sql`](02-criar-logins.sql) | Logins `vogel_sincronizador` e `vogel_portal` com permissão mínima | **Antes de rodar, troque as duas senhas** no começo do script |

- Se já existir uma BASELV de outro projeto: o passo 2 cria só as tabelas `LV_` ao lado das antigas, sem mexer nelas.
- Se o passo 2 parar por **collation diferente** da do ERP: não force, fale com a Vogel.

---

## 2. Na máquina da Vogel (PowerShell, na pasta do projeto)

```powershell
cd C:\Users\Wilson\Desenvolvimento\TesteWil
.\docs\implantacao\gerar-pacote.ps1 -Cliente "Nome do Cliente" -Servidor "NOME-DO-SERVIDOR" -Filiais 1
```

Opções:

| Opção | Quando usar |
|---|---|
| `-Filiais 1,2` | cliente com mais de uma filial |
| `-BaseErp NOME` | banco do ERP com outro nome |
| `-Autenticacao Windows` | usar o usuário do Windows em vez do login SQL |
| `-ConsultaDre arquivo.sql` | cliente com DRE própria (ex.: `.\docs\dre-propria-tabelas-sqlserver.sql`) |

Resultado: pasta `publicar\SincronizadorLV-Nome-do-Cliente`.

---

## 3. No servidor do cliente

1. Copie a pasta do pacote para, por exemplo, `C:\Vogel\SincronizadorLV`.
2. No `appsettings.json`, troque `DIGITE_A_SENHA_NO_SERVIDOR` pela senha do `vogel_sincronizador`, **nas duas linhas**.
3. Primeira sincronização (prompt de comando, dentro da pasta):

   ```bat
   SincronizadorLV.exe
   ```

   A saída tem que terminar em **"Fim: tudo gravado."** Na linha "Comercial", **não** pode aparecer o aviso "sem permuta (BASELV sem a coluna Permuta…)".

4. Agendar a tarefa diária (00:05), a de pedidos ("Sincronizar agora", a cada 5 min) e a de rotinas (07:00 às 19:00, a cada 2 h). PowerShell **como administrador**, dentro da pasta:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\agendar-tarefa.ps1 -RotinasACadaHoras 2
   ```

   Com login do Windows (`-Autenticacao Windows` no pacote), acrescente `-Usuario "DOMINIO\usuario"`.

5. Testar a tarefa:

   ```powershell
   Start-ScheduledTask -TaskName 'Vogel SincronizadorLV'
   ```

---

## 4. Conferir e cadastrar

1. No SSMS, rode [`03-conferencia.sql`](03-conferencia.sql) (só totais). A **VendaLiquida** de um mês fechado tem que bater com a **"Venda"** da tela de indicadores do ERP.
2. No portal, **Clientes → Novo**: nome, CNPJ, contato e a conexão com o login `vogel_portal`.
3. Com o cliente escolhido no topo da tela:
   - **DRE**, **Precificação** e **Comercial** → "SQL do banco" → **Usar BASELV (recomendado)** → Salvar → Atualizar do banco;
   - em **Clientes**, marcar **"Rotinas e alertas pela BASELV"**;
   - na **DRE**, revisar **"Classificar despesas"** (fixas, variáveis, depreciação, impostos);
   - em **Usuários**, criar o **Cliente Gestor**.
4. Registrar a implantação em `docs/privado/implantacoes.md` (fica fora do GitHub).

---

## Checklist

- [ ] `00-diagnostico.sql` sem tabela faltando
- [ ] `01-criar-baselv.sql` com as 8 tabelas `LV_`
- [ ] `02-criar-logins.sql` com as senhas trocadas e guardadas
- [ ] Pacote gerado e copiado; senha colocada no `appsettings.json`
- [ ] `SincronizadorLV.exe` terminou em "Fim: tudo gravado."
- [ ] Tarefas agendadas e testadas
- [ ] `03-conferencia.sql`: venda de um mês fechado igual à do ERP
- [ ] Cliente cadastrado no portal, lendo da BASELV, com o gestor criado
