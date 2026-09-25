using Portal.Data;

namespace Portal.Services;

/// <summary>Resumo de vendas do mês para o Painel, a partir dos lançamentos da DRE guardados no portal.</summary>
/// <param name="Mes">Último mês com receita.</param>
/// <param name="Parcial">O mês ainda está em andamento (os dados são desse mesmo mês).</param>
/// <param name="DiasComDados">Dias do mês já cobertos pelos dados (só quando parcial).</param>
/// <param name="VendaLiquida">Receita bruta − devoluções (a "Venda" do ERP).</param>
/// <param name="ProjecaoVenda">Venda do mês inteiro no ritmo atual (só quando parcial e com pelo menos 3 dias).</param>
/// <param name="ReceitaBruta">Faturamento bruto do mês (base do ponto de equilíbrio).</param>
/// <param name="Pe">Ponto de equilíbrio calculado com os 12 meses anteriores (meses sugeridos da DRE).</param>
/// <param name="MargemBrutaPct">Lucro bruto ÷ receita bruta do mês (vale também para o mês em andamento).</param>
/// <param name="MesFechado">Último mês completo: o próprio mês, ou o anterior se o mês ainda está em andamento.</param>
/// <param name="ResultadoMesFechado">Lucro líquido do mês fechado (despesas do mês em andamento ainda estão incompletas).</param>
public sealed record ResumoVendas(
    DateOnly Mes,
    bool Parcial,
    int DiasComDados,
    int DiasNoMes,
    decimal VendaLiquida,
    decimal? ProjecaoVenda,
    decimal? VendaMesAnterior,
    decimal? VendaMesmoMesAnoAnterior,
    decimal ReceitaBruta,
    decimal? ProjecaoReceitaBruta,
    PontoEquilibrioDre? Pe,
    decimal? MargemBrutaPct = null,
    DateOnly? MesFechado = null,
    decimal? ResultadoMesFechado = null,
    decimal? ReceitaMesFechado = null)
{
    public decimal? MargemLiquidaMesFechado => ResultadoMesFechado is decimal r && ReceitaMesFechado is decimal rb && rb > 0 ? r / rb : null;

    /// <summary>Valor comparável com um mês inteiro: a projeção, se o mês está em andamento.</summary>
    public decimal? VendaComparavel => Parcial ? ProjecaoVenda : VendaLiquida;

    public decimal? VariacaoMesAnterior => Variacao(VendaComparavel, VendaMesAnterior);
    public decimal? VariacaoAnoAnterior => Variacao(VendaComparavel, VendaMesmoMesAnoAnterior);

    /// <summary>Faturamento bruto do mês ÷ ponto de equilíbrio (1 = atingiu).</summary>
    public decimal? ProgressoPe => Pe is { PontoEquilibrio: > 0 } pe ? ReceitaBruta / pe.PontoEquilibrio : null;

    private static decimal? Variacao(decimal? atual, decimal? anterior) =>
        atual is decimal a && anterior is decimal b && b > 0 ? (a - b) / b : null;
}

/// <summary>Preços que precisam de atenção (só produtos com venda). Um produto pode ter mais de um motivo.</summary>
/// <param name="VendaMesEmRisco">Venda mensal (preço × qtd/mês) dos produtos com problema.</param>
public sealed record ResumoPrecos(int ComVenda, int ComProblema, int AbaixoRegra, int MargemNegativa, int SemCusto, decimal VendaMesEmRisco);

public static class PainelResumo
{
    public const int DiasMinimosProjecao = 3;

    /// <summary>
    /// O mês está em andamento se os dados são do próprio mês. Dias com dados: a sincronização da madrugada traz até
    /// o dia anterior; uma importação durante o dia já inclui parte do dia.
    /// </summary>
    public static (bool Parcial, int Dias, int DiasNoMes) Andamento(DateOnly mes, DateTime? dadosAte)
    {
        var diasNoMes = DateTime.DaysInMonth(mes.Year, mes.Month);
        if (dadosAte is not DateTime d || d.Year != mes.Year || d.Month != mes.Month) return (false, diasNoMes, diasNoMes);
        return (true, Math.Clamp(d.Hour < 6 ? d.Day - 1 : d.Day, 0, diasNoMes), diasNoMes);
    }

    /// <param name="lancamentos">Lançamentos da DRE da empresa (pelo menos os últimos 13 meses).</param>
    /// <param name="dadosAte">De quando são os dados (última sincronização ou importação). Define se o mês está em andamento.</param>
    public static ResumoVendas? Resumir(IReadOnlyCollection<LancamentoDre> lancamentos, DateTime? dadosAte)
    {
        var receita = new Dictionary<DateOnly, decimal>();
        var devolucoes = new Dictionary<DateOnly, decimal>();
        foreach (var l in lancamentos)
        {
            if (l.Grupo == GrupoDre.ReceitaBruta) receita[l.Competencia] = receita.GetValueOrDefault(l.Competencia) + l.Valor;
            else if (l.Grupo == GrupoDre.Deducoes && Dre.EhDevolucao(l.Conta)) devolucoes[l.Competencia] = devolucoes.GetValueOrDefault(l.Competencia) + l.Valor;
        }
        var comReceita = receita.Where(r => r.Value > 0).Select(r => r.Key).ToList();
        if (comReceita.Count == 0) return null;

        var mes = comReceita.Max();
        decimal? Venda(DateOnly m) => receita.TryGetValue(m, out var rb) && rb > 0 ? rb - devolucoes.GetValueOrDefault(m) : null;

        var (parcial, dias, diasNoMes) = Andamento(mes, dadosAte);

        var venda = Venda(mes)!.Value;
        var rbMes = receita[mes];
        decimal? Projetar(decimal valor) => parcial && dias >= DiasMinimosProjecao ? valor / dias * diasNoMes : null;

        // Ponto de equilíbrio com os 12 meses anteriores (o mês em andamento distorceria a média)
        var inicioPe = mes.AddMonths(-12);
        var basePe = lancamentos.Where(l => l.Competencia >= inicioPe && (l.Competencia < mes || (!parcial && l.Competencia == mes))).ToList();

        // Margem bruta do mês e resultado do último mês completo, pela montagem da DRE
        List<LinhaDre> Dre1Mes(DateOnly m) => Dre.Montar(lancamentos.Where(l => l.Competencia == m));
        decimal Linha(List<LinhaDre> dre, string titulo) => dre.First(l => l.Titulo == titulo).Ano;
        var dreMes = Dre1Mes(mes);
        var mesFechado = parcial ? mes.AddMonths(-1) : mes;
        var fechadoTemReceita = receita.GetValueOrDefault(mesFechado) > 0;
        var dreFechado = fechadoTemReceita ? Dre1Mes(mesFechado) : null;

        return new ResumoVendas(
            mes, parcial, parcial ? dias : diasNoMes, diasNoMes,
            venda, Projetar(venda),
            Venda(mes.AddMonths(-1)), Venda(mes.AddYears(-1)),
            rbMes, Projetar(rbMes),
            Dre.PontoEquilibrio(basePe),
            MargemBrutaPct: Linha(dreMes, "= Lucro bruto") / rbMes,
            MesFechado: fechadoTemReceita ? mesFechado : null,
            ResultadoMesFechado: dreFechado?[^1].Ano,
            ReceitaMesFechado: fechadoTemReceita ? receita[mesFechado] : null);
    }

    /// <summary>Produtos com venda nos últimos 90 dias que precisam de atenção (mesma regra do "Só problemas").</summary>
    public static ResumoPrecos ResumirPrecos(Cliente cliente, IEnumerable<Produto> produtos)
    {
        var comVenda = CalculoPreco.Calcular(cliente, produtos.Where(p => p.QtdMes > 0)).Produtos;
        var problema = comVenda.Where(CalculoPreco.TemProblema).ToList();
        return new ResumoPrecos(
            comVenda.Count,
            problema.Count,
            problema.Count(CalculoPreco.AbaixoDaRegra),
            problema.Count(a => a.MargemContribuicao < 0),
            problema.Count(a => a.Produto.Custo <= 0),
            problema.Sum(a => a.Produto.PrecoAtual * a.Produto.QtdMes));
    }
}
