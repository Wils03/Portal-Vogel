using Portal.Data;

namespace Portal.Services;

public record AnaliseProduto(
    Produto Produto,
    decimal? PrecoRegraSistema,
    decimal PrecoSugerido,
    decimal MargemContribuicao,
    decimal MargemContribuicaoPct,
    decimal MargemLiquidaPct,
    decimal DiferencaPct);

public record ResumoPrecificacao(
    decimal DespesasFixasPct,
    decimal MarkupDivisor,
    decimal MarkupMultiplicador,
    decimal MargemContribuicaoMediaPct,
    decimal PontoEquilibrioValor,
    decimal FaturamentoProdutos,
    List<AnaliseProduto> Produtos);

/// <summary>
/// Formação de preço por markup e ponto de equilíbrio.
/// Regra do ERP do cliente (quando o produto traz grupo e lucro desejado):
///   lucro sobre o preço de venda (margem): Preço = Custo / (1 - (impostos + lucro))
///   lucro sobre o custo (markup):          Preço = Custo × (1 + lucro) / (1 - impostos)
/// Preço sugerido = Custo / (1 - (impostos + comissão + outros variáveis + despesas fixas + margem desejada)).
/// Ponto de equilíbrio (R$) = Custos fixos mensais / Margem de contribuição média %.
/// </summary>
public static class CalculoPreco
{
    public static ResumoPrecificacao Calcular(Cliente c, IEnumerable<Produto> produtos)
    {
        var outrosPct = (c.ComissaoPct + c.OutrosVariaveisPct) / 100m;
        var variaveisPct = c.ImpostosPct / 100m + outrosPct;
        var fixasPct = c.FaturamentoMedioMensal > 0 ? c.CustosFixosMensais / c.FaturamentoMedioMensal : 0m;
        var divisor = 1m - variaveisPct - fixasPct - c.MargemDesejadaPct / 100m;
        var multiplicador = divisor > 0 ? 1m / divisor : 0m;

        var analises = produtos.Select(p =>
        {
            // Impostos do grupo do produto (quando vieram do sistema) substituem o % geral do cliente
            var variaveisProduto = p.ImpostosPct is decimal imp ? imp / 100m + outrosPct : variaveisPct;
            var divisorProduto = 1m - variaveisProduto - fixasPct - c.MargemDesejadaPct / 100m;
            var sugerido = divisorProduto > 0 ? p.Custo / divisorProduto : 0m;
            var mc = p.PrecoAtual * (1 - variaveisProduto) - p.Custo;
            var mcPct = p.PrecoAtual > 0 ? mc / p.PrecoAtual : 0m;
            var liquida = p.PrecoAtual > 0 ? (p.PrecoAtual * (1 - variaveisProduto - fixasPct) - p.Custo) / p.PrecoAtual : 0m;
            var diferenca = sugerido > 0 ? (p.PrecoAtual - sugerido) / sugerido : 0m;
            return new AnaliseProduto(p, PrecoRegraSistema(p), sugerido, mc, mcPct, liquida, diferenca);
        }).ToList();

        // Margem de contribuição média ponderada pelo faturamento de cada produto
        var faturamento = analises.Sum(a => a.Produto.PrecoAtual * a.Produto.QtdMes);
        var mcMedia = faturamento > 0
            ? analises.Sum(a => a.MargemContribuicao * a.Produto.QtdMes) / faturamento
            : (analises.Count > 0 ? analises.Average(a => a.MargemContribuicaoPct) : 1m - variaveisPct);

        var pe = mcMedia > 0 ? c.CustosFixosMensais / mcMedia : 0m;
        return new(fixasPct, divisor, multiplicador, mcMedia, pe, faturamento, analises);
    }

    /// <summary>Preço pela regra de formação do ERP do cliente (grupo de impostos + lucro desejado).</summary>
    /// <summary>Preço mais de 2% abaixo da regra do ERP conta como problema (mesmo critério do "marcar abaixo da regra").</summary>
    public const decimal ToleranciaRegra = -0.02m;

    public static bool AbaixoDaRegra(AnaliseProduto a) => AvisoPrecos.DiferencaRegra(a) < ToleranciaRegra;

    /// <summary>"Só problemas": margem de contribuição negativa, preço abaixo da regra do ERP ou custo zerado.</summary>
    public static bool TemProblema(AnaliseProduto a) => a.MargemContribuicao < 0 || AbaixoDaRegra(a) || a.Produto.Custo <= 0;

    public static decimal? PrecoRegraSistema(Produto p)
    {
        if (p.ImpostosPct is not decimal imp || p.LucroDesejadoPct is not decimal lucro || p.Custo <= 0) return null;
        imp /= 100m;
        lucro /= 100m;
        if (p.LucroSobreCusto == true)
            return imp < 1 ? p.Custo * (1 + lucro) / (1 - imp) : null;
        return imp + lucro < 1 ? p.Custo / (1 - imp - lucro) : null;
    }
}

/// <summary>Ponto de equilíbrio a partir da DRE: deduções e CMV são variáveis; despesas operacionais e financeiras são fixas.</summary>
public record PontoEquilibrioDre(
    int Meses,
    decimal FaturamentoMedio,
    decimal CustosFixosMedios,
    decimal DeducoesPct,
    decimal CmvPct,
    decimal MargemContribuicaoPct,
    decimal PontoEquilibrio,
    decimal MargemSeguranca);

public record LinhaDre(string Titulo, decimal[] Meses, bool Total, bool Negativa)
{
    public decimal Ano => Meses.Sum();
}

public static class Dre
{
    public static readonly Dictionary<GrupoDre, string> Nomes = new()
    {
        [GrupoDre.ReceitaBruta] = "Receita bruta",
        [GrupoDre.Deducoes] = "(-) Deduções / impostos sobre vendas",
        [GrupoDre.Cmv] = "(-) CMV / custo das vendas",
        [GrupoDre.DespesasOperacionais] = "(-) Despesas operacionais",
        [GrupoDre.ReceitasFinanceiras] = "(+) Receitas financeiras",
        [GrupoDre.DespesasFinanceiras] = "(-) Despesas financeiras",
        [GrupoDre.ImpostosSobreLucro] = "(-) IR / CSLL"
    };

    /// <summary>Aceita o nome do enum ou textos comuns vindos de planilhas.</summary>
    public static bool TentarGrupo(string? texto, out GrupoDre grupo)
    {
        var t = (texto ?? "").Trim().ToLowerInvariant();
        if (Enum.TryParse(t, true, out grupo)) return true;
        foreach (var (g, nome) in Nomes)
            if (nome.ToLowerInvariant().Contains(t) && t.Length > 2) { grupo = g; return true; }
        grupo = t switch
        {
            "receita" or "faturamento" or "vendas" => GrupoDre.ReceitaBruta,
            "deducao" or "dedução" or "impostos" => GrupoDre.Deducoes,
            "cmv" or "cpv" or "custo" => GrupoDre.Cmv,
            "despesa" or "despesas" => GrupoDre.DespesasOperacionais,
            "ir" or "csll" => GrupoDre.ImpostosSobreLucro,
            _ => (GrupoDre)(-1)
        };
        return Enum.IsDefined(grupo);
    }

    private record ResumoMes(DateOnly Mes, decimal Receita, decimal Deducoes, decimal Cmv, decimal Fixos);

    private static List<ResumoMes> ResumirMeses(IEnumerable<LancamentoDre> lancamentos) =>
        lancamentos.GroupBy(l => l.Competencia)
            .Select(g => new ResumoMes(
                g.Key,
                g.Where(l => l.Grupo == GrupoDre.ReceitaBruta).Sum(l => l.Valor),
                g.Where(l => l.Grupo == GrupoDre.Deducoes).Sum(l => l.Valor),
                g.Where(l => l.Grupo == GrupoDre.Cmv).Sum(l => l.Valor),
                g.Where(l => l.Grupo is GrupoDre.DespesasOperacionais or GrupoDre.DespesasFinanceiras).Sum(l => l.Valor)
                    - g.Where(l => l.Grupo == GrupoDre.ReceitasFinanceiras).Sum(l => l.Valor)))
            .Where(m => m.Receita > 0 && m.Fixos > 0)
            .OrderBy(m => m.Mes)
            .ToList();

    private static decimal Mediana(IEnumerable<decimal> valores)
    {
        var v = valores.OrderBy(x => x).ToList();
        return v.Count == 0 ? 0 : v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    /// <summary>Meses com vendas e despesas lançadas.</summary>
    public static List<DateOnly> MesesComMovimento(IEnumerable<LancamentoDre> lancamentos) =>
        ResumirMeses(lancamentos).Select(m => m.Mes).ToList();

    /// <summary>
    /// Sugere os meses "fechados": receita de pelo menos 50% e despesas de pelo menos 70% da mediana.
    /// Descarta mês com poucos dias de venda ou com contas ainda não lançadas.
    /// </summary>
    public static List<DateOnly> MesesSugeridosPe(IEnumerable<LancamentoDre> lancamentos)
    {
        var meses = ResumirMeses(lancamentos);
        var receitaMediana = Mediana(meses.Select(m => m.Receita));
        var fixosMediana = Mediana(meses.Select(m => m.Fixos));
        return meses.Where(m => m.Receita >= receitaMediana * 0.5m && m.Fixos >= fixosMediana * 0.7m).Select(m => m.Mes).ToList();
    }

    /// <summary>Calcula com os meses informados (padrão: <see cref="MesesSugeridosPe"/>).</summary>
    public static PontoEquilibrioDre? PontoEquilibrio(IEnumerable<LancamentoDre> lancamentos, IReadOnlyCollection<DateOnly>? mesesEscolhidos = null)
    {
        var lista = lancamentos.ToList();
        var escolhidos = mesesEscolhidos ?? MesesSugeridosPe(lista);
        var meses = ResumirMeses(lista).Where(m => escolhidos.Contains(m.Mes)).ToList();
        if (meses.Count == 0) return null;

        var receita = meses.Sum(m => m.Receita);
        var deducoesPct = meses.Sum(m => m.Deducoes) / receita;
        var cmvPct = meses.Sum(m => m.Cmv) / receita;
        var mc = 1m - deducoesPct - cmvPct;
        var fixos = meses.Average(m => m.Fixos);
        var faturamento = receita / meses.Count;
        var pe = mc > 0 ? fixos / mc : 0m;
        var seguranca = pe > 0 ? (faturamento - pe) / faturamento : 0m;
        return new(meses.Count, faturamento, fixos, deducoesPct, cmvPct, mc, pe, seguranca);
    }

    public const string Devolucoes = "(-) Devoluções de vendas";
    public const string VendaLiquida = "= Venda líquida";
    public const string ImpostosVenda = "(-) Impostos sobre vendas";

    /// <summary>Conta de devolução de venda (ex.: "Devoluções de vendas"), dentro das Deduções.</summary>
    public static bool EhDevolucao(string conta) => Planilhas.Normalizar(conta).StartsWith("devoluc");

    public static List<LinhaDre> Montar(IEnumerable<LancamentoDre> lancamentos)
    {
        var porGrupo = Enum.GetValues<GrupoDre>().ToDictionary(g => g, _ => new decimal[12]);
        foreach (var l in lancamentos)
            porGrupo[l.Grupo][l.Competencia.Month - 1] += l.Valor;

        // Deduções = devoluções + impostos sobre vendas; mostradas separadas, com a venda líquida (a "Venda" do ERP) no meio
        var devolucoes = new decimal[12];
        foreach (var l in lancamentos)
            if (l.Grupo == GrupoDre.Deducoes && EhDevolucao(l.Conta))
                devolucoes[l.Competencia.Month - 1] += l.Valor;

        decimal[] Calc(Func<int, decimal> f) => Enumerable.Range(0, 12).Select(f).ToArray();
        var rb = porGrupo[GrupoDre.ReceitaBruta];
        var vendaLiquida = Calc(i => rb[i] - devolucoes[i]);
        var impostosVenda = Calc(i => porGrupo[GrupoDre.Deducoes][i] - devolucoes[i]);
        var rl = Calc(i => rb[i] - porGrupo[GrupoDre.Deducoes][i]);
        var lb = Calc(i => rl[i] - porGrupo[GrupoDre.Cmv][i]);
        var ro = Calc(i => lb[i] - porGrupo[GrupoDre.DespesasOperacionais][i]);
        var lair = Calc(i => ro[i] + porGrupo[GrupoDre.ReceitasFinanceiras][i] - porGrupo[GrupoDre.DespesasFinanceiras][i]);
        var ll = Calc(i => lair[i] - porGrupo[GrupoDre.ImpostosSobreLucro][i]);

        return
        [
            new(Nomes[GrupoDre.ReceitaBruta], rb, false, false),
            new(Devolucoes, devolucoes, false, true),
            new(VendaLiquida, vendaLiquida, true, false),
            new(ImpostosVenda, impostosVenda, false, true),
            new("= Receita líquida", rl, true, false),
            new(Nomes[GrupoDre.Cmv], porGrupo[GrupoDre.Cmv], false, true),
            new("= Lucro bruto", lb, true, false),
            new(Nomes[GrupoDre.DespesasOperacionais], porGrupo[GrupoDre.DespesasOperacionais], false, true),
            new("= Resultado operacional", ro, true, false),
            new(Nomes[GrupoDre.ReceitasFinanceiras], porGrupo[GrupoDre.ReceitasFinanceiras], false, false),
            new(Nomes[GrupoDre.DespesasFinanceiras], porGrupo[GrupoDre.DespesasFinanceiras], false, true),
            new("= Resultado antes do IR", lair, true, false),
            new(Nomes[GrupoDre.ImpostosSobreLucro], porGrupo[GrupoDre.ImpostosSobreLucro], false, true),
            new("= Lucro líquido", ll, true, false)
        ];
    }
}
