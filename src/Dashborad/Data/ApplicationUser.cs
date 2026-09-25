using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Portal.Data;

/// <summary>Perfis de acesso. 0 = ainda não definido (usuários criados antes dos perfis).</summary>
public enum Perfil
{
    Indefinido = 0,
    VogelAdministrador = 1,
    VogelOperador = 2,
    ClienteGestor = 3,
    ClienteUsuario = 4
}

public class ApplicationUser : IdentityUser
{
    [StringLength(100)]
    public string? Nome { get; set; }

    public Perfil Perfil { get; set; }

    /// <summary>Empresa do usuário (perfis de cliente). Nulo para a equipe Vogel.</summary>
    public int? ClienteId { get; set; }
}
