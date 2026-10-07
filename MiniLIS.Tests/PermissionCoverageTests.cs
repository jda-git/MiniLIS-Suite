using FluentAssertions;
using MiniLIS.Application.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// Que ningún permiso del catálogo sea decorativo. La pantalla de Permisos promete que
    /// «quitar una marca cierra también la vía directa, no solo el botón»; si un permiso figura
    /// ahí pero nadie lo comprueba, esa promesa es falsa y el laboratorio cree tener un control
    /// de acceso que no existe (ISO 15189, cl. 7.6; ENS, control de acceso).
    ///
    /// Se comprobó sobre el código fuente y no por reflexión porque las comprobaciones viven en
    /// sitios muy distintos —[Authorize(Policy=…)], &lt;AuthorizeView Policy=…&gt;,
    /// HasAsync(...)— y en ficheros .razor, que en el ensamblado ya no se distinguen.
    ///
    /// Tres permisos se añadieron al catálogo sin llegar a comprobarse en ningún sitio
    /// (tubos.marcar-leido, tubos.incidencia-salvedad y configuracion.copia, corregidos en la
    /// v4.8.0): esta prueba es la que impide que vuelva a pasar.
    /// </summary>
    public class PermissionCoverageTests
    {
        /// <summary>Raíz de la solución, subiendo desde el directorio de ejecución hasta
        /// encontrarla. No vale una ruta relativa fija: cambia entre Debug y Release.</summary>
        private static DirectoryInfo SolutionRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MiniLIS.Suite.slnx")))
                dir = dir.Parent;
            dir.Should().NotBeNull("la prueba necesita el código fuente, no solo los ensamblados");
            return dir!;
        }

        private static IEnumerable<string> SourceFiles()
        {
            var root = SolutionRoot().FullName;
            foreach (var proyecto in new[] { "MiniLIS.Web", "MiniLIS.Infrastructure" })
                foreach (var f in Directory.EnumerateFiles(Path.Combine(root, proyecto), "*.*", SearchOption.AllDirectories))
                    if ((f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                        yield return f;
        }

        [Fact]
        public void Todo_permiso_del_catalogo_se_comprueba_en_alguna_parte()
        {
            var fuentes = SourceFiles().Select(File.ReadAllText).ToList();
            fuentes.Should().NotBeEmpty("debe encontrar el código fuente de la web y la infraestructura");

            // El nombre de la constante, que es como se escribe en el código: Permissions.X
            // (servicios y componentes) o Permissions.Policies.X ([Authorize], AuthorizeView).
            var catalogo = typeof(Permissions)
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(f => f.FieldType == typeof(string) && f.Name != nameof(Permissions.PolicyPrefix))
                .Select(f => f.Name)
                .ToList();

            catalogo.Should().HaveCountGreaterThan(20, "el catálogo debe haberse leído de verdad");

            var sinComprobar = catalogo
                .Where(nombre => !fuentes.Any(t => Regex.IsMatch(t, @"(Permissions|Policies)\." + nombre + @"\b")))
                .ToList();

            sinComprobar.Should().BeEmpty(
                "un permiso que aparece en Configuración → Permisos pero no se comprueba en ningún " +
                "sitio es una casilla que no hace nada: quien la desmarque creerá haber cerrado un acceso");
        }

        [Fact]
        public void Todo_permiso_del_catalogo_tiene_definicion_y_al_reves()
        {
            var constantes = typeof(Permissions)
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(f => f.FieldType == typeof(string) && f.Name != nameof(Permissions.PolicyPrefix))
                .Select(f => (string)f.GetValue(null)!)
                .ToList();

            var definidos = PermissionCatalog.All.Select(p => p.Code).ToList();

            definidos.Should().BeEquivalentTo(constantes,
                "un código sin ficha en el catálogo no sale en la pantalla de Permisos y nadie puede repartirlo; " +
                "una ficha sin código no la usa nadie");
            definidos.Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public void Todo_permiso_tiene_al_menos_un_rol_de_fabrica()
        {
            var huerfanos = PermissionCatalog.All.Where(p => p.Defaults.Length == 0).Select(p => p.Code).ToList();

            huerfanos.Should().BeEmpty(
                "un permiso que de fábrica no tiene ningún rol deja esa función inaccesible " +
                "en una instalación nueva hasta que alguien lo note");
        }
    }
}
