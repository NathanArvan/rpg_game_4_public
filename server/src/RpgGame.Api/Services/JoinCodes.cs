using Microsoft.EntityFrameworkCore;
using RpgGame.Infrastructure.Data;
using System.Security.Cryptography;

namespace RpgGame.Api.Services;

/// <summary>6-character session join codes (A–Z, 0–9), unique across sessions.</summary>
internal static class JoinCodes
{
    public static async Task<string> NewUniqueAsync(AppDbContext db)
    {
        string joinCode;
        do
        {
            joinCode = Generate();
        } while (await db.GameSessions.AnyAsync(s => s.JoinCode == joinCode));
        return joinCode;
    }

    public static string Generate()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return string.Create(6, chars, (span, alphabet) =>
        {
            var bytes = RandomNumberGenerator.GetBytes(6);
            for (int i = 0; i < span.Length; i++)
                span[i] = alphabet[bytes[i] % alphabet.Length];
        });
    }
}
