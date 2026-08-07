using System;

namespace MiniLIS.Application.Interfaces
{
    /// <summary>
    /// Punto único de conversión entre UTC (lo único que se almacena) y hora local de
    /// Madrid (lo único que se muestra). M-5: evita la conversión campo a campo dispersa
    /// por vistas y servicios, y el desfase incorrecto de usar .ToLocalTime() dependiendo
    /// de la zona horaria configurada en el sistema operativo del servidor.
    /// </summary>
    public interface ILocalTimeService
    {
        DateTime ToLocal(DateTime utc);
        DateTime? ToLocal(DateTime? utc);
        DateTime ToUtc(DateTime local);
        DateTime NowLocal();
    }
}
