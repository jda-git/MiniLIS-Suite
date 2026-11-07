using System.Linq;

namespace MiniLIS.Domain.Identity
{
    /// <summary>
    /// Qué se admite como nombre de usuario (v4.10). Hasta la v4.9 tenía que ser un correo,
    /// no porque lo exigiera Identity —que admite letras, dígitos y <c>- . _ @ +</c>— sino
    /// porque la pantalla de alta lo imponía.
    ///
    /// Se admite ahora el formato del directorio corporativo (<c>jpergon</c>) porque la
    /// autenticación local es transitoria: el día que se migre a LDAP/AD la identidad de cada
    /// persona será su usuario de dominio, y si hasta entonces se hubiera usado otra, la
    /// auditoría quedaría partida en dos nombres para la misma persona.
    /// </summary>
    public static class UserNameRules
    {
        public const int MinLength = 3;
        public const int MaxLength = 64;

        /// <summary>El mismo conjunto que admite Identity de serie, para que no haya un nombre
        /// que esta validación acepte y el almacén rechace después.</summary>
        public const string AllowedExtra = "-._@+";

        public static bool IsAllowedChar(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || AllowedExtra.Contains(c);

        /// <summary>Devuelve el motivo del rechazo, o null si el nombre vale.</summary>
        public static string? Validate(string? userName)
        {
            var n = (userName ?? "").Trim();

            if (n.Length == 0) return "El nombre de usuario es obligatorio.";
            if (n.Length < MinLength) return $"El nombre de usuario debe tener al menos {MinLength} caracteres.";
            if (n.Length > MaxLength) return $"El nombre de usuario no puede pasar de {MaxLength} caracteres.";

            var malos = n.Where(c => !IsAllowedChar(c)).Distinct().ToList();
            if (malos.Count > 0)
                return $"El nombre de usuario no admite «{string.Join("», «", malos)}». " +
                       $"Se pueden usar letras, dígitos y {string.Join(" ", AllowedExtra.ToCharArray())}, " +
                       "sin espacios ni acentos.";

            // Empezar por un signo complica leerlo y copiarlo, y no aporta nada.
            if (!char.IsLetterOrDigit(n[0]))
                return "El nombre de usuario debe empezar por una letra o un dígito.";

            return null;
        }
    }
}
