using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>
    /// Versiones de panel (M-4, circuito v4): Borrador → EnRevision → Aprobada (con fecha y
    /// hora de entrada en vigor) → Vigente → Retirada. Fuera de Borrador una versión no se
    /// modifica: el único camino para cambiarla es un borrador nuevo.
    ///
    /// Crean y editan borradores el Administrador y el Facultativo; aprueban, devuelven a
    /// borrador y retiran solo los facultativos. Las reglas de rol se comprueban aquí, no solo
    /// en la pantalla.
    /// </summary>
    public interface IPanelCatalogService
    {
        /// <summary>Versión vigente del panel. Antes pone en vigor las aprobadas cuya fecha ya
        /// ha llegado, para que un estudio nuevo reciba siempre la que corresponde.</summary>
        Task<PanelVersion?> GetVigenteVersionAsync(int panelId);
        Task<PanelVersion?> GetVersionWithTubesAsync(int panelVersionId);
        Task<List<PanelVersion>> GetVersionsForPanelAsync(int panelId);
        Task<int> CountSamplePanelsUsingVersionAsync(int panelVersionId);

        /// <summary>Números sugeridos para una versión nueva: siguiente menor y siguiente mayor.</summary>
        Task<PanelVersionNumberSuggestion> SuggestNextVersionAsync(int panelId);

        /// <summary>Crea un borrador con la versión indicada (mayor que todas las anteriores),
        /// copiando opcionalmente los tubos y referencias de otra versión.</summary>
        Task<PanelVersion> CreateDraftVersionAsync(int panelId, int versionMajor, int versionMinor, string changeNotes, int? copyFromVersionId = null);

        /// <summary>Guarda un borrador. Falla fuera de Borrador.</summary>
        Task SaveDraftVersionAsync(int panelVersionId, PanelVersionDraftInput input);

        /// <summary>Descarta un borrador que no usa ningún estudio.</summary>
        Task DeleteDraftAsync(int panelVersionId);

        /// <summary>Borrador → En revisión. Comprueba que esté completo (ficha maestra,
        /// fórmula y revisión de cada tubo, descripción del cambio y, si sube la versión mayor,
        /// su evaluación).</summary>
        Task SubmitForReviewAsync(int panelVersionId);

        /// <summary>En revisión (o aprobada aún no vigente) → Borrador, con motivo. Solo facultativos.</summary>
        Task ReturnToDraftAsync(int panelVersionId, string reason);

        /// <summary>En revisión → Aprobada, con fecha y hora de entrada en vigor (si ya ha
        /// pasado, entra en vigor al momento). Solo facultativos, y confirmando que la
        /// composición coincide con la ficha maestra.</summary>
        Task ApproveAsync(int panelVersionId, DateTime effectiveFromUtc, bool compositionVerified);

        /// <summary>Pone en vigor las versiones aprobadas cuya fecha ha llegado y retira las que
        /// sustituyen. Devuelve cuántas han entrado en vigor.</summary>
        Task<int> ActivateDueVersionsAsync(DateTime? nowUtc = null);

        /// <summary>Vigente → Retirada sin sustituta, con motivo. Solo facultativos.</summary>
        Task RetireAsync(int panelVersionId, string reason);

        /// <summary>Añade una aclaración a una versión ya enviada, aprobada o retirada, sin
        /// modificar su descripción original.</summary>
        Task AddClarificationAsync(int panelVersionId, string text);
    }

    public class PanelVersionNumberSuggestion
    {
        public int NextMinorMajor { get; init; }
        public int NextMinorMinor { get; init; }
        public int NextMajorMajor { get; init; }
        public int NextMajorMinor { get; init; }
        /// <summary>Versión más alta existente, o null si el panel no tiene ninguna.</summary>
        public string? Highest { get; init; }
    }

    public class PanelVersionDraftInput
    {
        public int VersionMajor { get; set; }
        public int VersionMinor { get; set; }
        public string? ChangeNotes { get; set; }
        public string? ChangeEvaluationRef { get; set; }
        public string? QmsDocumentRefCode { get; set; }
        public string? MasterSheetCode { get; set; }
        public string? MasterSheetRevision { get; set; }
        public string? ExternalSource { get; set; }
        public string? ExternalName { get; set; }
        public string? ExternalVersion { get; set; }
        public List<PanelTubeInput> Tubes { get; set; } = new();
    }

    public class PanelTubeInput
    {
        public string MarkerList { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public bool IsOptional { get; set; }
        public string? FormulaCode { get; set; }
        public string? FormulaRevision { get; set; }
    }

    /// <summary>
    /// Reglas para la descripción de un cambio de versión. No pueden juzgar si el texto es
    /// bueno, pero sí rechazar lo que claramente no explica nada ("nada", "cambio", "-"), que
    /// es lo que había en versiones reales de la base de pruebas.
    /// </summary>
    public static class ChangeNoteRules
    {
        public const int MinLength = 20;
        public const int MinWords = 3;

        private static readonly HashSet<string> Trivial = new(StringComparer.OrdinalIgnoreCase)
        {
            "nada", "cambio", "cambios", "ninguno", "ninguna", "varios", "varias", "ok", "na", "n/a",
            "-", "--", ".", "x", "prueba", "pruebas", "test", "sin cambios", "actualizacion",
            "actualización", "actualizado", "nueva version", "nueva versión", "modificacion", "modificación"
        };

        /// <summary>Devuelve el motivo del rechazo, o null si el texto es aceptable.</summary>
        public static string? Validate(string? text)
        {
            var t = (text ?? string.Empty).Trim();
            if (t.Length == 0) return "Describa el cambio respecto a la versión anterior.";
            if (Trivial.Contains(t.TrimEnd('.', '!', ' ')))
                return $"«{t}» no describe el cambio. Indique qué cambia (tubos, anticuerpos, clones, fluorocromos…) y por qué.";
            if (t.Length < MinLength)
                return $"La descripción del cambio es demasiado corta (mínimo {MinLength} caracteres). Indique qué cambia y por qué.";
            var words = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            if (words < MinWords)
                return $"La descripción del cambio debe tener al menos {MinWords} palabras. Indique qué cambia y por qué.";
            return null;
        }
    }
}
