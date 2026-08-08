using MiniLIS.Domain.Entities;
using System;

namespace MiniLIS.Tests.TestSupport
{
    /// <summary>Construcción mínima y válida de grafos Patient→ClinicalRequest→Sample para
    /// pruebas, sin pasar por la capa de servicios.</summary>
    public static class EntityBuilders
    {
        public static Patient NewPatient(string nhc = "NHC001", string fullName = "Paciente de Prueba") =>
            new Patient { NHC = nhc, NASI = "", FullName = fullName, CreatedBy = 1 };

        public static ClinicalRequest NewRequest(Patient patient, string requestNumber = "REQ001") =>
            new ClinicalRequest
            {
                Patient = patient,
                RequestNumber = requestNumber,
                OriginService = "Hematología",
                DoctorName = "Dr. Prueba",
                RequestDate = DateTime.UtcNow,
                CreatedBy = 1
            };

        public static Sample NewSample(ClinicalRequest request, string sampleNumber = "26-0001",
            SampleType sampleType = SampleType.SangrePeriferica) =>
            new Sample
            {
                ClinicalRequest = request,
                SampleNumber = sampleNumber,
                SampleType = sampleType,
                ReceptionDate = DateTime.UtcNow,
                ReceivedAtUtc = DateTime.UtcNow,
                RegisteredAtUtc = DateTime.UtcNow,
                CreatedBy = 1
            };
    }
}
