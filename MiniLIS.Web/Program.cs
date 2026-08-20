using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using MiniLIS.Application.Interfaces;
using MiniLIS.Domain.Identity;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString, b => b.MigrationsAssembly("MiniLIS.Infrastructure")));

// Identity Configuration
// NOTA (M-1): esta autenticación local es transitoria. La unidad debe migrar a su
// directorio corporativo (LDAP/AD/SSO) cuando exista integración disponible; hasta
// entonces, política de contraseña y sesión reforzadas según cl. ENS de control de acceso.
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options => {
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 12;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequiredUniqueChars = 4;
    options.User.RequireUniqueEmail = true;

    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// El prefijo __Host- exige Secure siempre activo; el entorno de desarrollo local
// (.claude/launch.json) sirve por http sin TLS, así que se relaja solo aquí — nunca
// en producción, donde SecurePolicy.Always y el nombre __Host- se aplican sin excepción.
var isDevelopment = builder.Environment.IsDevelopment();
builder.Services.ConfigureApplicationCookie(options => {
    options.LoginPath = "/login";
    options.LogoutPath = "/account/logout";
    options.AccessDeniedPath = "/acceso-denegado";   // no /login: son cosas distintas
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.Name = isDevelopment ? "MiniLIS.Auth" : "__Host-MiniLIS";
});

// Application Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, MiniLIS.Web.Services.CurrentUserService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<INumberingService, NumberingService>();
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IMasterDataService, MasterDataService>();
builder.Services.AddScoped<IPanelCatalogService, PanelCatalogService>();
builder.Services.AddSingleton<ILocalTimeService, LocalTimeService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<ISampleService, SampleService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddScoped<IStatisticsService, StatisticsService>();
builder.Services.AddScoped<IQualityIndicatorService, QualityIndicatorService>();
builder.Services.AddScoped<IWorklistService, WorklistService>();
builder.Services.AddScoped<IWorklistExportService, WorklistExportService>();
builder.Services.AddScoped<IStorageService, StorageService>();
builder.Services.AddScoped<IFcsLinkService, FcsLinkService>();
builder.Services.AddScoped<IContingencyService, ContingencyService>();
builder.Services.AddScoped<IAuditPackageService, AuditPackageService>();
builder.Services.AddScoped<IAuditQueryService, AuditQueryService>();
builder.Services.AddScoped<IExcedenteService, ExcedenteService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPatientDataExportPolicy, PatientDataExportPolicy>(); // N-2

builder.Services.AddHostedService<MiniLIS.Infrastructure.Workers.BackupWorker>();
builder.Services.AddHostedService<MiniLIS.Infrastructure.Workers.FcsVerificationWorker>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 10 * 1024 * 1024);

builder.Services.AddControllersWithViews(); // Required for AccountController and Antiforgery filters 

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
// NOTA (C-1): se evaluó un FallbackPolicy global (RequireAuthenticatedUser) como
// defensa en profundidad adicional, pero en este modelo de hosting híbrido
// (Blazor Interactive Server + controladores MVC) todas las páginas Blazor —
// protegidas y anónimas— comparten el mismo endpoint de enrutado
// (MapRazorComponents), sin metadatos de autorización por página a nivel HTTP.
// Un FallbackPolicy ahí rompe, según cómo se exima: o bien el circuito SignalR de
// páginas anónimas como /login (401 en la negociación), o bien la redirección
// automática de páginas protegidas (AuthorizeRouteView -> RedirectToLogin, que
// depende de que el framework trate la petición como no autorizada a nivel HTTP).
// El mecanismo existente — @attribute [Authorize] por página + AuthorizeRouteView —
// ya protege correctamente el resto de la aplicación (verificado), así que la
// corrección se limita a añadir el atributo que faltaba en las dos páginas
// vulnerables, sin tocar la política global.

var app = builder.Build();

// N-9: BackupService ya abortaba si faltaba Backup:EncryptionKey, pero solo al intentar la
// PRIMERA copia -- el administrador se enteraba cuando la copia de seguridad no existía, que
// es tarde. Fuera de desarrollo, la misma comprobación se hace aquí, en el arranque: un fallo
// ruidoso al desplegar es preferible a una instancia que parece funcionar pero nunca ha podido
// respaldarse. (ConnectionStrings:DefaultConnection ya se valida más arriba, al leerla.)
if (!app.Environment.IsDevelopment())
{
    var backupKey = builder.Configuration["Backup:EncryptionKey"];
    var backupKeyValid = !string.IsNullOrWhiteSpace(backupKey);
    if (backupKeyValid)
    {
        try { backupKeyValid = Convert.FromBase64String(backupKey!).Length == 32; }
        catch (FormatException) { backupKeyValid = false; }
    }
    if (!backupKeyValid)
    {
        throw new InvalidOperationException(
            "Backup:EncryptionKey no está configurada o no es una clave AES-256 válida (32 bytes en Base64). " +
            "Genere una con, por ejemplo, `openssl rand -base64 32` y configúrela como variable de entorno " +
            "Backup__EncryptionKey antes de desplegar -- sin ella, ninguna copia de seguridad puede crearse " +
            "(ver BackupService.GetEncryptionKeyOrThrow).");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// L-4: WebApplication añade estos dos automáticamente al detectar los servicios de
// Identity registrados, así que funcionaban sin esta llamada -- pero depender de
// comportamiento implícito en un sistema clínico es innecesario. Explícitos y en el
// orden que exige ASP.NET Core: autenticación antes que autorización, ambas antes
// del antiforgery y de los endpoints.
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Seed Database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();
        await MiniLIS.Infrastructure.Seed.DbInitializer.SeedIdentityAsync(services, builder.Configuration, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

await app.RunAsync();

// N-4: visibilidad mínima necesaria para que WebApplicationFactory<Program> (pruebas de
// integración/autorización) pueda referenciar este ensamblado -- las instrucciones de nivel
// superior generan una clase Program interna por defecto. No cambia ningún comportamiento
// de la aplicación.
public partial class Program { }
