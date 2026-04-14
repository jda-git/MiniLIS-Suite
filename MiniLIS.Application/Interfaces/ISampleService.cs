using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    public interface ISampleService
    {
        Task<List<Sample>> GetFilteredSamplesAsync(string? searchTerm, SampleStatus? status, DateTime? fromDate, DateTime? toDate);
        Task<bool> UpdateSampleStatusAsync(int sampleId, SampleStatus status);
        Task<byte[]> ExportSamplesToCsvAsync(List<Sample> samples);
        Task<Sample> RegisterSampleAsync(Patient patient, ClinicalRequest request, string sampleDiagnosis, string sampleType, string studyPanel = "", bool hasIncident = false, string incidentNotes = "");
        Task<Sample?> GetSampleByIdAsync(int sampleId);
        Task<bool> UpdateSampleAsync(Sample sample);
    }
}
