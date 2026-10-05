# MiniLIS Suite — Características, alcance e indicadores

**Documento de referencia para el cruce con el QMS (Flow Doc) y con los PNT del laboratorio.**

| | |
|---|---|
| **Versión de MiniLIS descrita** | 4.4.0 |
| **Fecha** | 26/09/2026 |
| **Estado de la aplicación** | En desarrollo activo. Funciona **solo con datos de prueba**; no contiene ni ha contenido datos de pacientes reales. |
| **Origen de los datos de este documento** | El código fuente de la versión indicada. Cada afirmación es verificable en el fichero citado. |

> **Cómo usar este documento.** Los apartados 1 a 7 describen lo que MiniLIS hace hoy. El
> apartado 8 es la lista de puntos donde la descripción del PNT o del QMS **tiene que coincidir
> literalmente** con el comportamiento del programa: son los que conviene revisar uno a uno.

---

## 1. Qué es MiniLIS y dónde está su frontera

MiniLIS es un sistema de información de laboratorio (LIS) para el proceso de citometría de
flujo: registro de la muestra, recepción, adquisición, redacción y validación del informe, y
trazabilidad del ciclo de vida de cada estudio.

**No es un producto sanitario** conforme al Reglamento (UE) 2017/745: no interviene en el
diagnóstico ni realiza cálculo clínico automatizado. Trata datos de salud de categoría especial
(RGPD art. 9), y el control de acceso y la auditoría responden a esa exigencia.

### 1.1 Lo que MiniLIS deliberadamente NO hace

Esto es lo primero que conviene cruzar, porque define qué registros siguen viviendo **solo** en
el QMS:

| Fuera del alcance de MiniLIS | Dónde vive |
|---|---|
| Gestión documental y PNT | QMS Flow Doc |
| Personal, competencias y autorizaciones | QMS |
| Equipos: mantenimiento, calibración, verificación | QMS (MiniLIS solo guarda el código del equipo) |
| Reactivos, lotes y caducidades | QMS |
| No conformidades formales y acciones correctivas | QMS (MiniLIS solo guarda la **referencia** al código) |
| Evaluación externa de la calidad (EQA) | QMS |
| Control ambiental | QMS |

**Regla de diseño aplicada en todo el sistema:** MiniLIS **no duplica maestros del QMS**.
Almacena únicamente el código de referencia, validado por formato
(`MiniLIS.Domain/Common/QmsReference.cs`). Si el PNT dice que un registro «se lleva en el LIS»
y aparece en esta tabla, hay discordancia.

### 1.2 El punto donde la frontera se vigila

La importación de resultados desde XML de Infinicyt (Editor de informe → «Importar resultados
Infinicyt») transfiere texto de poblaciones **ya calculado por Infinicyt** para que el
facultativo elija qué insertar. No calcula, deriva, compara ni clasifica ningún valor por su
cuenta. Es el punto más próximo a cruzar la frontera de producto sanitario y el candidato más
probable a recibir peticiones que la crucen.

---

## 2. Módulos y funciones

| Módulo | Ruta | Qué hace |
|---|---|---|
| Panel de inicio | `/` | Resumen de actividad y estados |
| Bandeja técnica | `/muestras` | Listado de muestras, filtros, alta, edición, etiquetas, exportación CSV |
| Lista de trabajo | `/trabajo` | Tablero por fase con semáforo de TAT |
| Hoja del citómetro | `/hoja-trabajo` | Generación del fichero de trabajo para el equipo |
| Editor de informes | `/informes/editar/{id}` | Redacción, marcadores, conclusión, validación |
| Versiones de paneles | `/paneles/versiones` | Circuito de versionado de paneles ligado al QMS |
| Notificaciones | `/notificaciones` | Valores críticos y nuevos diagnósticos pendientes de comunicar |
| Excedente | `/excedente` | Alícuotas criopreservadas, ubicación y eventos |
| Buscador | `/buscador` | Búsqueda de informes por contenido |
| Indicadores | `/indicadores` | Cuadro de indicadores de calidad |
| Auditoría | `/auditoria` | Consulta del registro de auditoría |
| Copias de seguridad | `/backup` | Copias cifradas y verificadas |
| Contingencia | `/contingencia` | Modo de caída del sistema |
| Evidencias | `/evidencias` | Paquete de evidencias para auditoría externa |
| Usuarios | `/usuarios` | Altas, roles, contraseñas |
| Configuración | `/configuracion` | Catálogos, plantillas, etiquetas, cabecera, firmas, permisos |

### 2.1 Ciclo de vida de la muestra

El estado **no se elige en un desplegable**: lo dirige la acción real.

| Estado | Se alcanza cuando |
|---|---|
| Recibida | Se registra el alta |
| En proceso | Se marca la lectura del primer tubo en el citómetro |
| Reportada parcial | Se guarda el primer borrador de informe |
| Finalizada | Se valida el informe |
| Rechazada | Se marca el rechazo en recepción (v4.3: al alta y al editar) |

Ninguna promoción rebaja un estado más avanzado. **El rechazo preanalítico vive en el estado de
recepción**, independiente de esta cadena.

### 2.2 Recepción (cl. 7.2, 7.4)

Tres resultados posibles: **Correcta**, **Con salvedad**, **Rechazada**. En los dos últimos se
exige al menos un motivo del catálogo configurable, y ambos **se trasladan al informe entregado
al clínico**:

- Salvedad → apartado **LIMITACIONES**.
- Rechazo → apartado **MUESTRA RECHAZADA PREANALÍTICAMENTE**, con cada motivo en su línea y,
  si consta, la notificación al peticionario.

Desde la v4.3, una muestra rechazada **se informa y se valida** como cualquier otra, para que
el motivo suba a la historia clínica.

### 2.3 Versionado de paneles ligado al QMS (v4.0)

Circuito: **Borrador → En revisión → Aprobada → Vigente → Retirada**.

Cada versión registra:

- Numeración `mayor.menor` elegida por quien la prepara, siempre posterior a las existentes.
- **Revisión de la ficha maestra** del QMS (por defecto `ANX-CIT-REA-003-01`, configurable con
  la clave `Qms:PanelMasterSheetCode`).
- Referencia externa (p. ej. EuroFlow) separada de la versión local.
- **Fórmula y revisión por tubo**.
- Evaluación del cambio (obligatoria si sube la versión mayor).
- Quién aprobó, cuándo, y desde cuándo y hasta cuándo estuvo en vigor.
- Entrada en vigor programable a día y hora.

Fuera de borrador una versión **no se modifica**: se añaden aclaraciones aparte. Las notas de
cambio que no explican nada («nada», «cambio», «-») se rechazan.

### 2.4 Trazabilidad del tubo (v4.0–v4.1)

- Cada tubo del estudio queda enlazado a su definición exacta y con el nombre de su fichero FCS
  fijado al crearlo.
- **Una lectura registrada queda bloqueada**; desmarcarla exige permiso y motivo.
- **Un panel con tubos leídos no se puede quitar** de la muestra.
- Un tubo no realizado exige justificación.
- La anulación por error exige justificación y, opcionalmente, el código de la no conformidad
  del QMS.

### 2.5 Excedente criopreservado

Trazabilidad **por alícuota individual**: cada vial es una fila con su ubicación, su estado y su
historial de eventos. Estados: Almacenada, Descongelada, Agotada, Eliminada, Cedida. Los tres
últimos **cierran la alícuota**: no admiten más movimientos, y reabrirlas por error exige
permiso y motivo, sin borrar el evento equivocado (v4.1).

---

## 3. Indicadores de calidad (cl. 8.8 / 8.9)

**Éste es el apartado central del cruce con el PNT de indicadores.**

MiniLIS **no almacena valores calculados**: los recalcula a demanda desde los datos primarios.
Eso significa que una cifra de hace un año se recalcula hoy con la misma regla, y que cambiar la
regla cambia el histórico.

**Los umbrales vienen sin definir a propósito.** La unidad debe fijarlos conscientemente: un
umbral por defecto inventado es peor que ninguno.

### 3.1 Catálogo

| Código | Nombre | Definición implementada | Unidad | Dirección |
|---|---|---|---|---|
| `TAT-TOTAL` | TAT total (recepción → validación) | `ValidatedAtUtc − ReceivedAtUtc` | Horas | Menor es mejor |
| `TAT-ADQ` | TAT de adquisición (registro → adquisición) | `AcquiredAtUtc − RegisteredAtUtc` | Horas | Menor es mejor |
| `TAT-ANA` | TAT analítico (adquisición → validación) | `ValidatedAtUtc − AcquiredAtUtc` | Horas | Menor es mejor |
| `PCT-RECHAZO` | % muestras rechazadas | rechazadas / recibidas | % | Menor es mejor |
| `PCT-SALVEDAD` | % aceptadas con salvedad | con salvedad / recibidas | % | Menor es mejor |
| `PCT-INCIDENCIA` | % con incidencia preanalítica | con incidencia / recibidas | % | Menor es mejor |
| `PCT-FUERA-PLAZO` | % informes fuera de objetivo | TAT-TOTAL > objetivo | % | Menor es mejor |
| `PCT-REAPERTURA` | % informes reabiertos tras validar | reabiertos / validados | % | Menor es mejor |
| `ACT-PANEL` | Actividad por panel y versión | recuento | Recuento | Mayor es mejor |
| `ACT-MUESTRA` | Actividad por tipo de muestra | recuento | Recuento | Mayor es mejor |
| `ACT-PETICIONARIO` | Actividad por servicio | recuento | Recuento | Mayor es mejor |

Son **once indicadores activos**. Cada uno admite en Configuración un **objetivo**, un **umbral
de aviso** y un **umbral crítico**, y una **referencia al documento del QMS** que lo define
(`QmsDocumentRef`), hoy **sin rellenar en ninguno**.

### 3.2 Estadístico empleado

Los TAT se presentan como **mediana y P90**, no como media. El histograma usa cortes fijos en
horas naturales: **0-24, 24-48, 48-72, 72-120, >120 h**. Son fijos a propósito: unos cortes que
cambiaran con los datos no serían comparables entre periodos.

### 3.3 Exclusiones — lo que hay que cruzar con el PNT literalmente

| Indicador | Qué excluye |
|---|---|
| `TAT-TOTAL` | Muestras rechazadas en recepción y muestras en estado Rechazada. Los estudios sin fecha de validación **no se ocultan**: se listan aparte como «casos abiertos», con su antigüedad. |
| `TAT-ADQ` | Muestras rechazadas en recepción. |
| `TAT-ANA` | Muestras rechazadas en recepción y en estado Rechazada. |
| `PCT-FUERA-PLAZO` | Rechazadas; **denominador = estudios validados en el periodo**. Si `TAT-TOTAL` no tiene objetivo definido, el indicador devuelve 0 sobre el denominador real. |
| `PCT-REAPERTURA` | Denominador = informes con fecha de validación en el periodo; numerador = eventos «Reopen» de la auditoría en ese periodo. |

**Frontera temporal.** Los indicadores de recepción y TAT se filtran por `ReceivedAtUtc` dentro
del periodo. `PCT-REAPERTURA` se filtra por fecha de validación y de reapertura. Si el PNT dice
«informes emitidos en el mes», hay que comprobar cuál de las dos fechas usa.

### 3.4 Desgloses

- `PCT-RECHAZO`, `PCT-SALVEDAD` y `PCT-INCIDENCIA` se desglosan **por descripción del motivo**
  del catálogo de rechazo, contando muestras distintas (una muestra con dos motivos cuenta una
  vez en cada uno).
- `PCT-FUERA-PLAZO` se desglosa por tipo de muestra.
- `PCT-REAPERTURA` se desglosa por el motivo escrito al reabrir.

### 3.5 Trazabilidad de la cifra

El cuadro da el agregado y permite **bajar al caso**: la lista nominal de las muestras detrás de
cada TAT y de cada incidencia. Es lo que pide una auditoría cuando cuestiona un número.

### 3.6 Indicador retirado

`TAT-PRE` (recepción → registro) se retiró en la v2.2.0: medía un intervalo que no existe,
porque el alta fija ambas marcas en el mismo instante, y daba cero por construcción. **Si el PNT
todavía lo menciona, hay discordancia.** El intervalo preanalítico con valor clínico real en
citometría es *extracción → recepción*, y requiere capturar la hora de extracción, que **hoy no
recoge ninguna pantalla**.

---

## 4. Enlaces explícitos con el QMS

| Dato en MiniLIS | Campo | Dónde se rellena |
|---|---|---|
| Documento que define un indicador | `QualityIndicator.QmsDocumentRef` | Configuración → Umbrales |
| Ficha maestra de paneles y su revisión | `PanelVersion.MasterSheetCode` / `MasterSheetRevision` | Versiones de paneles |
| Documento del QMS de la versión de panel | `PanelVersion.QmsDocumentRef` | Versiones de paneles |
| Evaluación del cambio de versión | `PanelVersion.ChangeEvaluationRef` | Versiones de paneles |
| Código del equipo en el maestro del QMS | `Cytometer.QmsEquipmentCode` | Configuración → Citómetros |
| No conformidad de recepción | `Sample.QmsNonConformityRef` | Recepción de la muestra |
| No conformidad de anulación de tubo | `SampleTube.VoidNonConformityRef` | Ficha de muestra |

Todos son **texto libre validado por formato**: MiniLIS no comprueba que el código exista en
Flow Doc ni sincroniza con él.

---

## 5. Control de acceso

### 5.1 Roles

Tres roles, **uno por usuario**: **Administrador**, **Facultativo**, **Técnico**.

### 5.2 Permisos configurables

Desde la v4.0 los permisos **no están escritos en el código**: son 37 permisos configurables por
rol desde *Configuración → Permisos*, y se comprueban tanto en la pantalla como en el servidor.

| Grupo | Permisos |
|---|---|
| Muestras y trabajo diario | 7 |
| Tubos | 6 |
| Informes | 5 |
| Consultas y exportaciones | 8 |
| Versiones de paneles | 3 |
| Administración | 8 |

Reparto de fábrica, resumido:

| Función | Admin | Facultativo | Técnico |
|---|:---:|:---:|:---:|
| Registrar y editar muestras | ✔ | ✔ | ✔ |
| Marcar tubo leído | ✔ | ✔ | ✔ |
| Desmarcar tubo leído | — | ✔ | — |
| Anular tubo o panel | ✔ | ✔ | — |
| Abrir y guardar informes | ✔ | ✔ | — |
| **Validar informe** | — | ✔ | — |
| **Reabrir informe validado** | — | ✔ | — |
| Aprobar versión de panel | — | ✔ | — |
| Exportar con identificadores | ✔ | ✔ | — |
| …sin justificarlo | ✔ | — | — |
| Configuración y administración | ✔ | ✔ | — |

**El administrador no valida informes**: es una decisión explícita, la validación es acto
facultativo. Dos permisos son fijos y no se pueden retirar al administrador: gestionar permisos
y gestionar usuarios.

### 5.3 Reglas que no dependen del permiso

- **Un informe validado no se puede modificar**, aunque se tenga permiso de guardar. Hay que
  reabrirlo primero, con motivo documentado de al menos 10 caracteres.
- Para validar hacen falta firma y que todos los tubos estén leídos, justificados o anulados
  (excepto en muestra rechazada, v4.3).

---

## 6. Registros y trazabilidad

### 6.1 Auditoría

Se registra usuario, nombre, fecha y hora UTC, IP, entidad, identificador, acción, contexto y
descripción del cambio. Acciones registradas de forma explícita, además de las altas,
modificaciones y bajas de las entidades auditadas:

`Validate`, `Reopen`, `Download`, `Reprint`, `Read`, `Search`, `Export`, `UnmarkRead`,
`ReadIncident`, `ReadIncidentCleared`, `DeferredEntry`, `ImportInfinicyt`, `AuditPackage`,
`NumberingRetry`, `Purge`.

Se auditan tanto las escrituras como **las consultas**: búsquedas y lecturas de historial de
paciente. La retención es configurable (`Audit:RetentionYears`).

### 6.2 Datos congelados en el informe

Para que reimprimir un informe emitido devuelva siempre lo mismo, se guarda copia congelada en
el momento de validar de: **equipo y software empleados**, **versiones de panel empleadas** y
**limitaciones analíticas marcadas**. Reabrir el informe suelta esas copias y se vuelven a
congelar en la siguiente validación.

Las notas de alcance de acreditación de los tubos **se leen en vivo**, porque una versión de
panel publicada es inmutable.

### 6.3 Paquete de evidencias para auditoría

Un ZIP con índice y diez documentos: actividad, indicadores, recepción, trazabilidad,
validaciones, paneles, accesos, copias de seguridad, usuarios y contingencia. Se puede generar
con o sin identificadores de paciente, y su generación queda auditada.

### 6.4 Contingencia (cl. 7.8)

Modo de contingencia con registro diferido y lista de pendientes. El sistema guarda la **fecha
de la última prueba anual** del modo, porque la cláusula exige probarlo al menos una vez al año
y documentarlo.

### 6.5 Copias de seguridad

Automáticas con frecuencia configurable, **cifradas en AES-256** y **verificadas** tras crearse.
La clave de cifrado es obligatoria fuera de desarrollo: sin ella la aplicación no arranca.

---

## 7. Qué es configurable sin tocar el código

| Catálogo | Contenido |
|---|---|
| Marcadores | Lista y orden |
| Plantillas de informe | Cabecera, marcadores, conclusiones predefinidas |
| Paneles y versiones | Composición por tubo, fórmula, revisión |
| Umbrales de indicadores | Objetivo, aviso, crítico, referencia al QMS |
| Motivos de rechazo | Código, descripción, exige texto libre |
| Incidencias de lectura | Código, descripción |
| **Limitaciones analíticas** | Frases de calidad de la muestra (v4.2) |
| Citómetros | Equipo, software, código del QMS |
| Hojas de trabajo | Perfiles CSV o XML por instrumento |
| Etiquetas | Dos formatos, dimensiones y contenido |
| Cabecera y firmas | Logotipo, líneas, facultativos firmantes |
| Escalas de intensidad | Valores del editor de informes |
| Permisos por rol | Los 37 permisos |
| Retenciones | Auditoría (años), excedente (días), frecuencia de copia |

Toda la configuración se puede **exportar a un fichero e importar en otro servidor**, con
detección de qué secciones son compatibles tras una actualización y copia de seguridad
obligatoria antes de cargar.

---

## 8. Puntos a cruzar con los PNT y el QMS

Lista de verificación. Cada punto es una afirmación concreta de MiniLIS que el documento del
QMS o el PNT correspondiente debe reflejar igual.

### 8.1 Indicadores

1. **¿El PNT define once indicadores, y los mismos?** Comprobar código a código contra 3.1.
2. **¿El PNT sigue citando `TAT-PRE`?** Está retirado desde la v2.2.0.
3. **¿El PNT dice «media» o «mediana»?** MiniLIS da **mediana y P90**.
4. **¿Coinciden los cortes del histograma** (24/48/72/120 h) con los del PNT?
5. **¿Coinciden las exclusiones?** Sobre todo: las rechazadas quedan fuera de los TAT, y los
   estudios sin validar se informan aparte, no se descartan en silencio.
6. **¿Coincide el denominador de `PCT-FUERA-PLAZO`** (validados en el periodo) con el del PNT?
7. **¿Coincide la frontera temporal?** MiniLIS filtra por fecha de **recepción**, no de emisión.
8. **¿Están fijados los objetivos y umbrales?** Hoy solo `TAT-TOTAL` tiene valores (objetivo 72 h,
   aviso 72 h, crítico 96 h). Los otros diez están **sin definir**, y `PCT-FUERA-PLAZO` no puede
   calcularse sin el objetivo de `TAT-TOTAL`.
9. **¿Está rellena la referencia al documento del QMS** en cada indicador? Hoy **ninguna lo
   está**: el campo existe y está vacío en los once.

### 8.2 Paneles

10. **¿El código de la ficha maestra configurado (`ANX-CIT-REA-003-01`) es el vigente en Flow Doc?**
11. **¿Las versiones migradas tienen su revisión de ficha maestra, fórmulas y datos de aprobación?**
    La migración automática de la v4.0 las dejó con la numeración convertida pero **sin esos
    datos**: hay que completarlos desde el QMS.
12. **¿El circuito del PNT coincide** con Borrador → En revisión → Aprobada → Vigente → Retirada?
13. **¿El PNT exige doble revisor?** MiniLIS aprueba con **un solo facultativo**, por decisión
    expresa de la unidad.

### 8.3 Recepción y no conformidades

14. **¿Los motivos de rechazo del catálogo coinciden** con los del PNT de recepción?
15. **¿El PNT distingue salvedad de rechazo** igual que MiniLIS, y espera que ambos salgan en el
    informe al clínico?
16. **¿Qué motivos deben abrir no conformidad en el QMS?** MiniLIS solo **avisa**; no la abre.
17. **¿El PNT contempla que una muestra rechazada se informe y valide?** Es el comportamiento
    desde la v4.3.

### 8.4 Registros y responsabilidades

18. **¿El PNT asigna la validación al facultativo en exclusiva?** Así está de fábrica; el
    administrador **no valida**.
19. **¿El PNT permite reabrir un informe validado, y con qué motivo mínimo?** MiniLIS exige 10
    caracteres y lo audita.
20. **¿Coincide la retención de auditoría configurada** con la que fija el QMS?
21. **¿Coincide la retención de excedente por tipo** con la del PNT de criopreservación?
22. **¿Está documentada la prueba anual del modo de contingencia?** MiniLIS guarda la fecha de la
    última; comprobar que existe la evidencia en el QMS.

### 8.5 Protección de datos

23. **¿El QMS contempla la exportación de listados con NHC o nombre?** Desde la v4.4 hay tres
    niveles (sin identificadores / con NHC / con NHC y nombre), con permiso, justificación y
    registro en auditoría.
24. **¿Está definido quién puede exportar identificadores sin justificarlo?** De fábrica, solo el
    administrador.

### 8.6 Estado del despliegue

25. **Cifrado en reposo**: la base sigue en SQLite sin cifrado propio. **No debe alojar datos
    reales sin volumen cifrado o migración a un motor con cifrado.** Las copias sí van cifradas.
26. **Identidad corporativa**: autenticación local propia, pendiente de integración con el
    directorio de la institución.
27. **Alojamiento**: pendiente de infraestructura gestionada por la institución.

---

## 9. Trazabilidad de versiones del software

El número de versión vive en un único sitio y la interfaz lo lee de los metadatos del ensamblado.
Cada versión desplegada se marca con una **etiqueta de git anotada**, que es lo que permite
responder «¿qué código exacto emitió este informe?», necesario para la trazabilidad de informes
que se conservan cinco años o más (cl. 7.6). Los ensamblados llevan además el identificador del
commit anexado a la versión (`4.4.0+58dfed5`).

El criterio de numeración está adaptado al laboratorio: el salto **MAYOR** se reserva a los
cambios que **obligan a revalidar** o que alteran el informe emitido, no a la compatibilidad
binaria. El detalle de cada versión está en `CHANGELOG.md`.

La batería de pruebas automáticas (341 en la v4.4.0) se ejecuta en cada cambio mediante
integración continua, y cubre entre otras cosas la matriz de autorización, la seudonimización de
las exportaciones, la inmutabilidad de los informes validados y la conservación del histórico en
las migraciones.
