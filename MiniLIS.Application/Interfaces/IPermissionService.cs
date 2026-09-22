using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>
    /// Permisos por rol, configurables desde Configuración → Permisos (v4.1). Sustituye a los
    /// roles escritos en el código: cada página, botón y servicio comprueba un PERMISO, y la
    /// tabla dice qué rol lo tiene. Así el laboratorio puede ajustar quién hace qué sin tocar
    /// el programa, y el reparto queda documentado en un solo sitio.
    ///
    /// Los valores de fábrica son los acordados en la versión 4.0 (ver PermissionCatalog).
    /// </summary>
    public interface IPermissionService
    {
        /// <summary>¿El usuario de la sesión tiene el permiso?</summary>
        Task<bool> HasAsync(string permissionCode);

        /// <summary>Igual, para un usuario concreto (controladores y políticas de acceso).</summary>
        Task<bool> HasAsync(ClaimsPrincipal user, string permissionCode);

        /// <summary>Matriz completa (rol → permisos), con los valores de fábrica aplicados a
        /// los permisos que aún no estén guardados.</summary>
        Task<RolePermissionMatrix> GetMatrixAsync();

        /// <summary>Guarda la matriz. Los permisos fijos (ver PermissionCatalog.Fixed) se
        /// reponen siempre: nadie puede dejar el sistema sin quien administre permisos.</summary>
        Task SaveMatrixAsync(RolePermissionMatrix matrix);

        /// <summary>Vuelve a los valores de fábrica.</summary>
        Task ResetToDefaultsAsync();
    }

    /// <summary>Qué permisos tiene cada rol. Rol → conjunto de códigos de permiso.</summary>
    public class RolePermissionMatrix
    {
        public Dictionary<string, HashSet<string>> ByRole { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool Has(string role, string permission)
            => ByRole.TryGetValue(role, out var set) && set.Contains(permission);

        public void Set(string role, string permission, bool allowed)
        {
            if (!ByRole.TryGetValue(role, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ByRole[role] = set;
            }
            if (allowed) set.Add(permission); else set.Remove(permission);
        }

        public RolePermissionMatrix Clone()
        {
            var copy = new RolePermissionMatrix();
            foreach (var (role, set) in ByRole)
                copy.ByRole[role] = new HashSet<string>(set, StringComparer.OrdinalIgnoreCase);
            return copy;
        }
    }

    public class PermissionDefinition
    {
        public string Code { get; init; } = "";
        public string Group { get; init; } = "";
        public string Title { get; init; } = "";
        public string? Description { get; init; }
        /// <summary>Roles que lo tienen de fábrica.</summary>
        public string[] Defaults { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Catálogo de permisos: lo que se puede repartir entre roles. Añadir uno nuevo aquí y
    /// usarlo en la página o el servicio correspondiente es todo lo que hace falta para que
    /// aparezca en la pantalla de Permisos.
    /// </summary>
    public static class PermissionCatalog
    {
        public const string RoleAdministrador = "Administrador";
        public const string RoleFacultativo = "Facultativo";
        public const string RoleTecnico = "Técnico";

        public static readonly string[] Roles = { RoleAdministrador, RoleFacultativo, RoleTecnico };

        private static readonly string[] Todos = { RoleAdministrador, RoleFacultativo, RoleTecnico };
        private static readonly string[] AdminYFacultativo = { RoleAdministrador, RoleFacultativo };
        private static readonly string[] SoloFacultativo = { RoleFacultativo };
        private static readonly string[] SoloAdmin = { RoleAdministrador };

        public static readonly IReadOnlyList<PermissionDefinition> All = new List<PermissionDefinition>
        {
            // ── Muestras y trabajo diario ──
            new() { Code = Permissions.MuestrasVer, Group = "Muestras y trabajo diario", Defaults = Todos,
                    Title = "Panel de inicio, Bandeja técnica, Lista de trabajo" },
            new() { Code = Permissions.MuestrasRegistrar, Group = "Muestras y trabajo diario", Defaults = Todos,
                    Title = "Registrar muestra nueva" },
            new() { Code = Permissions.MuestrasRegistroDiferido, Group = "Muestras y trabajo diario", Defaults = AdminYFacultativo,
                    Title = "Registro diferido (modo contingencia) al dar de alta" },
            new() { Code = Permissions.PacienteActualizarEnRegistro, Group = "Muestras y trabajo diario", Defaults = AdminYFacultativo,
                    Title = "Actualizar datos de un paciente existente al registrar (si no coinciden)" },
            new() { Code = Permissions.MuestrasEditar, Group = "Muestras y trabajo diario", Defaults = Todos,
                    Title = "Editar ficha de muestra (datos, recepción, paneles, alícuotas)" },
            new() { Code = Permissions.EtiquetasImprimir, Group = "Muestras y trabajo diario", Defaults = Todos,
                    Title = "Imprimir etiquetas" },
            new() { Code = Permissions.HojaTrabajoGenerar, Group = "Muestras y trabajo diario", Defaults = Todos,
                    Title = "Hoja del citómetro (generar y descargar)" },

            // ── Tubos ──
            new() { Code = Permissions.TubosMarcarLeido, Group = "Tubos", Defaults = Todos,
                    Title = "Marcar tubo como leído" },
            new() { Code = Permissions.TubosDesmarcarLeido, Group = "Tubos", Defaults = SoloFacultativo,
                    Title = "Desmarcar un tubo leído", Description = "Exige motivo; la lectura anterior queda en la auditoría." },
            new() { Code = Permissions.TubosIncidenciaSalvedad, Group = "Tubos", Defaults = Todos,
                    Title = "Incidencia de lectura «con salvedad»" },
            new() { Code = Permissions.TubosIncidenciaAnulaLectura, Group = "Tubos", Defaults = AdminYFacultativo,
                    Title = "Incidencia que anula o repite un tubo ya leído" },
            new() { Code = Permissions.TubosJustificarNoRealizado, Group = "Tubos", Defaults = Todos,
                    Title = "Justificar un tubo no realizado" },
            new() { Code = Permissions.TubosAnular, Group = "Tubos", Defaults = AdminYFacultativo,
                    Title = "Anular tubo o panel leído por error", Description = "Exige justificación; se recomienda abrir una no conformidad." },

            // ── Informes ──
            new() { Code = Permissions.InformesAbrir, Group = "Informes", Defaults = AdminYFacultativo,
                    Title = "Abrir el editor de informes" },
            new() { Code = Permissions.InformesGuardar, Group = "Informes", Defaults = AdminYFacultativo,
                    Title = "Guardar el informe (texto, marcadores, conclusión)", Description = "Un informe validado no se puede guardar aunque se tenga este permiso." },
            new() { Code = Permissions.InformesDescargar, Group = "Informes", Defaults = AdminYFacultativo,
                    Title = "Previsualizar o descargar PDF/ODT" },
            new() { Code = Permissions.InformesValidar, Group = "Informes", Defaults = SoloFacultativo,
                    Title = "Validar informe" },
            new() { Code = Permissions.InformesReabrir, Group = "Informes", Defaults = SoloFacultativo,
                    Title = "Reabrir un informe validado" },

            // ── Consultas y exportaciones ──
            new() { Code = Permissions.BuscadorVer, Group = "Consultas y exportaciones", Defaults = AdminYFacultativo,
                    Title = "Buscador / Estadísticas" },
            new() { Code = Permissions.NotificacionesVer, Group = "Consultas y exportaciones", Defaults = AdminYFacultativo,
                    Title = "Notificaciones (pantalla y CSV)" },
            new() { Code = Permissions.ExcedenteVer, Group = "Consultas y exportaciones", Defaults = AdminYFacultativo,
                    Title = "Excedente (pantalla y CSV)" },
            new() { Code = Permissions.ExportMuestras, Group = "Consultas y exportaciones", Defaults = AdminYFacultativo,
                    Title = "Exportar el CSV de muestras (Bandeja técnica)" },
            new() { Code = Permissions.ExportIdentificadores, Group = "Consultas y exportaciones", Defaults = AdminYFacultativo,
                    Title = "Exportar CSV con identificadores del paciente" },
            new() { Code = Permissions.ExportIdentificadoresSinJustificar, Group = "Consultas y exportaciones", Defaults = SoloAdmin,
                    Title = "…sin tener que justificarlo", Description = "Sin este permiso, incluir identificadores exige escribir una justificación que queda en la auditoría." },
            new() { Code = Permissions.IndicadoresVer, Group = "Consultas y exportaciones", Defaults = AdminYFacultativo,
                    Title = "Indicadores de calidad (pantalla y PDF)" },

            // ── Versiones de paneles ──
            new() { Code = Permissions.PanelVersionesVer, Group = "Versiones de paneles", Defaults = AdminYFacultativo,
                    Title = "Ver versiones" },
            new() { Code = Permissions.PanelVersionesEditar, Group = "Versiones de paneles", Defaults = AdminYFacultativo,
                    Title = "Crear, editar y enviar a revisión un borrador; añadir aclaraciones" },
            new() { Code = Permissions.PanelVersionesAprobar, Group = "Versiones de paneles", Defaults = SoloFacultativo,
                    Title = "Aprobar, devolver a borrador, retirar" },

            // ── Administración ──
            new() { Code = Permissions.ConfiguracionVer, Group = "Administración", Defaults = AdminYFacultativo,
                    Title = "Configuración (catálogos, plantillas, etiquetas, cabecera, firmas…)" },
            new() { Code = Permissions.ConfiguracionCopia, Group = "Administración", Defaults = AdminYFacultativo,
                    Title = "Copia de configuración (exportar, importar)" },
            new() { Code = Permissions.PermisosGestionar, Group = "Administración", Defaults = AdminYFacultativo,
                    Title = "Permisos por rol (esta misma pantalla)" },
            new() { Code = Permissions.UsuariosGestionar, Group = "Administración", Defaults = AdminYFacultativo,
                    Title = "Usuarios" },
            new() { Code = Permissions.AuditoriaVer, Group = "Administración", Defaults = AdminYFacultativo,
                    Title = "Auditoría" },
            new() { Code = Permissions.BackupGestionar, Group = "Administración", Defaults = AdminYFacultativo,
                    Title = "Copias de seguridad" },
            new() { Code = Permissions.ContingenciaGestionar, Group = "Administración", Defaults = AdminYFacultativo,
                    Title = "Contingencia" },
            new() { Code = Permissions.EvidenciasGenerar, Group = "Administración", Defaults = AdminYFacultativo,
                    Title = "Evidencias para auditoría" },
        };

        /// <summary>
        /// Permisos que el Administrador conserva siempre: si se pudieran quitar, nadie podría
        /// volver a repartir permisos ni dar de alta usuarios, y el sistema quedaría bloqueado.
        /// En la pantalla salen marcados y sin poder desmarcarse.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string[]> Fixed = new Dictionary<string, string[]>
        {
            [Permissions.PermisosGestionar] = new[] { RoleAdministrador },
            [Permissions.UsuariosGestionar] = new[] { RoleAdministrador }
        };

        public static IEnumerable<string> Groups => All.Select(p => p.Group).Distinct();

        public static PermissionDefinition? Find(string code) => All.FirstOrDefault(p => p.Code == code);

        public static RolePermissionMatrix DefaultMatrix()
        {
            var m = new RolePermissionMatrix();
            foreach (var role in Roles) m.ByRole[role] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in All)
                foreach (var role in p.Defaults)
                    m.Set(role, p.Code, true);
            return m;
        }
    }

    /// <summary>Códigos de permiso. Se usan en [Authorize(Policy = ...)], en AuthorizeView y en
    /// los servicios; nunca se comprueba un rol directamente.</summary>
    public static class Permissions
    {
        public const string MuestrasVer = "muestras.ver";
        public const string MuestrasRegistrar = "muestras.registrar";
        public const string MuestrasRegistroDiferido = "muestras.registro-diferido";
        public const string PacienteActualizarEnRegistro = "pacientes.actualizar-en-registro";
        public const string MuestrasEditar = "muestras.editar";
        public const string EtiquetasImprimir = "etiquetas.imprimir";
        public const string HojaTrabajoGenerar = "hoja-trabajo.generar";

        public const string TubosMarcarLeido = "tubos.marcar-leido";
        public const string TubosDesmarcarLeido = "tubos.desmarcar-leido";
        public const string TubosIncidenciaSalvedad = "tubos.incidencia-salvedad";
        public const string TubosIncidenciaAnulaLectura = "tubos.incidencia-anula";
        public const string TubosJustificarNoRealizado = "tubos.justificar-no-realizado";
        public const string TubosAnular = "tubos.anular";

        public const string InformesAbrir = "informes.abrir";
        public const string InformesGuardar = "informes.guardar";
        public const string InformesDescargar = "informes.descargar";
        public const string InformesValidar = "informes.validar";
        public const string InformesReabrir = "informes.reabrir";

        public const string BuscadorVer = "buscador.ver";
        public const string NotificacionesVer = "notificaciones.ver";
        public const string ExcedenteVer = "excedente.ver";
        public const string ExportMuestras = "export.muestras";
        public const string ExportIdentificadores = "export.identificadores";
        public const string ExportIdentificadoresSinJustificar = "export.identificadores-sin-justificar";
        public const string IndicadoresVer = "indicadores.ver";

        public const string PanelVersionesVer = "panel-versiones.ver";
        public const string PanelVersionesEditar = "panel-versiones.editar";
        public const string PanelVersionesAprobar = "panel-versiones.aprobar";

        public const string ConfiguracionVer = "configuracion.ver";
        public const string ConfiguracionCopia = "configuracion.copia";
        public const string PermisosGestionar = "permisos.gestionar";
        public const string UsuariosGestionar = "usuarios.gestionar";
        public const string AuditoriaVer = "auditoria.ver";
        public const string BackupGestionar = "backup.gestionar";
        public const string ContingenciaGestionar = "contingencia.gestionar";
        public const string EvidenciasGenerar = "evidencias.generar";

        /// <summary>Prefijo de las políticas de autorización: [Authorize(Policy = Permissions.Policy(x))].</summary>
        public const string PolicyPrefix = "perm:";
        public static string Policy(string code) => PolicyPrefix + code;

        /// <summary>Nombres de política para [Authorize(Policy = ...)] y &lt;AuthorizeView Policy="..."&gt;:
        /// el mismo código con el prefijo. Son constantes, así que valen en atributos.</summary>
        public static class Policies
        {
            public const string MuestrasVer = PolicyPrefix + Permissions.MuestrasVer;
            public const string MuestrasRegistrar = PolicyPrefix + Permissions.MuestrasRegistrar;
            public const string MuestrasRegistroDiferido = PolicyPrefix + Permissions.MuestrasRegistroDiferido;
            public const string PacienteActualizarEnRegistro = PolicyPrefix + Permissions.PacienteActualizarEnRegistro;
            public const string MuestrasEditar = PolicyPrefix + Permissions.MuestrasEditar;
            public const string EtiquetasImprimir = PolicyPrefix + Permissions.EtiquetasImprimir;
            public const string HojaTrabajoGenerar = PolicyPrefix + Permissions.HojaTrabajoGenerar;
            public const string TubosMarcarLeido = PolicyPrefix + Permissions.TubosMarcarLeido;
            public const string TubosDesmarcarLeido = PolicyPrefix + Permissions.TubosDesmarcarLeido;
            public const string TubosIncidenciaSalvedad = PolicyPrefix + Permissions.TubosIncidenciaSalvedad;
            public const string TubosIncidenciaAnulaLectura = PolicyPrefix + Permissions.TubosIncidenciaAnulaLectura;
            public const string TubosJustificarNoRealizado = PolicyPrefix + Permissions.TubosJustificarNoRealizado;
            public const string TubosAnular = PolicyPrefix + Permissions.TubosAnular;
            public const string InformesAbrir = PolicyPrefix + Permissions.InformesAbrir;
            public const string InformesGuardar = PolicyPrefix + Permissions.InformesGuardar;
            public const string InformesDescargar = PolicyPrefix + Permissions.InformesDescargar;
            public const string InformesValidar = PolicyPrefix + Permissions.InformesValidar;
            public const string InformesReabrir = PolicyPrefix + Permissions.InformesReabrir;
            public const string BuscadorVer = PolicyPrefix + Permissions.BuscadorVer;
            public const string NotificacionesVer = PolicyPrefix + Permissions.NotificacionesVer;
            public const string ExcedenteVer = PolicyPrefix + Permissions.ExcedenteVer;
            public const string ExportMuestras = PolicyPrefix + Permissions.ExportMuestras;
            public const string ExportIdentificadores = PolicyPrefix + Permissions.ExportIdentificadores;
            public const string ExportIdentificadoresSinJustificar = PolicyPrefix + Permissions.ExportIdentificadoresSinJustificar;
            public const string IndicadoresVer = PolicyPrefix + Permissions.IndicadoresVer;
            public const string PanelVersionesVer = PolicyPrefix + Permissions.PanelVersionesVer;
            public const string PanelVersionesEditar = PolicyPrefix + Permissions.PanelVersionesEditar;
            public const string PanelVersionesAprobar = PolicyPrefix + Permissions.PanelVersionesAprobar;
            public const string ConfiguracionVer = PolicyPrefix + Permissions.ConfiguracionVer;
            public const string ConfiguracionCopia = PolicyPrefix + Permissions.ConfiguracionCopia;
            public const string PermisosGestionar = PolicyPrefix + Permissions.PermisosGestionar;
            public const string UsuariosGestionar = PolicyPrefix + Permissions.UsuariosGestionar;
            public const string AuditoriaVer = PolicyPrefix + Permissions.AuditoriaVer;
            public const string BackupGestionar = PolicyPrefix + Permissions.BackupGestionar;
            public const string ContingenciaGestionar = PolicyPrefix + Permissions.ContingenciaGestionar;
            public const string EvidenciasGenerar = PolicyPrefix + Permissions.EvidenciasGenerar;
        }
    }
}
