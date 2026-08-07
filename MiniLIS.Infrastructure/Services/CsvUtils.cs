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
    }
}
