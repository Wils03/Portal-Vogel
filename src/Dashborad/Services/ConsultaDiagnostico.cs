using System.Data;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Portal.Data;

namespace Portal.Services;

/// <summary>
/// Conferência de números pela linha de comando, sem expor a string de conexão:
/// <c>dotnet run -- --consulta &lt;id do cliente&gt; &lt;arquivo.sql&gt;</c>.
/// Usa a conexão salva no portal e as mesmas travas da tela (só SELECT, transação desfeita).
/// </summary>
public static class ConsultaDiagnostico
{
    public static async Task<int> ExecutarAsync(string[] args, IServiceProvider servicos)
    {
        var i = Array.IndexOf(args, "--consulta");
        if (i < 0 || args.Length < i + 3 || !int.TryParse(args[i + 1], out var clienteId))
        {
            Console.Error.WriteLine("Uso: --consulta <id do cliente> <arquivo.sql>");
            return 2;
        }

        await using var db = await servicos.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync();
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId);
        if (cliente is null) { Console.Error.WriteLine("Cliente não encontrado."); return 1; }

        var bancos = servicos.GetRequiredService<BancoClienteService>();
        var tabela = await bancos.ConsultarAsync(cliente, await File.ReadAllTextAsync(args[i + 2]));
        Console.WriteLine($"# {cliente.Nome}");
        Console.WriteLine(string.Join(" | ", tabela.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
        foreach (DataRow r in tabela.Rows)
            Console.WriteLine(string.Join(" | ", r.ItemArray.Select(v => Convert.ToString(v, CultureInfo.GetCultureInfo("pt-BR")))));
        return 0;
    }
}
