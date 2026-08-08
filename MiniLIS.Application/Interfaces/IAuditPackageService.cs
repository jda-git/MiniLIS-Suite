using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>Nombres de los 11 documentos del paquete de evidencias (F-9), usados como
    /// claves para la selección de documentos a incluir.</summary>
    public static class AuditPackageDocuments
    {
        public const string Indice = "00_INDICE";
        public const string Actividad = "01_ACTIVIDAD";
        public const string Indicadores = "02_INDICADORES";
        public const string Recepcion = "03_RECEPCION";
        public const string Trazabilidad = "04_TRAZABILIDAD";
        public const string Validaciones = "05_VALIDACIONES";
        public const string Paneles = "06_PANELES";
        public const string Accesos = "07_ACCESOS";
        public const string Copias = "08_COPIAS";
        public const string Usuarios = "09_USUARIOS";
        public const string Contingencia = "10_CONTINGENCIA";

        public static readonly string[] All =
        {
            Actividad, Indicadores, Recepcion, Trazabilidad, Validaciones,
            Paneles, Accesos, Copias, Usuarios, Contingencia
        };
    }

    public interface IAuditPackageService
    {
        /// <summary>Genera el paquete de evidencias como ZIP. Solo lectura: no modifica ningún
        /// dato. Seudonimizado por defecto (C-2); incluir identificadores exige rol
        /// Administrador y motivo, comprobado por el llamador antes de invocar esto.</summary>
        Task<byte[]> GenerateAsync(DateTime desde, DateTime hasta, List<string> documentsToInclude,
            bool incluirIdentificadores, string? motivo, int? userId, string? username);
    }
}
