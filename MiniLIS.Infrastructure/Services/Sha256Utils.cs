using System.Security.Cryptography;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>Hash SHA-256 sobre bytes ya en memoria (M-6, reutilizado luego por F-9). El
    /// helper de BackupService es privado y opera sobre una ruta de fichero — no reutilizable
    /// aquí, donde el fichero ya se ha leído para comparar contra el hash guardado.</summary>
    public static class Sha256Utils
    {
        public static string ComputeHash(byte[] data) =>
            Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }
}
