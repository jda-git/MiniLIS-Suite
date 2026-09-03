using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>
    /// Búsqueda combinada sobre muestras e informes. Sustituye a la antigua pantalla de
    /// Estadísticas, cuyos agregados ya daba —mejor calculados— el cuadro de indicadores.
    ///
    /// Todos los criterios se combinan con Y lógica sobre un único IQueryable, de forma que
    /// el filtrado ocurre en la base de datos y no en memoria. Se proyecta a un DTO en vez de
    /// materializar el grafo de entidades: la lista puede ser larga y aquí solo se pintan
    /// unas columnas (misma lección que la Bandeja Técnica, ver CHANGELOG v2.3.0).
    /// </summary>
    public class ReportSearchService : IReportSearchService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILocalTimeService _localTimeService;
        private readonly ICurrentUserService _currentUserService;

        public ReportSearchService(ApplicationDbContext db, ILocalTimeService localTimeService, ICurrentUserService currentUserService)
        {
            _db = db;
            _localTimeService = localTimeService;
            _currentUserService = currentUserService;
        }

        // Se compara en minúsculas porque el LIKE de SQLite distingue mayúsculas fuera del
        // ASCII, y aquí se busca en texto clínico en español con tildes y eñes. Mismo criterio
        // que la búsqueda de la Bandeja Técnica.
        private static string Norm(string s) => s.Trim().ToLower();

        public async Task<ReportSearchResult> SearchAsync(ReportSearchFilter filtro, int maxResults = 500)
        {
            // Sin ningún criterio no se busca: devolver el histórico completo no es una
            // búsqueda, y con varios miles de estudios sería además lento e inútil.
            if (filtro.EstaVacio)
            {
                return new ReportSearchResult();
            }

            var q = _db.Samples
                .Include(s => s.ClinicalRequest).ThenInclude(cr => cr.Patient)
                .Include(s => s.Report)
                .AsQueryable();

            // --- Rango de fechas (sobre ReceptionDate, la fecha de negocio) --------------
            if (filtro.Desde.HasValue)
            {
                var desdeUtc = _localTimeService.ToUtc(filtro.Desde.Value.Date);
                q = q.Where(s => s.ReceptionDate >= desdeUtc);
            }
            if (filtro.Hasta.HasValue)
            {
                var hastaUtc = _localTimeService.ToUtc(filtro.Hasta.Value.Date.AddDays(1)).AddTicks(-1);
                q = q.Where(s => s.ReceptionDate <= hastaUtc);
            }

            // --- Contenido del informe ---------------------------------------------------
            if (!string.IsNullOrWhiteSpace(filtro.Conclusiones))
            {
                var t = Norm(filtro.Conclusiones);
                // Conclusions y el Diagnosis del informe son dos campos distintos que un
                // facultativo usa indistintamente para la conclusión diagnóstica.
                q = q.Where(s => s.Report != null &&
                    ((s.Report.Conclusions != null && s.Report.Conclusions.ToLower().Contains(t)) ||
                     (s.Report.Diagnosis != null && s.Report.Diagnosis.ToLower().Contains(t))));
            }
            if (!string.IsNullOrWhiteSpace(filtro.CuerpoInforme))
            {
                var t = Norm(filtro.CuerpoInforme);
                q = q.Where(s => s.Report != null &&
                    ((s.Report.ReportBody != null && s.Report.ReportBody.ToLower().Contains(t)) ||
                     (s.Report.AdditionalText != null && s.Report.AdditionalText.ToLower().Contains(t))));
            }

            // --- Datos de la petición ----------------------------------------------------
            if (!string.IsNullOrWhiteSpace(filtro.SospechaClinica))
            {
                var t = Norm(filtro.SospechaClinica);
                q = q.Where(s => s.Diagnosis.ToLower().Contains(t));
            }
            if (!string.IsNullOrWhiteSpace(filtro.Facultativo))
            {
                var t = Norm(filtro.Facultativo);
                q = q.Where(s => s.ClinicalRequest.DoctorName.ToLower().Contains(t));
            }
            if (!string.IsNullOrWhiteSpace(filtro.Servicio))
            {
                var t = Norm(filtro.Servicio);
                q = q.Where(s => s.ClinicalRequest.OriginService.ToLower().Contains(t));
            }

            // --- Marcadores --------------------------------------------------------------
            if (!string.IsNullOrWhiteSpace(filtro.Marcador))
            {
                var t = Norm(filtro.Marcador);
                // Los marcadores viven en dos sitios: la tabla de valores del informe (cuando
                // se usa plantilla) y el resumen en texto (redactado a mano). Buscar solo en
                // uno perdería la mitad de los estudios.
                q = q.Where(s => s.Report != null &&
                    (s.Report.MarkerValues.Any(mv => mv.Marker.Name.ToLower().Contains(t)) ||
                     (s.Report.MarkersSummary != null && s.Report.MarkersSummary.ToLower().Contains(t))));
            }

            // --- Paneles -----------------------------------------------------------------
            if (!string.IsNullOrWhiteSpace(filtro.Panel))
            {
                var t = Norm(filtro.Panel);
                // Panels es el dato vigente; StudyPanel es el campo de texto heredado que
                // conservan las muestras antiguas. Se buscan ambos para no perder histórico.
                q = q.Where(s =>
                    s.Panels.Any(sp => sp.Panel != null && sp.Panel.Name.ToLower().Contains(t)) ||
                    s.StudyPanel.ToLower().Contains(t));
            }

            // --- Paciente / muestra ------------------------------------------------------
            if (!string.IsNullOrWhiteSpace(filtro.Paciente))
            {
                var t = Norm(filtro.Paciente);
                q = q.Where(s =>
                    s.SampleNumber.ToLower().Contains(t) ||
                    s.ClinicalRequest.Patient.FullName.ToLower().Contains(t) ||
                    s.ClinicalRequest.Patient.NHC.ToLower().Contains(t) ||
                    s.ClinicalRequest.Patient.NASI.ToLower().Contains(t));
            }

            // --- Clasificación -----------------------------------------------------------
            if (filtro.TipoMuestra.HasValue)
            {
                q = q.Where(s => s.SampleType == filtro.TipoMuestra.Value);
            }
            if (filtro.Estado.HasValue)
            {
                q = q.Where(s => s.Status == filtro.Estado.Value);
            }
            if (filtro.SoloValidados)
            {
                q = q.Where(s => s.Report != null && s.Report.ValidatedAtUtc != null);
            }

            int total = await q.CountAsync();

            var rows = await q
                .OrderByDescending(s => s.ReceptionDate)
                .Take(maxResults)
                .Select(s => new
                {
                    s.Id,
                    s.SampleNumber,
                    s.ReceptionDate,
                    Patient = s.ClinicalRequest.Patient.FullName,
                    Nhc = s.ClinicalRequest.Patient.NHC,
                    Servicio = s.ClinicalRequest.OriginService,
                    Facultativo = s.ClinicalRequest.DoctorName,
                    s.Diagnosis,
                    s.Status,
                    Paneles = s.Panels.Where(sp => sp.Panel != null).Select(sp => sp.Panel!.Name).ToList(),
                    StudyPanelLegacy = s.StudyPanel,
                    TieneInforme = s.Report != null,
                    ValidatedAtUtc = s.Report != null ? s.Report.ValidatedAtUtc : null,
                    Conclusion = s.Report != null ? s.Report.Conclusions : null
                })
                .ToListAsync();

            var items = rows.Select(r => new ReportSearchResultItem
            {
                SampleId = r.Id,
                SampleNumber = r.SampleNumber,
                ReceptionDate = _localTimeService.ToLocal(r.ReceptionDate),
                Patient = r.Patient ?? "",
                Nhc = r.Nhc ?? "",
                Servicio = r.Servicio ?? "",
                Facultativo = r.Facultativo ?? "",
                SospechaClinica = r.Diagnosis ?? "",
                Paneles = r.Paneles.Any() ? string.Join(", ", r.Paneles) : (r.StudyPanelLegacy ?? ""),
                Estado = r.Status.ToString(),
                TieneInforme = r.TieneInforme,
                Validado = r.ValidatedAtUtc != null,
                ValidatedAtUtc = r.ValidatedAtUtc,
                Conclusion = Resumir(r.Conclusion, 220)
            }).ToList();

            await AuditarBusquedaAsync(filtro, total);

            return new ReportSearchResult
            {
                Items = items,
                TotalMatches = total,
                Truncated = total > items.Count
            };
        }

        private static string Resumir(string? texto, int max)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            var limpio = texto.Replace("\r", " ").Replace("\n", " ").Trim();
            while (limpio.Contains("  ")) limpio = limpio.Replace("  ", " ");
            return limpio.Length <= max ? limpio : limpio.Substring(0, max) + "…";
        }

        /// <summary>
        /// M-2: esta búsqueda alcanza contenido clínico e identificadores de paciente, así que
        /// queda constancia de quién buscó, con qué criterios y cuántos resultados obtuvo. Se
        /// registran los criterios, nunca el contenido devuelto.
        /// </summary>
        private async Task AuditarBusquedaAsync(ReportSearchFilter f, int total)
        {
            var criterios = new List<string>();
            if (f.Desde.HasValue || f.Hasta.HasValue)
                criterios.Add($"fechas={f.Desde:yyyy-MM-dd}..{f.Hasta:yyyy-MM-dd}");
            void Add(string nombre, string? valor)
            {
                if (!string.IsNullOrWhiteSpace(valor)) criterios.Add($"{nombre}=\"{valor.Trim()}\"");
            }
            Add("conclusiones", f.Conclusiones);
            Add("informe", f.CuerpoInforme);
            Add("sospecha", f.SospechaClinica);
            Add("facultativo", f.Facultativo);
            Add("servicio", f.Servicio);
            Add("marcador", f.Marcador);
            Add("panel", f.Panel);
            Add("paciente", f.Paciente);
            if (f.TipoMuestra.HasValue) criterios.Add($"tipo={f.TipoMuestra}");
            if (f.Estado.HasValue) criterios.Add($"estado={f.Estado}");
            if (f.SoloValidados) criterios.Add("solo validados");

            _db.AuditLogs.Add(new AuditLog
            {
                EntityName = nameof(SampleReport),
                EntityId = "",
                Action = "Search",
                UserId = await _currentUserService.GetUserIdAsync(),
                Username = await _currentUserService.GetUsernameAsync(),
                ActionContext = $"Búsqueda de informes: {string.Join(", ", criterios)} ({total} resultados)",
                TimestampUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        public byte[] ExportToCsv(List<ReportSearchResultItem> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Nº muestra;Fecha recepción;Paciente;NHC;Servicio;Facultativo;Sospecha clínica;Paneles;Estado;Validado;Conclusión");
            foreach (var i in items)
            {
                sb.AppendLine(string.Join(";", new[]
                {
                    CsvUtils.EscapeField(i.SampleNumber),
                    CsvUtils.EscapeField(i.ReceptionDate.ToString("dd/MM/yyyy")),
                    CsvUtils.EscapeField(i.Patient),
                    CsvUtils.EscapeField(i.Nhc),
                    CsvUtils.EscapeField(i.Servicio),
                    CsvUtils.EscapeField(i.Facultativo),
                    CsvUtils.EscapeField(i.SospechaClinica),
                    CsvUtils.EscapeField(i.Paneles),
                    CsvUtils.EscapeField(i.Estado),
                    CsvUtils.EscapeField(i.Validado ? "Sí" : "No"),
                    CsvUtils.EscapeField(i.Conclusion)
                }));
            }
            return CsvUtils.ToExcelBytes(sb.ToString());
        }
    }
}
