using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using Microsoft.AspNetCore.Identity;

namespace GestionRequerimientoDP.Infrastructure.Security;

public sealed class PasswordService : IPasswordService
{
    static readonly PasswordHasher<Usuario> Hasher = new();
    static readonly Usuario Dummy = new();

    public string Hash(string password) => Hasher.HashPassword(Dummy, password);

    public bool Verify(string hash, string password) =>
        !string.IsNullOrEmpty(hash) && Hasher.VerifyHashedPassword(Dummy, hash, password) != PasswordVerificationResult.Failed;
}
