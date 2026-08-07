using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniLIS.Infrastructure.Services
{
    /// <summary>
    /// Encoder Code 128 (subconjunto B) propio, sin dependencias de terceros (F-5). No existe
    /// ninguna librería de códigos de barras en el proyecto; introducir un paquete NuGet con
    /// binarios nativos para esto no es prudente cuando la especificación es pública y el
    /// alfabeto necesario (dígitos, guion, alguna letra mayúscula para "T03") es pequeño.
    /// La tabla de símbolos es la tabla pública estándar de Code 128 (ISO/IEC 15417); el
    /// resultado se puede verificar decodificando con la misma tabla.
    /// </summary>
    public static class Code128Encoder
    {
        private const int StartB = 104;
        private const int StopCode = 106;

        // Anchuras de barra/espacio (6 dígitos, alternando barra/espacio empezando en barra) para
        // los símbolos 0-102, más START A/B/C (103/104/105) y STOP (106, 7 dígitos).
        private static readonly string[] Patterns =
        {
            "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213",
            "221312","231212","112232","122132","122231","113222","123122","123221","223211","221132",
            "221231","213212","223112","312131","311222","321122","321221","312212","322112","322211",
            "212123","212321","232121","111323","131123","131321","112313","132113","132311","211313",
            "231113","231311","112133","112331","132131","113123","113321","133121","313121","211331",
            "231131","213113","213311","213131","311123","311321","331121","312113","312311","332111",
            "314111","221411","431111","111224","111422","121124","121421","141122","141221","112214",
            "112412","122114","122411","142112","142211","241211","221114","413111","241112","134111",
            "111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
            "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141",
            "114131","311141","411131",
            "211412", "211214", "211232", // START A, START B, START C
            "2331112" // STOP
        };

        /// <summary>Codifica una cadena en Code 128B. Devuelve la secuencia de anchuras de
        /// módulo (barra, espacio, barra, espacio…), empezando siempre en barra, incluyendo el
        /// carácter de arranque, el dígito de control (módulo 103) y el de parada. No incluye
        /// las zonas de silencio (quiet zones): eso es responsabilidad del renderizador.</summary>
        public static List<int> EncodeToModuleWidths(string data)
        {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("El texto a codificar no puede estar vacío.", nameof(data));

            var values = new List<int> { StartB };
            foreach (var c in data)
            {
                if (c < 32 || c > 127)
                    throw new ArgumentException($"Carácter '{c}' fuera del rango imprimible ASCII soportado por Code 128B.", nameof(data));
                values.Add(c - 32);
            }

            var checksum = StartB;
            for (var i = 0; i < data.Length; i++)
            {
                checksum += (data[i] - 32) * (i + 1);
            }
            checksum %= 103;
            values.Add(checksum);
            values.Add(StopCode);

            var widths = new List<int>();
            foreach (var value in values)
            {
                foreach (var ch in Patterns[value])
                {
                    widths.Add(ch - '0');
                }
            }
            return widths;
        }

        /// <summary>Total de módulos (unidades de anchura mínima de barra) del símbolo completo,
        /// incluyendo arranque/control/parada — útil para dimensionar el SVG.</summary>
        public static int TotalModules(string data) => EncodeToModuleWidths(data).Sum();
    }
}
