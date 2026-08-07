namespace MiniLIS.Domain.Entities
{
    /// <summary>
    /// Tipo de muestra (A-3). Alcance acotado a los cuatro tipos que ya tenían soporte en
    /// la interfaz (SP/MO/LCR/Otros) — el documento origen lista más tipos (GG, LP, LA, LBA,
    /// PAAF, AF) sin ningún soporte hoy en la UI; ampliar el enum sin la interfaz que los use
    /// queda fuera de alcance de este ticket. El valor 0 se deja libre a propósito: un
    /// default(SampleType) no debe confundirse con un tipo válido.
    /// </summary>
    public enum SampleType
    {
        SangrePeriferica = 1,       // SP
        MedulaOsea = 2,              // MO
        LiquidoCefalorraquideo = 3,  // LCR
        Otros = 99
    }

    public static class SampleTypeExtensions
    {
        public static string ToCode(this SampleType t) => t switch
        {
            SampleType.SangrePeriferica => "SP",
            SampleType.MedulaOsea => "MO",
            SampleType.LiquidoCefalorraquideo => "LCR",
            _ => "OTR"
        };

        public static string ToDisplayName(this SampleType t) => t switch
        {
            SampleType.SangrePeriferica => "Sangre periférica",
            SampleType.MedulaOsea => "Médula ósea",
            SampleType.LiquidoCefalorraquideo => "Líquido cefalorraquídeo",
            _ => "Otros"
        };
    }
}
