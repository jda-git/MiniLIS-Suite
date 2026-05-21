using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniLIS.Domain.Entities;
using MiniLIS.Domain.Identity;
using MiniLIS.Infrastructure.Persistence;
using System;
using System.Threading.Tasks;

namespace MiniLIS.Infrastructure.Seed
{
    public static class DbInitializer
    {
        public static async Task SeedIdentityAsync(IServiceProvider serviceProvider)
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

            // 2. Seed Default Admin
            const string adminEmail = "admin@minilis.com";
            const string adminUser = adminEmail;
            
            var existingAdmin = await userManager.FindByNameAsync(adminUser);
            if (existingAdmin == null)
            {
                var admin = new ApplicationUser
                {
                    UserName = adminUser,
                    Email = adminEmail,
                    FullName = "Administrador del Sistema",
                    EmailConfirmed = true,
                    IsActive = true,
                    MustChangePassword = true
                };

                var createPowerUser = await userManager.CreateAsync(admin, "Admin123!");
                if (createPowerUser.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, "Administrador");
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
            foreach (var pName in panels)
            {
                if (!await context.Panels.AnyAsync(p => p.Name == pName))
                {
                    context.Panels.Add(new Panel { Name = pName });
                }
            }

            await context.SaveChangesAsync();
        }
    }
}
