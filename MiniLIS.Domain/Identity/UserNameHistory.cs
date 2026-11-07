using System;
using System.ComponentModel.DataAnnotations;

namespace MiniLIS.Domain.Identity
{
    /// <summary>
    /// Todos los nombres de usuario que ha tenido cada persona, incluido el actual.
    ///
    /// Existe por dos motivos, los dos de trazabilidad:
    ///
    /// <para><b>Que un nombre no se reutilice nunca.</b> Un usuario dado de baja no libera su
    /// nombre, porque sigue en la tabla; pero renombrar a alguien sí lo liberaría, y entonces
    /// otra persona podría acabar con el nombre que aparece en los registros de auditoría de
    /// la primera. Un identificador tiene que apuntar a una sola persona para siempre.</para>
    ///
    /// <para><b>Que se pueda leer la auditoría antigua.</b> AuditLog guarda el nombre tal y como
    /// era en ese momento —que es lo correcto: refleja lo que fue verdad entonces— junto al
    /// UserId, que no cambia. Esta tabla permite responder «¿quién era jperez@hospital.es?»
    /// sin tener que adivinarlo.</para>
    /// </summary>
    public class UserNameHistory
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        [Required]
        [MaxLength(256)]
        public string UserName { get; set; } = string.Empty;

        /// <summary>En mayúsculas, como lo normaliza Identity: es la forma en que se compara,
        /// de modo que «JPerGon» y «jpergon» son el mismo nombre.</summary>
        [Required]
        [MaxLength(256)]
        public string NormalizedUserName { get; set; } = string.Empty;

        public DateTime AssignedAtUtc { get; set; }

        /// <summary>Nulo mientras sea el nombre en uso. Una fila por nombre y persona: si
        /// alguien recupera un nombre que ya tuvo (deshacer un renombrado equivocado), se
        /// reactiva su fila en vez de añadir otra. La cronología de cada cambio está en
        /// AuditLog; esto es la reserva del nombre.</summary>
        public DateTime? ReleasedAtUtc { get; set; }
    }
}
