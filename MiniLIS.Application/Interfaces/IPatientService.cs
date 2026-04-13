using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public interface IPatientService
    {
        Task<Patient?> GetByNHCAsync(string nhc);
        Task<Patient> UpsertPatientAsync(Patient patient);
        Task<List<Patient>> SearchByNameAsync(string name);
    }
}
