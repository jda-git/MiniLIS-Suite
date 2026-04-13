using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MiniLIS.Domain.Common;

namespace MiniLIS.Domain.Entities
{
    public class Patient : AuditableEntity
    {
        public int Id { get; set; }
        
        [Required]
        [MaxLength(20)]
        public string NHC { get; set; } = string.Empty;
        
        [MaxLength(20)]
        public string NASI { get; set; } = string.Empty;
        
        [Required]
        [MaxLength(200)]
        public string FullName { get; set; } = string.Empty;
        
        public DateTime? BirthDate { get; set; }

        public ICollection<ClinicalRequest> Requests { get; set; } = new List<ClinicalRequest>();
    }
}
