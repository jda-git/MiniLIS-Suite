using System;
using System.Collections.Generic;
using System.Linq;
using MiniLIS.Domain.Entities;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>
    /// Orden del listado de muestras: el día más reciente primero y, dentro del día, el número
    /// de muestra interno descendente. Lo comparten la bandeja técnica, el buscador y los CSV
    /// que se descargan de ellos, porque una exportación que reordena las filas no se puede
    /// cotejar con lo que se vio en pantalla.
    ///
    /// <para><b>Por el día y no por la fecha y hora.</b> La hora de recepción dentro de la
    /// jornada no ordena nada útil: muchas se teclean sin hora y quedan a las 00:00, y una
    /// recepción retrasada puede llevar hora anterior a la de una muestra registrada antes.
    /// Ordenando por el instante salían el 26-00022 (20:58), el 26-00020 (10:35) y el 26-00021
    /// (00:00) en ese orden, que para quien mira la lista está desordenado.</para>
    ///
    /// <para><b>Por el día LOCAL, y por eso en memoria y no en SQL.</b> ReceptionDate se guarda
    /// en UTC y la pantalla la muestra en hora de Madrid: una muestra recibida el 6 a las 00:00
    /// está guardada como el día 5 a las 22:00. Truncar el día en SQL agruparía por el día UTC
    /// y partiría la jornada a las 22:00 (23:00 en invierno) —el caso de arriba seguiría
    /// saliendo igual de desordenado—, y SQLite no sabe de husos horarios con cambio de hora.
    /// Agrupar por el día que se ve exige la zona horaria, así que se ordena después de traer
    /// las filas.</para>
    ///
    /// <para>Ordenar el número como texto es correcto con el formato AA-NNNNN: el año va
    /// delante y la secuencia lleva ceros a la izquierda, así que el orden alfabético coincide
    /// con el cronológico (26-00019 &gt; 26-00018 &gt; 25-99999). Comparación ordinal y no
    /// cultural: con las reglas del idioma, el guion se trata como un signo menor y el orden
    /// pasaría a depender de la configuración regional de la máquina.</para>
    /// </summary>
    public static class SampleOrdering
    {
        /// <summary>Para filas que ya llevan la fecha en hora local (las del buscador).</summary>
        public static IOrderedEnumerable<T> PorDiaYNumeroDescendente<T>(
            this IEnumerable<T> filas, Func<T, DateTime> fechaLocal, Func<T, string?> numero) =>
            filas.OrderByDescending(f => fechaLocal(f).Date)
                 .ThenByDescending(f => numero(f) ?? "", StringComparer.Ordinal);

        /// <summary>Para muestras tal y como salen de la base de datos, con la fecha en UTC.</summary>
        public static IOrderedEnumerable<Sample> PorRecepcionDescendente(
            this IEnumerable<Sample> muestras, ILocalTimeService hora) =>
            muestras.PorDiaYNumeroDescendente(s => hora.ToLocal(s.ReceptionDate), s => s.SampleNumber);
    }
}
