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
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options => {
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;

    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options => {
    options.LoginPath = "/login";
    options.LogoutPath = "/account/logout";
    options.AccessDeniedPath = "/login";
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
builder.Services.AddScoped<IExcedenteService, ExcedenteService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

builder.Services.AddHostedService<MiniLIS.Infrastructure.Workers.BackupWorker>();

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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


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
