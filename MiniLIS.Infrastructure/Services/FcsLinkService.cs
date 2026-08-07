using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>
    /// Enlace con los ficheros FCS de datos brutos (M-6). Sin subida de ficheros: el citómetro
    /// escribe directamente en una carpeta compartida (SystemSetting "Fcs:RootPath"); este
    /// servicio empareja por nombre normalizado, nunca por contenido ni por identidad.
    /// </summary>
    public class FcsLinkService : IFcsLinkService
    {
        private const string RootPathSettingKey = "Fcs:RootPath";

        private readonly ApplicationDbContext _db;
        private readonly IMasterDataService _masterService;
        private readonly ILogger<FcsLinkService> _logger;

        public FcsLinkService(ApplicationDbContext db, IMasterDataService masterService, ILogger<FcsLinkService> logger)
        {
            _db = db;
            _masterService = masterService;
            _logger = logger;
        }

        public async Task<List<SampleDataFile>> GetFilesForSampleAsync(int sampleId)
        {
            return await _db.SampleDataFiles
                .Include(f => f.SampleTube)
                .Where(f => f.SampleTube.SamplePanel.SampleId == sampleId)
                .OrderBy(f => f.SampleTube.TubeNumber)
                .ToListAsync();
        }

        public async Task<FcsVerificationSummary> RunVerificationPassAsync()
        {
            var rootPath = await _masterService.GetSettingAsync(RootPathSettingKey);
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                _logger.LogWarning("Carpeta de ficheros FCS no configurada o inexistente: {RootPath}", rootPath);
                return new FcsVerificationSummary { RootPathConfigured = false };
            }

            var files = Directory.GetFiles(rootPath, "*.fcs", SearchOption.AllDirectories);

            // Mapa nombre-esperado (mayúsculas) -> tubo, construido sobre todos los tubos con
            // panel/versión asignados (sin eso no se puede generar el nombre normalizado).
            var tubes = await _db.SampleTubes
                .Include(t => t.SamplePanel).ThenInclude(sp => sp.Sample)
                .Include(t => t.SamplePanel).ThenInclude(sp => sp.PanelVersion).ThenInclude(pv => pv!.Panel)
                .Where(t => t.SamplePanel.PanelVersion != null && t.SamplePanel.PanelVersion.Panel != null)
                .ToListAsync();

            var expectedNameToTube = new Dictionary<string, SampleTube>(StringComparer.OrdinalIgnoreCase);
            foreach (var tube in tubes)
            {
                var sample = tube.SamplePanel.Sample;
                var panel = tube.SamplePanel.PanelVersion!.Panel!;
                var version = tube.SamplePanel.PanelVersion.VersionNumber;
                var expectedName = FcsFileNaming.GenerateFileName(sample.SampleNumber, sample.SampleType.ToCode(), tube.TubeNumber, panel.Code, version);
                expectedNameToTube[expectedName] = tube;
            }

            var existingByTubeId = await _db.SampleDataFiles.ToDictionaryAsync(f => f.SampleTubeId);

            int newlyLinked = 0, reverified = 0, discrepancies = 0;

            foreach (var filePath in files)
            {
                var fileName = Path.GetFileName(filePath);
                if (!expectedNameToTube.TryGetValue(fileName, out var tube)) continue;

                byte[] bytes;
                try
                {
                    bytes = await File.ReadAllBytesAsync(filePath);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "No se pudo leer el fichero FCS {FilePath} (¿en uso por el citómetro?)", filePath);
                    continue;
                }

                var hash = Sha256Utils.ComputeHash(bytes);
                var nowUtc = DateTime.UtcNow;

                if (existingByTubeId.TryGetValue(tube.Id, out var existing))
                {
                    var discrepancy = existing.Sha256 != hash;
                    existing.LastVerifiedAtUtc = nowUtc;
                    existing.LastVerificationOk = !discrepancy;
                    existing.SizeBytes = bytes.LongLength;
                    if (discrepancy)
                    {
                        discrepancies++;
                        _logger.LogWarning("Discrepancia de hash detectada en {FileName}: el fichero ha cambiado desde el último enlace.", fileName);
                        // El hash guardado se conserva como el de referencia del primer enlace;
                        // solo se actualiza si el administrador vuelve a vincular manualmente.
                    }
                    else
                    {
                        reverified++;
                    }
                }
                else
                {
                    _db.SampleDataFiles.Add(new SampleDataFile
                    {
                        SampleTubeId = tube.Id,
                        FileName = fileName,
                        RelativePath = Path.GetRelativePath(rootPath, filePath),
                        Sha256 = hash,
                        SizeBytes = bytes.LongLength,
                        AcquiredAtUtc = File.GetLastWriteTimeUtc(filePath),
                        LastVerifiedAtUtc = nowUtc,
                        LastVerificationOk = true
                    });
                    tube.FcsFileName = fileName;
                    newlyLinked++;
                }
            }

            await _db.SaveChangesAsync();

            return new FcsVerificationSummary
            {
                RootPathConfigured = true,
                FilesScanned = files.Length,
                NewlyLinked = newlyLinked,
                Reverified = reverified,
                Discrepancies = discrepancies
            };
        }
    }
}
