using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniLIS.Infrastructure.Migrations
{
    /// <summary>
    /// N-5: solo datos, sin cambio de esquema -- Samples.SampleNumber ya era MaxLength(50), de
    /// sobra para AA-NNNNN. NumberingService pasa de emitir AA-NNNN (D4, techo de 9.999
    /// estudios/año) a AA-NNNNN (D5). Los números históricos se migran en la misma operación:
    /// GetMaxSequenceFromDbAsync ordena SampleNumber como CADENA, así que durante la convivencia
    /// de "26-0042" y "26-00043" el máximo calculado sería erróneo ("26-0042" > "26-00043"
    /// lexicográficamente, aunque 42 &lt; 43).
    /// </summary>
    public partial class MigrateSampleNumberToD5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Solo toca filas con el formato antiguo exacto (7 caracteres: AA-NNNN) -- una
            // reejecución accidental no encuentra nada que migrar (longitud ya es 8) y un
            // número manual con formato distinto queda intacto sin más comprobación que hacer.
            migrationBuilder.Sql(@"
                UPDATE Samples
                SET SampleNumber = substr(SampleNumber, 1, 3) || printf('%05d', CAST(substr(SampleNumber, 4) AS INTEGER))
                WHERE length(SampleNumber) = 7
                  AND substr(SampleNumber, 3, 1) = '-'
                  AND substr(SampleNumber, 1, 2) GLOB '[0-9][0-9]'
                  AND substr(SampleNumber, 4) GLOB '[0-9][0-9][0-9][0-9]';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversión best-effort: solo restaura a D4 los números cuya secuencia sigue
            // cabiendo en 4 dígitos. Una secuencia real que ya superó 9.999 tras aplicar esta
            // migración no tiene representación D4 válida -- se deja en D5, que es preferible a
            // truncar el número de un estudio real.
            migrationBuilder.Sql(@"
                UPDATE Samples
                SET SampleNumber = substr(SampleNumber, 1, 3) || printf('%04d', CAST(substr(SampleNumber, 4) AS INTEGER))
                WHERE length(SampleNumber) = 8
                  AND substr(SampleNumber, 3, 1) = '-'
                  AND substr(SampleNumber, 1, 2) GLOB '[0-9][0-9]'
                  AND substr(SampleNumber, 4) GLOB '[0-9][0-9][0-9][0-9][0-9]'
                  AND CAST(substr(SampleNumber, 4) AS INTEGER) <= 9999;
            ");
        }
    }
}
