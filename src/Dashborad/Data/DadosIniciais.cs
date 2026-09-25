using Microsoft.EntityFrameworkCore;

namespace Portal.Data;

public static class DadosIniciais
{
    /// <summary>Aplica as migrations e cria rotinas de exemplo (inativas) na primeira execução.</summary>
    public static async Task AplicarAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        // Usuários criados antes dos perfis eram todos da Vogel: viram administradores
        var semPerfil = await db.Users.Where(u => u.Perfil == Perfil.Indefinido && u.ClienteId == null).ToListAsync();
        foreach (var u in semPerfil)
        {
            u.Perfil = Perfil.VogelAdministrador;
            u.SecurityStamp = Guid.NewGuid().ToString(); // renova o login para carregar o perfil
        }
        if (semPerfil.Count > 0) await db.SaveChangesAsync();

        // Impressão digital do CNPJ dos clientes cadastrados antes do campo existir
        var semImpressao = await db.Clientes.Where(c => c.Impressao == null && c.Cnpj != null).ToListAsync();
        foreach (var c in semImpressao) c.Impressao = Cliente.CalcularImpressao(c.Cnpj);
        if (semImpressao.Count > 0) await db.SaveChangesAsync();

        if (await db.Rotinas.AnyAsync()) return;

        db.Rotinas.AddRange(
            new Rotina
            {
                Nome = "Notas com erro / não processadas",
                Descricao = "EXEMPLO: ajuste nomes de tabela e colunas para o banco do sistema dos clientes.",
                Sql = "SELECT COUNT(*) FROM NOTAS_ENTRADA WHERE STATUS IN ('ERRO', 'PENDENTE')",
                Severidade = Severidade.Critico,
                Limite = 0,
                ModeloMensagem = "Olá {cliente}! Identificamos {quantidade} nota(s) com erro ou não processada(s) em {data}. Favor revisar no sistema. Qualquer dúvida estamos à disposição.",
                HoraExecucao = 7,
                Ativa = false
            },
            new Rotina
            {
                Nome = "Produtos vendidos abaixo do custo",
                Descricao = "EXEMPLO: lista produtos com preço de venda menor que o custo.",
                Sql = "SELECT CODIGO, DESCRICAO, CUSTO, PRECO_VENDA FROM PRODUTOS WHERE PRECO_VENDA < CUSTO AND ATIVO = 1",
                Severidade = Severidade.Atencao,
                Limite = 0,
                ModeloMensagem = "Olá {cliente}! Encontramos {quantidade} produto(s) com preço de venda abaixo do custo. Vamos revisar a precificação?",
                HoraExecucao = 8,
                Ativa = false
            });
        await db.SaveChangesAsync();
    }
}
