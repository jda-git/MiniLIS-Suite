using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniLIS.Domain.Entities;
using MiniLIS.Domain.Identity;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Seed
{
    public static class DbInitializer
    {
        public static async Task SeedIdentityAsync(IServiceProvider serviceProvider, IConfiguration configuration, ILogger logger)
        {
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            // 1. Seed Roles
            string[] roleNames = { "Administrador", "Facultativo", "Técnico" };
            foreach (var roleName in roleNames)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new ApplicationRole(roleName));
                }
            }

            // 2. Seed Default Admin — solo si NINGÚN usuario tiene el rol Administrador,
            // no si existe un usuario con este nombre concreto. Así renombrar o desactivar
            // el admin sembrado no provoca que se recree con la contraseña por defecto.
            var existingAdmins = await userManager.GetUsersInRoleAsync("Administrador");
            if (existingAdmins.Count == 0)
            {
                var adminUser = configuration["Seed:AdminUser"] ?? "admin@minilis.com";
                var adminPassword = configuration["Seed:AdminPassword"];
                var generated = string.IsNullOrWhiteSpace(adminPassword);
                if (generated)
                {
                    // La contraseña se construye para cumplir la política definida en Program.cs
                    // (longitud 12+, dígito, mayúscula, minúscula, no alfanumérico, 4 caracteres
                    // únicos) por CONSTRUCCIÓN, no por azar -- Base64 (A-Za-z0-9+/) solo tiene un
                    // ~3% de símbolos por posición, así que generar 18 bytes al azar y esperar
                    // que "toque" un símbolo fallaba la política de Identity en torno al 48% de
                    // las veces (ver N-1), dejando el sistema sin ningún administrador sin avisar
                    // más que en el log del servidor. Si la política cambia, revisa
                    // GenerateCompliantPassword: la validación de más abajo aborta el arranque en
                    // vez de dejar el sistema sin administrador.
                    adminPassword = GenerateCompliantPassword();
                }

                // Validar ANTES de intentar el alta (tanto la generada como la configurada por
                // Seed:AdminPassword): así un endurecimiento futuro de la política no vuelve a
                // romper el sembrado en silencio -- un sistema clínico que arranca sin
                // administrador está en peor estado que uno que no arranca: el segundo es
                // evidente y se corrige de inmediato, el primero parece funcionar y no se puede
                // administrar.
                var passwordValidators = serviceProvider.GetServices<IPasswordValidator<ApplicationUser>>();
                var probeUser = new ApplicationUser { UserName = adminUser, Email = adminUser };
                foreach (var validator in passwordValidators)
                {
                    var validation = await validator.ValidateAsync(userManager, probeUser, adminPassword!);
                    if (!validation.Succeeded)
                    {
                        var errors = string.Join("; ", validation.Errors.Select(e => e.Description));
                        logger.LogCritical(
                            "[SEED] La contraseña {Origen} para el administrador inicial no cumple la política " +
                            "configurada: {Errors}. Revise GenerateCompliantPassword frente a Password options en Program.cs.",
                            generated ? "generada" : "configurada en Seed:AdminPassword", errors);
                        throw new InvalidOperationException(
                            "No se puede sembrar el administrador inicial: la contraseña " +
                            (generated ? "generada" : "configurada en Seed:AdminPassword") +
                            $" no cumple la política de contraseñas ({errors}).");
                    }
                }

                var admin = new ApplicationUser
                {
                    UserName = adminUser,
                    Email = adminUser,
                    FullName = "Administrador del Sistema",
                    EmailConfirmed = true,
                    IsActive = true,
                    MustChangePassword = true
                };

                var createPowerUser = await userManager.CreateAsync(admin, adminPassword!);
                if (createPowerUser.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, "Administrador");
                    if (generated)
                    {
                        logger.LogWarning(
                            "[SEED] Administrador inicial creado: {AdminUser} / contraseña temporal generada: {AdminPassword} " +
                            "— este mensaje solo se emite una vez, cámbiela en el primer inicio de sesión.",
                            adminUser, adminPassword);
                    }
                }
                else
                {
                    // Validado justo arriba contra la misma política, así que llegar aquí con un
                    // error de complejidad de contraseña no debería pasar -- si ocurre, es una
                    // causa distinta (ver Errors) y sigue mereciendo abortar el arranque en vez
                    // de continuar sin administrador.
                    var errors = string.Join("; ", createPowerUser.Errors.Select(e => e.Description));
                    logger.LogCritical("[SEED] No se pudo crear el administrador inicial: {Errors}", errors);
                    throw new InvalidOperationException($"No se pudo crear el administrador inicial: {errors}");
                }
            }

            // 2.5 Cleanup duplicate/malformed intensities and marker values
            var dbIntensities = await context.SystemSettings
                .Where(s => s.Key.StartsWith("Config:Intensity:"))
                .ToListAsync();

            var heteroSettings = dbIntensities
                .Where(s => s.Value != null && 
                           (s.Value.Contains("Hetero", StringComparison.OrdinalIgnoreCase) || 
                            s.Value.Contains("Heterog", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (heteroSettings.Count > 1)
            {
                var keep = heteroSettings.FirstOrDefault(s => s.Value == "Heterogéneo") ?? heteroSettings.First();
                keep.Value = "Heterogéneo";
                context.SystemSettings.Update(keep);

                foreach (var remove in heteroSettings.Where(s => s.Id != keep.Id))
                {
                    context.SystemSettings.Remove(remove);
                }
                await context.SaveChangesAsync();
            }
            else if (heteroSettings.Count == 1)
            {
                var single = heteroSettings.First();
                if (single.Value != "Heterogéneo")
                {
                    single.Value = "Heterogéneo";
                    context.SystemSettings.Update(single);
                    await context.SaveChangesAsync();
                }
            }

            var markerValuesToFix = await context.ReportMarkerValues
                .Where(v => v.IntensityValue != null && 
                           (v.IntensityValue.Contains("Hetero") || v.IntensityValue.Contains("Heterog")))
                .ToListAsync();

            bool fixedAnyMarkerValues = false;
            foreach (var mv in markerValuesToFix)
            {
                if (mv.IntensityValue != "Heterogéneo")
                {
                    mv.IntensityValue = "Heterogéneo";
                    context.ReportMarkerValues.Update(mv);
                    fixedAnyMarkerValues = true;
                }
            }

            if (fixedAnyMarkerValues)
            {
                await context.SaveChangesAsync();
            }

            // 3. Seed Intensities
            if (!await context.SystemSettings.AnyAsync(s => s.Key.StartsWith("Config:Intensity:")))
            {
                string[] intensities = { "-", "+", "++", "+d", "-/+d", "-/+", "+d/+", "+/++", "Heterogéneo" };
                for (int i = 0; i < intensities.Length; i++)
                {
                    string key = $"Config:Intensity:{i}";
                    context.SystemSettings.Add(new SystemSetting { Key = key, Value = intensities[i], Description = "Nivel de Intensidad" });
                }
            }

            // 4. Seed Markers
            string[] markerNames = { "CD34", "CD45", "CD117", "CD19", "CD20", "CD10", "CD3", "CD4", "CD8", "HLA-DR", "MPO", "CD79a", "TdT", "CD56", "CD13", "CD33", "CD11b", "CD14" };
            foreach (var mName in markerNames)
            {
                if (!await context.Markers.AnyAsync(m => m.Name == mName))
                {
                    context.Markers.Add(new Marker { Name = mName });
                }
            }

            // 5. Seed Panels
            string[] panels = { "LNH", "SMD", "CD34", "Leucemia Aguda", "Mieloma" };
            var seededCodes = new HashSet<string>(
                await context.Panels.Where(p => p.Code != null && p.Code != "").Select(p => p.Code!).ToListAsync(),
                StringComparer.OrdinalIgnoreCase);
            foreach (var pName in panels)
            {
                if (!await context.Panels.AnyAsync(p => p.Name == pName))
                {
                    var code = MiniLIS.Infrastructure.Seed.PanelVersionSeeder.DeriveCode(pName, seededCodes);
                    seededCodes.Add(code);
                    context.Panels.Add(new Panel { Name = pName, Code = code });
                }
            }

            // 6. Seed Rejection Reasons (F-4)
            var rejectionReasons = new (string Code, string Description, bool TypicallyRejects, bool RequiresFreeText)[]
            {
                ("VOL-INSUF", "Volumen insuficiente", true, false),
                ("COAG", "Muestra coagulada", true, false),
                ("HEMOL", "Muestra hemolizada", false, false),
                ("TEMP", "Temperatura de transporte incorrecta", false, false),
                ("ANTICOAG", "Anticoagulante inadecuado", true, false),
                ("DEMORA", "Demora excesiva desde la extracción", false, false),
                ("ID-ILEGIBLE", "Identificación incorrecta o ilegible", true, false),
                ("TUBO-DAÑADO", "Tubo dañado o derramado", true, false),
                ("PET-INCOMPLETA", "Petición incompleta o sin datos clínicos", false, false),
                ("SIN-PETICION", "Muestra no acompañada de petición", true, false),
                ("OTROS", "Otros", false, true),
            };
            int reasonOrder = 0;
            foreach (var (code, description, typicallyRejects, requiresFreeText) in rejectionReasons)
            {
                if (!await context.RejectionReasons.AnyAsync(r => r.Code == code))
                {
                    context.RejectionReasons.Add(new RejectionReason
                    {
                        Code = code,
                        Description = description,
                        TypicallyRejects = typicallyRejects,
                        RequiresFreeText = requiresFreeText,
                        DisplayOrder = reasonOrder
                    });
                }
                reasonOrder++;
            }

            // 6.1 Seed Tube Read Incident Reasons (nuevo)
            var tubeIncidentReasons = new (string Code, string Description, bool RequiresFreeText)[]
            {
                ("MUESTRA-INSUF", "Muestra insuficiente", false),
                ("ATASCO", "Atasco del equipo", false),
                ("FALLO-EQUIPO", "Fallo del equipo", false),
                ("COAGULOS", "Coágulos en la muestra", false),
                ("ERROR-ADQ", "Error de adquisición", false),
                ("OTRO", "Otro", true),
            };
            int tubeIncidentOrder = 0;
            foreach (var (code, description, requiresFreeText) in tubeIncidentReasons)
            {
                if (!await context.TubeReadIncidentReasons.AnyAsync(r => r.Code == code))
                {
                    context.TubeReadIncidentReasons.Add(new TubeReadIncidentReason
                    {
                        Code = code,
                        Description = description,
                        RequiresFreeText = requiresFreeText,
                        DisplayOrder = tubeIncidentOrder
                    });
                }
                tubeIncidentOrder++;
            }

            await context.SaveChangesAsync();

            // 6.5 Da a cada Panel sin PanelVersion una v1/Vigente (migración M-4, idempotente).
            await PanelVersionSeeder.RunAsync(context, logger);

            // 6.6 Migra las muestras con el antiguo HasIncident=true a ReceptionStatus=ConSalvedad
            // (F-4, idempotente: solo actúa sobre filas todavía no migradas).
            await ReceptionMigrator.RunAsync(context, logger);

            // 6.7 Expande los lotes históricos de StoredSpecimen (AliquotCount) en alícuotas
            // individuales con estado propio (F-7, idempotente: solo BatchId == Guid.Empty).
            await StoredSpecimenBatchMigrator.RunAsync(context, logger);

            // 6.8 Retira del catálogo los indicadores dados de baja (idempotente). Quitarlos de
            // la lista de más abajo solo evita sembrarlos en instalaciones nuevas; en una base
            // ya sembrada la fila persiste y el cuadro de mando la seguiría pintando.
            await RetiredIndicatorsCleaner.RunAsync(context, logger);

            // 7. Seed Quality Indicators (F-1). Umbrales sin definir a propósito: un umbral por
            // defecto inventado es peor que ninguno — la unidad debe fijarlos conscientemente.
            var indicators = new (string Code, string Name, string Definition, IndicatorUnit Unit, IndicatorDirection Direction)[]
            {
                // Sin "TAT-PRE" (recepción → registro): se retiró en v2.2.0 por medir un
                // intervalo que no existe — el alta fija ambas marcas en el mismo instante.
                // La fase preanalítica sigue cubierta por PCT-RECHAZO/SALVEDAD/INCIDENCIA.
                ("TAT-TOTAL", "TAT total (recepción → validación)", "ValidatedAtUtc - ReceivedAtUtc", IndicatorUnit.Horas, IndicatorDirection.MenorEsMejor),
                ("TAT-ADQ", "TAT de adquisición (registro → adquisición)", "AcquiredAtUtc - RegisteredAtUtc", IndicatorUnit.Horas, IndicatorDirection.MenorEsMejor),
                ("TAT-ANA", "TAT analítico (adquisición → validación)", "ValidatedAtUtc - AcquiredAtUtc", IndicatorUnit.Horas, IndicatorDirection.MenorEsMejor),
                ("PCT-RECHAZO", "% muestras rechazadas", "rechazadas / recibidas", IndicatorUnit.Porcentaje, IndicatorDirection.MenorEsMejor),
                ("PCT-SALVEDAD", "% aceptadas con salvedad", "con salvedad / recibidas", IndicatorUnit.Porcentaje, IndicatorDirection.MenorEsMejor),
                ("PCT-INCIDENCIA", "% con incidencia preanalítica", "con incidencia / recibidas", IndicatorUnit.Porcentaje, IndicatorDirection.MenorEsMejor),
                ("PCT-FUERA-PLAZO", "% informes fuera de objetivo", "TAT-TOTAL > objetivo", IndicatorUnit.Porcentaje, IndicatorDirection.MenorEsMejor),
                ("ACT-PANEL", "Actividad por panel y versión", "recuento", IndicatorUnit.Recuento, IndicatorDirection.MayorEsMejor),
                ("ACT-MUESTRA", "Actividad por tipo de muestra", "recuento", IndicatorUnit.Recuento, IndicatorDirection.MayorEsMejor),
                ("ACT-PETICIONARIO", "Actividad por servicio", "recuento", IndicatorUnit.Recuento, IndicatorDirection.MayorEsMejor),
                ("PCT-REAPERTURA", "% informes reabiertos tras validar", "reabiertos / validados", IndicatorUnit.Porcentaje, IndicatorDirection.MenorEsMejor),
            };

            int indicatorOrder = 1;
            foreach (var (code, name, definition, unit, direction) in indicators)
            {
                if (!await context.QualityIndicators.AnyAsync(q => q.Code == code))
                {
                    context.QualityIndicators.Add(new QualityIndicator
                    {
                        Code = code,
                        Name = name,
                        Definition = definition,
                        Unit = unit,
                        Direction = direction,
                        DisplayOrder = indicatorOrder
                    });
                }
                indicatorOrder++;
            }
            await context.SaveChangesAsync();

            // 8. Seed Worklist Export Profiles (F-6). Esquema de CAMPOS según especificación
            // documentada del fabricante para BD FACSDiva (XML, Canto II) y BD FACSuite (CSV,
            // FACSLyric) -- ver comentarios de cada perfil. Los nombres de elemento XML de
            // FACSDiva (raíz/grupo/fila) y el nombre exacto de PanelName/Task en cada equipo
            // real siguen sin confirmar contra un fichero de ejemplo: ValidatedAgainstInstrument
            // permanece en falso hasta que la unidad lo compruebe.
            //
            // SeedWorklistProfileAsync (no un simple "insertar si no existe"): esta migración
            // sustituye por completo el esquema de columnas de ambos perfiles respecto al que
            // ya estaba sembrado en sesiones anteriores. Un "insertar si no existe" habría
            // dejado los perfiles YA EXISTENTES con las columnas inventadas antiguas para
            // siempre. Se actualizan en sitio SOLO si siguen sin validar contra el equipo real
            // -- si un administrador ya los confirmó, no se tocan.
            {
                await SeedWorklistProfileAsync(context, new WorklistExportProfile
                {
                    Name = "FACSDiva — Canto II",
                    TargetInstrument = "FACSDiva",
                    FileFormat = WorklistFileFormat.Xml,
                    FileExtension = "xml",
                    Encoding = "UTF-8",
                    LineEnding = "CRLF",
                    Granularity = WorklistGranularity.PorPanel,
                    XmlRootElement = "Worklist",
                    XmlGroupElement = "Carousel",
                    XmlRowElement = "Specimen",
                    MaxRowsPerGroup = 40, // 1 carrusel BD FACSCanto II = 40 posiciones
                    MaxGroupsPerFile = 5, // tope documentado: 5 carruseles = 200 muestras/fichero
                    IsActive = true,
                    ValidatedAgainstInstrument = false,
                    Columns = new List<WorklistExportColumn>
                    {
                        new() { DisplayOrder = 1, ColumnHeader = "SampleID", ValueTemplate = "{SampleNumber}" },
                        // PanelName debe coincidir EXACTAMENTE (mayúsculas/espacios incluidos) con
                        // el nombre del Panel Template guardado en BD FACSDiva -- confirmar en
                        // recepción del equipo real y ajustar la plantilla si no coincide.
                        new() { DisplayOrder = 2, ColumnHeader = "PanelName", ValueTemplate = "{PanelName}" },
                        // Anonimizado: se duplica el identificador de muestra en vez del nombre
                        // real del paciente, tal como el propio fabricante contempla para
                        // "sistemas anonimizados" -- coherente con C-2/F-9 del resto de MiniLIS.
                        new() { DisplayOrder = 3, ColumnHeader = "SampleName", ValueTemplate = "{SampleNumber}" },
                        new() { DisplayOrder = 4, ColumnHeader = "CaseNumber", ValueTemplate = "{CaseNumber}" },
                        // MiniLIS no registra el tipo de tubo primario físico (Vacutainer, etc.):
                        // se deja vacío -- el campo lo admite (Requerido, permite nulo).
                        new() { DisplayOrder = 5, ColumnHeader = "PrimaryTubeType", ValueTemplate = "" },
                        // Se asume que la gradilla de preparación y el carrusel del equipo se
                        // cargan en el mismo orden impreso en la hoja de trabajo -- MiniLIS no
                        // rastrea la posición física real en dos pasos distintos.
                        new() { DisplayOrder = 6, ColumnHeader = "PrimaryRackPosition", ValueTemplate = "{PositionInGroup}" },
                        new() { DisplayOrder = 7, ColumnHeader = "CarouselPosition", ValueTemplate = "{PositionInGroup}" }
                    }
                });
            }

            {
                await SeedWorklistProfileAsync(context, new WorklistExportProfile
                {
                    Name = "FACSuite — FACSLyric",
                    TargetInstrument = "FACSuite",
                    FileFormat = WorklistFileFormat.Csv,
                    FileExtension = "csv",
                    Delimiter = ",", // BD FACSuite importa CSV separado por comas, no por punto y coma
                    Encoding = "UTF-8",
                    IncludeHeaderRow = true,
                    LineEnding = "CRLF",
                    Granularity = WorklistGranularity.PorPanel,
                    MaxRowsPerGroup = 40, // "40 Tube Rack" por defecto (ver columna Carrier Type)
                    MaxGroupsPerFile = null, // el CSV no tiene tope de fichero, solo de gradilla física
                    IsActive = true,
                    ValidatedAgainstInstrument = false,
                    Columns = new List<WorklistExportColumn>
                    {
                        new() { DisplayOrder = 1, ColumnHeader = "Sample ID", ValueTemplate = "{SampleNumber}" },
                        // Task debe coincidir EXACTAMENTE con el nombre del ensayo publicado en
                        // Biblioteca > Ensayos de BD FACSuite -- confirmar contra el equipo real.
                        new() { DisplayOrder = 2, ColumnHeader = "Task", ValueTemplate = "{PanelName}" },
                        new() { DisplayOrder = 3, ColumnHeader = "Loading Option", ValueTemplate = "Universal Loader" },
                        new() { DisplayOrder = 4, ColumnHeader = "Carrier Type", ValueTemplate = "40 Tube Rack" },
                        new() { DisplayOrder = 5, ColumnHeader = "Carrier ID", ValueTemplate = "{WorklistDate}-R{GroupIndex}" },
                        new() { DisplayOrder = 6, ColumnHeader = "Position", ValueTemplate = "{PositionInGroup}" },
                        // Solo obligatorios para ensayos IVD (p. ej. BD OneFlow): MiniLIS no
                        // rastrea lotes de reactivo hoy, se dejan vacíos.
                        new() { DisplayOrder = 7, ColumnHeader = "Reagent Lot ID", ValueTemplate = "" },
                        new() { DisplayOrder = 8, ColumnHeader = "Expiration Date", ValueTemplate = "" }
                    }
                });
            }
            await context.SaveChangesAsync();
        }

        /// <summary>Genera una contraseña que cumple la política de Identity configurada en
        /// Program.cs POR CONSTRUCCIÓN (un carácter de cada categoría exigida, no al azar sobre
        /// un alfabeto que puede no tocar ninguno -- ver N-1). Se excluyen los caracteres
        /// ambiguos I/O/l/0/1 porque esta contraseña se lee de un log y se teclea a mano una
        /// vez; con 20 caracteres del alfabeto reducido la entropía sigue siendo holgada
        /// (~humano ilegible, pero no confundible al copiarla).</summary>
        public static string GenerateCompliantPassword(int length = 20)
        {
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // sin I ni O
            const string lower = "abcdefghijkmnopqrstuvwxyz";  // sin l
            const string digits = "23456789";                  // sin 0 ni 1
            const string symbols = "!@#$%*-_=+?";
            const string all = upper + lower + digits + symbols;

            var chars = new List<char>
            {
                upper[RandomNumberGenerator.GetInt32(upper.Length)],
                lower[RandomNumberGenerator.GetInt32(lower.Length)],
                digits[RandomNumberGenerator.GetInt32(digits.Length)],
                symbols[RandomNumberGenerator.GetInt32(symbols.Length)]
            };

            while (chars.Count < length)
                chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);

            // Barajado Fisher-Yates con fuente criptográfica: sin esto, los cuatro caracteres
            // obligatorios quedarían siempre en las primeras posiciones.
            for (int i = chars.Count - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            return new string(chars.ToArray());
        }

        /// <summary>Inserta el perfil si no existe; si ya existe, lo actualiza en sitio (perfil
        /// y columnas) SOLO mientras siga sin validar contra el equipo real. Un perfil ya
        /// validado (ValidatedAgainstInstrument = true) representa la confirmación de un
        /// administrador contra su equipo concreto -- sobrescribirlo automáticamente en cada
        /// arranque, aunque el esquema de referencia de MiniLIS mejore, destruiría esa
        /// confirmación sin avisar. Se identifica por TargetInstrument (único por diseño: solo
        /// se siembra un perfil "FACSDiva" y uno "FACSuite").</summary>
        private static async Task SeedWorklistProfileAsync(ApplicationDbContext context, WorklistExportProfile desired)
        {
            var existing = await context.WorklistExportProfiles
                .Include(p => p.Columns)
                .FirstOrDefaultAsync(p => p.TargetInstrument == desired.TargetInstrument);

            if (existing == null)
            {
                context.WorklistExportProfiles.Add(desired);
                return;
            }

            if (existing.ValidatedAgainstInstrument) return;

            existing.Name = desired.Name;
            existing.FileFormat = desired.FileFormat;
            existing.FileExtension = desired.FileExtension;
            existing.Delimiter = desired.Delimiter;
            existing.Encoding = desired.Encoding;
            existing.IncludeHeaderRow = desired.IncludeHeaderRow;
            existing.LineEnding = desired.LineEnding;
            existing.Granularity = desired.Granularity;
            existing.XmlRootElement = desired.XmlRootElement;
            existing.XmlGroupElement = desired.XmlGroupElement;
            existing.XmlRowElement = desired.XmlRowElement;
            existing.MaxRowsPerGroup = desired.MaxRowsPerGroup;
            existing.MaxGroupsPerFile = desired.MaxGroupsPerFile;
            existing.IsActive = desired.IsActive;

            context.WorklistExportColumns.RemoveRange(existing.Columns);
            existing.Columns = desired.Columns;
        }
    }
}
