using Microsoft.Extensions.Configuration;
using MiniLIS.Application.Interfaces;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>Implementación de referencia (N-2): mismas reglas que ExportMuestras aplicaba
    /// de forma incrustada -- rol, rango de fechas obligatorio y acotado, e identificadores
    /// reservados a Administrador. Sin estado propio: cada llamada es autocontenida, así que
    /// se puede registrar como Scoped o Singleton indistintamente.</summary>
    public class PatientDataExportPolicy : IPatientDataExportPolicy
    {
        private readonly IConfiguration _configuration;
        private readonly IPermissionService _permissions;

        public PatientDataExportPolicy(IConfiguration configuration, IPermissionService permissions)
        {
            _configuration = configuration;
            _permissions = permissions;
        }

        /// <summary>Mínimo de la justificación que debe dar el facultativo para incluir identificadores.</summary>
        public const int MinJustificationLength = 10;

        public async Task<ExportDecision> EvaluateAsync(ClaimsPrincipal user, DateTime? desde, DateTime? hasta,
            ExportIdentityLevel nivel, string? justificacion = null)
        {
            var conIdentidad = nivel != ExportIdentityLevel.Ninguno;

            // Red de seguridad (N-2): aunque cada extremo tiene ya su propia política, quien no
            // pueda exportar nada de esto no obtiene datos de paciente por esta vía.
            if (!await _permissions.HasAsync(user, Permissions.ExportMuestras)
                && !await _permissions.HasAsync(user, Permissions.ExcedenteVer)
                && !await _permissions.HasAsync(user, Permissions.NotificacionesVer)
                && !await _permissions.HasAsync(user, Permissions.BuscadorVer))
                return new ExportDecision(false, "No tiene permiso para exportar datos de pacientes.", ExportIdentityLevel.Ninguno, IsForbidden: true);

            // v4.1: quién puede incluir identidad de paciente, y quién está exento de
            // justificarlo, sale de la matriz de permisos (Configuración → Permisos).
            //
            // v4.4: el NHC pide el mismo permiso que el nombre. Identifica al paciente dentro
            // del hospital, así que un listado con NHC es dato personal igual: lo que cambia
            // entre los dos niveles es cuánto daño hace si el fichero acaba donde no debe, no
            // si hace falta permiso.
            if (conIdentidad && !await _permissions.HasAsync(user, Permissions.ExportIdentificadores))
                return new ExportDecision(false, "No tiene permiso para incluir identificadores del paciente en la exportación.", ExportIdentityLevel.Ninguno, IsForbidden: true);

            var motivo = (justificacion ?? string.Empty).Trim();
            if (conIdentidad && !await _permissions.HasAsync(user, Permissions.ExportIdentificadoresSinJustificar) && motivo.Length < MinJustificationLength)
                return new ExportDecision(false,
                    $"Para incluir identificadores del paciente indique la justificación (mínimo {MinJustificationLength} caracteres).", ExportIdentityLevel.Ninguno);

            if (desde is null || hasta is null)
                return new ExportDecision(false, "Debe indicarse un rango de fechas (desde y hasta).", ExportIdentityLevel.Ninguno);

            if (hasta.Value < desde.Value)
                return new ExportDecision(false, "La fecha 'hasta' no puede ser anterior a 'desde'.", ExportIdentityLevel.Ninguno);

            var maxDias = _configuration.GetValue<int?>("Export:MaxRangoDias") ?? 366;
            if ((hasta.Value.Date - desde.Value.Date).TotalDays > maxDias)
                return new ExportDecision(false, $"El rango no puede superar {maxDias} días.", ExportIdentityLevel.Ninguno);

            return new ExportDecision(true, null, nivel,
                Justification: conIdentidad && motivo.Length > 0 ? (motivo.Length > 300 ? motivo[..300] : motivo) : null);
        }
    }
}
