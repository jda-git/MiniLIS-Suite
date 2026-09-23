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

## v4.1.0

Versión **MENOR**: no cambia el informe emitido ni obliga a revalidar. Añade un permiso
nuevo, que entra con su reparto de fábrica sin tocar lo ya configurado.

### Alícuotas almacenadas: lo cerrado queda cerrado

Una alícuota **agotada, eliminada o cedida** ya no está en el congelador (o ya no es
nuestra). Hasta ahora la pantalla seguía ofreciendo registrar una descongelación, un
traslado o una segunda eliminación sobre ella, lo que permitía anotar movimientos de un
tubo que no existe.

- La ventana de la alícuota ya no ofrece ninguna acción cuando está cerrada: explica desde
  cuándo y por qué, y muestra solo su histórico.
- **El servidor lo comprueba igual**, no solo la pantalla: cualquier evento sobre una
  alícuota cerrada se rechaza con el motivo.
- En el listado, el botón de registrar evento pasa a ser **Ver histórico**; el estado se ve
  como etiqueta y la fecha de caducidad se apaga (lo que no existe no caduca). La columna de
  alícuotas cuenta las que **quedan disponibles**.
- En el mapa de congeladores, las alícuotas cerradas salen tachadas: esa posición está libre.
- Eliminar sin motivo se rechaza también en el servidor, no solo en el formulario.

**Si se cerró por error**, un facultativo puede **reabrir la alícuota** dejando constancia:
exige un motivo y el cierre equivocado **no se borra** — la corrección se añade encima, en
el histórico (misma regla que las anulaciones de tubo de la v4.0). Lo reparte el permiso
nuevo *Reabrir una alícuota cerrada por error* (Configuración → Permisos), de fábrica solo
para el Facultativo.

### Correcciones

- La ventana de eventos de una alícuota se abría **al final de la página y con el título
  ilegible** (blanco sobre blanco): sus estilos vivían en la pantalla de muestras, así que en
  Excedente no se aplicaban y el contenido se pintaba como texto corriente. Ahora son estilos
  compartidos y la ventana se abre centrada, sin tener que bajar la página.

## v4.0.0

Versión **MAYOR**: cambia cómo figura la versión de panel en los informes nuevos y exige
revalidar los perfiles de hoja de trabajo que usen `{PanelVersion}` o `{PanelDisplayCode}`
(ver más abajo). Los estudios e informes anteriores no cambian.

### Versiones de panel homogéneas con el QMS

**Identificación.** El código estable del panel se separa de la versión local, que pasa a
tener dos componentes enteros, mayor y menor: `LEUCEMIA-AGUDA · v1.0`. El número lo elige
quien prepara la versión (un cambio menor puede ser 1.1; uno mayor, 2.0), siempre posterior a
todas las existentes: una versión no se reutiliza ni se intercala.

**Correspondencia con el QMS.** Cada versión registra la revisión de la ficha maestra de
paneles (`ANX-CIT-REA-003-01`, configurable con `Qms:PanelMasterSheetCode`), el PNT técnico,
la referencia externa (p. ej. EuroFlow) separada de la versión local, la evaluación del
cambio, y quién aprobó, cuándo, y desde cuándo y hasta cuándo estuvo en vigor.

**Composición de los tubos.** Cada tubo lleva su **fórmula y revisión** del QMS. La
combinación de anticuerpos (`16/13/34/…`) se mantiene como resumen.

**Circuito.** Borrador → En revisión → Aprobada → Vigente → Retirada, en la nueva pantalla
**Versiones de paneles** (menú lateral; Administrador y Facultativo).

- Preparan los borradores el Administrador y el Facultativo. Aprueba, devuelve a borrador y
  retira el Facultativo (se comprueba en el servidor, no solo en la pantalla).
- Para enviar a revisión hacen falta la revisión de la ficha maestra y la fórmula y revisión
  de cada tubo. Si sube la versión mayor, también la evaluación del cambio.
- Al aprobar se confirma que la composición coincide con la ficha maestra y se programa la
  **entrada en vigor** (día y hora). Llegado ese momento la versión entra en vigor sola y la
  anterior se retira.
- Fuera de borrador una versión no se modifica. Las notas que no explican el cambio («nada»,
  «cambio», «-»…) se rechazan; en versiones ya aprobadas se añade una **aclaración** aparte,
  sin reescribir la original.

**Trazabilidad de los tubos del estudio.**

- Cada tubo de un estudio queda enlazado a su definición exacta (con su fórmula) y con el
  nombre de su fichero FCS fijado al crearlo.
- **Una lectura registrada queda bloqueada**: el interruptor ya no se puede desmarcar.
- **Un panel con tubos leídos no se puede quitar** de la muestra.
- Si algo se registró por error, un **facultativo o administrador lo anula** (tubo o panel completo) con
  justificación obligatoria y, si procede, el código de la **no conformidad** del QMS (la
  pantalla lo recomienda). La lectura original se conserva; lo anulado sale del informe.
- **Un tubo de un panel solicitado que no se lee se tiene que justificar**. Mientras haya
  tubos sin leer ni justificar, el informe no se puede validar (el editor los muestra arriba).

**Informes validados.** El texto «Versión de panel» se congela al validar: reimprimir un
informe emitido da siempre el mismo texto, aunque luego cambien el formato o los datos.

### Permisos por rol, configurables

Nueva pestaña **Configuración → Permisos**: una tabla de funciones por rol con casillas. Lo
que se marca ahí es lo que comprueban las pantallas, los botones, las descargas y los
servicios; en el código ya no hay ningún rol escrito. Cambiarlo afecta también a las sesiones
abiertas.

- Un permiso que no exista aún en lo guardado toma su valor de fábrica, así que al actualizar
  MiniLIS los permisos nuevos llegan con el reparto previsto y no sin nadie.
- El Administrador conserva siempre **Permisos** y **Usuarios** (casillas fijas): sin ellos
  nadie podría volver a repartir permisos.
- Quitar una marca cierra también la vía directa (URL o descarga), no solo el botón.
- La pantalla de Permisos la abren de fábrica Administrador y Facultativo.

Valores de fábrica:


| Función | Admin | Facultativo | Técnico |
|---|:-:|:-:|:-:|
| Trabajo diario (bandeja, registro, ficha de muestra, etiquetas, hoja del citómetro) | ✅ | ✅ | ✅ |
| Registro diferido; actualizar datos de paciente al registrar | ✅ | ✅ | ❌ |
| Marcar tubo leído, incidencia «con salvedad», justificar tubo no realizado | ✅ | ✅ | ✅ |
| Desmarcar un tubo leído (con motivo, queda en la auditoría) | ❌ | ✅ | ❌ |
| Incidencia que anula o repite un tubo leído; anular tubo o panel | ✅ | ✅ | ❌ |
| Editor de informes: abrir, guardar, PDF/ODT | ✅ | ✅ | ❌ |
| Validar y reabrir informes | ❌ | ✅ | ❌ |
| Buscador, Notificaciones, Excedente, Indicadores | ✅ | ✅ | ❌ |
| Exportar CSV con identificadores del paciente | ✅ | ✅ con justificación | ❌ |
| Versiones de paneles: preparar borradores | ✅ | ✅ | ❌ |
| Versiones de paneles: aprobar, devolver, retirar | ❌ | ✅ | ❌ |
| Configuración, copia de configuración, Usuarios, Auditoría, Backup, Contingencia, Evidencias | ✅ | ✅ | ❌ |

Además:

- **Un informe validado es de solo lectura**: el editor lo muestra bloqueado y el servidor
  rechaza guardarlo. Para cambiarlo, un facultativo lo reabre con motivo.
- **Importar configuración nunca pone en vigor una versión de panel**: entra como borrador y
  la aprueba un facultativo.
- El **menú lateral** muestra solo lo que cada rol puede abrir (el técnico, el trabajo diario).
- Cada usuario sigue teniendo un único rol.

### Migración (automática al arrancar)

- `vNN` pasa a `vN.0` (`v01` → `v1.0`, `v02` → `v2.0`) y se guarda el código anterior. El
  identificador interno de cada versión y el vínculo de cada estudio con su versión no cambian.
- Los informes ya validados conservan su texto de versiones en el formato anterior
  (`LEUCEMIA-AGUDA-v02`), y los tubos ya registrados, su nombre de fichero FCS (`…-v02.fcs`).
- Las versiones migradas quedan con la ficha maestra, las fórmulas y la aprobación **sin
  rellenar** («no registrada en MiniLIS»): no se inventan. Hay que completarlas desde el QMS
  al preparar la siguiente versión de cada panel.
- La copia de configuración sube a la versión 2 del apartado Paneles; los ficheros de
  MiniLIS 3.x se convierten solos al importarlos.

### Revalidar

- **Perfiles de hoja de trabajo** que usen `{PanelVersion}` (ahora `1.0` en vez de `02`) o
  `{PanelDisplayCode}`: revalidarlos frente al citómetro antes de usarlos.
- Los ficheros FCS de los estudios nuevos se llaman `…_LEUCEMIA-AGUDA-v1-0.fcs`.

### Otros cambios desde 3.5.0

- **Estudios previos → Texto adicional**: nueva casilla para incluir en el informe el texto
  adicional de los estudios previos seleccionados, entre los marcadores y la conclusión.
- **Instalador de Windows** (`installer\Build-Installer.ps1`): comprueba requisitos, lleva su
  propio .NET 9, instala MiniLIS como servicio de Windows con HTTPS y deja los datos fuera de
  la carpeta del programa. Guía: `docs/INSTALACION.md`.

---

## v3.5.0

### Copia de configuración a fichero

Nueva pestaña **Configuración → Copia config.** para guardar la configuración del
laboratorio en un fichero (`.minilis-config.json`) y cargarla en otra instalación, o
restaurarla. Evita configurar punto a punto un servidor recién instalado cuando ya existe
una configuración funcional, y sirve como copia de seguridad de la configuración.

**Qué incluye:** marcadores, plantillas de informe, paneles (versión vigente y borradores,
con sus tubos y notas), umbrales de los indicadores de calidad, etiquetas, perfiles de hoja
de trabajo, motivos de rechazo, incidencias de lectura, citómetros, intensidades y plazos de
retención, cabecera del informe (con el logo) y facultativos firmantes. Las rutas propias
de cada equipo (carpeta FCS, carpeta de copias) se exportan marcadas como *locales* y al
importar no se aplican salvo que se marquen.

**Qué no incluye nunca:** pacientes, muestras, informes, usuarios, auditoría ni los
contadores de numeración (importarlos podría duplicar números de muestra). Los ajustes se
escriben por lista blanca de claves: un fichero no puede escribir ninguna otra.

**Compatibilidad entre versiones.** Cada apartado lleva su propia versión de esquema. Al
cargar un fichero, cada apartado sale como *Compatible*, *Compatible (convertido)* (de una
versión anterior, que se convierte), *Parcial* (trae campos que esta versión no conoce y se
ignoran, con su lista), *No aplicable* (de una versión más nueva de MiniLIS, o con datos no
válidos) o *Desconocido* (apartado que esta versión no tiene). Los apartados no aplicables
se saltan; el resto se puede aplicar.

**Seguridad de la carga:**

1. Se comprueba la huella SHA-256 del fichero: uno dañado o editado a mano no se aplica.
2. Antes de aplicar se muestra qué cambiaría en cada apartado (nuevo, modificado — con el
   valor anterior y el nuevo —, conflicto, desactivado, sin cambios), sin guardar nada.
3. **Copia previa obligatoria** de la configuración actual: se guarda en el servidor
   (carpeta `config-backups`, configurable con `ConfigTransfer:BackupDirectory`), se vuelve
   a leer para verificarla y se descarga. El servicio rechaza la importación sin ella.
4. Todo se aplica en una única transacción: si falla un apartado, no se aplica ninguno.
5. Exportación, copia previa e importación quedan en la auditoría, y se puede descargar un
   informe de la importación.

**Reglas de aplicación:**

- Los elementos se identifican por nombre o código, nunca por el Id interno.
- *Fusionar* (por defecto) añade y actualiza; *Reemplazar* además **desactiva** lo que no
  viene en el fichero. Nunca se borra nada que puedan referenciar los informes emitidos.
- Paneles (M-4): una versión publicada nunca se modifica. Si el fichero trae otra versión
  vigente, en un panel que ya tiene estudios se crea como **borrador** para revisarlo y
  publicarlo a mano (se marca como *Conflicto*); en un panel sin estudios —el caso de una
  instalación nueva, con los paneles de relleno que siembra MiniLIS— se publica como versión
  nueva y la anterior queda retirada.
- Un perfil de hoja de trabajo nuevo o modificado queda **sin validar** frente al
  instrumento: la validación es de cada instalación.
- De los indicadores de calidad solo se importan los umbrales; un código que esta versión
  no conoce se omite.

«Restaurar», en *Copias en el servidor*, carga una copia previa en el mismo flujo, en modo
Reemplazar.

Los ficheros (configuración, copia previa, informe de importación) se guardan con el
diálogo **«Guardar como»** del navegador, para elegir carpeta y nombre, en Chrome y Edge; en
los demás navegadores van a la carpeta de descargas. La pantalla indica siempre el resultado.

### Correcciones

- **Configuración:** los botones deshabilitados ahora se ven atenuados. Antes parecían
  activos y daba la impresión de que al pulsarlos no pasaba nada.
- **Ficha de muestra → Nueva alícuota de excedente:** el icono del calendario del campo
  *Caducidad* quedaba cortado. Los campos de la ficha y de *Nueva muestra* ya no se salen de
  su recuadro.

---

## v3.4.0

### Dos formatos de etiqueta de muestra, a elegir en Configuración

**Configuración → Etiquetas** permite elegir el formato de la etiqueta de **muestra**, con
una vista previa de ambos. Las etiquetas de tubo y de alícuota no cambian.

**Tipo 1** (el de siempre, reorganizado): un código de barras con el nº de muestra; la
fecha pasa a la misma línea que el NHC, a su derecha, y en la línea que ocupaba se ponen
el NASI y el Nº LAB.

```
[código de barras del nº de muestra]
26-00010                           MO
NHC: 2444337         21/08/2026 10:09
NASI: 172846  Nº LAB: 12345678
```

**Tipo 2**: dos códigos de barras de media altura, el del nº de muestra y el del Nº LAB,
con sus propios tamaños de fuente y altura de código, configurables aparte.

```
[código de barras del nº de muestra]
26-00010                           MO
NHC: 2444337         21/08/2026 10:09
Nº LAB: 12345678
[código de barras del Nº LAB]
```

**El Nº LAB puede venir vacío o con caracteres no codificables**, porque lo teclea el
usuario, y el codificador Code 128B lanza una excepción en ambos casos. Sin protección,
una sola muestra así habría tumbado **la pantalla de impresión entera**, no solo su
etiqueta. Ya había una en la base de pruebas: la 26-00009, sin Nº LAB. Ahora se marca en
la etiqueta —«SIN Nº LAB» o «Nº LAB NO CODIFICABLE», con el número en texto— y el resto se
imprime con normalidad.

**Un solo generador de etiquetas.** El marcado se ha sacado de la pantalla de impresión a
`MiniLIS.Web/Services/LabelRenderer.cs`, y sus estilos a `app.css`, para que la vista
previa de Configuración sea literalmente lo que se imprime. Mantener dos plantillas
separadas fue la causa de varios fallos de impresión anteriores en esa misma pantalla.

La vista previa **avisa si el contenido no cabe** en el alto de la etiqueta con las
medidas elegidas, porque la etiqueta recorta lo que sobra sin dar ningún error.

Una configuración guardada antes de esta versión sigue funcionando sin cambios: se abre
en Tipo 1. Quince pruebas nuevas; entre ellas, que ningún Nº LAB vacío o no codificable
pueda romper la impresión.

---

## v3.3.4

### La pantalla de acceso no avisaba del bloqueo de cuenta

Tras **5 intentos fallidos** la cuenta queda bloqueada **15 minutos**, y durante ese tiempo
se rechaza incluso la contraseña correcta. Pero el mensaje era el mismo que el de una
contraseña errónea, así que quien quedaba bloqueado seguía probando la clave buena,
convencido de que estaba mal. Así ocurrió: tras cuatro fallos y el bloqueo, hubo seis
intentos más con la cuenta ya bloqueada.

El mensaje sigue siendo **idéntico para las cuatro causas** de fallo —contraseña errónea,
usuario inexistente, usuario inactivo y cuenta bloqueada—, porque distinguirlas permitiría
averiguar qué cuentas existen. Lo que cambia es que ahora menciona el bloqueo en todos los
casos:

> CREDENCIALES NO VÁLIDAS. TRAS 5 INTENTOS FALLIDOS EL ACCESO QUEDA BLOQUEADO 15 MINUTOS.

Así avisa sin revelar nada. Las cifras se leen de la configuración de Identity, no están
escritas en el texto, para que no se desfasen si se cambian.

De paso se corrige una errata: decía «CREDENTILES».

---

## v3.3.3

### Unificación de las dos líneas de trabajo paralelas

Las versiones 3.3.0 (aviso de incidencias de adquisición) y 3.3.1–3.3.2 (confirmación de
guardados en Configuración; icono de ordenación de la Bandeja) se hicieron en dos sesiones
de trabajo distintas sobre el mismo repositorio. La historia quedó lineal y sin conflictos
—no compartían ningún fichero de código—, pero revisando la unión aparecieron dos cosas.

**La pestaña Citómetros quedó fuera del aviso común.** La v3.3.1 unificó la confirmación
de guardado de Configuración en un único aviso, pero no conocía esa pestaña. Ahora usa el
mismo mecanismo: confirmación que se oculta sola, y aviso fijo si falla el sistema. El
error de nombre duplicado se mantiene junto al formulario, porque es un problema del campo
con el formulario aún abierto.

**Editar un citómetro existente nunca había funcionado.** Fallo de la v3.1.0, que se
verificó creando un citómetro pero no editándolo; salió a la luz al comprobar lo anterior.
El formulario edita una copia —para que «Cancelar» no deje la fila a medias—, y el
contexto de datos, que en Blazor Server dura todo el circuito, ya rastreaba el original:
guardar la copia lanzaba *«cannot be tracked»*. Misma causa raíz que la v2.7.3.

Y había un segundo problema detrás del primero: aunque no hubiera fallado, la copia no
lleva los campos de auditoría, así que **cada edición habría reescrito la fecha y el autor
del alta** del equipo. Ahora se copian solo los campos editables sobre la instancia
rastreada. Dos pruebas nuevas reproducen ambos fallos: las dos fallaban antes del arreglo.

---

## v3.3.2

### Bandeja Técnica: retirado un icono de ordenación que no hacía nada

La columna «Código» mostraba un icono de ordenar (⇅) que no tenía ninguna acción: era
puramente decorativo e invitaba a pulsarlo sin resultado. Se retira. La bandeja ya se
presenta ordenada por fecha, y para localizar una muestra concreta están la búsqueda por
código, NHC o nombre y los filtros de estado, tipo y fechas.

---

## v3.3.1

### Configuración: los guardados ahora se confirman

En **Etiquetas**, al cambiar el tamaño de letra o de etiqueta y pulsar «Guardar», no
aparecía ningún aviso: los cambios sí se guardaban, pero parecía que el botón no había
hecho nada. Lo mismo pasaba en **Hoja de trabajo**, **carpeta de ficheros FCS** e
**Intensidades**. Solo Cabecera y Firmas mostraban confirmación, porque el aviso estaba
copiado dentro de esas dos pestañas.

Ahora el aviso es común a toda la página de Configuración:

- Al guardar aparece «… guardada», que se oculta solo a los 6 segundos.
- Si el guardado falla, el error **se queda hasta que se cierra**. Si se ocultara solo,
  podría pasar desapercibido que el cambio no se guardó. Antes, estas cuatro pestañas ni
  siquiera capturaban el error.
- Al cambiar de pestaña, el aviso se borra, para que la confirmación de una pestaña no
  aparezca en otra.

---

## v3.3.0

### El editor avisa de las incidencias de adquisición

El técnico registra en la lectura de tubos las incidencias del citómetro —atasco, muestra
insuficiente, error de adquisición— con su resolución. **Ese dato no llegaba al
facultativo**: podía validar un informe sin enterarse de que un tubo había dado problemas.

Ahora el editor de informe muestra un aviso con cada tubo afectado, su motivo, la
resolución y quién y cuándo la registró:

```
2 incidencias de adquisición registradas en este estudio
  Leucemia Aguda — T3   Muestra insuficiente    SE USA CON SALVEDAD
  Leucemia Aguda — T4   Error de adquisición    SE REPITE
```

**No aparece en el informe entregado al clínico**, y así se indica expresamente en el
propio aviso: es información para que el facultativo decida si procede un comentario en el
texto o en las conclusiones.

Va **arriba del todo**, antes de Identificación, y no junto a los paneles: el objetivo es
que no se pueda validar sin haberlo visto, y el apartado de paneles queda muy abajo en un
formulario largo. La resolución se colorea aparte —«anula la lectura» en rojo frente a las
demás— porque no todas tienen la misma repercusión.

Se listan las incidencias de **cualquier** tubo, se haya usado su lectura o no: una que
anuló la lectura también es relevante para interpretar el resultado.

Dos pruebas fijan las dos mitades del requisito: que el dato llega al editor resuelto
(motivo, resolución y notas) y que **no** se cuela en el documento. La segunda detectaría
una fuga si alguien tocara el renderizado del informe.

---

## v3.2.0

### El buscador combina varios términos dentro de un mismo campo

Antes cada campo hacía **una sola búsqueda literal**: escribir `CD34 - CD117 +` en Marcador
buscaba esa cadena entera y no encontraba nada. La combinación con Y existía solo *entre*
campos, no *dentro* de uno.

Ahora, en cualquier campo de texto:

| Se escribe | Significa |
|---|---|
| `CD34 - & CD117 +` | **los dos** |
| `CD34 - \| CD117 +` | **cualquiera** de los dos |
| `CD34 -` | el texto entero y literal, como siempre |

**Por qué `&` y `\|` y no otros símbolos.** Las cadenas reales de marcadores son del estilo
`CD117 -/+d, HLA-DR -/+, MPO +d/+`: «+», «−», «/» y el espacio **forman parte del dato** y
no pueden separar términos. Tampoco valen las palabras «y»/«o», que aparecen a cada línea
en el cuerpo del informe. Se comprobó sobre los datos que ni `&` ni `\|` aparecen en ningún
campo buscable.

Sin operador, el comportamiento es idéntico al anterior: quien no conozca la sintaxis no
se ve afectado.

**Mezclar ambos operadores avisa en vez de adivinar.** Resolver `A & B \| C` con una
precedencia implícita daría un resultado que el usuario no espera y no podría detectar;
se muestra un aviso indicando que ese campo no se ha tenido en cuenta. Callarlo sería peor:
el resultado parecería completo sin serlo.

Aplica a conclusión diagnóstica, cuerpo del informe, sospecha clínica, facultativo,
servicio, marcador, panel y paciente.

---

## v3.1.0

> **Altera el informe emitido** cuando se selecciona un citómetro. Sin selección, el
> informe sale exactamente igual que antes, así que el despliegue no obliga a revalidar
> nada por sí solo; sí conviene hacerlo antes de empezar a declarar el equipo.

### Citómetro y software empleados en el informe

Nueva pestaña **Configuración → Citómetros**: equipos del laboratorio con su software de
adquisición y de análisis, cada uno con su versión. En el editor de informe se elige con un
desplegable, que muestra los tres datos antes de guardar, y quedan impresos bajo «PANELES
EMPLEADOS»:

```
Citómetro: Navios EX (n/s AN12345)
Software de adquisición: Navios Software v1.3
Software de análisis: Infinicyt v2.0
```

**No es un maestro de equipos.** Sigue vigente el principio F-0 / I.2: la calibración, el
mantenimiento y la validación del equipo viven en el sistema de calidad. Esto es una lista
de selección para no teclear a mano, con un campo «Código QMS» que enlaza cada equipo con
su ficha — mismo patrón que `QmsDocumentRef` en paneles e indicadores.

**El informe guarda una copia congelada del texto, no una referencia al catálogo.** Es la
decisión de diseño importante y es la contraria a la de las notas de acreditación de los
tubos (v3.0.0), por un motivo concreto: una versión de panel publicada es **inmutable**
(M-4), pero el catálogo de citómetros **se edita en cuanto se actualiza un software**. Sin
congelar, un informe de hace dos años declararía retroactivamente la versión nueva. Hay una
prueba dedicada a ese escenario.

Si no se elige citómetro, el apartado no aparece: un informe que no lo declara es preferible
a uno que declara un hueco, y así los estudios antiguos no se ensucian.

---

## v3.0.1

### Maquetación de la cabecera del informe

**PDF.** «TIPO DE MUESTRA: Sangre periférica» partía en dos líneas. La fila repartía el
ancho 4/3/3, pero el nº de muestra tiene ancho fijo (`AA-NNNNN`) y le sobraba sitio,
mientras que al tipo —el único de los tres que varía— le faltaba. Pasa a 3/5/2,5: el tipo
se desplaza a la izquierda y cabe en una línea incluso con el valor más largo del catálogo,
«Líquido cefalorraquídeo». De paso, «Nº PETICIÓN» queda casi alineado con «NASI» de la
fila superior.

**ODT.** La cabecera salía desplazada a la derecha de la hoja porque **el documento no
definía su página**: sin `page-layout`, cada programa aplicaba sus márgenes por defecto, y
con los de Word (2,54 cm) el área de texto queda en 15,9 cm — menos que los 17 cm que
sumaban las columnas de la tabla, que se desbordaba.

Ahora el ODT fija A4 con márgenes de 2 cm (área de 17 cm) y la tabla mide 16,8 cm anclada
a la izquierda, de modo que entra con holgura y se ve igual en Word que en LibreOffice.
Dos pruebas nuevas comprueban que la página está definida y que ambos XML siguen siendo
válidos: uno mal formado abriría el documento roto sin dar ningún error.

---

## v3.0.0

> **Salto MAYOR: altera el informe emitido al clínico.** Según el criterio de este proyecto
> requiere validación documentada antes de su uso (ISO 15189, cl. 7.6).

### El informe declara el alcance de acreditación de cada tubo

ISO 15189 exige poder identificar en el informe qué pruebas están dentro del alcance de la
acreditación. El apartado «PANELES EMPLEADOS» pasa a mostrar, **a la derecha de cada
tubo**, la nota escrita en la definición de su panel:

```
CD34 — T1: CD34/45/7add        Acreditado ISO 15189
CD34 — T2: CD3/CD45/7add       Prueba fuera del alcance de la acreditación
```

El texto se traslada **tal cual, sin interpretarlo**: MiniLIS documenta lo que el
laboratorio escribió en la definición del panel, no decide qué está acreditado. Esa
frontera es la misma que mantiene la aplicación fuera del alcance de producto sanitario.

**El apartado se genera ahora desde los datos, no desde el campo de texto libre.** Era la
parte importante de la decisión: `PanelsUsedText` es editable, solo se autorrellena la
primera vez y ya se quedó obsoleto una vez (v2.5.0). Una declaración de alcance de
acreditación no puede depender de que nadie borre una línea. Los estudios antiguos o los
informes redactados a mano siguen mostrando su texto libre, para no vaciarles el apartado.

**La nota se lee de la versión de panel, sin duplicarla en el estudio.** `SampleTube`
congela la lista de marcadores pero no las notas — y no hace falta congelarlas: una versión
publicada es **inmutable** (M-4), así que la nota de `CD34-v02/T1` no puede cambiar nunca.
Una sola fuente de verdad, sin migración ni datos repetidos.

Solo se listan los tubos **realmente leídos**: incluir los de un panel no leído sería
afirmar un alcance de acreditación sobre una prueba que no se hizo.

---

## v2.7.3

### El alta de muestra ofrecía la versión RETIRADA de un panel

Tras publicar `CD34-v02` y quedar `CD34-v01` como retirada, la pantalla de registro seguía
ofreciendo la v01 — con sus tubos antiguos. **Se habrían registrado estudios contra una
versión de panel dada de baja**, y el informe habría declarado esa versión en su
trazabilidad.

**No ocurría siempre**, y eso es lo que lo hacía difícil de ver: solo después de haber
abierto la pantalla de versiones del panel.

La causa es la suma de dos cosas. Un `Include` filtrado **no es fiable cuando el contexto
ya sigue otras entidades de esa navegación**: la corrección de navegaciones de EF Core
vuelve a enganchar las que están en el rastreador, aunque la consulta SQL las haya
excluido correctamente. Y `ApplicationDbContext` está registrado como *Scoped*, que en
Blazor Server **dura todo el circuito** —la sesión entera, no una petición—. Así que
bastaba con visitar antes la pantalla de versiones, que carga vigente y retirada, para que
la retirada quedara rastreada y se colara después en el alta.

Corregido con `AsNoTracking()` en la consulta —donde no es una optimización sino la
condición para que el filtro se respete— y repitiendo el predicado al resolver la versión,
para no depender del estado del rastreador.

**El mismo patrón afectaba a la impresión de etiquetas.** `GetSamplesByIdsAsync` filtra los
paneles a los solicitados, y la Bandeja Técnica carga todos en el mismo contexto: se
habrían impreso etiquetas de tubos de paneles que nadie pidió. Blindado igual.

Cubierto con dos pruebas: una con el contexto limpio y otra que reproduce la secuencia
real —abrir versiones y luego registrar—, que es la que fallaba.

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
