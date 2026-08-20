using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using System.Linq;
using Xunit;

namespace MiniLIS.Tests
{
    /// <summary>
    /// N-4: la prueba más valiosa del ticket. C-1 descartó (con razón, ver Program.cs) un
    /// FallbackPolicy global porque en este hosting híbrido (Blazor Interactive Server +
    /// controladores MVC) una política HTTP global no distingue páginas protegidas de
    /// anónimas. Esta prueba es la defensa en profundidad que sustituye a esa política:
    /// recorre TODAS las páginas Blazor por reflexión (nunca una lista escrita a mano, que
    /// quedaría obsoleta en la siguiente página que se añada) y falla si alguna no declara
    /// explícitamente su postura de acceso.
    /// </summary>
    public class AuthorizationReflectionTests
    {
        // Única lista blanca de rutas anónimas -- cualquier otra página que aparezca como
        // AllowAnonymous hace fallar la prueba, no solo las páginas sin ningún atributo.
        private static readonly string[] AllowedAnonymousRoutes = { "/login", "/Error" };

        private static System.Collections.Generic.List<System.Type> GetRazorPages() =>
            typeof(Program).Assembly.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(RouteAttribute), inherit: false).Any())
                .ToList();

        [Fact]
        public void Toda_pagina_declara_Authorize_o_AllowAnonymous_explicitamente()
        {
            var paginas = GetRazorPages();

            // Si esto es 0, algo cambió en cómo Blazor compila @page y la prueba no está
            // mirando lo que cree que mira -- mejor fallar aquí que dar un falso "todo bien".
            paginas.Should().NotBeEmpty("debe encontrar páginas Blazor reales vía RouteAttribute");

            var sinAtributo = paginas
                .Where(t => !t.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false).Any()
                         && !t.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false).Any())
                .Select(t => t.FullName)
                .ToList();

            sinAtributo.Should().BeEmpty(
                "toda página debe declarar explícitamente su política de acceso; " +
                "no hay FallbackPolicy global (ver la decisión C-1 en Program.cs)");
        }

        [Fact]
        public void Solo_login_y_error_son_paginas_anonimas()
        {
            var paginas = GetRazorPages();

            var anonimas = paginas
                .Where(t => t.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false).Any())
                .SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), inherit: false).Cast<RouteAttribute>())
                .Select(r => r.Template)
                .ToList();

            anonimas.Should().BeEquivalentTo(AllowedAnonymousRoutes,
                "cualquier página anónima fuera de esta lista blanca es una regresión de seguridad, no una mejora de UX");
        }
    }
}
