using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Common;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;

namespace MiniLIS.Infrastructure.Services
{
    public class PanelCatalogService : IPanelCatalogService
    {
        public const string RoleFacultativo = "Facultativo";
        public const string RoleAdministrador = "Administrador";

        /// <summary>Código de la ficha maestra de paneles del QMS que se propone por defecto.</summary>
        public const string DefaultMasterSheetCode = "ANX-CIT-REA-003-01";

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUserService;
        private readonly IPermissionService _permissions;
        private readonly string _masterSheetCode;

        public PanelCatalogService(ApplicationDbContext db, ICurrentUserService currentUserService,
            IPermissionService? permissions = null, IConfiguration? configuration = null)
        {
            _db = db;
            _currentUserService = currentUserService;
            // Sin servicio de permisos (usos internos y pruebas antiguas) se resuelve con la
            // matriz por defecto sobre los roles del usuario.
            _permissions = permissions ?? new PermissionService(db, currentUserService);
            _masterSheetCode = configuration?["Qms:PanelMasterSheetCode"] ?? DefaultMasterSheetCode;
        }

        // ── Consultas ───────────────────────────────────────────────────────────────────

        public async Task<PanelVersion?> GetVigenteVersionAsync(int panelId)
        {
            await ActivateDueVersionsAsync();
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
                .Include(v => v.Clarifications)
                .FirstOrDefaultAsync(v => v.Id == panelVersionId);
        }

        public async Task<List<PanelVersion>> GetVersionsForPanelAsync(int panelId)
        {
            await ActivateDueVersionsAsync();
            return await _db.PanelVersions
                .Include(v => v.Panel)
                .Include(v => v.Tubes)
                .Include(v => v.Clarifications)
                .Where(v => v.PanelId == panelId)
                .OrderByDescending(v => v.VersionMajor).ThenByDescending(v => v.VersionMinor)
                .ToListAsync();
        }

        public async Task<int> CountSamplePanelsUsingVersionAsync(int panelVersionId)
            => await _db.SamplePanels.CountAsync(sp => sp.PanelVersionId == panelVersionId);

        public async Task<PanelVersionNumberSuggestion> SuggestNextVersionAsync(int panelId)
        {
            var highest = await HighestAsync(panelId, excludeVersionId: null);
            if (highest == null)
                return new PanelVersionNumberSuggestion { NextMinorMajor = 1, NextMinorMinor = 0, NextMajorMajor = 1, NextMajorMinor = 0 };
            return new PanelVersionNumberSuggestion
            {
                Highest = $"v{highest.Value.Major}.{highest.Value.Minor}",
                NextMinorMajor = highest.Value.Major,
                NextMinorMinor = highest.Value.Minor + 1,
                NextMajorMajor = highest.Value.Major + 1,
                NextMajorMinor = 0
            };
        }

        private async Task<(int Major, int Minor)?> HighestAsync(int panelId, int? excludeVersionId)
        {
            var all = await _db.PanelVersions
                .Where(v => v.PanelId == panelId && v.Id != (excludeVersionId ?? 0))
                .Select(v => new { v.VersionMajor, v.VersionMinor })
                .ToListAsync();
            if (all.Count == 0) return null;
            var top = all.OrderByDescending(v => v.VersionMajor).ThenByDescending(v => v.VersionMinor).First();
            return (top.VersionMajor, top.VersionMinor);
        }

        /// <summary>La versión nueva debe ser mayor que todas las del panel: no se puede
        /// reutilizar ni intercalar un número (la 1.0 no se "sobrescribe").</summary>
        private async Task ValidateNumberAsync(int panelId, int major, int minor, int? excludeVersionId)
        {
            if (major < 1 || minor < 0)
                throw new InvalidOperationException("La versión debe ser 1.0 o superior (mayor ≥ 1, menor ≥ 0).");
            var highest = await HighestAsync(panelId, excludeVersionId);
            if (highest != null && PanelVersion.Compare(major, minor, highest.Value.Major, highest.Value.Minor) <= 0)
                throw new InvalidOperationException(
                    $"La versión v{major}.{minor} no es posterior a la v{highest.Value.Major}.{highest.Value.Minor} ya existente. " +
                    "Una versión no se puede reutilizar ni intercalar: elija un número mayor.");
        }

        // ── Roles ───────────────────────────────────────────────────────────────────────

        private async Task RequireEditorAsync()
        {
            if (!await _permissions.HasAsync(Permissions.PanelVersionesEditar))
                throw new UnauthorizedAccessException("No tiene permiso para preparar versiones de panel.");
        }

        private async Task RequireFacultativoAsync(string action)
        {
            if (!await _permissions.HasAsync(Permissions.PanelVersionesAprobar))
                throw new UnauthorizedAccessException($"No tiene permiso para {action}.");
        }

        private async Task<PanelVersion> LoadAsync(int panelVersionId)
            => await _db.PanelVersions.Include(v => v.Panel).Include(v => v.Tubes).FirstOrDefaultAsync(v => v.Id == panelVersionId)
               ?? throw new InvalidOperationException("La versión de panel no existe.");

        // ── Borradores ──────────────────────────────────────────────────────────────────

        public async Task<PanelVersion> CreateDraftVersionAsync(int panelId, int versionMajor, int versionMinor, string changeNotes, int? copyFromVersionId = null)
        {
            await RequireEditorAsync();
            var noteError = ChangeNoteRules.Validate(changeNotes);
            if (noteError != null) throw new InvalidOperationException(noteError);

            // Una sola versión en preparación por panel: con dos borradores a la vez no queda
            // claro cuál sustituye a cuál.
            if (await _db.PanelVersions.AnyAsync(v => v.PanelId == panelId &&
                    (v.Status == PanelVersionStatus.Borrador || v.Status == PanelVersionStatus.EnRevision)))
                throw new InvalidOperationException("Ya hay una versión de este panel en borrador o en revisión. Termínela o descártela antes de crear otra.");

            await ValidateNumberAsync(panelId, versionMajor, versionMinor, null);
            _currentUserService.ActionContext = "Nueva versión de panel";

            var maxOrdinal = await _db.PanelVersions.Where(v => v.PanelId == panelId).Select(v => (int?)v.Ordinal).MaxAsync() ?? 0;
            var draft = new PanelVersion
            {
                PanelId = panelId,
                Ordinal = maxOrdinal + 1,
                VersionMajor = versionMajor,
                VersionMinor = versionMinor,
                Status = PanelVersionStatus.Borrador,
                ChangeNotes = changeNotes.Trim(),
                MasterSheetCode = _masterSheetCode
            };

            if (copyFromVersionId.HasValue)
            {
                var source = await _db.PanelVersions.Include(v => v.Tubes).FirstOrDefaultAsync(v => v.Id == copyFromVersionId.Value);
                if (source != null)
                {
                    int tn = 1;
                    foreach (var t in source.Tubes.OrderBy(t => t.TubeNumber))
                    {
                        draft.Tubes.Add(new PanelTube
                        {
                            TubeNumber = tn++, MarkerList = t.MarkerList, Notes = t.Notes, IsOptional = t.IsOptional,
                            FormulaCode = t.FormulaCode, FormulaRevision = t.FormulaRevision
                        });
                    }
                    draft.QmsDocumentRef = new QmsReference { Code = source.QmsDocumentRef.Code };
                    draft.MasterSheetCode = source.MasterSheetCode ?? _masterSheetCode;
                    draft.ExternalSource = source.ExternalSource;
                    draft.ExternalName = source.ExternalName;
                    draft.ExternalVersion = source.ExternalVersion;
                    // La revisión de la ficha maestra NO se copia: una versión nueva se define
                    // en una revisión nueva de la ficha.
                }
            }

            _db.PanelVersions.Add(draft);
            await _db.SaveChangesAsync();
            return draft;
        }

        public async Task SaveDraftVersionAsync(int panelVersionId, PanelVersionDraftInput input)
        {
            await RequireEditorAsync();
            var existing = await LoadAsync(panelVersionId);

            // Inmutabilidad (M-4): solo un borrador se edita. En revisión, aprobada, vigente o
            // retirada no se modifica; el camino es una versión nueva.
            if (existing.Status != PanelVersionStatus.Borrador)
                throw new InvalidOperationException("Solo se puede modificar una versión en borrador. Cree una versión nueva para introducir cambios.");

            await ValidateNumberAsync(existing.PanelId, input.VersionMajor, input.VersionMinor, existing.Id);
            _currentUserService.ActionContext = "Edición de versión de panel (borrador)";

            existing.VersionMajor = input.VersionMajor;
            existing.VersionMinor = input.VersionMinor;
            existing.ChangeNotes = Clean(input.ChangeNotes, 500);
            existing.ChangeEvaluationRef = Clean(input.ChangeEvaluationRef, 100);
            existing.QmsDocumentRef.Code = Clean(input.QmsDocumentRefCode, 100);
            existing.MasterSheetCode = Clean(input.MasterSheetCode, 50);
            existing.MasterSheetRevision = Clean(input.MasterSheetRevision, 20);
            existing.ExternalSource = Clean(input.ExternalSource, 50);
            existing.ExternalName = Clean(input.ExternalName, 100);
            existing.ExternalVersion = Clean(input.ExternalVersion, 30);

            _db.PanelTubes.RemoveRange(existing.Tubes);
            existing.Tubes.Clear();
            int order = 1;
            foreach (var t in input.Tubes)
            {
                existing.Tubes.Add(new PanelTube
                {
                    TubeNumber = order++,
                    MarkerList = (t.MarkerList ?? string.Empty).Trim(),
                    Notes = Clean(t.Notes, 200),
                    IsOptional = t.IsOptional,
                    FormulaCode = Clean(t.FormulaCode, 50),
                    FormulaRevision = Clean(t.FormulaRevision, 20)
                });
            }

            await _db.SaveChangesAsync();
        }

        public async Task DeleteDraftAsync(int panelVersionId)
        {
            await RequireEditorAsync();
            var v = await LoadAsync(panelVersionId);
            if (v.Status != PanelVersionStatus.Borrador)
                throw new InvalidOperationException("Solo se puede descartar una versión en borrador.");
            if (await _db.SamplePanels.AnyAsync(sp => sp.PanelVersionId == v.Id))
                throw new InvalidOperationException("La versión la usa algún estudio: no se puede descartar.");
            _currentUserService.ActionContext = "Descarte de borrador de versión de panel";
            _db.PanelVersions.Remove(v);
            await _db.SaveChangesAsync();
        }

        private static string? Clean(string? s, int max)
        {
            var t = (s ?? string.Empty).Trim();
            if (t.Length == 0) return null;
            return t.Length > max ? t[..max] : t;
        }

        // ── Circuito ────────────────────────────────────────────────────────────────────

        public async Task SubmitForReviewAsync(int panelVersionId)
        {
            await RequireEditorAsync();
            var v = await LoadAsync(panelVersionId);
            if (v.Status != PanelVersionStatus.Borrador)
                throw new InvalidOperationException("Solo se puede enviar a revisión una versión en borrador.");

            var faltan = new List<string>();
            if (!v.Tubes.Any()) faltan.Add("al menos un tubo");
            foreach (var t in v.Tubes.OrderBy(t => t.TubeNumber))
            {
                if (string.IsNullOrWhiteSpace(t.MarkerList)) faltan.Add($"la combinación del tubo {t.TubeNumber}");
                if (string.IsNullOrWhiteSpace(t.FormulaCode) || string.IsNullOrWhiteSpace(t.FormulaRevision))
                    faltan.Add($"la fórmula y su revisión del tubo {t.TubeNumber}");
            }
            if (string.IsNullOrWhiteSpace(v.MasterSheetCode) || string.IsNullOrWhiteSpace(v.MasterSheetRevision))
                faltan.Add("el código y la revisión de la ficha maestra");
            var noteError = ChangeNoteRules.Validate(v.ChangeNotes);
            if (noteError != null) faltan.Add("una descripción del cambio válida (" + noteError + ")");

            // Cambio mayor respecto a la última versión aprobada: exige su evaluación en el QMS.
            var previous = await _db.PanelVersions
                .Where(x => x.PanelId == v.PanelId && x.Id != v.Id &&
                            (x.Status == PanelVersionStatus.Vigente || x.Status == PanelVersionStatus.Retirada || x.Status == PanelVersionStatus.Aprobada))
                .OrderByDescending(x => x.VersionMajor).ThenByDescending(x => x.VersionMinor)
                .FirstOrDefaultAsync();
            if (previous != null && v.VersionMajor > previous.VersionMajor && string.IsNullOrWhiteSpace(v.ChangeEvaluationRef))
                faltan.Add($"la referencia a la evaluación del cambio (sube la versión mayor respecto a la {previous.VersionLabel})");

            if (faltan.Any())
                throw new InvalidOperationException("No se puede enviar a revisión. Falta: " + string.Join("; ", faltan) + ".");

            _currentUserService.ActionContext = "Versión de panel enviada a revisión";
            v.Status = PanelVersionStatus.EnRevision;
            v.SubmittedByUserId = await _currentUserService.GetUserIdAsync();
            v.SubmittedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        public async Task ReturnToDraftAsync(int panelVersionId, string reason)
        {
            await RequireFacultativoAsync("devolver una versión a borrador");
            var v = await LoadAsync(panelVersionId);
            if (v.Status != PanelVersionStatus.EnRevision && v.Status != PanelVersionStatus.Aprobada)
                throw new InvalidOperationException("Solo se puede devolver a borrador una versión en revisión o aprobada que aún no ha entrado en vigor.");
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
                throw new InvalidOperationException("Indique el motivo (mínimo 10 caracteres).");

            _currentUserService.ActionContext = "Versión de panel devuelta a borrador: " + reason.Trim();
            v.Status = PanelVersionStatus.Borrador;
            v.SubmittedByUserId = null;
            v.SubmittedAtUtc = null;
            v.ApprovedByUserId = null;
            v.ApprovedByName = null;
            v.ApprovedAtUtc = null;
            v.CompositionVerified = false;
            v.EffectiveFromUtc = null;
            await _db.SaveChangesAsync();
        }

        public async Task ApproveAsync(int panelVersionId, DateTime effectiveFromUtc, bool compositionVerified)
        {
            await RequireFacultativoAsync("aprobar una versión de panel");
            var v = await LoadAsync(panelVersionId);
            if (v.Status != PanelVersionStatus.EnRevision)
                throw new InvalidOperationException("Solo se puede aprobar una versión en revisión.");
            if (!compositionVerified)
                throw new InvalidOperationException($"Confirme que la composición coincide con la ficha maestra ({v.MasterSheetCode} rev. {v.MasterSheetRevision}).");

            var now = DateTime.UtcNow;
            var userId = await _currentUserService.GetUserIdAsync();
            var userName = await _db.Users.Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync();

            _currentUserService.ActionContext = "Aprobación de versión de panel";
            v.Status = PanelVersionStatus.Aprobada;
            v.ApprovedByUserId = userId;
            v.ApprovedByName = userName ?? await _currentUserService.GetUsernameAsync();
            v.ApprovedAtUtc = now;
            v.CompositionVerified = true;
            v.EffectiveFromUtc = effectiveFromUtc < now ? now : effectiveFromUtc;
            await _db.SaveChangesAsync();

            await ActivateDueVersionsAsync(now);
        }

        public async Task<int> ActivateDueVersionsAsync(DateTime? nowUtc = null)
        {
            var now = nowUtc ?? DateTime.UtcNow;
            var due = await _db.PanelVersions
                .Where(v => v.Status == PanelVersionStatus.Aprobada && v.EffectiveFromUtc != null && v.EffectiveFromUtc <= now)
                .ToListAsync();
            if (due.Count == 0) return 0;

            var previousContext = _currentUserService.ActionContext;
            _currentUserService.ActionContext = "Entrada en vigor programada de versión de panel";
            int activated = 0;
            foreach (var group in due.GroupBy(v => v.PanelId))
            {
                var winner = group.OrderByDescending(v => v.VersionMajor).ThenByDescending(v => v.VersionMinor).First();

                var vigente = await _db.PanelVersions.FirstOrDefaultAsync(v => v.PanelId == group.Key && v.Status == PanelVersionStatus.Vigente);
                if (vigente != null)
                {
                    vigente.Status = PanelVersionStatus.Retirada;
                    vigente.EffectiveToUtc = winner.EffectiveFromUtc;
                    vigente.RetirementReason = $"Sustituida por la {winner.VersionLabel}";
                }
                // Si había varias aprobadas vencidas a la vez, solo entra en vigor la más
                // alta; las otras no llegaron a estar vigentes.
                foreach (var loser in group.Where(v => v.Id != winner.Id))
                {
                    loser.Status = PanelVersionStatus.Retirada;
                    loser.EffectiveToUtc = loser.EffectiveFromUtc;
                    loser.RetirementReason = $"Sustituida por la {winner.VersionLabel} antes de entrar en vigor";
                }
                winner.Status = PanelVersionStatus.Vigente;
                activated++;
            }
            await _db.SaveChangesAsync();
            _currentUserService.ActionContext = previousContext;
            return activated;
        }

        public async Task RetireAsync(int panelVersionId, string reason)
        {
            await RequireFacultativoAsync("retirar una versión de panel");
            var v = await LoadAsync(panelVersionId);
            if (v.Status != PanelVersionStatus.Vigente)
                throw new InvalidOperationException("Solo se puede retirar la versión vigente.");
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
                throw new InvalidOperationException("Indique el motivo de la retirada (mínimo 10 caracteres).");

            _currentUserService.ActionContext = "Retirada de versión de panel";
            v.Status = PanelVersionStatus.Retirada;
            v.EffectiveToUtc = DateTime.UtcNow;
            v.RetiredByUserId = await _currentUserService.GetUserIdAsync();
            v.RetirementReason = reason.Trim().Length > 300 ? reason.Trim()[..300] : reason.Trim();
            await _db.SaveChangesAsync();
        }

        public async Task AddClarificationAsync(int panelVersionId, string text)
        {
            await RequireEditorAsync();
            var v = await LoadAsync(panelVersionId);
            if (v.Status == PanelVersionStatus.Borrador)
                throw new InvalidOperationException("En un borrador, corrija directamente la descripción del cambio.");
            var t = (text ?? string.Empty).Trim();
            if (t.Length < 10) throw new InvalidOperationException("La aclaración debe tener al menos 10 caracteres.");

            var userId = await _currentUserService.GetUserIdAsync();
            var name = await _db.Users.Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync()
                       ?? await _currentUserService.GetUsernameAsync();
            _currentUserService.ActionContext = "Aclaración de versión de panel";
            _db.PanelVersionClarifications.Add(new PanelVersionClarification
            {
                PanelVersionId = v.Id,
                Text = t.Length > 500 ? t[..500] : t,
                AuthorName = name
            });
            await _db.SaveChangesAsync();
        }
    }
}
