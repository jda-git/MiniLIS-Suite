
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
    
    var sample = await db.Samples
        .Include(s => s.Panels)
        .FirstOrDefaultAsync(s => s.SampleNumber == "26-0072");
        
    if (sample != null) {
        var report = await db.SampleReports.FirstOrDefaultAsync(r => r.SampleId == sample.Id);
        Console.WriteLine($"Sample: {sample.SampleNumber}");
        Console.WriteLine($"Status: {sample.Status}");
        Console.WriteLine($"ReceptionDate: {sample.ReceptionDate}");
        Console.WriteLine($"UpdatedAtUtc: {sample.UpdatedAtUtc}");
        Console.WriteLine($"ReportDate: {report?.ReportDate}");
        
        if (report?.ReportDate != null) {
            var diff = report.ReportDate.Value - sample.ReceptionDate;
            Console.WriteLine($"Diff: {diff.TotalDays} days ({diff.TotalHours} hours)");
        }
    } else {
        Console.WriteLine("Sample 26-0072 not found.");
    }
}
