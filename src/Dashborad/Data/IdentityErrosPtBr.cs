using Microsoft.AspNetCore.Identity;

namespace Portal.Data;

/// <summary>Mensagens do ASP.NET Identity (cadastro, senha, login) em português.</summary>
public class IdentityErrosPtBr : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Erro(nameof(DefaultError), "Ocorreu um erro desconhecido.");
    public override IdentityError ConcurrencyFailure() => Erro(nameof(ConcurrencyFailure), "O registro foi alterado por outra pessoa. Tente novamente.");
    public override IdentityError PasswordMismatch() => Erro(nameof(PasswordMismatch), "Senha incorreta.");
    public override IdentityError InvalidToken() => Erro(nameof(InvalidToken), "Token inválido.");
    public override IdentityError LoginAlreadyAssociated() => Erro(nameof(LoginAlreadyAssociated), "Já existe um usuário com este login.");
    public override IdentityError InvalidUserName(string? userName) => Erro(nameof(InvalidUserName), $"O usuário '{userName}' é inválido.");
    public override IdentityError InvalidEmail(string? email) => Erro(nameof(InvalidEmail), $"O e-mail '{email}' é inválido.");
    public override IdentityError DuplicateUserName(string userName) => Erro(nameof(DuplicateUserName), $"O usuário '{userName}' já está cadastrado.");
    public override IdentityError DuplicateEmail(string email) => Erro(nameof(DuplicateEmail), $"O e-mail '{email}' já está cadastrado.");
    public override IdentityError InvalidRoleName(string? role) => Erro(nameof(InvalidRoleName), $"O perfil '{role}' é inválido.");
    public override IdentityError DuplicateRoleName(string role) => Erro(nameof(DuplicateRoleName), $"O perfil '{role}' já existe.");
    public override IdentityError UserAlreadyHasPassword() => Erro(nameof(UserAlreadyHasPassword), "O usuário já possui senha.");
    public override IdentityError UserLockoutNotEnabled() => Erro(nameof(UserLockoutNotEnabled), "O bloqueio não está habilitado para este usuário.");
    public override IdentityError UserAlreadyInRole(string role) => Erro(nameof(UserAlreadyInRole), $"O usuário já está no perfil '{role}'.");
    public override IdentityError UserNotInRole(string role) => Erro(nameof(UserNotInRole), $"O usuário não está no perfil '{role}'.");
    public override IdentityError PasswordTooShort(int length) => Erro(nameof(PasswordTooShort), $"A senha deve ter pelo menos {length} caracteres.");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Erro(nameof(PasswordRequiresUniqueChars), $"A senha deve ter pelo menos {uniqueChars} caracteres diferentes.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Erro(nameof(PasswordRequiresNonAlphanumeric), "A senha deve ter pelo menos um caractere especial (ex.: # @ !).");
    public override IdentityError PasswordRequiresDigit() => Erro(nameof(PasswordRequiresDigit), "A senha deve ter pelo menos um número (0-9).");
    public override IdentityError PasswordRequiresLower() => Erro(nameof(PasswordRequiresLower), "A senha deve ter pelo menos uma letra minúscula.");
    public override IdentityError PasswordRequiresUpper() => Erro(nameof(PasswordRequiresUpper), "A senha deve ter pelo menos uma letra maiúscula.");

    private static IdentityError Erro(string codigo, string descricao) => new() { Code = codigo, Description = descricao };
}
