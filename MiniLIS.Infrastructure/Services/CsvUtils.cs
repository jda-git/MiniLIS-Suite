using System.Linq;
using System.Text;

namespace MiniLIS.Infrastructure.Services
{
    public static class CsvUtils
    {
        /// <summary>
        /// Escapa un campo para un CSV delimitado por ';'. Si contiene el delimitador,
        /// comillas o un salto de línea, lo envuelve en comillas dobles y duplica las
        /// comillas internas, siguiendo el criterio habitual de RFC 4180.
        /// </summary>
        public static string EscapeField(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            bool needsQuoting = value.Contains(';') || value.Contains('"') ||
                                 value.Contains('\n') || value.Contains('\r');

            if (!needsQuoting) return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Convierte el CSV a bytes con marca de orden de bytes (BOM) de UTF-8. Sin ella,
        /// Excel abre el fichero como ANSI y destroza cualquier tilde o eñe: "Recepción"
        /// aparece como "RecepciÃ³n".
        ///
        /// OJO con el atajo aparente: `new UTF8Encoding(true).GetBytes(...)` NO escribe el
        /// BOM. Ese parámetro solo hace que GetPreamble() lo devuelva, y GetBytes jamás
        /// antepone el preámbulo — hay que concatenarlo a mano, como se hace aquí. Es un
        /// fallo silencioso: compila, genera un CSV válido y solo se nota al abrirlo.
        /// </summary>
        public static byte[] ToExcelBytes(string csv) =>
            Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
    }
}
