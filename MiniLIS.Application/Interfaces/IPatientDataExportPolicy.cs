using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>Resultado de evaluar una petición de exportación de datos de paciente: si se
    /// permite, si el rechazo debe traducirse en 403 (autorización) o 400 (petición inválida),
    /// el motivo legible para mostrar al usuario, y si la exportación resultante debe incluir
    /// identificadores directos (NHC/nombre) o ir seudonimizada.</summary>
    /// <summary>Justification: motivo indicado para incluir identificadores (obligatorio para el
    /// facultativo); se guarda en la auditoría de la exportación.</summary>
    public record ExportDecision(bool Allowed, string? DenialReason, bool IncludeIdentifiers, bool IsForbidden = false, string? Justification = null);

    /// <summary>
    /// Punto único de decisión para toda exportación que pueda devolver NHC, NASI o nombre de
    /// paciente (Regla 3 — la identidad del paciente no sale del sistema sin control ni sin
    /// registro). Ninguna exportación nueva debe evaluar permisos por su cuenta: la interacción
    /// C-2/N-2 demostró que una implementación de referencia bien hecha (ExportMuestras) no
    /// evita que otras dos rutas paralelas del mismo requisito nazcan sin restricción.
    /// </summary>
    public interface IPatientDataExportPolicy
    {
        Task<ExportDecision> EvaluateAsync(ClaimsPrincipal user, DateTime? desde, DateTime? hasta, bool incluirIdentificadores, string? justificacion = null);
    }
}
