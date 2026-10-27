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

/// <summary>
/// Ponto de equilíbrio a partir da DRE: deduções, CMV e despesas variáveis acompanham a venda;
/// despesas fixas, depreciação e resultado financeiro são fixos.
/// </summary>
public record PontoEquilibrioDre(
    int Meses,
    decimal FaturamentoMedio,
    decimal CustosFixosMedios,
    decimal DeducoesPct,
    decimal CmvPct,
    decimal MargemContribuicaoPct,
    decimal PontoEquilibrio,
    decimal MargemSeguranca,
    decimal DespesasVariaveisPct = 0);

/// <param name="Grupo">Grupo cujas contas podem ser abertas na tela (nulo nas linhas de total).</param>
public record LinhaDre(string Titulo, decimal[] Meses, bool Total, bool Negativa, GrupoDre? Grupo = null)
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
        [GrupoDre.DespesasOperacionais] = "(-) Despesas fixas",
        [GrupoDre.ReceitasFinanceiras] = "(+) Receitas financeiras",
        [GrupoDre.DespesasFinanceiras] = "(-) Despesas financeiras",
        [GrupoDre.ImpostosSobreLucro] = "(-) IR / CSLL",
        [GrupoDre.DespesasVariaveis] = "(-) Despesas variáveis",
        [GrupoDre.Depreciacao] = "(-) Depreciação e amortização"
    };

    /// <summary>Grupos de despesa que a Vogel/gestor reclassifica conta a conta (fixa, variável, depreciação).</summary>
    public static readonly GrupoDre[] GruposDeDespesa = [GrupoDre.DespesasOperacionais, GrupoDre.DespesasVariaveis, GrupoDre.Depreciacao];

    /// <summary>
    /// Sugestão pelo nome da conta, enquanto ninguém classificou: comissões, taxas de cartão, fretes e royalties são variáveis;
    /// depreciação/amortização vai para a linha própria. Nulo = fica fixa.
    /// </summary>
    public static GrupoDre? Sugerir(string conta)
    {
        var c = Planilhas.Normalizar(conta);
        if (c.Contains("deprecia") || c.Contains("amortiza")) return GrupoDre.Depreciacao;
        if (c.Contains("comiss") || c.Contains("frete") || c.Contains("royalt") || (c.Contains("taxa") && c.Contains("cart"))) return GrupoDre.DespesasVariaveis;
        return null;
    }

    /// <summary>
    /// Aplica a classificação das despesas (salva no portal; sem ela, a sugestão pelo nome) sobre os lançamentos.
    /// Só mexe nos grupos de despesa; altera os objetos recebidos (carregados sem rastreamento) e devolve a mesma lista.
    /// </summary>
    public static List<LancamentoDre> Classificar(List<LancamentoDre> lancamentos, IReadOnlyDictionary<string, GrupoDre>? classes)
    {
        foreach (var l in lancamentos.Where(l => GruposDeDespesa.Contains(l.Grupo)))
        {
            if (classes is not null && classes.TryGetValue(l.Conta, out var g) && GruposDeDespesa.Contains(g)) l.Grupo = g;
            else if (l.Grupo == GrupoDre.DespesasOperacionais && Sugerir(l.Conta) is GrupoDre sugerido) l.Grupo = sugerido;
        }
        return lancamentos;
    }

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
            "despesa" or "despesas" or "fixas" or "despesasfixas" => GrupoDre.DespesasOperacionais,
            "variavel" or "variaveis" or "variáveis" => GrupoDre.DespesasVariaveis,
            "depreciacao" or "depreciação" => GrupoDre.Depreciacao,
            "ir" or "csll" => GrupoDre.ImpostosSobreLucro,
            _ => (GrupoDre)(-1)
        };
        return Enum.IsDefined(grupo);
    }

    private record ResumoMes(DateOnly Mes, decimal Receita, decimal Deducoes, decimal Cmv, decimal Variaveis, decimal Fixos);

    private static List<ResumoMes> ResumirMeses(IEnumerable<LancamentoDre> lancamentos) =>
        lancamentos.GroupBy(l => l.Competencia)
            .Select(g => new ResumoMes(
                g.Key,
                g.Where(l => l.Grupo == GrupoDre.ReceitaBruta).Sum(l => l.Valor),
                g.Where(l => l.Grupo == GrupoDre.Deducoes).Sum(l => l.Valor),
                g.Where(l => l.Grupo == GrupoDre.Cmv).Sum(l => l.Valor),
                g.Where(l => l.Grupo == GrupoDre.DespesasVariaveis).Sum(l => l.Valor),
                g.Where(l => l.Grupo is GrupoDre.DespesasOperacionais or GrupoDre.Depreciacao or GrupoDre.DespesasFinanceiras).Sum(l => l.Valor)
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
        var variaveisPct = meses.Sum(m => m.Variaveis) / receita;
        var mc = 1m - deducoesPct - cmvPct - variaveisPct;
        var fixos = meses.Average(m => m.Fixos);
        var faturamento = receita / meses.Count;
        var pe = mc > 0 ? fixos / mc : 0m;
        var seguranca = pe > 0 ? (faturamento - pe) / faturamento : 0m;
        return new(meses.Count, faturamento, fixos, deducoesPct, cmvPct, mc, pe, seguranca, variaveisPct);
    }

    public const string Devolucoes = "(-) Devoluções de vendas";
    public const string VendaLiquida = "= Venda líquida";
    public const string ImpostosVenda = "(-) Impostos sobre vendas";

    /// <summary>Conta de devolução de venda (ex.: "Devoluções de vendas"), dentro das Deduções.</summary>
    public static bool EhDevolucao(string conta) => Planilhas.Normalizar(conta).StartsWith("devoluc");

    public const string ReceitaLiquida = "= Receita líquida";
    public const string LucroBruto = "= Lucro bruto";
    public const string MargemContribuicao = "= Margem de contribuição";
    public const string Ebitda = "= EBITDA";
    public const string ResultadoOperacional = "= Resultado operacional";
    public const string ResultadoAntesIr = "= Resultado antes do IR";
    public const string LucroLiquido = "= Lucro líquido";

    /// <summary>Margens mostradas abaixo da DRE (linha da DRE ÷ receita bruta).</summary>
    public static readonly (string Nome, string Linha, string Dica)[] Margens =
    [
        ("Margem bruta", LucroBruto, "Lucro bruto ÷ receita bruta: o que sobra da venda depois de impostos, devoluções e custo das mercadorias."),
        ("Margem de contribuição", MargemContribuicao, "Margem de contribuição ÷ receita bruta: o que sobra depois também das despesas variáveis (comissões, taxas de cartão, fretes) para pagar as despesas fixas."),
        ("Margem operacional (EBITDA)", Ebitda, "EBITDA ÷ receita bruta: o resultado da operação da loja, antes de depreciação, juros e impostos sobre o lucro."),
        ("Margem líquida", LucroLiquido, "Lucro líquido ÷ receita bruta: quanto de cada R$ 100 vendidos virou lucro no fim."),
    ];

    /// <summary>
    /// DRE gerencial, mês a mês: receita → venda líquida → receita líquida → lucro bruto → margem de contribuição →
    /// EBITDA → resultado operacional → resultado antes do IR → lucro líquido. Sempre as mesmas linhas, na mesma ordem.
    /// </summary>
    public static List<LinhaDre> Montar(IEnumerable<LancamentoDre> lancamentos)
    {
        var lista = lancamentos as IReadOnlyCollection<LancamentoDre> ?? lancamentos.ToList();
        var porGrupo = Enum.GetValues<GrupoDre>().ToDictionary(g => g, _ => new decimal[12]);
        foreach (var l in lista)
            porGrupo[l.Grupo][l.Competencia.Month - 1] += l.Valor;

        // Deduções = devoluções + impostos sobre vendas; mostradas separadas, com a venda líquida (a "Venda" do ERP) no meio
        var devolucoes = new decimal[12];
        foreach (var l in lista)
            if (l.Grupo == GrupoDre.Deducoes && EhDevolucao(l.Conta))
                devolucoes[l.Competencia.Month - 1] += l.Valor;

        decimal[] Calc(Func<int, decimal> f) => Enumerable.Range(0, 12).Select(f).ToArray();
        decimal[] G(GrupoDre g) => porGrupo[g];
        var rb = G(GrupoDre.ReceitaBruta);
        var vendaLiquida = Calc(i => rb[i] - devolucoes[i]);
        var impostosVenda = Calc(i => G(GrupoDre.Deducoes)[i] - devolucoes[i]);
        var rl = Calc(i => rb[i] - G(GrupoDre.Deducoes)[i]);
        var lb = Calc(i => rl[i] - G(GrupoDre.Cmv)[i]);
        var mc = Calc(i => lb[i] - G(GrupoDre.DespesasVariaveis)[i]);
        var ebitda = Calc(i => mc[i] - G(GrupoDre.DespesasOperacionais)[i]);
        var ro = Calc(i => ebitda[i] - G(GrupoDre.Depreciacao)[i]);
        var lair = Calc(i => ro[i] + G(GrupoDre.ReceitasFinanceiras)[i] - G(GrupoDre.DespesasFinanceiras)[i]);
        var ll = Calc(i => lair[i] - G(GrupoDre.ImpostosSobreLucro)[i]);

        LinhaDre Grupo(GrupoDre g, bool negativa) => new(Nomes[g], G(g), false, negativa, g);
        return
        [
            Grupo(GrupoDre.ReceitaBruta, false),
            new(Devolucoes, devolucoes, false, true),
            new(VendaLiquida, vendaLiquida, true, false),
            new(ImpostosVenda, impostosVenda, false, true, GrupoDre.Deducoes),
            new(ReceitaLiquida, rl, true, false),
            Grupo(GrupoDre.Cmv, true),
            new(LucroBruto, lb, true, false),
            Grupo(GrupoDre.DespesasVariaveis, true),
            new(MargemContribuicao, mc, true, false),
            Grupo(GrupoDre.DespesasOperacionais, true),
            new(Ebitda, ebitda, true, false),
            Grupo(GrupoDre.Depreciacao, true),
            new(ResultadoOperacional, ro, true, false),
            Grupo(GrupoDre.ReceitasFinanceiras, false),
            Grupo(GrupoDre.DespesasFinanceiras, true),
            new(ResultadoAntesIr, lair, true, false),
            Grupo(GrupoDre.ImpostosSobreLucro, true),
            new(LucroLiquido, ll, true, false)
        ];
    }
}
