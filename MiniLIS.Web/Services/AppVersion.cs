using System;
using System.Reflection;

namespace MiniLIS.Web.Services
{
    /// <summary>
    /// Versión de la aplicación, leída de los metadatos del ensamblado. El número real vive
    /// en Directory.Build.props (raíz de la solución) y de ahí lo hereda todo: nunca debe
    /// escribirse a mano en una página, porque en cuanto hay dos orígenes divergen — que es
    /// exactamente lo que ocurría antes, con la pantalla de acceso mostrando "2.0.4.Final"
    /// mientras los ensamblados se compilaban como 1.0.0.
    ///
    /// El SDK de .NET incorpora SourceLink de serie, así que en un repositorio git la versión
    /// informativa ya viene con el commit anexado ("2.1.0+c339fa2b51…"). No hace falta
    /// estamparlo en el build: basta con recortarlo aquí para mostrarlo.
    /// </summary>
    public static class AppVersion
    {
        private const int ShortCommitLength = 7;

        /// <summary>Número de versión limpio, para mostrar al usuario: "2.1.0".</summary>
        public static string Display { get; }

        /// <summary>Commit corto del que se compiló, o cadena vacía si el build no lo estampó
        /// (por ejemplo, al compilar fuera de un repositorio git).</summary>
        public static string Commit { get; }

        /// <summary>Versión y commit: "2.1.0+c339fa2". Es el dato que hay que pedir ante una
        /// incidencia — identifica el código exacto que está en ejecución, cosa que el número
        /// de versión por sí solo no hace entre despliegues de una misma versión.</summary>
        public static string Full { get; }

        static AppVersion()
        {
            var informational = typeof(AppVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (string.IsNullOrWhiteSpace(informational))
            {
                // No debería ocurrir con Directory.Build.props en su sitio, pero mostrar una
                // versión inventada sería peor que admitir que no se conoce.
                Display = "desconocida";
                Commit = string.Empty;
                Full = Display;
                return;
            }

            var separator = informational.IndexOf('+');
            if (separator < 0)
            {
                Display = informational;
                Commit = string.Empty;
                Full = informational;
                return;
            }

            Display = informational[..separator];

            var commit = informational[(separator + 1)..];
            Commit = commit.Length > ShortCommitLength ? commit[..ShortCommitLength] : commit;
            Full = $"{Display}+{Commit}";
        }
    }
}
