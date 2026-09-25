using System.Globalization;
using System.Text;

namespace Portal.Services;

/// <summary>Mensagem de WhatsApp pedindo a revisão de preços dos produtos marcados na precificação.</summary>
public static class AvisoPrecos
{
    /// <summary>Acima disto a mensagem fica longa demais para o WhatsApp: lista os primeiros e resume o resto.</summary>
    public const int MaximoItens = 30;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Diferença do preço atual para a regra do ERP (−0,146 = 14,6% abaixo). Nulo sem regra.</summary>
    public static decimal? DiferencaRegra(AnaliseProduto a) =>
        a.PrecoRegraSistema is decimal r && r > 0 ? (a.Produto.PrecoAtual - r) / r : null;

    public static string Montar(string empresa, string? destinatario, IReadOnlyList<AnaliseProduto> itens, string? remetente, DateTime quando)
    {
        var t = new StringBuilder();
        t.AppendLine($"*Revisão de preços — {empresa}*");
        t.AppendLine(quando.ToString("dd/MM/yyyy", PtBr));
        t.AppendLine();
        t.AppendLine(string.IsNullOrWhiteSpace(destinatario) ? "Olá!" : $"Olá, {destinatario.Trim()}!");
        t.AppendLine(itens.Count == 1
            ? "O produto abaixo está com o preço de venda abaixo do esperado:"
            : $"Os {itens.Count} produtos abaixo estão com o preço de venda abaixo do esperado:");

        foreach (var a in itens.Take(MaximoItens))
        {
            var p = a.Produto;
            t.AppendLine();
            t.AppendLine($"• *{p.Codigo}* – {p.Descricao}");
            var linha = $"  Preço atual: {Moeda(p.PrecoAtual)}";
            if (a.PrecoRegraSistema is decimal regra)
                linha += $" | Regra do ERP: {Moeda(regra)}" + (DiferencaRegra(a) is decimal d ? $" ({Pct(d)})" : "");
            t.AppendLine(linha);
            t.AppendLine($"  Sugerido c/ despesas fixas: {Moeda(a.PrecoSugerido)} | Margem líquida atual: {Pct(a.MargemLiquidaPct)}");
        }
        if (itens.Count > MaximoItens)
        {
            t.AppendLine();
            t.AppendLine($"… e mais {itens.Count - MaximoItens} produto(s). A lista completa está na tela de Precificação do portal.");
        }

        t.AppendLine();
        t.AppendLine("Por favor, revise os preços no sistema.");
        if (!string.IsNullOrWhiteSpace(remetente))
            t.Append($"Enviado por {remetente} pelo Portal Vogel.");
        return t.ToString().Replace("\r\n", "\n").TrimEnd(); // quebra de linha simples, igual no WhatsApp
    }

    private static string Moeda(decimal v) => v.ToString("C2", PtBr);

    private static string Pct(decimal v) => (v > 0 ? "+" : "") + v.ToString("P1", PtBr);
}
