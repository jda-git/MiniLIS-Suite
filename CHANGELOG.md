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

## v2.7.2

### Los acentos salían rotos al abrir los CSV en Excel

«Recepción» aparecía como «RecepciÃ³n» y «Nº» como «NÂº»: los ficheros iban en UTF-8 pero
**sin marca de orden de bytes (BOM)**, y sin ella Excel los abre como ANSI.

La causa es una trampa de .NET que parece correcta al leerla:
`new UTF8Encoding(true).GetBytes(...)` **no escribe el BOM**. Ese parámetro solo hace que
`GetPreamble()` lo devuelva; `GetBytes` nunca antepone el preámbulo. Compila, produce un
CSV válido, y el fallo solo se ve al abrirlo.

Afectaba a **cinco exportaciones**: las tres nuevas de esta versión (detalle de TAT, de
incidencias y del buscador) y dos anteriores que ya lo arrastraban, las de **excedente y
notificaciones**.

En vez de parchear cada sitio, la conversión pasa a `CsvUtils.ToExcelBytes`, que ya usan
las nueve exportaciones del programa —incluidas las cuatro que sí lo hacían bien pero
repetían el código—. Cubierto con pruebas que comprueban el BOM y que dejan constancia de
por qué el atajo no vale, para que no vuelva a colarse.

---

## v2.7.1

### El botón de exportar CSV no se encontraba

Quedaba a **cuatro niveles de profundidad**: desplegar la tarjeta del indicador, pulsar
«Detalle por muestra», que la lista no estuviera vacía y, solo entonces, aparecía. Con el
rango por defecto —el mes en curso— bastaba con que no hubiera estudios ese mes para que
el botón no llegara a existir.

Ahora la barra de detalle muestra **el recuento junto al indicador** («Detalle por
muestra · 4») en cuanto se despliega la tarjeta, tomado de lo ya calculado y sin consultar
la base de datos, y el **CSV se descarga en un solo clic** sin pasar por la tabla: si el
detalle no está cargado, lo trae y exporta. Con cero muestras, el desplegable queda
deshabilitado en vez de prometer algo que no hay.

### Los atajos de fecha no recalculaban

«Mes en curso», «Trimestre», «Año en curso» y «Año anterior» cambiaban el rango pero **no
volvían a calcular**: la pantalla mostraba un periodo en los selectores y las cifras de
otro, sin señal alguna de que estuvieran obsoletas. Fallo anterior a esta versión,
detectado al comprobar el arreglo del CSV.

Al recalcular se cierra además el detalle que hubiera abierto, porque correspondía al
rango anterior y habría quedado junto a una cifra que ya no lo explica.

---

## v2.7.0

### Estadísticas duplicaba Indicadores, y no coincidía con él

La pantalla de Estadísticas daba cuatro cifras —total de muestras, incidencias, su
porcentaje y el TAT medio— que el cuadro de indicadores ya cubría. No era solo
duplicación: **las dos pantallas podían responder distinto a la misma pregunta**, por tres
motivos.

**Filtraban por columnas distintas.** Estadísticas acotaba por `ReceptionDate` (fecha de
negocio) e Indicadores por `ReceivedAtUtc` (marca de proceso). Son columnas diferentes, y
editar una muestra actualiza la primera pero no la segunda: con el mismo rango podían
estar contando conjuntos distintos.

**Usaban estadísticos distintos.** Media frente a mediana y P90. Para un TAT la media es
el estadístico equivocado: un solo estudio que tardó tres semanas desplaza el resultado.

**Y el TAT de Estadísticas podía inventarse la fecha final.** Resolvía el fin con la
cadena `ValidatedAtUtc ?? FinalizedAt ?? UpdatedAtUtc ?? CreatedAtUtc`, de modo que una
muestra finalizada sin fecha de validación acababa usando **la fecha de creación** y
producía un TAT próximo a cero, indistinguible de los reales al promediarse. Indicadores
nunca hace eso: si falta la validación, la muestra se cuenta como caso abierto y queda
fuera del cálculo, visible como tal.

Para una unidad acreditada, dos pantallas que responden distinto a «¿cuál fue nuestro
TAT?» son un problema en sí mismas. Se retira `StatisticsService` completo.

### Indicadores gana el detalle nominal por muestra

Lo único que Estadísticas aportaba y no estaba cubierto era el **listado por muestra con
exportación a CSV**. Se traslada a Indicadores como desplegable «Detalle por muestra» en
TAT-TOTAL y PCT-INCIDENCIA, con su botón de exportación.

Vive junto al indicador a propósito: **usa exactamente sus mismos criterios** —igual rango
sobre `ReceivedAtUtc`, iguales exclusiones—, así que el detalle no puede contradecir a la
cifra que explica, que era justamente el defecto anterior. Se carga solo al desplegarlo y
únicamente un indicador a la vez: son listas de pacientes, no conviene traerlas sin que se
pidan ni dejar varias abiertas en pantalla.

### La pantalla pasa a ser un buscador de muestras e informes

En lugar de retirar la ruta, `/estadisticas` (y ahora también `/buscador`) sirve un
buscador que combina **todos los parámetros del estudio a la vez**, con Y lógica: rellenar
dos campos estrecha el resultado.

- Rango de fechas, sobre la fecha de recepción.
- Conclusión diagnóstica y cuerpo del informe.
- Sospecha clínica, facultativo solicitante y servicio de procedencia.
- **Marcador**, buscado tanto en los valores del informe como en el resumen redactado a
  mano: viven en dos sitios distintos y buscar solo en uno perdería la mitad de los
  estudios.
- **Panel realizado**, buscado en los paneles del estudio y en el campo de texto heredado,
  para no perder el histórico antiguo.
- Paciente o nº de muestra, tipo, estado y «solo validados».

Con resultados exportables a CSV. **Sin ningún criterio no busca**: volcar el histórico
entero no es una búsqueda y con miles de estudios sería lento e inútil.

**La búsqueda queda auditada** (M-2). Alcanza contenido clínico e identificadores de
paciente, así que consta quién buscó, con qué criterios y cuántos resultados obtuvo —
nunca lo devuelto.

---

## v2.6.0

### Barra de acciones del editor de informe

Los botones estaban repartidos en dos bloques a distinta altura, y «Validar» vivía en su
propio apartado **por encima** de los intermedios: la acción final quedaba arriba y el
conjunto se leía al revés del flujo real de trabajo.

Ahora van en **una sola fila**, en el orden en que se usan: Previsualizar PDF · PDF · ODT ·
Guardar Informe │ Validar informe. La barra queda **fija al pie**, porque el formulario es
largo y antes había que bajar hasta el final para guardar o validar.

**«Validar» va tras un divisor y en sólido**, no como quinto botón de la serie. Es
irreversible sin una reapertura documentada, firma a nombre del facultativo y cierra la
muestra, mientras que los demás son rutinarios y repetibles: pegado a «Guardar» —que se
pulsa decenas de veces al día— invitaría al clic equivocado en la única acción con peso
legal. Una vez validado, ese hueco muestra el estado en lugar del botón.

El apartado «Validación» se queda como **registro**: estado, quién validó y cuándo, y el
formulario de reapertura, que necesita el campo de motivo y no cabe en una barra.

### «Volver» ya no pierde el trabajo en silencio

Salía del editor con `NavigateTo` sin comprobar nada: quien redactara una conclusión y
pulsara «Volver» la perdía sin aviso alguno. Ahora, si hay cambios pendientes, ofrece
guardarlos antes de salir; si el guardado falla, no sale.

Los cambios se detectan comparando una **huella del estado editable** en vez de marcar
«sucio» desde cada control: son más de veinticinco campos repartidos por el formulario y
bastaría olvidar uno para que el aviso no saltara. La huella se toma al terminar la carga
—después del autorrelleno de «Paneles empleados», que si no contaría como edición del
usuario— y se renueva tras cada guardado.

**Sin indicador de «guardado/sin guardar»**, deliberadamente: previsualizar, PDF, ODT y
validar ya guardan antes de ejecutarse, así que el indicador pasaría casi todo el tiempo
en «guardado» y no aportaría información. El valor real estaba en avisar al salir, que es
lo que se ha implementado.

---

## v2.5.0

### Corregido: el informe podía declarar menos paneles de los empleados

Detectado sobre una muestra con cuatro paneles leídos (Mieloma, CD34, LNH y Leucemia
Aguda): el apartado «PANELES EMPLEADOS» solo listaba dos, mientras el pie declaraba las
cuatro versiones de panel. Un informe que se contradice consigo mismo en trazabilidad.

**Causa.** «Paneles empleados» se rellena automáticamente **solo si está vacío**. Se genera
al abrir el editor por primera vez y se guarda; si después se leen más tubos, el
auto-relleno ya no se ejecuta y el listado guardado se queda corto. El pie de versiones,
en cambio, se calculaba en vivo al generar el PDF, y por eso sí reflejaba los cuatro.

**No se corrige sobrescribiendo el campo**, que es editable y cuyo contenido final es
responsabilidad del facultativo: regenerarlo en cada carga borraría sus ediciones. En su
lugar, el editor compara lo guardado con los tubos realmente leídos y, si hay desfase,
**lo avisa y ofrece un botón para actualizar el listado**. La discrepancia se hace visible
en vez de resolverse en silencio o emitirse tal cual.

**Además, la línea «Versión de panel» solo declara ya los paneles con algún tubo leído.**
Antes incluía todos los paneles de la muestra, de modo que un panel solicitado y nunca
leído aparecía igualmente en la trazabilidad del informe. Ahora concuerda con el listado
de paneles empleados.

Esto obligó a incluir los tubos en la consulta del informe (PDF y ODT): traía
`Panels → PanelVersion` pero no `Panels → Tubes`, así que filtrar por «tiene algún tubo
leído» habría dejado la colección vacía y **eliminado la línea de versión sin dar ningún
error**. Cubierto con dos pruebas nuevas.

---

## v2.4.0

### Ventana de lectura de tubos («Paneles de Estudio»)

Es la pantalla donde el técnico marca qué tubos ha leído en el citómetro y registra las
incidencias de adquisición. Revisada en legibilidad, en flujo de trabajo y en seguridad
del dato.

**Legibilidad.** La ventana pasa de 600 a 900 px: llevaba una tabla de cuatro columnas
con listas de marcadores y firma de lectura, y todo salía comprimido. La firma de quién
leyó el tubo estaba **apilada debajo del interruptor**, lo que obligaba a un texto de 9 px
para el nombre y 8 px para la fecha — ilegible justo donde hay que poder comprobar quién
registró la lectura. Ahora va **a la derecha del interruptor**, en línea, a 11,5 px. Mismo
tratamiento para el distintivo de incidencia.

**Progreso por panel.** La columna mostraba un recuento estático («1 tubo(s)»). Ahora
indica el avance real —«3/4 leídos»— con color según el estado: gris sin empezar, ámbar a
medias, verde completo. El técnico ve de un vistazo qué panel le queda pendiente.

**«Marcar todos».** Un panel se lee en una sola sesión de citómetro, pero marcar sus tubos
exigía un clic por tubo. Se añade un botón que marca de una vez los pendientes del panel.
Solo suma lecturas: **desmarcar sigue siendo tubo a tubo**, porque borra la firma de quién
lo leyó y no debe ocurrir por descuido.

**Confirmación al eliminar un panel con lecturas.** Eliminar un panel arrastra sus tubos y
con ellos el registro de lectura —quién, cuándo, incidencias—, y bastaba un clic sin aviso
alguno. Ahora, si el panel tiene alguna lectura o incidencia registrada, se pide
confirmación explícita. Si no tiene ninguna, se elimina directamente y no se molesta al
usuario.

**Estado de la incidencia visible.** El botón de incidencia se veía igual hubiera
incidencia o no; había que leer el tooltip. Con incidencia registrada pasa a icono
relleno y fondo ámbar: deja de ser una acción disponible para ser un estado del tubo.

---

## v2.3.0

### La Bandeja Técnica abre con una ventana de 3 meses

Hasta ahora la bandeja cargaba **el histórico completo** al abrirse, sin paginación y con
nueve `Include` anidados (paneles → tubos → usuario que leyó, informe → firmantes →
usuario, incidencias → motivo). El coste crece en línea recta con el número de muestras:
**0,43 ms por muestra**, medido sembrando bases de 1.000 a 40.000 muestras con 5 tubos
cada una.

| Muestras | Bandeja | Panel de control | Indicador TAT | Búsqueda por NHC |
|---:|---:|---:|---:|---:|
| 1.000 | 0,4 s | 2 ms | 2 ms | 45 ms |
| 5.000 | 2,1 s | 4 ms | 12 ms | 258 ms |
| 20.000 | 8,9 s | 134 ms | 116 ms | 1,1 s |
| 40.000 | 17,3 s | 35 ms | 136 ms | 2,2 s |

Con 4.000-5.000 muestras al año, la espera se notaba ya el segundo año y era inasumible
hacia el octavo. **La base de datos no era el cuello de botella** —el panel de control
resuelve en 35 ms sobre 40.000 muestras y los indicadores sobre todo el histórico en
136 ms—, sino la materialización de decenas de miles de grafos de entidades para pintar
una tabla.

La bandeja abre ahora con los **últimos 3 meses**. El histórico se consulta ajustando las
fechas o con el botón «Ver todo el histórico».

**El alcance de la lista va siempre visible**, y esto no es cosmético: una bandeja que
muestra un subconjunto sin decirlo llevaría a dar por inexistente una muestra que sí está
registrada. Por la misma razón, **escribir en la caja de búsqueda consulta siempre el
histórico completo**, aunque la ventana esté activa — una búsqueda que oculta
coincidencias en silencio es peligrosa en un sistema clínico. Si el usuario fija fechas a
mano, mandan las suyas. «Limpiar filtros» devuelve a la vista de tres meses, no al
histórico entero.

### Índice en `Sample.ReceivedAtUtc`

Los doce indicadores de calidad acotan por `ReceivedAtUtc` (`FilteredReceivedQuery`), no
por `ReceptionDate`. Solo esta última estaba indexada, así que cada indicador recorría la
tabla completa. Son columnas distintas —fecha de negocio frente a marca de proceso— y
necesitan índices distintos.

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
