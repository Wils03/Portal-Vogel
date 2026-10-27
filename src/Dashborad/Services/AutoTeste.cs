using System.Data;
using System.Text;
using Portal.Data;

namespace Portal.Services;

/// <summary>Validações rápidas das regras de negócio: <c>dotnet run -- --self-test</c>.</summary>
public static class AutoTeste
{
    public static int Executar()
    {
        var falhas = 0;
        void Checar(string nome, bool ok)
        {
            Console.WriteLine($"{(ok ? "OK   " : "FALHA")} {nome}");
            if (!ok) falhas++;
        }
        bool Lanca(Action a) { try { a(); return false; } catch (InvalidOperationException) { return true; } }

        // Trava de SQL somente leitura
        Checar("SELECT permitido", !Lanca(() => BancoClienteService.ValidarSomenteLeitura("select count(*) from notas where status = 'ERRO'")));
        Checar("WITH permitido", !Lanca(() => BancoClienteService.ValidarSomenteLeitura("WITH x AS (SELECT 1 a) SELECT * FROM x;")));
        Checar("Coluna com 'update' no nome permitida", !Lanca(() => BancoClienteService.ValidarSomenteLeitura("SELECT dt_update, created_at FROM t")));
        Checar("DELETE bloqueado", Lanca(() => BancoClienteService.ValidarSomenteLeitura("DELETE FROM notas")));
        Checar("Dois comandos bloqueados", Lanca(() => BancoClienteService.ValidarSomenteLeitura("SELECT 1; DROP TABLE notas")));
        Checar("UPDATE escondido após comentário bloqueado", Lanca(() => BancoClienteService.ValidarSomenteLeitura("/* x */ UPDATE notas SET a = 1")));

        // Quantidade da rotina
        var escalar = new DataTable(); escalar.Columns.Add("c", typeof(object)); escalar.Rows.Add(50L);
        Checar("COUNT vira quantidade", RotinaService.ExtrairQuantidade(escalar) == 50);
        var linhas = new DataTable(); linhas.Columns.Add("a", typeof(object)); linhas.Columns.Add("b", typeof(object));
        linhas.Rows.Add(1, "x"); linhas.Rows.Add(2, "y"); linhas.Rows.Add(3, "z");
        Checar("Linhas contadas", RotinaService.ExtrairQuantidade(linhas) == 3);

        var msg = RotinaService.MontarMensagem(new Rotina { Nome = "Notas", ModeloMensagem = "Olá {cliente}, {quantidade} notas" },
            new Cliente { Nome = "Loja X", Contato = "Ana" }, 50);
        Checar("Mensagem com variáveis", msg == "Olá Ana, 50 notas");
        Checar("Link WhatsApp", MensagemService.LinkWhatsApp("+55 (51) 99999-0000", "a b") == "https://wa.me/5551999990000?text=a%20b");

        // Precificação: variáveis 10%, fixas 20%, margem 10% => divisor 0,60
        var cli = new Cliente { ImpostosPct = 8, ComissaoPct = 2, MargemDesejadaPct = 10, CustosFixosMensais = 20000, FaturamentoMedioMensal = 100000 };
        var r = CalculoPreco.Calcular(cli, [new Produto { Custo = 60, PrecoAtual = 100, QtdMes = 1000 }]);
        Checar("Markup divisor 0,60", r.MarkupDivisor == 0.60m);
        Checar("Preço sugerido 100", Math.Round(r.Produtos[0].PrecoSugerido, 2) == 100m);
        Checar("MC% 30%", r.MargemContribuicaoMediaPct == 0.30m);
        Checar("Ponto de equilíbrio 66.666,67", Math.Round(r.PontoEquilibrioValor, 2) == 66666.67m);

        // Regra do ERP: margem sobre a venda e markup sobre o custo
        var margem = CalculoPreco.PrecoRegraSistema(new Produto { Custo = 189.1381m, ImpostosPct = 26.25m, LucroDesejadoPct = 32m, LucroSobreCusto = false });
        Checar("Regra ERP margem (189,14 / (1 - 58,25%) = 453,03)", margem is decimal m1 && Math.Round(m1, 2) == 453.03m);
        var markup = CalculoPreco.PrecoRegraSistema(new Produto { Custo = 100m, ImpostosPct = 9.25m, LucroDesejadoPct = 30m, LucroSobreCusto = true });
        Checar("Regra ERP markup (100 × 1,30 / 0,9075 = 143,25)", markup is decimal m2 && Math.Round(m2, 2) == 143.25m);
        var cliProd = new Cliente { ImpostosPct = 20, MargemDesejadaPct = 10 };
        var rProd = CalculoPreco.Calcular(cliProd, [new Produto { Custo = 50, PrecoAtual = 100, ImpostosPct = 10m }]);
        Checar("Impostos do grupo do produto substituem os do cliente", rProd.Produtos[0].MargemContribuicaoPct == 0.40m);
        Checar("Modelo SQL precificação passa na trava de leitura", !Lanca(() => BancoClienteService.ValidarSomenteLeitura(
            ModelosSql.AplicarFilial(ModelosSql.PrecificacaoSqlServer, 1))));

        // DRE
        LancamentoDre L0(GrupoDre g, decimal v, string conta) => new() { Competencia = new DateOnly(2026, 1, 1), Grupo = g, Conta = conta, Valor = v };
        var dre = Dre.Montar(
        [
            new LancamentoDre { Competencia = new DateOnly(2026, 1, 1), Grupo = GrupoDre.ReceitaBruta, Valor = 1000 },
            new LancamentoDre { Competencia = new DateOnly(2026, 1, 1), Grupo = GrupoDre.Deducoes, Valor = 100 },
            new LancamentoDre { Competencia = new DateOnly(2026, 1, 1), Grupo = GrupoDre.Cmv, Valor = 500 },
            new LancamentoDre { Competencia = new DateOnly(2026, 2, 1), Grupo = GrupoDre.DespesasOperacionais, Valor = 200 }
        ]);
        Checar("DRE lucro líquido anual 200", dre[^1].Ano == 200);
        Checar("DRE fevereiro negativo", dre[^1].Meses[1] == -200);
        var dreDev = Dre.Montar(
        [
            new LancamentoDre { Competencia = new DateOnly(2026, 8, 1), Grupo = GrupoDre.ReceitaBruta, Conta = "Vendas NF-e", Valor = 2117855.51m },
            new LancamentoDre { Competencia = new DateOnly(2026, 8, 1), Grupo = GrupoDre.Deducoes, Conta = "Devoluções de vendas", Valor = 62800.72m },
            new LancamentoDre { Competencia = new DateOnly(2026, 8, 1), Grupo = GrupoDre.Deducoes, Conta = "COFINS", Valor = 44599.79m }
        ]);
        Checar("DRE: devoluções separadas dos impostos; venda líquida = receita bruta − devoluções (a Venda do ERP)",
            dreDev.Single(l => l.Titulo == Dre.Devolucoes).Meses[7] == 62800.72m
            && dreDev.Single(l => l.Titulo == Dre.VendaLiquida).Meses[7] == 2055054.79m
            && dreDev.Single(l => l.Titulo == Dre.ImpostosVenda).Meses[7] == 44599.79m
            && dreDev.Single(l => l.Titulo == "= Receita líquida").Meses[7] == 2117855.51m - 62800.72m - 44599.79m);
        var dreNova = Dre.Montar(Dre.Classificar(
        [
            L0(GrupoDre.ReceitaBruta, 1000, "Vendas"), L0(GrupoDre.Deducoes, 100, "ICMS"), L0(GrupoDre.Cmv, 500, "CMV"),
            L0(GrupoDre.DespesasOperacionais, 50, "Comissões de vendedores"), L0(GrupoDre.DespesasOperacionais, 30, "Taxas de cartão"),
            L0(GrupoDre.DespesasOperacionais, 200, "Aluguel"), L0(GrupoDre.DespesasOperacionais, 20, "Depreciação"),
            L0(GrupoDre.DespesasOperacionais, 40, "Fretes sobre vendas"), L0(GrupoDre.DespesasFinanceiras, 10, "Juros"),
        ], new Dictionary<string, GrupoDre> { ["Fretes sobre vendas"] = GrupoDre.DespesasOperacionais }));
        decimal Jan(string titulo) => dreNova.Single(l => l.Titulo == titulo).Meses[0];
        Checar("DRE nova: comissões e taxas de cartão viram variáveis pelo nome; frete marcado como fixo fica fixo; depreciação em linha própria",
            Jan(Dre.Nomes[GrupoDre.DespesasVariaveis]) == 80m && Jan(Dre.Nomes[GrupoDre.DespesasOperacionais]) == 240m && Jan(Dre.Nomes[GrupoDre.Depreciacao]) == 20m);
        Checar("DRE nova: lucro bruto 400 → margem de contribuição 320 → EBITDA 80 → resultado operacional 60 → lucro líquido 50",
            Jan(Dre.LucroBruto) == 400m && Jan(Dre.MargemContribuicao) == 320m && Jan(Dre.Ebitda) == 80m && Jan(Dre.ResultadoOperacional) == 60m && Jan(Dre.LucroLiquido) == 50m);
        Checar("Reforma: tributos reconhecidos pelo nome da conta",
            Dre.TributoDe("CBS a recolher") == Tributo.Cbs && Dre.TributoDe("IBS UF/Município") == Tributo.Ibs && Dre.TributoDe("Imposto Seletivo") == Tributo.ImpostoSeletivo
            && Dre.TributoDe("ICMS ST") == Tributo.Icms && Dre.TributoDe("COFINS") == Tributo.PisCofins && Dre.TributoDe("Comissões") == Tributo.Outros
            && Dre.TributoDe("Simples Nacional - DAS") == Tributo.Simples && Dre.TributoDe("ISSQN") == Tributo.Iss);
        var dreReforma = Dre.Montar(Dre.Classificar(
        [
            L0(GrupoDre.ReceitaBruta, 1000, "Vendas"), L0(GrupoDre.Deducoes, 30, "Devoluções de vendas"), L0(GrupoDre.Deducoes, 90, "ICMS"),
            L0(GrupoDre.Deducoes, 9, "CBS"), L0(GrupoDre.DespesasOperacionais, 1, "IBS a recolher"), L0(GrupoDre.DespesasOperacionais, 100, "Aluguel"),
        ], null));
        decimal JanR(string titulo) => dreReforma.Single(l => l.Titulo == titulo).Meses[0];
        Checar("Reforma: guia de IBS lançada como despesa vira imposto sobre vendas; DRE separa tributos atuais (90) e da reforma (CBS 9 + IBS 1); devolução fora",
            JanR(Dre.ImpostosVenda) == 100m && JanR(Dre.TributosAtuais) == 90m && JanR(Dre.TributosReforma) == 10m
            && JanR(Dre.Nomes[GrupoDre.DespesasOperacionais]) == 100m && JanR(Dre.ReceitaLiquida) == 870m);
        var peVar = Dre.PontoEquilibrio(Dre.Classificar(
            [L0(GrupoDre.ReceitaBruta, 1000, "Vendas"), L0(GrupoDre.Deducoes, 100, "ICMS"), L0(GrupoDre.Cmv, 500, "CMV"),
             L0(GrupoDre.DespesasOperacionais, 100, "Comissões"), L0(GrupoDre.DespesasOperacionais, 150, "Aluguel")], null));
        Checar("PE com despesas variáveis: MC = 1 − 10% − 50% − 10% = 30%; fixos 150 → PE 500",
            peVar is { MargemContribuicaoPct: 0.3m, DespesasVariaveisPct: 0.1m, CustosFixosMedios: 150m, PontoEquilibrio: 500m });

        // Projeção: 2025 inteiro (receita 1000/mês, jul 2000) e 2026 até setembro com +10%; hoje = 15/10/2026
        var lancProj = new List<LancamentoDre>();
        void Mes(int a, int m, decimal receita)
        {
            var d = new DateOnly(a, m, 1);
            lancProj.Add(new() { Competencia = d, Grupo = GrupoDre.ReceitaBruta, Conta = "Vendas", Valor = receita });
            lancProj.Add(new() { Competencia = d, Grupo = GrupoDre.Deducoes, Conta = "ICMS", Valor = receita * 0.1m });
            lancProj.Add(new() { Competencia = d, Grupo = GrupoDre.Cmv, Conta = "CMV", Valor = receita * 0.5m });
            lancProj.Add(new() { Competencia = d, Grupo = GrupoDre.DespesasOperacionais, Conta = "Aluguel", Valor = 200 });
        }
        for (var m = 1; m <= 12; m++) Mes(2025, m, m == 7 ? 2000 : 1000);
        for (var m = 1; m <= 9; m++) Mes(2026, m, m == 7 ? 2200 : 1100);
        Mes(2026, 10, 300); // outubro em andamento: não entra no crescimento nem na estrutura
        var proj = Projecao.Projetar(lancProj, 2026, new DateOnly(2026, 10, 15))!;
        Checar("Projeção: crescimento dos meses fechados (+10%) aplicado ao mesmo mês do ano anterior; outubro em andamento é projetado (1100), com o parcial à parte",
            Math.Round(proj.CrescimentoRealizado!.Value, 4) == 0.10m && proj.MesesRealizados == 9
            && proj.Meses[9] is { Projetado: true, Receita: 1100m, ReceitaParcial: 300m } && proj.Receita == 1100m * 11 + 2200m);
        Checar("Projeção: estrutura dos últimos 12 meses fechados (MC 40%, fixos 200) e lucro projetado = 1100 × 40% − 200 = 240",
            proj.Estrutura is { Meses: 12, FixosMes: 200m } && proj.Estrutura.MargemContribuicaoPct == 0.4m && proj.Meses[11].LucroLiquido == 240m);
        var otimista = Projecao.Projetar(lancProj, 2026, new DateOnly(2026, 10, 15), 0.05m)!;
        Checar("Cenários: +5 p.p. só muda os meses que faltam (out–dez: 1000 × 1,15)", otimista.Meses[8].Receita == 1100m && otimista.Meses[11].Receita == 1150m);
        var simHoje = Projecao.Simular(proj.Estrutura);
        var simPreco = Projecao.Simular(proj.Estrutura, precoPct: 0.05m);
        Checar("Simulador: +2 p.p. de impostos sobre vendas tiram 2% da receita do lucro bruto",
            simHoje.LucroBruto - Projecao.Simular(proj.Estrutura, impostosPp: 0.02m).LucroBruto == simHoje.Receita * 0.02m);
        Checar("Simulador: +5% no preço sobe a receita 5%, o CMV não muda, e o ponto de equilíbrio cai",
            simPreco.Receita == simHoje.Receita * 1.05m && simPreco.LucroBruto - simHoje.LucroBruto == simHoje.Receita * 0.05m * 0.9m
            && simPreco.PontoEquilibrioMes < simHoje.PontoEquilibrioMes);

        // Regra suspeita: lucro ≥ 50% sobre o preço de venda (provável markup com a base errada no ERP)
        var bucha = new Produto { Codigo = "B", Descricao = "Bucha", Custo = 1.6643m, PrecoAtual = 4.5m, QtdMes = 46, ImpostosPct = 9.25m, LucroDesejadoPct = 82.78m, LucroSobreCusto = false };
        var normal = new Produto { Codigo = "N", Descricao = "Normal", Custo = 10m, PrecoAtual = 12m, QtdMes = 10, ImpostosPct = 9.25m, LucroDesejadoPct = 30m, LucroSobreCusto = false };
        var porCusto = new Produto { Codigo = "M", Descricao = "Markup", Custo = 10m, PrecoAtual = 12m, QtdMes = 10, ImpostosPct = 9.25m, LucroDesejadoPct = 80m, LucroSobreCusto = true };
        var analiseSusp = CalculoPreco.Calcular(new Cliente(), [bucha, normal, porCusto]).Produtos;
        Checar("Regra suspeita: 82,78% sobre o preço de venda dá a regra do ERP (R$ 20,88) mas não conta como abaixo da regra; 30% sobre o preço e 80% sobre o custo continuam normais",
            Math.Round(analiseSusp[0].PrecoRegraSistema!.Value, 2) == 20.88m && CalculoPreco.RegraSuspeita(bucha) && !CalculoPreco.AbaixoDaRegra(analiseSusp[0])
            && !CalculoPreco.RegraSuspeita(normal) && CalculoPreco.AbaixoDaRegra(analiseSusp[1]) && !CalculoPreco.RegraSuspeita(porCusto)
            && PainelResumo.ResumirPrecos(new Cliente(), [bucha, normal, porCusto]) is { RegraSuspeita: 1 });

        // "Lucro desejado sobre" em branco: segue a base predominante da empresa
        List<Produto> deMarkup = [new() { LucroDesejadoPct = 60, LucroSobreCusto = true }, new() { LucroDesejadoPct = 70, LucroSobreCusto = true },
                                  new() { LucroDesejadoPct = 20, LucroSobreCusto = false }, new() { LucroDesejadoPct = 80, LucroSobreCusto = null }];
        ImportacaoDados.PreencherBaseDoLucro(deMarkup);
        List<Produto> semBase = [new() { LucroDesejadoPct = 80, LucroSobreCusto = null }];
        ImportacaoDados.PreencherBaseDoLucro(semBase);
        Checar("Base do lucro em branco: empresa que forma preço por markup (maioria \"Custo\") → o branco vira markup e não é regra suspeita; sem nenhuma base, fica em branco",
            deMarkup[3].LucroSobreCusto == true && !CalculoPreco.RegraSuspeita(deMarkup[3]) && semBase[0].LucroSobreCusto is null && !CalculoPreco.RegraSuspeita(semBase[0]));

        // Lucro atual na base do ERP: custo 100, preço 160, sem impostos → markup 60%; mesmo preço como margem → 37,5%
        var mk = new Produto { Custo = 100, PrecoAtual = 160, ImpostosPct = 0, LucroDesejadoPct = 60, LucroSobreCusto = true };
        var mg = new Produto { Custo = 100, PrecoAtual = 160, ImpostosPct = 0, LucroDesejadoPct = 37.5m, LucroSobreCusto = false };
        Checar("Lucro atual na base do ERP: preço 160 sobre custo 100 = markup 60% ou margem 37,5% (cada um igual à própria regra)",
            CalculoPreco.LucroAtualPct(mk) == 60m && CalculoPreco.LucroAtualPct(mg) == 37.5m
            && CalculoPreco.PrecoRegraSistema(mk) == 160m && CalculoPreco.PrecoRegraSistema(mg) == 160m);

        // Ponto de equilíbrio pela DRE: receita 1000, deduções 100, CMV 500 => MC 40%; fixos 200 => PE 500
        var pe = Dre.PontoEquilibrio(
        [
            new LancamentoDre { Competencia = new DateOnly(2026, 1, 1), Grupo = GrupoDre.ReceitaBruta, Valor = 1000 },
            new LancamentoDre { Competencia = new DateOnly(2026, 1, 1), Grupo = GrupoDre.Deducoes, Valor = 100 },
            new LancamentoDre { Competencia = new DateOnly(2026, 1, 1), Grupo = GrupoDre.Cmv, Valor = 500 },
            new LancamentoDre { Competencia = new DateOnly(2026, 1, 1), Grupo = GrupoDre.DespesasOperacionais, Valor = 200 },
            new LancamentoDre { Competencia = new DateOnly(2026, 2, 1), Grupo = GrupoDre.ReceitaBruta, Valor = 999 } // mês sem despesas: fora
        ]);
        Checar("PE: MC 40%, PE 500, segurança 50%", pe is { Meses: 1, MargemContribuicaoPct: 0.4m, PontoEquilibrio: 500m, MargemSeguranca: 0.5m });
        var mesesPe = new List<LancamentoDre>();
        foreach (var (mes, receita, despesas) in new[] { (1, 1000m, 300m), (2, 1100m, 310m), (3, 900m, 290m), (4, 1050m, 150m), (5, 20m, 40m) })
        {
            mesesPe.Add(new LancamentoDre { Competencia = new DateOnly(2025, mes, 1), Grupo = GrupoDre.ReceitaBruta, Valor = receita });
            mesesPe.Add(new LancamentoDre { Competencia = new DateOnly(2025, mes, 1), Grupo = GrupoDre.DespesasOperacionais, Valor = despesas });
        }
        Checar("PE descarta mês com poucas vendas e mês com contas não lançadas",
            Dre.MesesSugeridosPe(mesesPe).SequenceEqual([new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1), new DateOnly(2025, 3, 1)]));
        Checar("Modelo SQL DRE passa na trava de leitura", !Lanca(() => BancoClienteService.ValidarSomenteLeitura(
            ModelosSql.AplicarPeriodo(ModelosSql.DreSqlServer, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1)))));
        Checar("Modelos SQL da BASELV passam na trava de leitura", !Lanca(() => BancoClienteService.ValidarSomenteLeitura(
            ModelosSql.AplicarPeriodo(ModelosSql.DreBaseLv, new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1))))
            && !Lanca(() => BancoClienteService.ValidarSomenteLeitura(ModelosSql.AplicarFilial(ModelosSql.PrecificacaoBaseLv, 1))));
        // Painel: venda do mês, projeção, comparações e ponto de equilíbrio
        LancamentoDre L(int ano, int mes, GrupoDre g, decimal v, string conta = "x") => new() { Competencia = new DateOnly(ano, mes, 1), Grupo = g, Conta = conta, Valor = v };
        List<LancamentoDre> painel =
        [
            L(2025, 9, GrupoDre.ReceitaBruta, 3000), L(2025, 9, GrupoDre.Cmv, 1500), L(2025, 9, GrupoDre.DespesasOperacionais, 600),
            L(2026, 8, GrupoDre.ReceitaBruta, 2600), L(2026, 8, GrupoDre.Deducoes, 100, "Devoluções de vendas"), L(2026, 8, GrupoDre.Cmv, 1300), L(2026, 8, GrupoDre.DespesasOperacionais, 600),
            L(2026, 9, GrupoDre.ReceitaBruta, 1050), L(2026, 9, GrupoDre.Deducoes, 50, "Devoluções de vendas"), L(2026, 9, GrupoDre.Deducoes, 30, "ICMS"),
        ];
        var emAndamento = PainelResumo.Resumir(painel, new DateTime(2026, 9, 11, 0, 5, 0));
        Checar("Painel: mês em andamento (sincronizado dia 11 às 00:05 = 10 dias), venda líquida 1000, projeção 3000",
            emAndamento is { Parcial: true, DiasComDados: 10, VendaLiquida: 1000m, ProjecaoVenda: 3000m });
        Checar("Painel: compara a projeção com o mês anterior (+20%) e com o mesmo mês do ano passado (0%)",
            emAndamento?.VariacaoMesAnterior == 0.2m && emAndamento?.VariacaoAnoAnterior == 0m);
        Checar("Painel: ponto de equilíbrio pelos 12 meses anteriores e progresso do faturamento bruto do mês",
            emAndamento?.Pe is not null && emAndamento.ProgressoPe is decimal prog && prog > 0.8m && prog < 0.9m);
        var fechado = PainelResumo.Resumir(painel, new DateTime(2026, 10, 2, 0, 5, 0));
        Checar("Painel: mês fechado não tem projeção e compara o valor real (−60% vs agosto)",
            fechado is { Parcial: false, ProjecaoVenda: null } && fechado.VariacaoMesAnterior == -0.6m);
        Checar("Painel: começo do mês (2 dias) ainda sem projeção", PainelResumo.Resumir(painel, new DateTime(2026, 9, 3, 0, 5, 0)) is { Parcial: true, ProjecaoVenda: null });
        Checar("Painel: margem bruta do mês em andamento (1050 − 80 de deduções = 970 → 92,4%)",
            emAndamento?.MargemBrutaPct is decimal mb && Math.Round(mb, 3) == 0.924m);
        Checar("Painel: resultado do último mês fechado (ago: 2600 − 100 − 1300 − 600 = 600; 23,1% da receita)",
            emAndamento is { MesFechado: var mf, ResultadoMesFechado: 600m } && mf == new DateOnly(2026, 8, 1)
            && Math.Round(emAndamento.MargemLiquidaMesFechado!.Value, 3) == 0.231m);
        var clientePrecos = new Cliente { ImpostosPct = 10 };
        var resumoPrecos = PainelResumo.ResumirPrecos(clientePrecos,
        [
            new Produto { Codigo = "ok", Custo = 10, PrecoAtual = 20, QtdMes = 5 },
            new Produto { Codigo = "prejuizo", Custo = 20, PrecoAtual = 21, QtdMes = 2 },       // 21 × 0,9 − 20 < 0
            new Produto { Codigo = "semcusto", Custo = 0, PrecoAtual = 15, QtdMes = 1 },
            new Produto { Codigo = "abaixo", Custo = 10, PrecoAtual = 15, QtdMes = 4, ImpostosPct = 10, LucroDesejadoPct = 50, LucroSobreCusto = true },
            new Produto { Codigo = "semvenda", Custo = 20, PrecoAtual = 1, QtdMes = 0 },
        ]);
        Checar("Painel: preços com problema contam só produtos com venda (margem negativa, sem custo, abaixo da regra)",
            resumoPrecos is { ComVenda: 4, ComProblema: 3, MargemNegativa: 1, SemCusto: 1, AbaixoRegra: 1 }
            && resumoPrecos.VendaMesEmRisco == 21 * 2 + 15 * 1 + 15 * 4);
        Checar("Painel: sem receita não há resumo", PainelResumo.Resumir([L(2026, 9, GrupoDre.Cmv, 10)], DateTime.Now) is null);
        Checar("Dados desatualizados: mais de 36 h ou nunca atualizados",
            DataDosDados.Desatualizado(DateTime.Now.AddHours(-40), DateTime.Now) && !DataDosDados.Desatualizado(DateTime.Now.AddHours(-10), DateTime.Now)
            && DataDosDados.Desatualizado(null, DateTime.Now));
        // Comercial: relatório de metas (Diário, Acumulado, Ano anterior, Meta geral) e dias úteis
        var feriadosTeste = new HashSet<DateOnly> { new(2026, 9, 7) }; // segunda-feira
        Checar("Dias úteis como o ERP (seg–sex 1, sáb ½): set/2026 = 22 + 4×½ = 24; menos o feriado de 07/09 = 23; até 11/09 = 8,5",
            Calendario.DiasUteis(new DateOnly(2026, 9, 1), new HashSet<DateOnly>()) == 24m
            && Calendario.DiasUteis(new DateOnly(2026, 9, 1), feriadosTeste) == 23m
            && Calendario.DiasUteis(new DateOnly(2026, 9, 1), feriadosTeste, new DateOnly(2026, 9, 11)) == 8.5m);
        var agosto = Calendario.DiasUteis(new DateOnly(2026, 8, 1), new HashSet<DateOnly>());
        Checar("Dias úteis do print do ERP: ago/2026 = 21 + 5×½ = 23,5 → 24; meta do dia 276.250 ÷ 24 = 11.510",
            agosto == 23.5m && Calendario.Arredondar(agosto) == 24 && Math.Round(276250m / Calendario.Arredondar(agosto), 0) == 11510m);
        VendaVendedor Vd(string vend, DateOnly dia, decimal bruta, decimal dev, decimal cmv, int docs, int itens, string? ger = "G") =>
            new() { Data = dia, Competencia = new DateOnly(dia.Year, dia.Month, 1), Vendedor = vend, NomeVendedor = "Nome " + vend, Gerente = ger,
                    NomeGerente = ger is null ? null : "Gerente " + ger, VendaBruta = bruta, Devolucoes = dev, Cmv = cmv, Documentos = docs, Itens = itens };
        var d11 = new DateOnly(2026, 9, 11);
        List<VendaVendedor> vendasMetas =
        [
            Vd("A", new DateOnly(2026, 9, 1), 4000, 0, 2400, 40, 90), Vd("A", d11, 1100, 100, 600, 10, 25),
            Vd("B", d11, 500, 0, 300, 5, 10), Vd("C", new DateOnly(2026, 9, 2), 300, 0, 100, 3, 3, ger: null),
            Vd("A", new DateOnly(2025, 9, 5), 4000, 0, 2000, 30, 60), Vd("A", new DateOnly(2025, 9, 20), 9999, 0, 0, 1, 1), // depois do dia 11: fora do período
            Vd("A", new DateOnly(2026, 9, 14), 7777, 0, 0, 1, 1), // depois da data final: fora
        ];
        List<MetaVendedor> metasTeste = [new() { Competencia = new DateOnly(2026, 9, 1), Vendedor = "A", Meta = 25000 }, new() { Competencia = new DateOnly(2026, 9, 1), Vendedor = "D", Meta = 5000 }];
        var rel = ComercialResumo.Montar(d11, vendasMetas, metasTeste, feriadosTeste);
        var la = rel.Vendedores.Single(v => v.Vendedor == "A");
        Checar("Metas: diário = só o dia (1000; meta do dia 25000÷23 = 1086,96 → 92%); acumulado = dia 1 até a data (5000); dias úteis 9/23",
            la is { Dia.Venda: 1000m, Acumulado.Venda: 5000m, Acumulado.Documentos: 50 } && Math.Round(la.MetaDia!.Value, 2) == 1086.96m
            && Math.Round(la.AtingimentoDia!.Value, 2) == 0.92m && rel is { DiasUteisMes: 23, DiasUteisAcumulado: 9, DiasUteisMesExato: 23m });
        Checar("Metas: meta acumulada = 25000 × 9 ÷ 23 = 9782,61 (51,1%); meta geral 25000 (20%); diferença −20000 (faltam)",
            Math.Round(la.MetaAcumulada!.Value, 2) == 9782.61m && Math.Round(la.AtingimentoAcumulado!.Value, 3) == 0.511m && la.AtingimentoGeral == 0.2m && la.Diferenca == -20000m);
        Checar("Metas: ano anterior = mesmo período (01 a 11/09/2025: 4000, margem 50%) e crescimento +25%; CMV/margem/ticket do acumulado",
            la.AnoAnterior.Venda == 4000m && la.AnoAnterior.MargemPct == 0.5m && la.Crescimento == 0.25m && la.Acumulado.MargemPct == 0.4m && la.Acumulado.TicketMedio == 100m);
        Checar("Metas: vendedor com meta e sem venda aparece; totais somam vendas e metas; sem gerente fica separado",
            rel.Vendedores.Any(v => v is { Vendedor: "D", MetaMes: 5000m, Diferenca: -5000m }) && rel.Total is { Acumulado.Venda: 5800m, MetaMes: 30000m }
            && rel.Grupos.Any(g => g.Gerente is null && g.Vendedores.Any(v => v.Vendedor == "C")));
        var ajustado = ComercialResumo.Montar(d11, vendasMetas, metasTeste, feriadosTeste, diasUteisAjuste: 24);
        Checar("Metas: dias úteis ajustados à mão (24) mudam a meta do dia (25000÷24) e mantêm o calculado para referência",
            ajustado is { DiasUteisMes: 24, DiasUteisCalculados: 23 } && Math.Round(ajustado.Vendedores.Single(v => v.Vendedor == "A").MetaDia!.Value, 2) == 1041.67m);
        var comCota = ComercialResumo.Montar(d11, vendasMetas,
            [.. metasTeste, new MetaVendedor { Competencia = new DateOnly(2026, 9, 1), Vendedor = "A", Meta = 30000, DoErp = true }], feriadosTeste);
        Checar("Metas: a cota do ERP tem preferência sobre a meta digitada no portal", comCota.Vendedores.Single(v => v.Vendedor == "A").MetaMes == 30000m);
        var comZerada = ComercialResumo.Montar(d11, vendasMetas,
            [.. metasTeste, new MetaVendedor { Competencia = new DateOnly(2026, 9, 1), Vendedor = "Z", Meta = 0, DoErp = true }], feriadosTeste);
        Checar("Metas: estrutura com cota zero e sem vendas é 'zerada'; com meta e sem venda (D) ou com venda (A) não é",
            comZerada.Vendedores.Single(v => v.Vendedor == "Z").Zerada && !comZerada.Vendedores.Single(v => v.Vendedor == "D").Zerada
            && !comZerada.Vendedores.Single(v => v.Vendedor == "A").Zerada);
        var soAnoAnterior = ComercialResumo.Montar(d11, [.. vendasMetas, Vd("X", new DateOnly(2025, 9, 3), 800, 0, 400, 2, 2)], metasTeste, feriadosTeste);
        Checar("Metas: sem meta e sem venda no mês (só ano anterior) não aparece, mas o ano anterior entra no total, como no ERP",
            soAnoAnterior.Vendedores.Single(v => v.Vendedor == "X").Zerada && soAnoAnterior.Total.AnoAnterior.Venda == 4800m);
        var tabelaComercial = new System.Data.DataTable();
        foreach (var c in new[] { "Data", "Vendedor", "VendaBruta", "Permuta" }) tabelaComercial.Columns.Add(c, c == "Vendedor" ? typeof(string) : typeof(object));
        tabelaComercial.Rows.Add(new DateTime(2026, 8, 3), "A", 100m, 1);
        tabelaComercial.Rows.Add(new DateTime(2026, 8, 3), "A", 50m, 0);
        var lidasComercial = ImportacaoDados.VendasVendedores(tabelaComercial, 1);
        var semColuna = ImportacaoDados.VendasVendedores(new System.Data.DataView(tabelaComercial).ToTable(false, "Data", "Vendedor", "VendaBruta"), 1);
        Checar("Comercial: coluna Permuta separa as vendas em permuta; SQL antigo (sem a coluna) = tudo sem permuta",
            lidasComercial.Count(v => v.Permuta) == 1 && lidasComercial.Single(v => v.Permuta).VendaBruta == 100m && semColuna.All(v => !v.Permuta));
        var resumoMeta = ResumoMeta.De(rel)!;
        Checar("Painel: meta do mês = total do Comercial (5800 de 30000; ritmo = venda − meta acumulada; faltam 24200 em 14 dias úteis = 1728,57/dia; projeção 5800÷9×23)",
            resumoMeta is { Venda: 5800m, Meta: 30000m, DiasUteisRestantes: 14, VendedoresComMeta: 2, VendedoresNaMeta: 0 }
            && resumoMeta.Ritmo == 5800m - rel.Total.MetaAcumulada && Math.Round(resumoMeta.FaltaPorDia!.Value, 2) == 1728.57m
            && Math.Round(resumoMeta.Projecao!.Value, 2) == 14822.22m
            && ResumoMeta.De(ComercialResumo.Montar(d11, vendasMetas, [], feriadosTeste)) is null);
        var pend = new SituacaoPedido(1, new DateTime(2026, 10, 27, 9, 16, 0), PedidoSincronizacao.Pendente, null, null, null);
        Checar("Sincronizar agora: pedido pendente que uma sincronização feita depois dele já atendeu conta como concluído; sem ela, segue pendente e após 20 min fica 'sem resposta' (botão livre)",
            PedidoSincronizacao.Atendido(pend, new DateTime(2026, 10, 27, 9, 18, 28)) is { Status: PedidoSincronizacao.Concluido, EmAndamento: false }
            && PedidoSincronizacao.Atendido(pend, null).EmAndamento && !pend.SemResposta(pend.PedidoEm.AddMinutes(5)) && pend.SemResposta(pend.PedidoEm.AddMinutes(21)));
        var soComCota = ComercialResumo.Montar(d11, vendasMetas, metasTeste, feriadosTeste, somente: new HashSet<string> { "A" });
        Checar("Metas: filtro 'só quem tem meta no ERP' mostra só esses e os totais somam só eles",
            soComCota.Vendedores.Select(v => v.Vendedor).SequenceEqual(["A"]) && soComCota.Total is { Acumulado.Venda: 5000m, MetaMes: 25000m });

        Checar("Grupo por texto 'CMV'", Dre.TentarGrupo("CMV", out var g) && g == GrupoDre.Cmv);
        Checar("Grupo inválido rejeitado", !Dre.TentarGrupo("xyz", out _));

        // Planilhas
        var csv = "Código;Descrição;Custo;Preço;QtdMes\n001;\"Arroz; 5kg\";1.234,56;R$ 10,50;3\n";
        var lidas = Planilhas.Ler(new MemoryStream(Encoding.UTF8.GetBytes(csv)), "p.csv");
        Checar("CSV com ; entre aspas", lidas.Count == 1 && lidas[0].Campo("descricao") == "Arroz; 5kg");
        Checar("Decimal pt-BR", Planilhas.Decimal(lidas[0].Campo("custo")) == 1234.56m && Planilhas.Decimal(lidas[0].Campo("preco")) == 10.50m);
        Checar("Competência mm/aaaa", Planilhas.Mes("03/2026") == new DateOnly(2026, 3, 1));
        var xlsx = Planilhas.GerarXlsx("T", ["Codigo", "Custo"], [["A1", 2.5m]]);
        var relida = Planilhas.Ler(new MemoryStream(xlsx), "t.xlsx");
        Checar("XLSX ida e volta", relida.Count == 1 && relida[0].Campo("codigo") == "A1" && Planilhas.Decimal(relida[0].Campo("custo")) == 2.5m);

        // Aviso de revisão de preços
        var itensAviso = CalculoPreco.Calcular(new Cliente { ImpostosPct = 10, MargemDesejadaPct = 10 },
        [
            new Produto { Codigo = "001", Descricao = "Arroz 5kg", Custo = 100m, PrecoAtual = 120m, ImpostosPct = 9.25m, LucroDesejadoPct = 30m, LucroSobreCusto = true },
            new Produto { Codigo = "002", Descricao = "Feijão 1kg", Custo = 5m, PrecoAtual = 6m }
        ]).Produtos;
        var aviso = AvisoPrecos.Montar("Loja X", "João", itensAviso, "gestor@loja.com", new DateTime(2026, 9, 24));
        Checar("Aviso de preços: cabeçalho, destinatário, itens e remetente",
            aviso.StartsWith("*Revisão de preços — Loja X*\n24/09/2026") && aviso.Contains("Olá, João!") && aviso.Contains("Os 2 produtos")
            && aviso.Contains("• *001* – Arroz 5kg") && aviso.Contains("Regra do ERP: R$ 143,25 (-16,2%)") && aviso.Contains("Enviado por gestor@loja.com"));
        var muitos = Enumerable.Range(1, AvisoPrecos.MaximoItens + 5)
            .Select(i => new AnaliseProduto(new Produto { Codigo = $"P{i}", Descricao = "x", PrecoAtual = 1 }, null, 1, 0, 0, 0, 0)).ToList();
        var avisoLongo = AvisoPrecos.Montar("Loja X", null, muitos, null, DateTime.Today);
        Checar("Aviso de preços com muitos itens lista 30 e resume o resto",
            avisoLongo.Contains("• *P30*") && !avisoLongo.Contains("• *P31*") && avisoLongo.Contains("… e mais 5 produto(s)"));

        // Perfis: cada cliente só enxerga a própria empresa
        var alertasTeste = new List<Alerta> { new() { Id = 1, ClienteId = 1 }, new() { Id = 2, ClienteId = 2 }, new() { Id = 3, ClienteId = 1 } }.AsQueryable();
        var adm = new Acesso("a", null, Perfil.VogelAdministrador, null, null);
        var operador = new Acesso("o", null, Perfil.VogelOperador, null, null);
        var gestor1 = new Acesso("g", null, Perfil.ClienteGestor, 1, "Loja 1");
        var usuario2 = new Acesso("u", null, Perfil.ClienteUsuario, 2, "Loja 2");
        var semEmpresa = new Acesso("s", null, Perfil.ClienteGestor, null, null);
        Checar("Perfil Vogel vê todos os clientes", adm.Filtrar(alertasTeste).Count() == 3 && operador.Filtrar(alertasTeste).Count() == 3);
        Checar("Cliente vê só a própria empresa", gestor1.Filtrar(alertasTeste).All(a => a.ClienteId == 1) && gestor1.Filtrar(alertasTeste).Count() == 2
            && usuario2.Filtrar(alertasTeste).Single().Id == 2 && !usuario2.PodeVerCliente(1));
        Checar("Cliente sem empresa ativa não vê nada", semEmpresa.Filtrar(alertasTeste).Count() == 0 && !semEmpresa.GerenciaUsuarios && !semEmpresa.Altera);
        Checar("Só a Vogel consulta o banco do cliente; só o administrador edita SQL",
            operador.ConsultaBancoCliente && !gestor1.ConsultaBancoCliente && adm.EditaSql && !operador.EditaSql && !gestor1.EditaSql);
        Checar("Gestor atualiza DRE/precificação do banco com o SQL da Vogel; usuário e sem empresa não",
            gestor1.AtualizaDoBanco && operador.AtualizaDoBanco && !usuario2.AtualizaDoBanco && !semEmpresa.AtualizaDoBanco);
        Checar("Usuário do cliente só consulta; gestor altera", !usuario2.Altera && gestor1.Altera && !usuario2.EnviaMensagens && !gestor1.EnviaMensagens);
        Checar("Gestor cria só perfis de cliente e só da própria empresa",
            !gestor1.PerfisQuePodeAtribuir.Contains(Perfil.VogelAdministrador)
            && gestor1.PodeGerenciar(new ApplicationUser { Id = "x", Perfil = Perfil.ClienteUsuario, ClienteId = 1 })
            && !gestor1.PodeGerenciar(new ApplicationUser { Id = "y", Perfil = Perfil.ClienteUsuario, ClienteId = 2 })
            && !gestor1.PodeGerenciar(new ApplicationUser { Id = "z", Perfil = Perfil.VogelOperador })
            && !gestor1.PodeGerenciar(new ApplicationUser { Id = "g", Perfil = Perfil.ClienteGestor, ClienteId = 1 }));
        Checar("Gestor programa rotinas e vê a DRE; usuário do cliente não",
            gestor1.ProgramaRotinas && gestor1.VeDre && !usuario2.ProgramaRotinas && !usuario2.VeDre && operador.ProgramaRotinas && !semEmpresa.ProgramaRotinas);
        Checar("Operador não gerencia usuários", !operador.GerenciaUsuarios && operador.PerfisQuePodeAtribuir.Count == 0);
        Checar("Impressão do CNPJ: só dígitos, SHA-256 com 64 caracteres",
            Cliente.CalcularImpressao("12.345.678/0001-90") is { Length: 64 } h && h == Cliente.CalcularImpressao("12345678000190") && Cliente.CalcularImpressao("") is null);

        // Ciclo do alerta e disparo automático (banco em memória, Pagebot simulado)
        foreach (var (nome, ok) in AutoTesteDisparo.ExecutarAsync().GetAwaiter().GetResult())
            Checar(nome, ok);

        Console.WriteLine(falhas == 0 ? "Todos os testes passaram." : $"{falhas} teste(s) falharam.");
        return falhas == 0 ? 0 : 1;
    }
}
