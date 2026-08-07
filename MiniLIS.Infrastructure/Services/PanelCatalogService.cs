using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;

namespace MiniLIS.Infrastructure.Services
{
    public class PanelCatalogService : IPanelCatalogService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUserService;

        public PanelCatalogService(ApplicationDbContext db, ICurrentUserService currentUserService)
        {
            _db = db;
            _currentUserService = currentUserService;
        }

        public async Task<PanelVersion?> GetVigenteVersionAsync(int panelId)
        {
            return await _db.PanelVersions
                .Include(v => v.Panel)
                .Include(v => v.Tubes)
                .FirstOrDefaultAsync(v => v.PanelId == panelId && v.Status == PanelVersionStatus.Vigente);
        }

        public async Task<PanelVersion?> GetVersionWithTubesAsync(int panelVersionId)
        {
            return await _db.PanelVersions
                .Include(v => v.Panel)
                .Include(v => v.Tubes)
                .FirstOrDefaultAsync(v => v.Id == panelVersionId);
        }

        public async Task<List<PanelVersion>> GetVersionsForPanelAsync(int panelId)
        {
            return await _db.PanelVersions
                .Include(v => v.Tubes)
                .Where(v => v.PanelId == panelId)
                .OrderByDescending(v => v.VersionNumber)
                .ToListAsync();
        }

        public async Task<int> CountSamplePanelsUsingVersionAsync(int panelVersionId)
        {
            return await _db.SamplePanels.CountAsync(sp => sp.PanelVersionId == panelVersionId);
        }

        public async Task<PanelVersion> CreateDraftVersionAsync(int panelId, string changeNotes, int? copyFromVersionId = null)
        {
            if (string.IsNullOrWhiteSpace(changeNotes))
                throw new InvalidOperationException("Las notas de cambio son obligatorias para crear una versión nueva.");

            _currentUserService.ActionContext = "Nueva versión de panel";

            var maxVersion = await _db.PanelVersions
                .Where(v => v.PanelId == panelId)
                .Select(v => (int?)v.VersionNumber)
                .MaxAsync() ?? 0;

            var draft = new PanelVersion
            {
                PanelId = panelId,
                VersionNumber = maxVersion + 1,
                Status = PanelVersionStatus.Borrador,
                ChangeNotes = changeNotes
            };

            if (copyFromVersionId.HasValue)
            {
                var source = await _db.PanelVersions
                    .Include(v => v.Tubes)
                    .FirstOrDefaultAsync(v => v.Id == copyFromVersionId.Value);

                if (source != null)
                {
                    int tn = 1;
                    foreach (var t in source.Tubes.OrderBy(t => t.TubeNumber))
                    {
                        draft.Tubes.Add(new PanelTube
                        {
                            TubeNumber = tn++,
                            MarkerList = t.MarkerList,
                            Notes = t.Notes,
                            IsOptional = t.IsOptional
                        });
                    }
                    draft.QmsDocumentRef = new Domain.Common.QmsReference { Code = source.QmsDocumentRef.Code };
                }
            }

            _db.PanelVersions.Add(draft);
            await _db.SaveChangesAsync();
            return draft;
        }

        public async Task SaveDraftVersionAsync(int panelVersionId, string? changeNotes, string? qmsDocumentRefCode, List<PanelTube> tubes)
        {
            _currentUserService.ActionContext = "Edición de versión de panel (borrador)";

            var existing = await _db.PanelVersions
                .Include(v => v.Tubes)
                .FirstOrDefaultAsync(v => v.Id == panelVersionId);

            if (existing == null)
                throw new InvalidOperationException("La versión de panel no existe.");

            // Regla de inmutabilidad (M-4): solo una versión en Borrador se edita libremente.
            // Una vigente o retirada, tenga o no estudios asociados, no se modifica —
            // la única vía de cambio es crear una versión nueva.
            if (existing.Status != PanelVersionStatus.Borrador)
                throw new InvalidOperationException("Solo se puede modificar una versión en borrador. Cree una nueva versión para introducir cambios.");

            existing.ChangeNotes = changeNotes;
            existing.QmsDocumentRef.Code = qmsDocumentRefCode;

            _db.PanelTubes.RemoveRange(existing.Tubes);
            existing.Tubes.Clear();

            int order = 1;
            foreach (var t in tubes.OrderBy(t => t.TubeNumber))
            {
                existing.Tubes.Add(new PanelTube
                {
                    TubeNumber = order++,
                    MarkerList = t.MarkerList,
                    Notes = t.Notes,
                    IsOptional = t.IsOptional
                });
            }

            await _db.SaveChangesAsync();
        }

        public async Task PublishVersionAsync(int panelVersionId)
        {
            _currentUserService.ActionContext = "Publicación de versión de panel";

            var version = await _db.PanelVersions
                .Include(v => v.Tubes)
                .FirstOrDefaultAsync(v => v.Id == panelVersionId);

            if (version == null)
                throw new InvalidOperationException("La versión de panel no existe.");

            if (version.Status != PanelVersionStatus.Borrador)
                throw new InvalidOperationException("Solo se puede publicar una versión en borrador.");

            if (!version.Tubes.Any())
                throw new InvalidOperationException("La versión no tiene ningún tubo definido. Añada al menos uno antes de publicarla.");

            var previousVigente = await _db.PanelVersions
                .FirstOrDefaultAsync(v => v.PanelId == version.PanelId && v.Status == PanelVersionStatus.Vigente);

            var now = DateTime.UtcNow;
            if (previousVigente != null)
            {
                previousVigente.Status = PanelVersionStatus.Retirada;
                previousVigente.EffectiveToUtc = now;
            }

            version.Status = PanelVersionStatus.Vigente;
            version.EffectiveFromUtc = now;

            await _db.SaveChangesAsync();
        }
    }
}
