using System;
using System.Security.Claims;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>Resultado de evaluar una petición de exportación de datos de paciente: si se
    /// permite, si el rechazo debe traducirse en 403 (autorización) o 400 (petición inválida),
    /// el motivo legible para mostrar al usuario, y si la exportación resultante debe incluir
    /// identificadores directos (NHC/nombre) o ir seudonimizada.</summary>
    public record ExportDecision(bool Allowed, string? DenialReason, bool IncludeIdentifiers, bool IsForbidden = false);

    /// <summary>
    /// Punto único de decisión para toda exportación que pueda devolver NHC, NASI o nombre de
    /// paciente (Regla 3 — la identidad del paciente no sale del sistema sin control ni sin
    /// registro). Ninguna exportación nueva debe evaluar permisos por su cuenta: la interacción
    /// C-2/N-2 demostró que una implementación de referencia bien hecha (ExportMuestras) no
    /// evita que otras dos rutas paralelas del mismo requisito nazcan sin restricción.
    /// </summary>
    public interface IPatientDataExportPolicy
    {
        ExportDecision Evaluate(ClaimsPrincipal user, DateTime? desde, DateTime? hasta, bool incluirIdentificadores);
    }
}
