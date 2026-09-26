using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>
    /// Cuánta identidad del paciente lleva una exportación. Tres niveles y no un sí/no porque
    /// para la mayor parte del trabajo basta el NHC: llevarse además el nombre multiplica el
    /// daño si el fichero se traspapela, y no aporta nada que el NHC no resuelva dentro del
    /// hospital (minimización del dato, RGPD art. 5.1.c).
    /// </summary>
    public enum ExportIdentityLevel
    {
        /// <summary>Seudonimizada: ni NHC ni nombre. Es lo que sale por defecto.</summary>
        Ninguno = 0,

        /// <summary>Con NHC, sin nombre.</summary>
        SoloNhc = 1,

        /// <summary>Con NHC y nombre del paciente.</summary>
        NhcYNombre = 2
    }

    /// <summary>Resultado de evaluar una petición de exportación de datos de paciente: si se
    /// permite, si el rechazo debe traducirse en 403 (autorización) o 400 (petición inválida),
    /// el motivo legible para mostrar al usuario, y cuánta identidad puede llevar el fichero
    /// resultante.</summary>
    /// <summary>Justification: motivo indicado para incluir identificadores (obligatorio para el
    /// facultativo); se guarda en la auditoría de la exportación.</summary>
    public record ExportDecision(bool Allowed, string? DenialReason, ExportIdentityLevel Level, bool IsForbidden = false, string? Justification = null)
    {
        /// <summary>¿Lleva algún identificador directo? Se conserva porque es lo que preguntan
        /// las exportaciones que solo distinguen entre seudonimizada y completa.</summary>
        public bool IncludeIdentifiers => Level != ExportIdentityLevel.Ninguno;

        /// <summary>Cómo describir el nivel en la auditoría.</summary>
        public string LevelDescription => Level switch
        {
            ExportIdentityLevel.Ninguno => "seudonimizada",
            ExportIdentityLevel.SoloNhc => "con NHC (sin nombre)",
            _ => "con NHC y nombre"
        };
    }

    /// <summary>
    /// Punto único de decisión para toda exportación que pueda devolver NHC, NASI o nombre de
    /// paciente (Regla 3 — la identidad del paciente no sale del sistema sin control ni sin
    /// registro). Ninguna exportación nueva debe evaluar permisos por su cuenta: la interacción
    /// C-2/N-2 demostró que una implementación de referencia bien hecha (ExportMuestras) no
    /// evita que otras dos rutas paralelas del mismo requisito nazcan sin restricción -- y el
    /// CSV del buscador, añadido después, nació sin permiso, sin justificación y sin auditoría.
    /// </summary>
    public interface IPatientDataExportPolicy
    {
        Task<ExportDecision> EvaluateAsync(ClaimsPrincipal user, DateTime? desde, DateTime? hasta,
            ExportIdentityLevel nivel, string? justificacion = null);
    }
}
