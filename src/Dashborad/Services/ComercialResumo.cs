using Portal.Data;

namespace Portal.Services;

/// <summary>
/// Dias úteis como as funções fDiasUteis/fDiasUteisMes do ERP: segunda a sexta = 1 dia, sábado = ½ dia, domingo e feriados = 0.
/// O relatório "Metas" do ERP usa o número arredondado (ago/2026: 23,5 → 24; meta do dia 276.250 ÷ 24 = 11.510).
/// </summary>
public static class Calendario
{
    /// <summary>Dias úteis (com frações) do mês de <paramref name="mes"/>, do dia 1 até <paramref name="ate"/> (inclusive; nulo = fim do mês).</summary>
    public static decimal DiasUteis(DateOnly mes, IReadOnlySet<DateOnly> feriados, DateOnly? ate = null)
    {
        var inicio = new DateOnly(mes.Year, mes.Month, 1);
        var fim = inicio.AddMonths(1).AddDays(-1);
        if (ate is DateOnly a && a < fim) fim = a;
        var dias = 0m;
        for (var d = inicio; d <= fim; d = d.AddDays(1))
        {
            if (feriados.Contains(d)) continue;
            if (d.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday) dias += 1m;
            else if (d.DayOfWeek == DayOfWeek.Saturday) dias += 0.5m;
        }
        return dias;
    }

    /// <summary>Como o relatório do ERP mostra e usa: arredondado (0,5 para cima).</summary>
    public static int Arredondar(decimal dias) => (int)Math.Round(dias, MidpointRounding.AwayFromZero);
}

/// <summary>Venda, CMV, número de vendas e itens de um período (dia, mês até a data, ano anterior).</summary>
public sealed record Bloco(decimal Venda, decimal Cmv, int Documentos, int Itens)
{
    public static readonly Bloco Vazio = new(0, 0, 0, 0);

    /// <summary>(Venda − CMV) ÷ venda: a "Margem Contrib." do relatório de metas do ERP.</summary>
    public decimal? MargemPct => Venda > 0 ? (Venda - Cmv) / Venda : null;

    public decimal? TicketMedio => Documentos > 0 ? Venda / Documentos : null;

    public decimal? ItensPorVenda => Documentos > 0 ? (decimal)Itens / Documentos : null;

    public static Bloco Somar(IEnumerable<VendaVendedor> vendas) => vendas.Aggregate(Vazio, (b, v) =>
        new Bloco(b.Venda + v.VendaBruta - v.Devolucoes, b.Cmv + v.Cmv, b.Documentos + v.Documentos, b.Itens + v.Itens));

    public static Bloco operator +(Bloco a, Bloco b) => new(a.Venda + b.Venda, a.Cmv + b.Cmv, a.Documentos + b.Documentos, a.Itens + b.Itens);
}

/// <summary>Uma linha do relatório de metas (vendedor, gerente ou total).</summary>
/// <param name="MetaMes">Meta do mês ("Meta Geral").</param>
/// <param name="MetaDia">Meta do mês ÷ dias úteis do mês.</param>
/// <param name="MetaAcumulada">Meta do mês × dias úteis até a data ÷ dias úteis do mês.</param>
public sealed record LinhaMetas(
    string Vendedor, string Nome, string? Gerente, string? NomeGerente,
    Bloco Dia, Bloco Acumulado, Bloco AnoAnterior,
    decimal? MetaMes, decimal? MetaDia, decimal? MetaAcumulada)
{
    public decimal? AtingimentoDia => MetaDia is > 0 ? Dia.Venda / MetaDia : null;
    public decimal? AtingimentoAcumulado => MetaAcumulada is > 0 ? Acumulado.Venda / MetaAcumulada : null;

    /// <summary>Acumulado do mês ÷ mesmo período do ano anterior − 1.</summary>
    public decimal? Crescimento => AnoAnterior.Venda > 0 ? Acumulado.Venda / AnoAnterior.Venda - 1 : null;

    /// <summary>Acumulado do mês ÷ meta do mês inteiro.</summary>
    public decimal? AtingimentoGeral => MetaMes is > 0 ? Acumulado.Venda / MetaMes : null;

    /// <summary>Acumulado − meta do mês: negativo = quanto falta para a meta; positivo = quanto passou.</summary>
    public decimal? Diferenca => MetaMes is decimal m ? Acumulado.Venda - m : null;

    /// <summary>Acumulado − meta proporcional aos dias úteis até a data (está adiantado ou atrasado no ritmo da meta).</summary>
    public decimal? DiferencaAcumulada => MetaAcumulada is decimal m ? Acumulado.Venda - m : null;

    /// <summary>Sem venda, CMV ou vendas no dia, no mês e no ano anterior, e sem meta (ou cota zero no ERP).</summary>
    public bool Zerada => (MetaMes ?? 0) == 0 && Vazio(Dia) && Vazio(Acumulado) && Vazio(AnoAnterior);

    private static bool Vazio(Bloco b) => b is { Venda: 0, Cmv: 0, Documentos: 0 };
}

public sealed record GrupoMetas(string? Gerente, string Nome, LinhaMetas Total, List<LinhaMetas> Vendedores);

/// <param name="DiasUteisCalculados">Dias úteis do mês pelo calendário do ERP, arredondados, antes do ajuste manual.</param>
/// <param name="DiasUteisMesExato">Com a fração do sábado (ex.: 23,5), antes de arredondar.</param>
public sealed record RelatorioMetas(
    DateOnly DataFinal, int DiasUteisAcumulado, int DiasUteisMes, int DiasUteisCalculados, decimal DiasUteisMesExato, int FeriadosNoMes,
    LinhaMetas Total, List<GrupoMetas> Grupos)
{
    public IEnumerable<LinhaMetas> Vendedores => Grupos.SelectMany(g => g.Vendedores);
}

public static class ComercialResumo
{
    public const string SemGerente = "Sem gerente";

    /// <summary>
    /// Relatório de metas até <paramref name="dataFinal"/>, como o "Metas" do ERP: Diário (o dia), Acumulado (do dia 1 até a data),
    /// Acumulado do ano anterior (mesmo período) e Meta geral (mês inteiro), por vendedor e total.
    /// </summary>
    /// <param name="diasUteisAjuste">Dias úteis do mês informados à mão (nulo = calendário do ERP).</param>
    /// <param name="somente">Só estes vendedores (ex.: os que têm cota no ERP); os totais somam só eles. Nulo = todos.</param>
    public static RelatorioMetas Montar(DateOnly dataFinal, IReadOnlyCollection<VendaVendedor> vendas, IEnumerable<MetaVendedor> metas,
        IReadOnlySet<DateOnly> feriados, int? diasUteisAjuste = null, IReadOnlySet<string>? somente = null)
    {
        var mes = new DateOnly(dataFinal.Year, dataFinal.Month, 1);
        var exato = Calendario.DiasUteis(mes, feriados);
        var calculados = Calendario.Arredondar(exato);
        var diasMes = diasUteisAjuste is > 0 ? diasUteisAjuste.Value : calculados;
        var diasAcum = Math.Min(diasMes, Calendario.Arredondar(Calendario.DiasUteis(mes, feriados, dataFinal)));
        var feriadosNoMes = feriados.Count(f => f.Year == mes.Year && f.Month == mes.Month && f.DayOfWeek != DayOfWeek.Sunday);

        var inicioAnt = mes.AddYears(-1);
        var fimAnt = dataFinal.AddYears(-1);
        // Cota do ERP tem preferência sobre a meta digitada no portal
        var metasMes = metas.Where(m => m.Competencia == mes).GroupBy(m => m.Vendedor)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.DoErp).First().Meta);

        var doDia = vendas.Where(v => v.Data == dataFinal).ToLookup(v => v.Vendedor);
        var doMes = vendas.Where(v => v.Data >= mes && v.Data <= dataFinal).ToLookup(v => v.Vendedor);
        var anoAnt = vendas.Where(v => v.Data >= inicioAnt && v.Data <= fimAnt).ToLookup(v => v.Vendedor);
        var cadastro = vendas.GroupBy(v => v.Vendedor).ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.Data).First());

        var codigos = doMes.Select(g => g.Key).Union(anoAnt.Select(g => g.Key)).Union(metasMes.Keys).Distinct()
            .Where(c => somente is null || somente.Contains(c));
        var linhas = codigos.Select(cod =>
        {
            cadastro.TryGetValue(cod, out var c);
            decimal? meta = metasMes.TryGetValue(cod, out var m) ? m : null;
            return Linha(cod, c?.NomeVendedor ?? cod, c?.Gerente, c?.NomeGerente,
                Bloco.Somar(doDia[cod]), Bloco.Somar(doMes[cod]), Bloco.Somar(anoAnt[cod]), meta, diasMes, diasAcum);
        }).ToList();

        var grupos = linhas.GroupBy(l => l.Gerente)
            .Select(g => new GrupoMetas(g.Key, g.First().NomeGerente ?? g.Key ?? SemGerente,
                Somar(g.ToList(), g.First().NomeGerente ?? SemGerente), g.OrderByDescending(l => l.Acumulado.Venda).ThenBy(l => l.Nome).ToList()))
            .OrderByDescending(g => g.Total.Acumulado.Venda).ToList();

        return new RelatorioMetas(dataFinal, diasAcum, diasMes, calculados, exato, feriadosNoMes, Somar(linhas, "Totais"), grupos);
    }

    private static LinhaMetas Linha(string cod, string nome, string? gerente, string? nomeGerente, Bloco dia, Bloco acum, Bloco ant,
        decimal? meta, int diasMes, int diasAcum) =>
        new(cod, nome, gerente, nomeGerente, dia, acum, ant, meta,
            meta is decimal m && diasMes > 0 ? m / diasMes : null,
            meta is decimal m2 && diasMes > 0 ? m2 * diasAcum / diasMes : null);

    private static LinhaMetas Somar(List<LinhaMetas> linhas, string nome)
    {
        decimal? Soma(Func<LinhaMetas, decimal?> f) => linhas.Any(l => f(l) is not null) ? linhas.Sum(l => f(l) ?? 0) : null;
        return new LinhaMetas("", nome, null, null,
            linhas.Aggregate(Bloco.Vazio, (b, l) => b + l.Dia), linhas.Aggregate(Bloco.Vazio, (b, l) => b + l.Acumulado),
            linhas.Aggregate(Bloco.Vazio, (b, l) => b + l.AnoAnterior),
            Soma(l => l.MetaMes), Soma(l => l.MetaDia), Soma(l => l.MetaAcumulada));
    }
}
