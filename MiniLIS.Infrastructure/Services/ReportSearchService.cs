using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Entities;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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

        // --- Combinación de varios términos dentro de un mismo campo ----------------------
        //
        // Operadores "&" (todos) y "|" (alguno). La elección no es estética: las cadenas
        // reales de marcadores son del estilo "CD117 -/+d, HLA-DR -/+, MPO +d/+", de modo que
        // "+", "-", "/" y el espacio forman parte del dato y no pueden separar términos.
        // Tampoco valen las palabras "y"/"o": en el cuerpo del informe aparecen a cada línea.
        // "&" y "|" no aparecen en ningún campo buscable.
        //
        // Sin operador, el texto se busca entero y literal: quien no conozca la sintaxis
        // obtiene el comportamiento de siempre.

        private const char OperadorY = '&';
        private const char OperadorO = '|';

        private sealed class TerminosCampo
        {
            public List<string> Terminos { get; init; } = new();
            public bool EsO { get; init; }
            public string? Error { get; init; }
        }

        private static TerminosCampo Analizar(string entrada, string nombreCampo)
        {
            bool tieneY = entrada.Contains(OperadorY);
            bool tieneO = entrada.Contains(OperadorO);

            // Mezclar ambos exigiría explicar precedencias; es preferible decirlo que
            // resolverlo en silencio de una forma que el usuario no espera.
            if (tieneY && tieneO)
            {
                return new TerminosCampo
                {
                    Error = $"En «{nombreCampo}» no se pueden mezclar «&» y «|» en la misma búsqueda. " +
                            "Use uno u otro, o haga dos búsquedas."
                };
            }

            var separador = tieneO ? OperadorO : OperadorY;
            var partes = entrada
                .Split(separador)
                .Select(x => Norm(x))
                .Where(x => x.Length > 0)
                .ToList();

            if (partes.Count == 0)
            {
                return new TerminosCampo { Error = $"En «{nombreCampo}» no hay ningún término que buscar." };
            }

            return new TerminosCampo { Terminos = partes, EsO = tieneO };
        }

        /// <summary>
        /// Aplica un campo de texto a la consulta. `construir` recibe UN término ya
        /// normalizado y devuelve la condición para ese término (que puede mirar en varias
        /// columnas); aquí solo se combinan las condiciones de todos los términos.
        /// </summary>
        private static IQueryable<Sample> AplicarTexto(
            IQueryable<Sample> q,
            string? entrada,
            string nombreCampo,
            Func<string, Expression<Func<Sample, bool>>> construir,
            List<string> errores)
        {
            if (string.IsNullOrWhiteSpace(entrada)) return q;

            var analisis = Analizar(entrada, nombreCampo);
            if (analisis.Error != null)
            {
                errores.Add(analisis.Error);
                return q;
            }

            if (analisis.EsO)
            {
                var combinado = construir(analisis.Terminos[0]);
                foreach (var t in analisis.Terminos.Skip(1))
                {
                    combinado = PredicateUtils.O(combinado, construir(t));
                }
                return q.Where(combinado);
            }

            // La Y se obtiene encadenando Where: más simple y con el mismo resultado.
            foreach (var t in analisis.Terminos)
            {
                q = q.Where(construir(t));
            }
            return q;
        }

        public async Task<ReportSearchResult> SearchAsync(ReportSearchFilter filtro, int maxResults = 500)
        {
            // Sin ningún criterio no se busca: devolver el histórico completo no es una
            // búsqueda, y con varios miles de estudios sería además lento e inútil.
            if (filtro.EstaVacio)
            {
                return new ReportSearchResult();
            }

            var errores = new List<string>();

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
            // Conclusions y el Diagnosis del informe son dos campos distintos que un
            // facultativo usa indistintamente para la conclusión diagnóstica.
            q = AplicarTexto(q, filtro.Conclusiones, "Conclusión diagnóstica", t =>
                s => s.Report != null &&
                    ((s.Report.Conclusions != null && s.Report.Conclusions.ToLower().Contains(t)) ||
                     (s.Report.Diagnosis != null && s.Report.Diagnosis.ToLower().Contains(t))), errores);

            q = AplicarTexto(q, filtro.CuerpoInforme, "Cuerpo del informe", t =>
                s => s.Report != null &&
                    ((s.Report.ReportBody != null && s.Report.ReportBody.ToLower().Contains(t)) ||
                     (s.Report.AdditionalText != null && s.Report.AdditionalText.ToLower().Contains(t))), errores);

            // --- Datos de la petición ----------------------------------------------------
            q = AplicarTexto(q, filtro.SospechaClinica, "Sospecha clínica", t =>
                s => s.Diagnosis.ToLower().Contains(t), errores);

            q = AplicarTexto(q, filtro.Facultativo, "Facultativo solicitante", t =>
                s => s.ClinicalRequest.DoctorName.ToLower().Contains(t), errores);

            q = AplicarTexto(q, filtro.Servicio, "Servicio de procedencia", t =>
                s => s.ClinicalRequest.OriginService.ToLower().Contains(t), errores);

            // --- Marcadores --------------------------------------------------------------
            // Los marcadores viven en dos sitios: la tabla de valores del informe (cuando se
            // usa plantilla) y el resumen en texto (redactado a mano). Buscar solo en uno
            // perdería la mitad de los estudios.
            //
            // El resumen es el que permite buscar la INTENSIDAD ("CD34 -", "CD117 +"), porque
            // la tabla de valores guarda nombre e intensidad en columnas separadas y el nombre
            // por sí solo no distingue "CD34 -" de "CD34 ++".
            q = AplicarTexto(q, filtro.Marcador, "Marcador", t =>
                s => s.Report != null &&
                    (s.Report.MarkerValues.Any(mv => mv.Marker.Name.ToLower().Contains(t)) ||
                     (s.Report.MarkersSummary != null && s.Report.MarkersSummary.ToLower().Contains(t))), errores);

            // --- Paneles -----------------------------------------------------------------
            // Panels es el dato vigente; StudyPanel es el campo de texto heredado que
            // conservan las muestras antiguas. Se buscan ambos para no perder histórico.
            q = AplicarTexto(q, filtro.Panel, "Panel realizado", t =>
                s => s.Panels.Any(sp => sp.Panel != null && sp.Panel.Name.ToLower().Contains(t)) ||
                     s.StudyPanel.ToLower().Contains(t), errores);

            // --- Paciente / muestra ------------------------------------------------------
            q = AplicarTexto(q, filtro.Paciente, "Paciente o nº de muestra", t =>
                s => s.SampleNumber.ToLower().Contains(t) ||
                     s.ClinicalRequest.Patient.FullName.ToLower().Contains(t) ||
                     s.ClinicalRequest.Patient.NHC.ToLower().Contains(t) ||
                     s.ClinicalRequest.Patient.NASI.ToLower().Contains(t), errores);

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
                Truncated = total > items.Count,
                Avisos = errores
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
