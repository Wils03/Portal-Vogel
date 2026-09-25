using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Portal.Data;

namespace Portal.Services;

/// <summary>
/// Copia uma vez os dados do portal do SQLite antigo (Data/app.db) para o banco atual (PORTAL_VG):
/// <c>dotnet run -- --copiar-sqlite Data/app.db</c>. Mantém os mesmos Ids (as chaves de criptografia continuam valendo).
/// Só roda com o banco de destino vazio.
/// </summary>
public static class CopiaSqlite
{
    public static async Task<int> ExecutarAsync(string arquivo, IServiceProvider servicos)
    {
        if (!File.Exists(arquivo)) { Console.Error.WriteLine($"Arquivo não encontrado: {arquivo}"); return 1; }
        var destinoFabrica = servicos.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

        // Mesmas opções do Identity do portal (tabela de passkeys etc.), lendo do SQLite
        var origemOpcoes = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"DataSource={arquivo}").UseApplicationServiceProvider(servicos).Options;
        await using var origem = new ApplicationDbContext(origemOpcoes);
        await using var destino = await destinoFabrica.CreateDbContextAsync();

        Console.WriteLine("Criando as tabelas no banco de destino...");
        await destino.Database.MigrateAsync();
        if (await destino.Users.AnyAsync() || await destino.Clientes.AnyAsync() || await destino.Rotinas.AnyAsync())
        {
            Console.Error.WriteLine("O banco de destino já tem dados. Nada foi copiado.");
            return 1;
        }

        await destino.Database.OpenConnectionAsync();
        foreach (var tipo in OrdemPorDependencia(destino.Model))
        {
            var dados = await LerTudoAsync(origem, tipo.ClrType);
            if (dados.Count == 0) { Console.WriteLine($"  {tipo.GetTableName()}: vazio"); continue; }

            var tabela = tipo.GetTableName()!;
            var identidade = tipo.FindPrimaryKey()!.Properties
                .Any(p => p.ValueGenerated == ValueGenerated.OnAdd && p.ClrType == typeof(int));
            await using var transacao = await destino.Database.BeginTransactionAsync();
            if (identidade) await destino.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT [{tabela}] ON");
            destino.AddRange(dados);
            await destino.SaveChangesAsync();
            if (identidade) await destino.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT [{tabela}] OFF");
            await transacao.CommitAsync();
            destino.ChangeTracker.Clear();
            Console.WriteLine($"  {tabela}: {dados.Count} linha(s)");
        }
        Console.WriteLine("Cópia concluída.");
        return 0;
    }

    /// <summary>Tabelas "pai" primeiro (clientes, rotinas, usuários) e depois as que dependem delas.</summary>
    private static List<IEntityType> OrdemPorDependencia(IModel modelo)
    {
        var tipos = modelo.GetEntityTypes().Where(t => !t.IsOwned() && t.GetTableName() is not null).ToList();
        var ordem = new List<IEntityType>();
        var visitados = new HashSet<IEntityType>();
        void Visitar(IEntityType t)
        {
            if (!visitados.Add(t)) return;
            foreach (var fk in t.GetForeignKeys())
                if (fk.PrincipalEntityType != t) Visitar(fk.PrincipalEntityType);
            ordem.Add(t);
        }
        foreach (var t in tipos) Visitar(t);
        return ordem;
    }

    private static async Task<List<object>> LerTudoAsync(ApplicationDbContext contexto, Type tipo)
    {
        var set = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(tipo).Invoke(contexto, null)!;
        var semRastreio = typeof(EntityFrameworkQueryableExtensions).GetMethod(nameof(EntityFrameworkQueryableExtensions.AsNoTracking))!
            .MakeGenericMethod(tipo).Invoke(null, [set])!;
        var lista = (Task)typeof(EntityFrameworkQueryableExtensions).GetMethod(nameof(EntityFrameworkQueryableExtensions.ToListAsync))!
            .MakeGenericMethod(tipo).Invoke(null, [semRastreio, CancellationToken.None])!;
        await lista;
        var resultado = (System.Collections.IEnumerable)lista.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)!.GetValue(lista)!;
        return resultado.Cast<object>().ToList();
    }
}
