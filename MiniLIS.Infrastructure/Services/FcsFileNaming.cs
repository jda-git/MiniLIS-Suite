using System.Globalization;
using System.Text;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>
    /// Nomenclatura de ficheros FCS (F-6, reutilizado por M-6) y saneado de valores para la
    /// hoja de trabajo del citómetro. Mismas reglas en ambos: solo A-Z0-9-_, sin espacios,
    /// tildes ni ñ — un nombre de espécimen con caracteres no ASCII puede romper la
    /// importación o corromper el nombre del fichero resultante.
    /// </summary>
    public static class FcsFileNaming
    {
        public static string GenerateFileName(string sampleNumber, string sampleTypeCode, int tubeNumber, string panelCode, int panelVersion)
        {
            var number = Sanitize(sampleNumber);
            var type = Sanitize(sampleTypeCode);
            var panel = Sanitize(panelCode);
            return $"{number}_{type}_T{tubeNumber:D2}_{panel}-v{panelVersion:D2}.fcs";
        }

        /// <summary>Mayúsculas, sin tildes/ñ/espacios, solo A-Z0-9-_. Devuelve además si el
        /// valor tuvo que modificarse, para poder avisar en la vista previa (F-6, punto 7).</summary>
        public static string Sanitize(string? value, out bool wasModified)
        {
            var original = value ?? string.Empty;
            var normalized = original.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

                if (char.IsLetterOrDigit(c) && c <= 'z')
                {
                    sb.Append(char.ToUpperInvariant(c));
                }
                else if (c == '-' || c == '_')
                {
                    sb.Append(c);
                }
                else if (sb.Length > 0 && sb[^1] != '-')
                {
                    sb.Append('-');
                }
            }

            var result = sb.ToString().Trim('-');
            wasModified = result != original;
            return result;
        }

        public static string Sanitize(string? value) => Sanitize(value, out _);
    }
}
