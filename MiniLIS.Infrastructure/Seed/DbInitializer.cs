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
            const string adminUser = "admin";
            
            var existingAdmin = await userManager.FindByNameAsync(adminUser);
            if (existingAdmin == null)
            {
                var admin = new ApplicationUser
                {
                    UserName = adminUser,
                    Email = adminEmail,
                    FullName = "Administrador del Sistema",
                    EmailConfirmed = true,
                    IsActive = true
                };

                var createPowerUser = await userManager.CreateAsync(admin, "Admin123!");
                if (createPowerUser.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, "Administrador");
                }
            }

            // 3. Seed Intensities
            string[] intensities = { "-", "+", "++", "+d", "-/+d", "-/+", "+d/+", "+/++", "Heterogéneo" };
            for (int i = 0; i < intensities.Length; i++)
            {
                string key = $"Config:Intensity:{i}";
                if (!await context.SystemSettings.AnyAsync(s => s.Key == key))
                {
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
