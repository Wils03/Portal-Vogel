using System.Data;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Portal.Data;

namespace Portal.Services;

/// <summary>
/// Ciclo do alerta e proteções do envio automático, com banco em memória, consulta simulada e Pagebot simulado
/// (nenhuma chamada sai para a internet).
/// </summary>
public static class AutoTesteDisparo
{
    public static async Task<List<(string Nome, bool Ok)>> ExecutarAsync()
    {
        var resultados = new List<(string, bool)>();
        void Checar(string nome, bool ok) => resultados.Add((nome, ok));

        // Configuração base: horário sempre aberto, sem intervalo, sem modo teste (cada cenário ajusta o que testa)
        Dictionary<string, string?> Config(params (string Chave, string Valor)[] extras)
        {
            var c = new Dictionary<string, string?>
            {
                ["Mensagens:Modo"] = "Pagebot",
                ["Mensagens:ModoTeste"] = "false",
                ["Mensagens:HorarioInicio"] = "0",
                ["Mensagens:HorarioFim"] = "24",
                ["Mensagens:EnviarSabado"] = "true",
                ["Mensagens:EnviarDomingo"] = "true",
                ["Mensagens:IntervaloSegundos"] = "0",
                ["Mensagens:LimiteDiario"] = "100",
                ["Mensagens:LimitePorClienteDia"] = "100",
                ["Mensagens:Pagebot:Url"] = "https://pagebot.teste/send-text",
                ["Mensagens:Pagebot:Token"] = "token-teste"
            };
            foreach (var (k, v) in extras) c["Mensagens:" + k] = v;
            return c;
        }

        // 1) Ciclo: aparece → envia 1x → continua → não reenvia → zera → resolve → volta → envia de novo
        await using (var c = await Cenario.CriarAsync(Config()))
        {
            c.Banco.Quantidade = 50;
            var r1 = await c.Rotinas.ExecutarAsync(1);
            var alertas = await c.AlertasAsync();
            Checar("Problema novo: cria alerta e envia 1 mensagem", r1[0].Situacao == SituacaoRotina.AlertaNovo && alertas.Count == 1 && c.Pagebot.Envios.Count == 1);
            var corpo = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(c.Pagebot.Envios[0].Corpo)!;
            Checar("Envio ao WhatsApp do cliente, token no cabeçalho, forceSend desligado",
                corpo["number"].GetString() == "5551999990000" && corpo["message"].GetString() == "Olá Ana, 50 notas"
                && corpo["forceSend"].GetBoolean() == false && c.Pagebot.Envios[0].Token == "token-teste");
            Checar("Alerta fica Notificado após o envio", alertas[0].Status == StatusAlerta.Notificado);

            c.Banco.Quantidade = 55;
            var r2 = await c.Rotinas.ExecutarAsync(1);
            alertas = await c.AlertasAsync();
            Checar("Problema continua: atualiza o mesmo alerta, sem nova mensagem",
                r2[0].Situacao == SituacaoRotina.AlertaAtualizado && alertas.Count == 1 && alertas[0].Quantidade == 55 && c.Pagebot.Envios.Count == 1);

            c.Banco.Quantidade = 0;
            var r3 = await c.Rotinas.ExecutarAsync(1);
            alertas = await c.AlertasAsync();
            Checar("Lista zerou: alerta resolvido automaticamente", r3[0].Situacao == SituacaoRotina.Resolvido && alertas[0].Status == StatusAlerta.Resolvido);

            c.Banco.Quantidade = 3;
            var r4 = await c.Rotinas.ExecutarAsync(1);
            alertas = await c.AlertasAsync();
            Checar("Problema voltou: novo alerta e nova mensagem", r4[0].Situacao == SituacaoRotina.AlertaNovo && alertas.Count == 2 && c.Pagebot.Envios.Count == 2);
        }

        // 2) Pagebot com erro: no máximo 3 tentativas
        await using (var c = await Cenario.CriarAsync(Config()))
        {
            c.Pagebot.Falhar = true;
            c.Banco.Quantidade = 7;
            await c.Rotinas.ExecutarAsync(1);
            for (var i = 0; i < 5; i++) await c.Mensageiro.DispararPendentesAsync();
            Checar("Pagebot com erro: no máximo 3 tentativas por alerta", c.Pagebot.Envios.Count == 3 && (await c.AlertasAsync())[0].Status == StatusAlerta.Novo);
        }

        // 2b) Atendimento aberto no Pagebot (chat_03): não gasta tentativa, não conta no limite, espera 2 h
        await using (var c = await Cenario.CriarAsync(Config(("LimiteDiario", "1"))))
        {
            c.Pagebot.RespostaFixa = (System.Net.HttpStatusCode.BadRequest,
                "{\"status\":\"400\",\"msg\":\"Chat already openned, verify or change 'ForceSend' option\",\"errorCode\":\"chat_03\"}");
            c.Banco.Quantidade = 7;
            await c.Rotinas.ExecutarAsync(1);
            for (var i = 0; i < 5; i++) await c.Mensageiro.DispararPendentesAsync();
            var plano = await c.Mensageiro.PlanejarAsync();
            var situacao = await c.Mensageiro.SituacaoAsync();
            Checar("Atendimento aberto no Pagebot: 1 tentativa, alerta segue pendente e espera 2 h",
                c.Pagebot.Envios.Count == 1 && plano.Single().Bloqueio!.Contains("Atendimento aberto") && situacao.EnviadosHoje == 0);
        }

        // 2c) forceSend ligado na configuração
        await using (var c = await Cenario.CriarAsync(Config(("ForceSend", "true"))))
        {
            c.Banco.Quantidade = 2;
            await c.Rotinas.ExecutarAsync(1);
            var corpo = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(c.Pagebot.Envios.Single().Corpo)!;
            Checar("forceSend ligado na configuração vai como true para o Pagebot", corpo["forceSend"].GetBoolean());
        }

        // 2d) Programação da empresa
        await using (var c = await Cenario.CriarAsync(Config()))
        {
            await c.ProgramarEmpresaAsync(ativa: true, auto: false, hora: null);
            c.Banco.Quantidade = 5;
            await c.Rotinas.ExecutarAsync(1);
            var alertas = await c.AlertasAsync();
            Checar("Empresa desligou o aviso automático: alerta criado, nenhuma mensagem",
                alertas.Count == 1 && alertas[0].Status == StatusAlerta.Novo && c.Pagebot.Envios.Count == 0);
        }
        await using (var c = await Cenario.CriarAsync(Config()))
        {
            await c.ProgramarEmpresaAsync(ativa: false, auto: true, hora: null);
            await c.HoraPadraoAsync(0);
            c.Banco.Quantidade = 5;
            var manual = await c.Rotinas.ExecutarAsync(1);
            var agendadas = await c.Rotinas.ExecutarAgendadasAsync(DateTime.Today.AddHours(23));
            Checar("Empresa desligou a rotina: não roda (nem agendada, nem 'Executar agora')",
                manual.Count == 0 && agendadas == 0 && (await c.AlertasAsync()).Count == 0);
        }
        await using (var c = await Cenario.CriarAsync(Config()))
        {
            await c.HoraPadraoAsync(7);
            await c.ProgramarEmpresaAsync(ativa: true, auto: true, hora: 22);
            c.Banco.Quantidade = 0;
            var as10 = await c.Rotinas.ExecutarAgendadasAsync(DateTime.Today.AddHours(10));
            var as22 = await c.Rotinas.ExecutarAgendadasAsync(DateTime.Today.AddHours(22).AddMinutes(1));
            var denovo = await c.Rotinas.ExecutarAgendadasAsync(DateTime.Today.AddHours(22).AddMinutes(30));
            Checar("Horário da empresa substitui o padrão e roda uma vez por dia", as10 == 0 && as22 == 1 && denovo == 0);
        }
        await using (var c = await Cenario.CriarAsync(Config()))
        {
            await c.HoraPadraoAsync(7);
            await c.CriarClienteAsync(2, "Loja Dois", "5551988887777");
            await c.ProgramarEmpresaAsync(ativa: true, auto: true, hora: 22); // só a empresa 1 muda o horário
            var as8 = await c.Rotinas.ExecutarAgendadasAsync(DateTime.Today.AddHours(8));
            Checar("Cada empresa no seu horário: às 8h roda só a que ficou no padrão (7h)", as8 == 1);
        }
        Checar("Empresa não liga aviso que a Vogel não permitiu",
            !ConfigRotina.Efetiva(new Rotina { Ativa = true, EnviarAutomaticamente = false }, new RotinaCliente { EnviarAutomaticamente = true }).EnvioAutomatico
            && !ConfigRotina.Efetiva(new Rotina { Ativa = false }, new RotinaCliente { Ativa = true }).Ativa);

        // 2e) Aviso de preços para um número escolhido
        await using (var c = await Cenario.CriarAsync(Config()))
        {
            var cli = new Cliente { Id = 1, Nome = "Loja Teste", WhatsApp = "5551999990000" };
            var r = await c.Mensageiro.EnviarParaAsync(cli, "(51) 97777-6666 ", "revisar preços", "gestor");
            var corpo = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(c.Pagebot.Envios.Single().Corpo)!;
            var invalido = await c.Mensageiro.EnviarParaAsync(cli, "123", "x", "gestor");
            Checar("Aviso de preços vai para o número escolhido (não o do cliente); número inválido é recusado",
                r.Sucesso && corpo["number"].GetString() == "51977776666" && !invalido.Sucesso && c.Pagebot.Envios.Count == 1);
        }
        await using (var c = await Cenario.CriarAsync(Config(("ModoTeste", "true"), ("NumeroTeste", "5551911112222"))))
        {
            var cli = new Cliente { Id = 1, Nome = "Loja Teste", WhatsApp = "5551999990000" };
            await c.Mensageiro.EnviarParaAsync(cli, "5551977776666", "revisar preços", "gestor");
            var corpo = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(c.Pagebot.Envios.Single().Corpo)!;
            Checar("Aviso de preços em modo teste vai para o número de teste, citando o destino real",
                corpo["number"].GetString() == "5551911112222" && corpo["message"].GetString()!.StartsWith("[TESTE → Loja Teste (5551977776666)]"));
        }

        // 2f) Excluir cliente do portal
        await using (var c = await Cenario.CriarAsync(Config()))
        {
            await c.CriarClienteAsync(2, "Loja Dois", "5551988887777");
            c.Banco.Quantidade = 3;
            await c.Rotinas.ExecutarAsync(1); // alerta nos dois clientes (e o registro de programação de cada um)
            await using (var db = c.Fabrica.CreateDbContext())
            {
                db.Produtos.Add(new Produto { ClienteId = 1, Codigo = "1", Descricao = "x" });
                db.LancamentosDre.Add(new LancamentoDre { ClienteId = 1, Competencia = new DateOnly(2026, 1, 1), Conta = "x", Valor = 1 });
                db.Mensagens.Add(new MensagemEnviada { ClienteId = 1, DataHora = DateTime.Now, Destino = "x", Texto = "histórico", Canal = "Pagebot", Sucesso = true });
                db.Users.Add(new ApplicationUser { Id = "u1", UserName = "g@loja", Email = "g@loja", Perfil = Perfil.ClienteGestor, ClienteId = 1, SecurityStamp = "a" });
                await db.SaveChangesAsync();
            }
            var antes = await ExclusaoCliente.ContarAsync(c.Fabrica, 1);
            await ExclusaoCliente.ExcluirAsync(c.Fabrica, 1);
            await using (var db = c.Fabrica.CreateDbContext())
            {
                var usuario = await db.Users.SingleAsync(u => u.Id == "u1");
                Checar("Excluir cliente: apaga cadastro, produtos, DRE, alertas e programação; desativa usuários; mantém mensagens e o outro cliente",
                    antes is { Produtos: 1, Lancamentos: 1, Alertas: 1, Programacoes: 1, Usuarios: 1 }
                    && !await db.Clientes.AnyAsync(x => x.Id == 1) && !await db.Produtos.AnyAsync() && !await db.LancamentosDre.AnyAsync()
                    && !await db.Alertas.AnyAsync(a => a.ClienteId == 1) && !await db.RotinasClientes.AnyAsync(r => r.ClienteId == 1)
                    && usuario.LockoutEnd == DateTimeOffset.MaxValue && usuario.SecurityStamp != "a"
                    && await db.Mensagens.AnyAsync(m => m.ClienteId == 1)
                    && await db.Clientes.AnyAsync(x => x.Id == 2) && await db.Alertas.AnyAsync(a => a.ClienteId == 2));
            }
        }

        // 2g) Importação automática da BASELV para o portal
        await using (var c = await Cenario.CriarAsync(Config()))
        {
            ImportacaoBaseLv.LimparTentativas();
            await using (var db = c.Fabrica.CreateDbContext())
            {
                var cli = await db.Clientes.SingleAsync(x => x.Id == 1);
                cli.SqlDre = ModelosSql.DreBaseLv;
                cli.SqlPrecificacao = ModelosSql.PrecificacaoBaseLv;
                db.LancamentosDre.Add(new LancamentoDre { ClienteId = 1, Competencia = new DateOnly(2026, 1, 1), Grupo = GrupoDre.ReceitaBruta, Conta = "antigo", Valor = 1 });
                await db.SaveChangesAsync();
            }
            var sincronizadoEm = new DateTime(2026, 9, 26, 0, 6, 0);
            c.Banco.Responder = sql =>
            {
                var t = new DataTable();
                if (sql.Contains("LV_Sincronizacao"))
                {
                    t.Columns.Add("Tabela", typeof(string)); t.Columns.Add("UltimoFim", typeof(DateTime));
                    t.Rows.Add("Dre", sincronizadoEm); t.Rows.Add("Precificacao", sincronizadoEm);
                }
                else if (sql.Contains("LV_Dre"))
                {
                    var ano = sql.Contains("'20260101'") && !sql.Contains("'20250101'") ? 2026 : 2025;
                    t.Columns.Add("Competencia", typeof(DateTime)); t.Columns.Add("Grupo", typeof(string)); t.Columns.Add("Conta", typeof(string)); t.Columns.Add("Valor", typeof(decimal));
                    t.Rows.Add(new DateTime(ano, 8, 1), "ReceitaBruta", "Vendas NF-e", 1000m);
                    t.Rows.Add(new DateTime(ano, 8, 1), "Deducoes", "Devoluções de vendas", 50m);
                }
                else if (sql.Contains("LV_Precificacao"))
                {
                    foreach (var col in new[] { "Codigo", "Descricao" }) t.Columns.Add(col, typeof(string));
                    foreach (var col in new[] { "Custo", "Preco", "QtdMes" }) t.Columns.Add(col, typeof(decimal));
                    t.Rows.Add("P1", "Produto 1", 10m, 20m, 3m); t.Rows.Add("P2", "Produto 2", 5m, 9m, 0m);
                }
                return t;
            };
            var importacao = new ImportacaoBaseLv(c.Fabrica, c.Banco, new ConfigurationBuilder().Build(), NullLogger<ImportacaoBaseLv>.Instance);

            var cedo = await importacao.ExecutarAgendadaAsync(new DateTime(2026, 9, 26, 5, 0, 0));
            var consultasCedo = c.Banco.Consultas;
            var primeira = await importacao.ExecutarAgendadaAsync(new DateTime(2026, 9, 26, 7, 0, 0));
            Cliente depois;
            List<LancamentoDre> dre;
            int produtos;
            await using (var db = c.Fabrica.CreateDbContext())
            {
                depois = await db.Clientes.AsNoTracking().SingleAsync(x => x.Id == 1);
                dre = await db.LancamentosDre.AsNoTracking().ToListAsync();
                produtos = await db.Produtos.CountAsync();
            }
            Checar("Importação BASELV: antes da hora (6h) não faz nada", cedo.Count == 0 && consultasCedo == 0);
            Checar("Importação BASELV: às 7h traz a DRE do ano atual e do anterior (substitui o ano) e a Precificação",
                primeira is [{ Dre: true, Precificacao: true, Erro: null }]
                && dre.Count == 4 && dre.All(l => l.Conta != "antigo") && dre.Select(l => l.Competencia.Year).Distinct().Count() == 2 && produtos == 2);
            Checar("Importação BASELV: registra a data dos dados = última sincronização do cliente",
                depois.DreDadosDe == sincronizadoEm && depois.PrecificacaoDadosDe == sincronizadoEm && depois.DreAtualizadoEm == new DateTime(2026, 9, 26, 7, 0, 0));

            var consultasAntes = c.Banco.Consultas;
            var semNovidade = await importacao.ExecutarAgendadaAsync(new DateTime(2026, 9, 26, 9, 0, 0));
            Checar("Importação BASELV: sem sincronização nova, só confere a data e não reimporta",
                semNovidade.Count == 0 && c.Banco.Consultas == consultasAntes + 1);

            sincronizadoEm = new DateTime(2026, 9, 27, 0, 6, 0);
            c.Banco.SemConexao = true;
            var falha = await importacao.ExecutarAgendadaAsync(new DateTime(2026, 9, 27, 7, 0, 0));
            var consultasFalha = c.Banco.Consultas;
            var cedoDemais = await importacao.ExecutarAgendadaAsync(new DateTime(2026, 9, 27, 7, 30, 0));
            int dreDepoisFalha;
            await using (var db = c.Fabrica.CreateDbContext()) dreDepoisFalha = await db.LancamentosDre.CountAsync();
            Checar("Importação BASELV: sem conexão registra a falha, não apaga nada e só tenta de novo depois de 1 hora",
                falha is [{ Erro: not null, Dre: false }] && dreDepoisFalha == 4 && cedoDemais.Count == 0 && c.Banco.Consultas == consultasFalha);

            c.Banco.SemConexao = false;
            var voltou = await importacao.ExecutarAgendadaAsync(new DateTime(2026, 9, 27, 8, 5, 0));
            Checar("Importação BASELV: quando a conexão volta, importa a sincronização nova", voltou is [{ Dre: true, Precificacao: true }]);
        }

        // 2h) Rotinas e alertas pela BASELV (resultado gravado pelo SincronizadorLV em LV_Rotina)
        await using (var c = await Cenario.CriarAsync(Config()))
        {
            await using (var db = c.Fabrica.CreateDbContext())
            {
                (await db.Clientes.SingleAsync(x => x.Id == 1)).RotinasPelaBaseLv = true;
                await db.SaveChangesAsync();
            }
            object executadoEm = DateTime.Now.AddHours(-3), quantidade = 3m, amostra = "[{\"Nota\":\"1\"},{\"Nota\":\"2\"},{\"Nota\":\"3\"}]", erro = DBNull.Value;
            var semLinha = false;
            c.Banco.Responder = sql =>
            {
                var t = new DataTable();
                foreach (var col in new[] { "ExecutadoEm", "Quantidade", "Amostra", "Erro" }) t.Columns.Add(col, typeof(object));
                if (!semLinha) t.Rows.Add(executadoEm, quantidade, amostra, erro);
                return t;
            };

            await c.Rotinas.ExecutarAsync(1);
            var alertas = await c.AlertasAsync();
            Checar("Rotinas pela BASELV: lê LV_Rotina (não roda o SQL na BASE) e abre o alerta com a quantidade e o detalhe sincronizados",
                c.Banco.Sqls.All(q => q.Contains("BASELV.dbo.LV_Rotina") && q.Contains("RotinaId = 1"))
                && alertas is [{ Status: StatusAlerta.Novo or StatusAlerta.Notificado, Quantidade: 3m }] && alertas[0].DetalheJson == (string)amostra);

            quantidade = 0m;
            await c.Rotinas.ExecutarAsync(1);
            Checar("Rotinas pela BASELV: quando a sincronização mostra zero, o alerta se resolve sozinho",
                (await c.AlertasAsync()) is [{ Status: StatusAlerta.Resolvido }]);

            erro = "Invalid object name 'Nfe_status'";
            await c.Rotinas.ExecutarAsync(1);
            var comErro = (await c.AlertasAsync()).Where(a => a.Status == StatusAlerta.Erro).ToList();
            Checar("Rotinas pela BASELV: SQL que falhou no servidor do cliente vira alerta de falha com o motivo",
                comErro is [{ Erro: var e1 }] && e1!.Contains("Nfe_status"));

            erro = DBNull.Value;
            executadoEm = DateTime.Now.AddHours(-40);
            await c.Rotinas.ExecutarAsync(1);
            var velho = (await c.AlertasAsync()).Where(a => a.Status == StatusAlerta.Erro).ToList();
            Checar("Rotinas pela BASELV: resultado com mais de 36 h (sincronização parada) vira falha, sem abrir alerta com dado velho",
                velho is [{ Erro: var e2 }] && e2!.Contains("desatualizado"));

            semLinha = true;
            await c.Rotinas.ExecutarAsync(1);
            var semSinc = (await c.AlertasAsync()).Where(a => a.Status == StatusAlerta.Erro).ToList();
            Checar("Rotinas pela BASELV: rotina ainda não sincronizada vira falha explicando o que fazer",
                semSinc is [{ Erro: var e3 }] && e3!.Contains("ainda não foi sincronizada"));
        }

        // 2i) Botão "Sincronizar agora" (pedido em BASELV.dbo.LV_Pedido)
        await using (var c = await Cenario.CriarAsync(Config()))
        {
            var pedidos = new PedidoSincronizacao(c.Banco);
            Cliente Empresa(Action<Cliente>? ajuste = null)
            {
                var e = new Cliente { Id = 1, Nome = "Loja Teste", TipoBanco = TipoBanco.SqlServer, ConexaoCriptografada = "x" };
                ajuste?.Invoke(e);
                return e;
            }
            var t0 = new DateTime(2026, 9, 26, 10, 0, 0);

            var semBaseLv = await pedidos.PedirAsync(Empresa(), "g@loja", t0);
            Checar("Sincronizar agora: empresa que não usa a BASELV não registra pedido", !semBaseLv.Registrado && c.Banco.Pedidos.Count == 0);

            var empresa = Empresa(e => e.SqlDre = ModelosSql.DreBaseLv);
            c.Banco.Agora = t0;
            var primeiro = await pedidos.PedirAsync(empresa, "g@loja", t0);
            var repetido = await pedidos.PedirAsync(empresa, "g@loja", t0.AddMinutes(1));
            Checar("Sincronizar agora: registra o pedido e não duplica enquanto ele está pendente",
                primeiro is { Registrado: true, Situacao.Status: PedidoSincronizacao.Pendente } && !repetido.Registrado && c.Banco.Pedidos.Count == 1);

            c.Banco.Pedidos[0] = c.Banco.Pedidos[0] with { Status = PedidoSincronizacao.Concluido, IniciadoEm = t0.AddMinutes(4), ConcluidoEm = t0.AddMinutes(5) };
            var cedo = await pedidos.PedirAsync(empresa, "g@loja", t0.AddMinutes(6));
            c.Banco.Agora = t0.AddMinutes(11);
            var depois = await pedidos.PedirAsync(empresa, "g@loja", t0.AddMinutes(11));
            Checar("Sincronizar agora: espera 10 minutos entre pedidos (para não sobrecarregar o ERP do cliente)",
                !cedo.Registrado && cedo.Mensagem.Contains("Aguarde") && depois.Registrado && c.Banco.Pedidos.Count == 2);

            c.Banco.Pedidos[1] = c.Banco.Pedidos[1] with { Status = PedidoSincronizacao.Executando, IniciadoEm = t0.AddMinutes(12) };
            var executando = await pedidos.PedirAsync(empresa, "g@loja", t0.AddMinutes(30));
            var travado = await pedidos.PedirAsync(empresa, "g@loja", t0.AddMinutes(80));
            Checar("Sincronizar agora: não pede de novo durante a execução, mas aceita se ficar travado por mais de 1 hora",
                !executando.Registrado && travado.Registrado && c.Banco.Pedidos.Count == 3);

            var semResposta = PedidoSincronizacao.Descrever(c.Banco.Pedidos[2], t0.AddMinutes(80 + 25));
            Checar("Sincronizar agora: pedido sem resposta por 20 min avisa para conferir a tarefa agendada no servidor",
                semResposta.Contains("sem resposta") && semResposta.Contains("Pedidos"));
            Checar("Sincronizar agora: rotinas pela BASELV também habilitam o botão; Firebird/sem conexão não",
                PedidoSincronizacao.Disponivel(Empresa(e => e.RotinasPelaBaseLv = true))
                && !PedidoSincronizacao.Disponivel(Empresa(e => { e.SqlDre = ModelosSql.DreBaseLv; e.TipoBanco = TipoBanco.Firebird; }))
                && !PedidoSincronizacao.Disponivel(Empresa(e => { e.SqlDre = ModelosSql.DreBaseLv; e.ConexaoCriptografada = null; })));
        }

        // 3) Rotina sem "enviar automaticamente"
        await using (var c = await Cenario.CriarAsync(Config(), enviarAutomaticamente: false))
        {
            c.Banco.Quantidade = 9;
            await c.Rotinas.ExecutarAsync(1);
            await c.Mensageiro.DispararPendentesAsync();
            Checar("Sem 'enviar automaticamente': alerta criado e nada enviado", c.Pagebot.Envios.Count == 0 && (await c.AlertasAsync()).Count == 1);
        }

        // 4) Modo teste: vai para o número de teste, com o cliente identificado
        await using (var c = await Cenario.CriarAsync(Config(("ModoTeste", "true"), ("NumeroTeste", "55 (51) 98888-7777"))))
        {
            c.Banco.Quantidade = 4;
            await c.Rotinas.ExecutarAsync(1);
            var corpo = c.Pagebot.Envios.Count == 1 ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(c.Pagebot.Envios[0].Corpo)! : null;
            Checar("Modo teste: envia só para o número de teste, com [TESTE → cliente]",
                corpo is not null && corpo["number"].GetString() == "5551988887777"
                && corpo["message"].GetString()!.StartsWith("[TESTE → Loja Teste (5551999990000)]"));
        }
        await using (var c = await Cenario.CriarAsync(Config(("ModoTeste", "true"), ("NumeroTeste", ""))))
        {
            c.Banco.Quantidade = 4;
            await c.Rotinas.ExecutarAsync(1);
            var plano = await c.Mensageiro.PlanejarAsync();
            Checar("Modo teste sem número: nada é enviado", c.Pagebot.Envios.Count == 0 && plano.Single().Bloqueio!.Contains("NumeroTeste"));
        }

        // 5) Um por cliente por dia, com os alertas agrupados numa mensagem só
        await using (var c = await Cenario.CriarAsync(Config(("LimitePorClienteDia", "1"))))
        {
            await c.CriarRotinaAsync(2, "Notas rejeitadas");
            c.Banco.Quantidade = 5;
            // gera os dois alertas com o envio desligado e depois libera, para caírem juntos
            await c.AlterarEnvioAutomaticoAsync(false);
            await c.Rotinas.ExecutarAsync(1);
            await c.Rotinas.ExecutarAsync(2);
            await c.AlterarEnvioAutomaticoAsync(true);
            await c.Mensageiro.DispararPendentesAsync();
            var texto = c.Pagebot.Envios.Count == 1 ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(c.Pagebot.Envios[0].Corpo)!["message"].GetString()! : "";
            var alertas = await c.AlertasAsync();
            Checar("Vários alertas do mesmo cliente: UMA mensagem com o resumo",
                c.Pagebot.Envios.Count == 1 && texto.Contains("Notas com erro: 5") && texto.Contains("Notas rejeitadas: 5")
                && alertas.All(a => a.Status == StatusAlerta.Notificado));

            // problema some e volta no mesmo dia: alerta novo, mas o cliente já recebeu hoje
            c.Banco.Quantidade = 0;
            await c.Rotinas.ExecutarAsync(1);
            c.Banco.Quantidade = 8;
            await c.Rotinas.ExecutarAsync(1);
            await c.Mensageiro.DispararPendentesAsync();
            var plano = await c.Mensageiro.PlanejarAsync();
            Checar("Limite 1 por cliente/dia: segundo envio fica para amanhã",
                c.Pagebot.Envios.Count == 1 && plano.Single().Bloqueio!.Contains("já recebeu"));
        }

        // 6) Limite diário total
        await using (var c = await Cenario.CriarAsync(Config(("LimiteDiario", "1"))))
        {
            await c.CriarClienteAsync(2, "Outra Loja", "5551977776666");
            c.Banco.Quantidade = 2;
            await c.Rotinas.ExecutarAsync(1); // gera para os 2 clientes e envia 1
            await c.Mensageiro.DispararPendentesAsync();
            var plano = await c.Mensageiro.PlanejarAsync();
            Checar("Limite diário: com limite 1, o segundo cliente fica para amanhã",
                c.Pagebot.Envios.Count == 1 && plano.Single().Bloqueio!.Contains("Limite diário"));
        }

        // 7) Intervalo entre envios
        await using (var c = await Cenario.CriarAsync(Config(("IntervaloSegundos", "3600"))))
        {
            await c.CriarClienteAsync(2, "Outra Loja", "5551977776666");
            c.Banco.Quantidade = 2;
            await c.Rotinas.ExecutarAsync(1);
            for (var i = 0; i < 3; i++) await c.Mensageiro.DispararPendentesAsync();
            Checar("Intervalo: com 1 h entre envios, sai só 1 mesmo chamando várias vezes", c.Pagebot.Envios.Count == 1);
        }

        // 8) Simulação não envia nada
        await using (var c = await Cenario.CriarAsync(Config(), enviarAutomaticamente: false))
        {
            c.Banco.Quantidade = 6;
            await c.Rotinas.ExecutarAsync(1);
            await c.AlterarEnvioAutomaticoAsync(true);
            var plano = await c.Mensageiro.PlanejarAsync();
            Checar("Simular: mostra o que sairia e não envia", plano.Count == 1 && plano[0].Bloqueio is null && c.Pagebot.Envios.Count == 0);
        }

        // 10) Configuração pela tela: grava no banco, token criptografado, passa a valer na hora
        await using (var c = await Cenario.CriarAsync(Config(("ModoTeste", "true"), ("NumeroTeste", ""))))
        {
            var store = new ConfiguracaoMensagensStore(new ConfigurationBuilder().Build(), new EphemeralDataProtectionProvider());
            await store.SalvarAsync(c.Fabrica, new ConfiguracaoMensagens { Modo = "Pagebot", ModoTeste = true, NumeroTeste = "55 51 99513-2833", LimiteDiario = 5,
                LimitePorClienteDia = 1, IntervaloSegundos = 30, TentativasMaximas = 3, HorarioInicio = 8, HorarioFim = 18 }, "meu-token", "teste");
            await using var db = c.Fabrica.CreateDbContext();
            var salvo = await db.ConfiguracoesMensagens.SingleAsync();
            Checar("Configurações da tela: token gravado criptografado e aplicado",
                salvo.TokenCriptografado is not null && !salvo.TokenCriptografado.Contains("meu-token")
                && store.Atual.Pagebot.Token == "meu-token" && store.Atual.NumeroTeste == "5551995132833" && store.Atual.LimiteDiario == 5);
            await store.SalvarAsync(c.Fabrica, salvo, novoToken: null, "teste");
            Checar("Salvar sem informar token mantém o token atual", store.Atual.Pagebot.Token == "meu-token");
        }

        // 9) Horário comercial
        await using (var c = await Cenario.CriarAsync(Config(("HorarioInicio", "8"), ("HorarioFim", "18"), ("EnviarSabado", "false"), ("EnviarDomingo", "false"))))
        {
            var m = c.Mensageiro;
            Checar("Horário comercial: seg 10h sim, seg 20h não, sáb/dom não",
                m.DentroDoHorario(new DateTime(2026, 9, 21, 10, 0, 0)) && !m.DentroDoHorario(new DateTime(2026, 9, 21, 20, 0, 0))
                && !m.DentroDoHorario(new DateTime(2026, 9, 26, 10, 0, 0)) && !m.DentroDoHorario(new DateTime(2026, 9, 27, 10, 0, 0)));
        }

        return resultados;
    }

    private sealed class Cenario : IAsyncDisposable
    {
        private SqliteConnection conexao = null!;
        private FabricaTeste fabrica = null!;
        public IDbContextFactory<ApplicationDbContext> Fabrica => fabrica;
        public BancoFalso Banco { get; private set; } = null!;
        public PagebotFalso Pagebot { get; } = new();
        public MensagemService Mensageiro { get; private set; } = null!;
        public RotinaService Rotinas { get; private set; } = null!;

        public static async Task<Cenario> CriarAsync(Dictionary<string, string?> valores, bool enviarAutomaticamente = true)
        {
            var c = new Cenario { conexao = new SqliteConnection("DataSource=:memory:") };
            await c.conexao.OpenAsync();
            c.fabrica = new FabricaTeste(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(c.conexao).Options);
            await using (var db = c.fabrica.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();
                db.Clientes.Add(new Cliente { Id = 1, Nome = "Loja Teste", Contato = "Ana", WhatsApp = "5551999990000", Ativo = true, ConexaoCriptografada = "x" });
                db.Rotinas.Add(new Rotina { Id = 1, Nome = "Notas com erro", Sql = "SELECT 1", Limite = 0, Ativa = true, EnviarAutomaticamente = enviarAutomaticamente, ModeloMensagem = "Olá {cliente}, {quantidade} notas" });
                await db.SaveChangesAsync();
            }
            var config = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
            c.Banco = new BancoFalso(config);
            c.Mensageiro = new MensagemService(c.fabrica, new HttpClient(c.Pagebot), new ConfiguracaoMensagensStore(config));
            c.Rotinas = new RotinaService(c.fabrica, c.Banco, c.Mensageiro, NullLogger<RotinaService>.Instance);
            return c;
        }

        public async Task<List<Alerta>> AlertasAsync()
        {
            await using var db = fabrica.CreateDbContext();
            return await db.Alertas.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
        }

        public async Task CriarRotinaAsync(int id, string nome)
        {
            await using var db = fabrica.CreateDbContext();
            db.Rotinas.Add(new Rotina { Id = id, Nome = nome, Sql = "SELECT 1", Limite = 0, Ativa = true, EnviarAutomaticamente = true, ModeloMensagem = "{rotina}: {quantidade}" });
            await db.SaveChangesAsync();
        }

        public async Task CriarClienteAsync(int id, string nome, string whatsApp)
        {
            await using var db = fabrica.CreateDbContext();
            db.Clientes.Add(new Cliente { Id = id, Nome = nome, Contato = nome, WhatsApp = whatsApp, Ativo = true, ConexaoCriptografada = "x" });
            await db.SaveChangesAsync();
        }

        public async Task ProgramarEmpresaAsync(bool ativa, bool auto, int? hora, int clienteId = 1)
        {
            await using var db = fabrica.CreateDbContext();
            db.RotinasClientes.Add(new RotinaCliente { RotinaId = 1, ClienteId = clienteId, Ativa = ativa, EnviarAutomaticamente = auto, HoraExecucao = hora });
            await db.SaveChangesAsync();
        }

        public async Task HoraPadraoAsync(int? hora)
        {
            await using var db = fabrica.CreateDbContext();
            await db.Rotinas.ExecuteUpdateAsync(u => u.SetProperty(r => r.HoraExecucao, hora));
        }

        public async Task AlterarEnvioAutomaticoAsync(bool ligado)
        {
            await using var db = fabrica.CreateDbContext();
            await db.Rotinas.ExecuteUpdateAsync(u => u.SetProperty(r => r.EnviarAutomaticamente, ligado));
        }

        public async ValueTask DisposeAsync() => await conexao.DisposeAsync();
    }

    private sealed class FabricaTeste(DbContextOptions<ApplicationDbContext> opcoes) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(opcoes);
    }

    private sealed class BancoFalso(IConfiguration config)
        : BancoClienteService(new EphemeralDataProtectionProvider(), config)
    {
        public decimal Quantidade { get; set; }

        /// <summary>Resposta por consulta (nulo = o COUNT padrão com <see cref="Quantidade"/>).</summary>
        public Func<string, DataTable?>? Responder { get; set; }

        /// <summary>Simula servidor do cliente sem conexão.</summary>
        public bool SemConexao { get; set; }

        public int Consultas { get; private set; }
        public List<string> Sqls { get; } = [];

        /// <summary>Pedidos de "Sincronizar agora" gravados (simula BASELV.dbo.LV_Pedido).</summary>
        public List<SituacaoPedido> Pedidos { get; } = [];
        public DateTime Agora { get; set; } = DateTime.Now;

        public override Task RegistrarPedidoSincronizacaoAsync(Cliente cliente, string? usuario, CancellationToken ct = default)
        {
            Pedidos.Add(new SituacaoPedido(Pedidos.Count + 1, Agora, PedidoSincronizacao.Pendente, null, null, null));
            return Task.CompletedTask;
        }

        public override Task<DataTable> ConsultarAsync(Cliente cliente, string sql, CancellationToken ct = default, int? limiteLinhas = null)
        {
            Consultas++;
            Sqls.Add(sql);
            if (SemConexao) throw new InvalidOperationException("O tempo limite de espera foi atingido (servidor sem conexão).");
            if (sql.Contains("LV_Pedido"))
            {
                var t = new DataTable();
                foreach (var col in new[] { "Id", "PedidoEm", "Status", "IniciadoEm", "ConcluidoEm", "Erro" }) t.Columns.Add(col, typeof(object));
                if (Pedidos.LastOrDefault() is { } ultimo)
                    t.Rows.Add(ultimo.Id, ultimo.PedidoEm, ultimo.Status, (object?)ultimo.IniciadoEm ?? DBNull.Value, (object?)ultimo.ConcluidoEm ?? DBNull.Value, (object?)ultimo.Erro ?? DBNull.Value);
                return Task.FromResult(t);
            }
            if (Responder?.Invoke(sql) is DataTable resposta) return Task.FromResult(resposta);
            var tabela = new DataTable();
            tabela.Columns.Add("quantidade", typeof(object));
            tabela.Rows.Add(Quantidade);
            return Task.FromResult(tabela);
        }
    }

    private sealed class PagebotFalso : HttpMessageHandler
    {
        public List<(string Corpo, string? Token)> Envios { get; } = [];
        public bool Falhar { get; set; }
        public (HttpStatusCode Status, string Corpo)? RespostaFixa { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var corpo = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Envios.Add((corpo, request.Headers.TryGetValues("access-token", out var v) ? v.FirstOrDefault() : null));
            if (RespostaFixa is { } r) return new HttpResponseMessage(r.Status) { Content = new StringContent(r.Corpo) };
            return new HttpResponseMessage(Falhar ? HttpStatusCode.InternalServerError : HttpStatusCode.Accepted);
        }
    }
}
