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

## v2.2.1

### Corregido el desglose de PCT-INCIDENCIA

Agrupaba por `RejectionReason.Category`, campo que la siembra deja con su valor por
defecto (`"Preanalítica"`) en los once motivos del catálogo. El resultado era **un
desglose de una sola barra que repetía el total**, sin informar de nada.

Pasa a agrupar por descripción, como ya hacían PCT-RECHAZO y PCT-SALVEDAD. Ahora sí se ve
la causa concreta de cada incidencia — «Demora excesiva desde la extracción», «Muestra
coagulada»… —, que es justo lo que permite actuar sobre el servicio peticionario que
corresponda. `Category` sigue en el modelo por si el laboratorio decide definir una
taxonomía real; ese sería el indicador natural para mostrarla.

---

## v2.2.0

### Retirado el indicador TAT-PRE

El cuadro de mando incluía «TAT preanalítico (recepción → registro)», definido como
`RegisteredAtUtc - ReceivedAtUtc`. **Medía un intervalo que no existe:** el alta es de un
solo paso, y `RegisterSampleAsync` asigna a ambas marcas el mismo instante, de modo que el
resultado era cero por construcción y no por buen desempeño. Aparecía en el panel como
«0 h (P90: 0 h)» de forma permanente.

Solo podía dar un valor distinto de cero en el registro diferido (modo contingencia, F-8),
donde el operador teclea ambas marcas a mano — es decir, mediría lo que alguien escribió,
no un hecho observado por el sistema.

Un indicador siempre a cero en un cuadro de mando de acreditación es peor que no tenerlo:
sugiere que la unidad no entiende su propio indicador o que rellena el panel. **La fase
preanalítica sigue cubierta** por PCT-RECHAZO, PCT-SALVEDAD y PCT-INCIDENCIA, que sí miden
hechos reales. La cadena de TAT queda completa sin él: TAT-ADQ arranca en `RegisteredAtUtc`,
que coincide con `ReceivedAtUtc`, así que no se abre ningún hueco.

Se retira del catálogo también en las bases ya sembradas (`RetiredIndicatorsCleaner`,
idempotente): quitarlo de la lista de siembra solo habría evitado crearlo en instalaciones
nuevas.

**Sobre el intervalo extracción → recepción:** es el que sí tiene valor clínico en
citometría, porque la viabilidad celular se degrada con el transporte, pero **ya está
controlado en el proceso y no procede añadirlo aquí**. El dato vive en el LIS del
hospital, y el técnico lo comprueba al registrar: si detecta una demora, levanta una
incidencia con el motivo «Demora excesiva desde la extracción» (`DEMORA`). Eso queda como
dato estructurado y lo recogen PCT-RECHAZO, PCT-SALVEDAD y PCT-INCIDENCIA, con desglose
por causa y filtrables por servicio peticionario.

Capturar `CollectedAtUtc` a mano en MiniLIS supondría teclear en cada muestra un dato que
ya existe en el LIS corporativo, para calcular una métrica cuya parte accionable ya se
registra. La vía correcta para medir la distribución completa de tiempos de transporte es
la integración con el LIS del hospital, no la doble introducción manual.

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
