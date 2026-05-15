
using Microsoft.EntityFrameworkCore;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Domain.Entities;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    
    var reportsToFix = await db.SampleReports
        .Include(r => r.Sample)
        .Where(r => r.Sample.Status == SampleStatus.Finalizada && !r.ReportDate.HasValue)
        .ToListAsync();
        
    Console.WriteLine($"Found {reportsToFix.Count} finalized reports without ReportDate.");
    
    foreach (var r in reportsToFix)
    {
        // Set ReportDate to Sample's UpdatedAtUtc or ReceptionDate + 1 day as fallback
        r.ReportDate = r.UpdatedAtUtc ?? r.Sample.ReceptionDate.AddDays(1);
        r.IsFinalized = true;
    }
    
    await db.SaveChangesAsync();
    Console.WriteLine("Fix applied.");
}
