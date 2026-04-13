using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public class ClinicalRequest : AuditableEntity
    {
        public int Id { get; set; }
        
        public int PatientId { get; set; }
        public required virtual Patient Patient { get; set; }

        [Required]
        [MaxLength(50)]
        public string RequestNumber { get; set; } = string.Empty;
        
        [MaxLength(100)]
        public string OriginService { get; set; } = string.Empty;
        
        [MaxLength(100)]
        public string DoctorName { get; set; } = string.Empty;
        
        public DateTime RequestDate { get; set; } = DateTime.UtcNow;

        public ICollection<Sample> Samples { get; set; } = new List<Sample>();
    }
}
