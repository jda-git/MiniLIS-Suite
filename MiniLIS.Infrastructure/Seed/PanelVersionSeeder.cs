using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;

namespace MiniLIS.Infrastructure.Seed
{
    /// <summary>
    /// Migración de datos de M-4: da a cada Panel sin PanelVersion una v1/Vigente,
    /// derivando Code de Name y partiendo TubeListText en PanelTube. Idempotente:
    /// solo actúa sobre paneles que todavía no tienen ninguna versión, así que es
    /// seguro llamarla en cada arranque (cubre tanto la migración de paneles
    /// históricos como los paneles recién sembrados en una instalación nueva).
    /// </summary>
    public static class PanelVersionSeeder
    {
        public static string DeriveCode(string name, HashSet<string> existingCodes)
        {
            var normalized = name.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark) continue;

                if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
                else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }

            var code = sb.ToString().Trim('-');
            if (code.Length > 20) code = code[..20].TrimEnd('-');
            if (code.Length == 0) code = "PANEL";

            if (!existingCodes.Contains(code)) return code;

            int suffix = 2;
            string candidate;
            do
            {
                var baseLen = Math.Min(code.Length, 20 - suffix.ToString().Length - 1);
                candidate = $"{code[..baseLen]}-{suffix}";
                suffix++;
            } while (existingCodes.Contains(candidate));

            return candidate;
        }

        public static async Task RunAsync(ApplicationDbContext db, ILogger logger)
        {
            var panelsNeedingVersion = await db.Panels
                .Include(p => p.Versions)
                .Where(p => !p.Versions.Any())
                .ToListAsync();

            if (panelsNeedingVersion.Count == 0) return;

            var existingCodes = new HashSet<string>(
                await db.Panels.Where(p => p.Code != null && p.Code != "").Select(p => p.Code!).ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            int migratedCount = 0;
            int tubesFallbackCount = 0;
            var collisions = new List<string>();

            foreach (var panel in panelsNeedingVersion)
            {
                // "TMP-<Id>" es el código provisional que asigna la propia migración para
                // poder crear el índice único sin colisionar entre filas existentes.
                if (string.IsNullOrWhiteSpace(panel.Code) || panel.Code.StartsWith("TMP-", StringComparison.Ordinal))
                {
                    existingCodes.Remove(panel.Code);
                    var code = DeriveCode(panel.Name, existingCodes);
                    if (existingCodes.Contains(code))
                    {
                        collisions.Add($"{panel.Name} -> {code}");
                    }
                    panel.Code = code;
                    existingCodes.Add(code);
                }

                var version = new PanelVersion
                {
                    PanelId = panel.Id,
                    VersionNumber = 1,
                    Status = PanelVersionStatus.Vigente,
                    EffectiveFromUtc = panel.CreatedAtUtc
                };

                var tubeTexts = (panel.TubeListText ?? string.Empty)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

                if (tubeTexts.Count == 0)
                {
                    tubeTexts.Add(panel.Name);
                    tubesFallbackCount++;
                    logger.LogWarning(
                        "[MIGRACION-PANELES] Panel '{Panel}' ({Code}) sin TubeListText: se crea un único tubo con el nombre del panel. Revisar manualmente.",
                        panel.Name, panel.Code);
                }

                int tubeNumber = 1;
                foreach (var text in tubeTexts)
                {
                    version.Tubes.Add(new PanelTube { TubeNumber = tubeNumber++, MarkerList = text });
                }

                db.PanelVersions.Add(version);
                migratedCount++;
            }

            await db.SaveChangesAsync();

            logger.LogInformation(
                "[MIGRACION-PANELES] {Count} panel(es) migrados a PanelVersion v1/Vigente. " +
                "Tubo por defecto asignado (sin TubeListText): {Fallback}. Colisiones de código resueltas: {Collisions}.",
                migratedCount, tubesFallbackCount, collisions.Count == 0 ? "ninguna" : string.Join("; ", collisions));
        }
    }
}
