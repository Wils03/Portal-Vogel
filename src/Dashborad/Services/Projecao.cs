using Portal.Data;

namespace Portal.Services;

/// <summary>
/// Estrutura de custos dos últimos 12 meses fechados, em % da receita bruta (deduções, CMV, despesas variáveis, IR/CSLL)
/// e despesas fixas médias por mês (fixas + depreciação + resultado financeiro).
/// </summary>
public sealed record EstruturaCustos(int Meses, decimal ReceitaPeriodo, decimal DeducoesPct, decimal CmvPct, decimal VariaveisPct, decimal IrPct, decimal FixosMes)
{
    public decimal MargemContribuicaoPct => 1 - DeducoesPct - CmvPct - VariaveisPct;

    /// <summary>Lucro líquido aproximado de um mês com esta receita.</summary>
    public decimal Lucro(decimal receita) => receita * (MargemContribuicaoPct - IrPct) - FixosMes;
}

/// <param name="ReceitaParcial">Mês em andamento: o que já foi vendido até agora (a projeção é do mês inteiro).</param>
public sealed record MesProjetado(DateOnly Mes, bool Projetado, decimal Receita, decimal LucroLiquido, decimal? ReceitaAnoAnterior, decimal? ReceitaParcial = null);

/// <param name="Crescimento">Crescimento usado nos meses projetados (nulo = sem ano anterior: média dos últimos 3 meses).</param>
/// <param name="CrescimentoRealizado">Meses fechados do ano contra os mesmos meses do ano anterior.</param>
public sealed record ProjecaoAno(int Ano, List<MesProjetado> Meses, decimal? Crescimento, decimal? CrescimentoRealizado, EstruturaCustos Estrutura)
{
    public decimal Receita => Meses.Sum(m => m.Receita);
    public decimal LucroLiquido => Meses.Sum(m => m.LucroLiquido);
    public decimal? ReceitaAnoAnterior => Meses.All(m => m.ReceitaAnoAnterior is not null) ? Meses.Sum(m => m.ReceitaAnoAnterior!.Value) : null;
    public int MesesRealizados => Meses.Count(m => !m.Projetado);
}

/// <summary>Resultado anual de uma simulação "e se" (base: últimos 12 meses fechados).</summary>
public sealed record ResultadoSimulado(decimal Receita, decimal LucroBruto, decimal MargemContribuicao, decimal Fixos, decimal Resultado, decimal? PontoEquilibrioMes)
{
    public decimal? MargemContribuicaoPct => Receita != 0 ? MargemContribuicao / Receita : null;
    public decimal? MargemLiquidaPct => Receita != 0 ? Resultado / Receita : null;
}

public static class Projecao
{
    private sealed record Mes(DateOnly Competencia, decimal Receita, decimal Deducoes, decimal Cmv, decimal Variaveis, decimal Fixos, decimal Ir, decimal LucroLiquido);

    private static Dictionary<DateOnly, Mes> PorMes(IEnumerable<LancamentoDre> lancamentos) =>
        lancamentos.GroupBy(l => l.Competencia).ToDictionary(g => g.Key, g =>
        {
            decimal S(params GrupoDre[] grupos) => g.Where(l => grupos.Contains(l.Grupo)).Sum(l => l.Valor);
            var fixos = S(GrupoDre.DespesasOperacionais, GrupoDre.Depreciacao, GrupoDre.DespesasFinanceiras) - S(GrupoDre.ReceitasFinanceiras);
            var receita = S(GrupoDre.ReceitaBruta);
            var ll = receita - S(GrupoDre.Deducoes, GrupoDre.Cmv, GrupoDre.DespesasVariaveis, GrupoDre.ImpostosSobreLucro) - fixos;
            return new Mes(g.Key, receita, S(GrupoDre.Deducoes), S(GrupoDre.Cmv), S(GrupoDre.DespesasVariaveis), fixos, S(GrupoDre.ImpostosSobreLucro), ll);
        });

    /// <summary>Mês fechado: tem receita e já terminou (o mês de hoje está em andamento).</summary>
    private static bool Fechado(Mes m, DateOnly hoje) => m.Receita > 0 && m.Competencia < new DateOnly(hoje.Year, hoje.Month, 1);

    /// <summary>Estrutura dos últimos 12 meses fechados (nulo se não houver nenhum).</summary>
    public static EstruturaCustos? Estrutura(IEnumerable<LancamentoDre> lancamentos, DateOnly hoje)
    {
        var meses = PorMes(lancamentos).Values.Where(m => Fechado(m, hoje)).OrderByDescending(m => m.Competencia).Take(12).ToList();
        var receita = meses.Sum(m => m.Receita);
        if (receita <= 0) return null;
        var comFixos = meses.Where(m => m.Fixos > 0).ToList();
        return new EstruturaCustos(meses.Count, receita, meses.Sum(m => m.Deducoes) / receita, meses.Sum(m => m.Cmv) / receita,
            meses.Sum(m => m.Variaveis) / receita, meses.Sum(m => m.Ir) / receita, comFixos.Count > 0 ? comFixos.Average(m => m.Fixos) : 0);
    }

    /// <summary>
    /// Ano com os meses fechados como realizados e os demais projetados: receita do mesmo mês do ano anterior × (1 + crescimento),
    /// onde o crescimento é o dos meses fechados do ano contra os mesmos meses do ano anterior (+ <paramref name="ajuste"/>, para os cenários).
    /// Sem ano anterior, usa a média dos últimos 3 meses fechados. Lucro dos meses projetados pela estrutura de custos dos últimos 12 meses.
    /// </summary>
    public static ProjecaoAno? Projetar(IEnumerable<LancamentoDre> lancamentos, int ano, DateOnly hoje, decimal ajuste = 0)
    {
        var lista = lancamentos as IReadOnlyCollection<LancamentoDre> ?? lancamentos.ToList();
        var estrutura = Estrutura(lista, hoje);
        if (estrutura is null) return null;
        var meses = PorMes(lista);
        Mes? M(DateOnly d) => meses.GetValueOrDefault(d);

        var fechados = Enumerable.Range(1, 12).Select(m => new DateOnly(ano, m, 1)).Where(d => M(d) is { } x && Fechado(x, hoje)).ToList();
        var atual = fechados.Sum(d => M(d)!.Receita);
        var anterior = fechados.Sum(d => M(d.AddYears(-1))?.Receita ?? 0);
        decimal? realizado = anterior > 0 && fechados.All(d => M(d.AddYears(-1)) is { Receita: > 0 }) ? atual / anterior - 1 : null;
        decimal? crescimento = realizado is decimal r ? r + ajuste : null;
        var ultimos3 = meses.Values.Where(m => Fechado(m, hoje)).OrderByDescending(m => m.Competencia).Take(3).ToList();
        var mediaRecente = ultimos3.Count > 0 ? ultimos3.Average(m => m.Receita) * (1 + ajuste) : 0;

        var resultado = new List<MesProjetado>();
        for (var m = 1; m <= 12; m++)
        {
            var d = new DateOnly(ano, m, 1);
            decimal? receitaAnt = M(d.AddYears(-1)) is { Receita: > 0 } ant ? ant.Receita : null;
            if (fechados.Contains(d))
            {
                resultado.Add(new MesProjetado(d, false, M(d)!.Receita, M(d)!.LucroLiquido, receitaAnt));
                continue;
            }
            var receita = crescimento is decimal c && receitaAnt is decimal ra ? ra * (1 + c) : mediaRecente;
            var parcial = M(d) is { Receita: > 0 } emAndamento ? emAndamento.Receita : (decimal?)null;
            resultado.Add(new MesProjetado(d, true, Math.Round(receita, 2), Math.Round(estrutura.Lucro(receita), 2), receitaAnt, parcial));
        }
        return new ProjecaoAno(ano, resultado, crescimento, realizado, estrutura);
    }

    /// <summary>
    /// "E se" sobre um ano típico (os últimos 12 meses fechados): preço e volume mexem na receita; o CMV acompanha o volume
    /// (e a variação de custo); deduções, despesas variáveis e IR acompanham a receita; despesas fixas só mudam pela própria variação.
    /// </summary>
    public static ResultadoSimulado Simular(EstruturaCustos e, decimal precoPct = 0, decimal volumePct = 0, decimal cmvPct = 0, decimal fixosPct = 0)
    {
        var receitaBase = e.ReceitaPeriodo * 12m / Math.Max(1, e.Meses);
        var receita = receitaBase * (1 + precoPct) * (1 + volumePct);
        var cmv = e.CmvPct * receitaBase * (1 + volumePct) * (1 + cmvPct);
        var lucroBruto = receita * (1 - e.DeducoesPct) - cmv;
        var mc = lucroBruto - receita * e.VariaveisPct;
        var fixos = e.FixosMes * 12 * (1 + fixosPct);
        var resultado = mc - fixos - receita * e.IrPct;
        var mcPct = receita != 0 ? mc / receita : 0;
        return new ResultadoSimulado(receita, lucroBruto, mc, fixos, resultado, mcPct > 0 ? fixos / 12 / mcPct : null);
    }
}
