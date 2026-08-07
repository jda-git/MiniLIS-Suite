namespace MiniLIS.Application.Interfaces
{
    /// <summary>Datos del registro diferido en modo contingencia (F-8): permite introducir las
    /// marcas temporales reales (cuándo llegó de verdad la muestra) en vez de la hora de la
    /// transcripción. Única excepción controlada a la regla de M-5 de que esas marcas son
    /// siempre automáticas.</summary>
    public class DeferredEntryInput
    {
        public bool IsDeferredEntry { get; set; }
        public string? Reason { get; set; }
        public System.DateTime? ReceivedAtUtcOverride { get; set; }
        public System.DateTime? RegisteredAtUtcOverride { get; set; }
    }
}
