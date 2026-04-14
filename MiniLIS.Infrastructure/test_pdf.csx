using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniLIS.Infrastructure.Persistence;
using MiniLIS.Infrastructure.Services;
using MiniLIS.Domain.Entities;
using System.IO;

var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(\"Data Source=minilis.db\").Options;
using var db = new ApplicationDbContext(options);
var master = new MasterDataService(db);
var doc = new DocumentService(db, master);
var report = db.SampleReports.OrderByDescending(r => r.Id).FirstOrDefault();
if(report == null) {
    Console.WriteLine(\"No reports\");
    return;
}
var pdfBytes = doc.GeneratePdfAsync(report).Result;
File.WriteAllBytes(\"test.pdf\", pdfBytes);
Console.WriteLine(\"Success! Size: \" + pdfBytes.Length);
