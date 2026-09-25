using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Portal.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Rotina> Rotinas => Set<Rotina>();
    public DbSet<Alerta> Alertas => Set<Alerta>();
    public DbSet<MensagemEnviada> Mensagens => Set<MensagemEnviada>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<LancamentoDre> LancamentosDre => Set<LancamentoDre>();
    public DbSet<ConfiguracaoMensagens> ConfiguracoesMensagens => Set<ConfiguracaoMensagens>();
    public DbSet<RotinaCliente> RotinasClientes => Set<RotinaCliente>();
    public DbSet<VendaVendedor> VendasVendedores => Set<VendaVendedor>();
    public DbSet<MetaVendedor> MetasVendedores => Set<MetaVendedor>();
    public DbSet<FeriadoCliente> Feriados => Set<FeriadoCliente>();
    public DbSet<DiasUteisMes> DiasUteis => Set<DiasUteisMes>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Custos e quantidades vêm do ERP com até 6 casas (o padrão do SQL Server cortaria em 2)
        configurationBuilder.Properties<decimal>().HavePrecision(18, 6);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Cada empresa é uma "base" (como a REV_Base do REV ENTRADAS): Id + impressão digital do CNPJ.
        // Todos os dados da empresa ficam ligados pelo ClienteId.
        builder.Entity<Cliente>().HasIndex(c => c.Impressao).IsUnique().HasFilter("[Impressao] IS NOT NULL");

        builder.Entity<Produto>().HasOne<Cliente>().WithMany().HasForeignKey(p => p.ClienteId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<LancamentoDre>().HasOne<Cliente>().WithMany().HasForeignKey(l => l.ClienteId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<RotinaCliente>().HasOne<Cliente>().WithMany().HasForeignKey(r => r.ClienteId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<RotinaCliente>().HasOne<Rotina>().WithMany().HasForeignKey(r => r.RotinaId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<VendaVendedor>().HasOne<Cliente>().WithMany().HasForeignKey(v => v.ClienteId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<MetaVendedor>().HasOne<Cliente>().WithMany().HasForeignKey(m => m.ClienteId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<FeriadoCliente>().HasOne<Cliente>().WithMany().HasForeignKey(f => f.ClienteId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<DiasUteisMes>().HasOne<Cliente>().WithMany().HasForeignKey(d => d.ClienteId).OnDelete(DeleteBehavior.Cascade);
        // Mensagens ficam como histórico mesmo se o cliente for excluído: sem chave estrangeira

        builder.Entity<Alerta>().HasIndex(a => new { a.Status, a.DataHora });
        builder.Entity<Produto>().HasIndex(p => new { p.ClienteId, p.Codigo });
        builder.Entity<LancamentoDre>().HasIndex(l => new { l.ClienteId, l.Competencia });
        builder.Entity<MensagemEnviada>().HasIndex(m => new { m.ClienteId, m.DataHora });
        builder.Entity<RotinaCliente>().HasIndex(r => new { r.RotinaId, r.ClienteId }).IsUnique();
        builder.Entity<VendaVendedor>().HasIndex(v => new { v.ClienteId, v.Data });
        builder.Entity<FeriadoCliente>().HasIndex(f => new { f.ClienteId, f.Data }).IsUnique();
        builder.Entity<DiasUteisMes>().HasIndex(d => new { d.ClienteId, d.Competencia }).IsUnique();
        builder.Entity<MetaVendedor>().HasIndex(m => new { m.ClienteId, m.Competencia, m.Vendedor }).IsUnique();
    }
}
