using System;

namespace MiniLIS.Web.Services
{
    /// <summary>
    /// Política de sesión, en un único sitio: la usan la cookie de autenticación
    /// (Program.cs), la revalidación del circuito y el aviso de inactividad de la pantalla.
    /// Con los tres valores escritos por separado acabarían discrepando, y el usuario vería
    /// un aviso de cierre inminente en una sesión que ya había caducado, o al revés.
    ///
    /// Puesto compartido de laboratorio: la sesión caduca por inactividad, y cerrar el
    /// navegador la cierra siempre (no hay sesión persistente, ver AccountController).
    /// </summary>
    public static class SessionPolicy
    {
        /// <summary>Inactividad tras la cual se cierra la sesión. Es también la caducidad de
        /// la cookie, que se renueva con cada petición (SlidingExpiration).</summary>
        public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

        /// <summary>Antelación del aviso. Un minuto da tiempo a reaccionar sin convertirse en
        /// un cartel que sale cada poco.</summary>
        public static readonly TimeSpan IdleWarning = TimeSpan.FromMinutes(1);

        /// <summary>Cada cuánto el circuito comprueba contra la base de datos que el usuario
        /// sigue activo y con los mismos roles. En Blazor Server la pestaña abierta no hace
        /// peticiones HTTP, así que sin esto una baja de usuario no surtiría efecto hasta que
        /// cerrara la pestaña.</summary>
        public static readonly TimeSpan RevalidationInterval = TimeSpan.FromMinutes(5);

        public static int IdleTimeoutSeconds => (int)IdleTimeout.TotalSeconds;
        public static int IdleWarningSeconds => (int)IdleWarning.TotalSeconds;
    }
}
