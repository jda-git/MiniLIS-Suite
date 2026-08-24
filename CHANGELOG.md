# Registro de cambios

Cambios relevantes de MiniLIS Suite. El detalle completo está en el historial de git;
aquí queda lo que afecta al uso, al informe emitido o a la validación del sistema.

## Esquema de numeración

`MAYOR.MENOR.PARCHE`, con el criterio adaptado a un sistema de laboratorio: lo que
determina el salto no es la compatibilidad binaria —no hay API pública ni consumidores
externos— sino **si el cambio obliga a revalidar**.

| Salto | Cuándo | Consecuencia |
|---|---|---|
| **MAYOR** | Cambios que obligan a revalidar el sistema o que alteran el informe emitido al clínico. | Requiere validación documentada antes de su uso (ISO 15189, cl. 7.6). |
| **MENOR** | Funcionalidad nueva, compatible con los datos existentes. | Notificación al Servicio de Informática antes de desplegar. |
| **PARCHE** | Correcciones sin cambio funcional. | Despliegue ordinario. |

El número vive en un único sitio, `Directory.Build.props`, y de ahí lo heredan los cinco
proyectos. La interfaz lo lee de los metadatos del ensamblado
(`MiniLIS.Web/Services/AppVersion.cs`): no debe escribirse a mano en ninguna página.

Cada versión desplegada se marca con una etiqueta git anotada (`v2.1.0`), que es lo que
permite responder «¿qué código exacto es esta versión?» — necesario para la trazabilidad
de los informes, que se conservan cinco años o más.

Los ensamblados llevan además el commit anexado a la versión informativa
(`2.1.0+c339fa2`), cortesía de SourceLink. Es el dato a pedir ante una incidencia:
identifica el código en ejecución, cosa que el número de versión por sí solo no hace
entre despliegues de una misma versión.

---

## v2.1.0

Primera versión con numeración formal. Hasta aquí el número mostrado en la pantalla de
acceso era una cadena escrita a mano (`2.0.4.Final`) que no se correspondía con nada: los
ensamblados se compilaban como `1.0.0` y no existía ninguna etiqueta en el repositorio.

### Excedente y alícuotas (F-7)

- **Cada alícuota criopreservada pasa a ser una unidad propia** (`BatchId` / `AliquotIndex`
  / `BatchSize`) en lugar de una fila con un contador por lote. Antes, registrar la
  descongelación de un vial marcaba como descongelado el lote entero: con veinte alícuotas
  almacenadas, descongelar una dejaba las veinte en ese estado y sin forma de distinguirlas.
- Los lotes históricos se expanden automáticamente en alícuotas individuales al arrancar
  (`StoredSpecimenBatchMigrator`). La fila original conserva su historial y su estado; las
  hermanas nuevas se crean como *Almacenada*, por no poder determinarse su estado real
  anterior, y queda constancia en el log.
- Impresión de etiquetas por lote, reutilizando la pantalla de etiquetas de muestra. Cada
  etiqueta lleva su código de barras propio, el tipo y número de alícuota (`TUB 3/20`) y la
  fecha de almacenamiento.
- Corregido el código de barras de las alícuotas, que resultaba ilegible para el lector: el
  ancho de módulo se estiraba para llenar la etiqueta, produciendo barras desproporcionadas
  con datos cortos. Ahora es fijo.
- Corregida la exportación CSV, donde Excel interpretaba como fechas los valores de alícuota
  `1/20` a `12/20` (día/mes válido) y dejaba el resto como texto.
- Formulario de alta de alícuotas: campos ensanchados, eran demasiado estrechos para leer lo
  que se escribía.

### Recepción e informe (F-4)

- **El rechazo preanalítico se traslada al informe.** La salvedad ya constaba
  («LIMITACIONES»), pero una muestra rechazada no dejaba rastro alguno en el documento
  entregado al clínico. Se añade el apartado «MUESTRA RECHAZADA PREANALÍTICAMENTE» con el
  motivo y, si consta, la notificación al peticionario (cl. 7.4).
- El panel de control contaba las muestras rechazadas por el estado del flujo de trabajo, no
  por el estado de recepción, de modo que una muestra rechazada en recepción aparecía como
  «Recibida» y el contador de rechazadas mostraba siempre cero.
- **Ciclo de vida de la muestra:** la promoción a *En proceso* al marcarse la lectura del
  primer tubo. Ese estado existía en el desplegable pero ningún camino del código lo
  asignaba: la muestra saltaba de *Recibida* a *Reportada parcial* al guardarse el primer
  borrador, salvo que alguien lo marcara a mano.

### Corrección de raíz

- **Paquetes desalineados con `net9.0`.** `Microsoft.Extensions.Identity.Stores` iba fijado a
  `10.0.5` y `Microsoft.Extensions.Hosting.Abstractions` a `10.0.8`, mientras el resto de la
  solución usa `9.0.*`. Con la caché de NuGet limpia —es decir, en CI y no en local— se
  resolvían de verdad esas versiones, que esperan una firma de criptografía interna distinta
  de la del framework compartido de ASP.NET Core 9. El síntoma era un
  `MissingMethodException` al validar cualquier token antiforgery, que llegaba al usuario
  como un error 400 en el inicio de sesión.

### Mantenimiento

- Eliminado `test_pdf.csx`, script de comprobación manual de PDF inservible desde hacía tres
  refactorizaciones (no parseaba, no compilaba y su cometido ya lo cubre
  `DocumentServiceTests`).
- README actualizado al estado real: la aplicación funciona con datos de prueba y no ha
  contenido datos de pacientes reales. El documento afirmaba lo contrario.
- 164 pruebas automatizadas, verdes en integración continua.
