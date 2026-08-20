using Microsoft.Extensions.Configuration;
using MiniLIS.Application.Interfaces;
using System;
using System.Security.Claims;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>Implementación de referencia (N-2): mismas reglas que ExportMuestras aplicaba
    /// de forma incrustada -- rol, rango de fechas obligatorio y acotado, e identificadores
    /// reservados a Administrador. Sin estado propio: cada llamada es autocontenida, así que
    /// se puede registrar como Scoped o Singleton indistintamente.</summary>
    public class PatientDataExportPolicy : IPatientDataExportPolicy
    {
        private readonly IConfiguration _configuration;

        public PatientDataExportPolicy(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public ExportDecision Evaluate(ClaimsPrincipal user, DateTime? desde, DateTime? hasta, bool incluirIdentificadores)
        {
            if (!user.IsInRole("Administrador") && !user.IsInRole("Facultativo"))
                return new ExportDecision(false, "Solo Administrador o Facultativo pueden exportar datos de pacientes.", false, IsForbidden: true);

            if (incluirIdentificadores && !user.IsInRole("Administrador"))
                return new ExportDecision(false, "Solo Administrador puede incluir identificadores del paciente en la exportación.", false, IsForbidden: true);

            if (desde is null || hasta is null)
                return new ExportDecision(false, "Debe indicarse un rango de fechas (desde y hasta).", false);

            if (hasta.Value < desde.Value)
                return new ExportDecision(false, "La fecha 'hasta' no puede ser anterior a 'desde'.", false);

            var maxDias = _configuration.GetValue<int?>("Export:MaxRangoDias") ?? 366;
            if ((hasta.Value.Date - desde.Value.Date).TotalDays > maxDias)
                return new ExportDecision(false, $"El rango no puede superar {maxDias} días.", false);

            return new ExportDecision(true, null, incluirIdentificadores);
        }
    }
}
