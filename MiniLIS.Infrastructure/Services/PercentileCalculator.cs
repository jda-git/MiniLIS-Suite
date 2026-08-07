using System;
using System.Collections.Generic;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>
    /// Cálculo de mediana y percentiles para los indicadores TAT-* (F-1).
    /// Método fijado: "rango más cercano" (nearest rank) — ceil(P/100 × N) sobre la
    /// serie ordenada ascendentemente. Se documenta aquí porque hay varias definiciones
    /// de percentil y dan resultados distintos con muestras pequeñas (ver F-1, "Precisiones
    /// estadísticas"). Se usa la mediana y el P90, nunca la media: un caso olvidado un mes
    /// desplaza la media entera y oculta el comportamiento real.
    /// </summary>
    public static class PercentileCalculator
    {
        public static double NearestRank(IReadOnlyList<double> sortedAscending, double percentile)
        {
            if (sortedAscending.Count == 0) return 0;
            var rank = (int)Math.Ceiling(percentile / 100.0 * sortedAscending.Count);
            rank = Math.Clamp(rank, 1, sortedAscending.Count);
            return sortedAscending[rank - 1];
        }

        public static double Median(IReadOnlyList<double> sortedAscending) => NearestRank(sortedAscending, 50);
    }
}
