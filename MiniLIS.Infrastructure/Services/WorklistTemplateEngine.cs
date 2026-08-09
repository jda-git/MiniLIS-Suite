namespace MiniLIS.Infrastructure.Services
{
    /// <summary>Contexto de sustitución para la hoja de trabajo del citómetro (F-6). Conjunto
    /// fijo y cerrado de propiedades: no existe (ni existirá por descuido) ningún campo de
    /// nombre, NHC o NASI aquí, así que WorklistTemplateEngine no puede reintroducir identidad
    /// en la exportación aunque alguien lo intente desde un perfil mal configurado.</summary>
    public class WorklistTemplateContext
    {
        public string SampleNumber { get; init; } = string.Empty;
        public string SampleTypeCode { get; init; } = string.Empty;
        public string SampleTypeName { get; init; } = string.Empty;
        public int TubeNumber { get; init; }
        public string TubeNumberPadded { get; init; } = string.Empty;
        public string PanelCode { get; init; } = string.Empty;
        public string PanelVersion { get; init; } = string.Empty;
        public string PanelDisplayCode { get; init; } = string.Empty;

        /// <summary>Nombre completo del panel (Panel.Name, ej. "Leucemia Aguda"), no el código
        /// corto -- suele ser el candidato más realista para hacer coincidir de forma exacta
        /// con el nombre del Panel Template (FACSDiva) o del Assay de Biblioteca (FACSuite).</summary>
        public string PanelName { get; init; } = string.Empty;
        public string MarkerList { get; init; } = string.Empty;
        public string FcsFileName { get; init; } = string.Empty;
        public string ReceptionDate { get; init; } = string.Empty;
        public string WorklistDate { get; init; } = string.Empty;
        public int SequenceInWorklist { get; init; }

        /// <summary>Nº de petición del peticionario (ClinicalRequest.RequestNumber): el
        /// equivalente LIS más cercano a "número de caso/orden" (CaseNumber de FACSDiva).</summary>
        public string CaseNumber { get; init; } = string.Empty;

        /// <summary>Posición dentro del carrusel/gradilla actual (1..MaxRowsPerGroup del
        /// perfil); reinicia en 1 al pasar al siguiente grupo. Ver WorklistExportService.</summary>
        public int PositionInGroup { get; init; }

        /// <summary>Índice de carrusel/gradilla (1-based), incrementa cada vez que
        /// PositionInGroup vuelve a 1.</summary>
        public int GroupIndex { get; init; }
    }

    public static class WorklistTemplateEngine
    {
        /// <summary>Sustituye los marcadores {Nombre} de la plantilla contra el contexto. Los
        /// valores del contexto ya llegan saneados (solo A-Z0-9-_) por quien lo construye
        /// (WorklistExportService): aquí solo se sustituye, no se sanea de nuevo — así los
        /// separadores literales que el administrador ponga en la plantilla (p. ej. el punto de
        /// {FcsFileName}) no se destruyen.</summary>
        public static string Render(string template, WorklistTemplateContext ctx)
        {
            return (template ?? string.Empty)
                .Replace("{SampleNumber}", ctx.SampleNumber)
                .Replace("{SampleTypeCode}", ctx.SampleTypeCode)
                .Replace("{SampleTypeName}", ctx.SampleTypeName)
                .Replace("{TubeNumber}", ctx.TubeNumber.ToString())
                .Replace("{TubeNumberPadded}", ctx.TubeNumberPadded)
                .Replace("{PanelCode}", ctx.PanelCode)
                .Replace("{PanelVersion}", ctx.PanelVersion)
                .Replace("{PanelDisplayCode}", ctx.PanelDisplayCode)
                .Replace("{PanelName}", ctx.PanelName)
                .Replace("{MarkerList}", ctx.MarkerList)
                .Replace("{FcsFileName}", ctx.FcsFileName)
                .Replace("{ReceptionDate}", ctx.ReceptionDate)
                .Replace("{WorklistDate}", ctx.WorklistDate)
                .Replace("{SequenceInWorklist}", ctx.SequenceInWorklist.ToString())
                .Replace("{CaseNumber}", ctx.CaseNumber)
                .Replace("{PositionInGroup}", ctx.PositionInGroup.ToString())
                .Replace("{GroupIndex}", ctx.GroupIndex.ToString());
        }
    }
}
