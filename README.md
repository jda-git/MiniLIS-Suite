# MiniLIS Suite

**MiniLIS Suite** es un sistema de información de laboratorio (LIS) para el proceso de citometría de flujo e inmunología de una unidad hospitalaria: registro de muestras, adquisición, redacción y validación de informes, y trazabilidad ISO 15189 del ciclo de vida de cada estudio. Construido con **Blazor Server** y **.NET 9**.

## Estado y alcance

En desarrollo activo. **La aplicación funciona hoy únicamente con datos de prueba**: no contiene ni ha contenido datos de pacientes reales. Su puesta en uso asistencial queda condicionada al despliegue en infraestructura gestionada por la institución — ver [Estado del despliegue](#estado-del-despliegue).

**No es un producto sanitario conforme al Reglamento (UE) 2017/745** (dispositivos médicos) — es una herramienta de gestión de laboratorio, no interviene en el diagnóstico ni en el cálculo clínico automatizado. Está diseñada para tratar datos de salud de categoría especial (RGPD art. 9); el control de acceso, la auditoría y las medidas del ENS existentes en el código responden a esa exigencia, no son una funcionalidad opcional.

El módulo donde esta frontera se vigila activamente es la importación de resultados desde XML de Infinicyt (Editor de Informe → "Importar resultados Infinicyt"): transfiere texto de poblaciones ya calculado por Infinicyt para que el facultativo elija qué insertar en el informe, pero no calcula, deriva, compara ni clasifica ningún valor por su cuenta — es, de todos los puntos de la aplicación, el más próximo a cruzar esa línea, y el más probable candidato a recibir peticiones que la crucen ("que calcule el porcentaje sobre celularidad total", "que marque en rojo si supera un umbral", "que compare con el estudio previo"). Ver la cabecera de `InfinicytXmlParser.cs` para el detalle.

MiniLIS cubre el proceso de la muestra. Fuera de su alcance deliberadamente, y responsabilidad del sistema de gestión de calidad de la unidad (QMS Flow Doc): gestión documental y procedimientos normalizados, personal y competencias, equipos y su mantenimiento/calibración, reactivos y lotes, no conformidades formales, evaluación externa de la calidad (EQA), y control ambiental. MiniLIS solo almacena referencias en texto a esos registros, nunca los sustituye.

## Funcionalidades principales

- Registro transaccional de pacientes, peticiones y muestras, con numeración correlativa automática (`AA-NNNNN`) y modo de contingencia para caídas del sistema.
- Recepción con registro de aceptación/salvedad/rechazo y notificación al peticionario. Tanto la salvedad ("LIMITACIONES") como el rechazo preanalítico ("MUESTRA RECHAZADA PREANALÍTICAMENTE", con su motivo y la notificación al peticionario si consta) se trasladan al informe entregado al clínico (cl. 7.4).
- Ciclo de vida de la muestra dirigido por la acción real y no por un desplegable: se registra como *Recibida*, pasa a *En proceso* al marcarse la lectura del primer tubo en el citómetro, a *Reportada parcial* al guardarse el primer borrador de informe y a *Finalizada* al validarse. Ninguna de esas promociones rebaja un estado más avanzado. El rechazo preanalítico vive en el estado de recepción (F-4), independiente de esta cadena, y el panel de control lo cuenta desde ahí.
- Editor de informes con captura de intensidades/porcentajes de marcadores y síntesis de texto.
- Generación de PDF (QuestPDF) y ODT editable (LibreOffice).
- Cuadro de indicadores de calidad (TAT, % de rechazo/salvedad, actividad) según ISO 15189 cl. 8.8/8.9.
- Etiquetas con código de barras Code128, hoja de trabajo del citómetro, enlace con ficheros FCS.
- Excedente criopreservado con trazabilidad **por alícuota individual**: cada vial es una fila propia (`BatchId`/`AliquotIndex`/`BatchSize`) con su ubicación, su estado y su historial de eventos, de modo que descongelar una no altera el estado de sus hermanas. Impresión de etiquetas por lote desde la misma pantalla de etiquetas de muestra.
- Auditoría de escritura y de consulta (búsquedas, lecturas de historial), paquete de evidencias para auditoría externa.
- Concurrencia optimista, backups cifrados y verificados, control de acceso por rol.

## Stack técnico

- **Frontend**: Blazor Server (ASP.NET Core 9, Interactive Server), Bootstrap 5, Bootstrap Icons.
- **Backend**: C#, Entity Framework Core sobre **SQLite**.
- **Documentos**: QuestPDF (PDF), manipulación OpenXML/ZIP (ODT).
- **Pruebas**: xUnit + FluentAssertions sobre Sqlite en memoria (`MiniLIS.Tests`), 164 pruebas — incluidas las de autorización y de login real, que levantan el host completo (`WebApplicationFactory`).
- **Integración continua**: GitHub Actions (`.github/workflows/ci.yml`) compila en `Release` y ejecuta toda la batería en cada `push` y cada *pull request*.
- **Arquitectura**: Domain / Application / Infrastructure / Web.

Todos los paquetes van fijados a `9.0.*`, en línea con el *target framework* `net9.0`. Conviene no introducir referencias a versiones `10.x`: se resuelven contra un framework compartido distinto del que ejecuta la aplicación y fallan en tiempo de ejecución, no al compilar (el síntoma típico es un `MissingMethodException` dentro de Data Protection que solo aparece con la caché de NuGet limpia, es decir, en CI y no en local).

## Versionado

El número de versión vive en un **único sitio**, `Directory.Build.props`, y de ahí lo heredan los cinco proyectos. La interfaz lo lee de los metadatos del ensamblado (`MiniLIS.Web/Services/AppVersion.cs`): **no debe escribirse a mano en ninguna página** — cuando había dos orígenes divergieron, con la pantalla de acceso mostrando `2.0.4.Final` mientras los ensamblados se compilaban como `1.0.0`.

El esquema es `MAYOR.MENOR.PARCHE`, pero con el criterio de un sistema de laboratorio y no de una librería: lo que determina el salto no es la compatibilidad binaria sino si el cambio **obliga a revalidar** (MAYOR), aporta funcionalidad nueva compatible (MENOR) o corrige sin cambio funcional (PARCHE). Ver [CHANGELOG.md](CHANGELOG.md).

Publicar una versión:

```bash
# 1. Actualizar <Version> en Directory.Build.props y añadir la entrada en CHANGELOG.md
# 2. Etiquetar el commit desplegado
git tag -a v2.1.0 -m "v2.1.0"
git push origin v2.1.0
```

La etiqueta es lo que permite responder *«¿qué código exacto es esta versión?»*, necesario para la trazabilidad de los informes (ISO 15189 cl. 7.6), que se conservan cinco años o más. Los ensamblados llevan además el commit anexado a la versión informativa (`2.1.0+c339fa2`, cortesía de SourceLink, sin configuración adicional): es el dato a pedir ante una incidencia, porque identifica el código en ejecución incluso entre despliegues de una misma versión.

## Puesta en marcha

### Requisitos

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

No requiere ningún motor de base de datos externo: usa SQLite (fichero local, `minilis.db`).

### Instalación

1. Clonar el repositorio:
   ```bash
   git clone https://github.com/jda-git/MiniLIS-Suite.git
   ```

2. Compilar la solución:
   ```bash
   dotnet build MiniLIS.Suite.slnx
   ```

3. Ejecutar las pruebas:
   ```bash
   dotnet test MiniLIS.Tests/MiniLIS.Tests.csproj
   ```

4. Arrancar la aplicación:
   ```bash
   cd MiniLIS.Web
   dotnet run
   ```

### Despliegue en producción

Revisar `appsettings.Production.json` antes de desplegar: `AllowedHosts` trae un valor de aviso (`CAMBIAR-AL-DOMINIO-REAL-DE-PRODUCCION`) que debe sustituirse por el dominio real — de lo contrario, el filtrado de host de ASP.NET Core rechaza todas las peticiones (falla de forma segura, no abierta).

**Valores que deben definirse en producción** (variables de entorno o almacén seguro del hosting — nunca en un `appsettings*.json` versionado):

| Clave | Obligatoria | Efecto si falta |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | Sí | La aplicación no arranca (`InvalidOperationException` explícita en `Program.cs`). |
| `Backup__EncryptionKey` | Sí | La aplicación no arranca fuera de `Development` (comprobación explícita en `Program.cs`, N-9) — antes solo se descubría al fallar la primera copia de seguridad. Clave AES-256, 32 bytes en Base64 (`openssl rand -base64 32`). |
| `Seed__AdminUser` | No | Se usa `admin@minilis.com` por defecto. |
| `Seed__AdminPassword` | No | Se genera una contraseña que cumple la política configurada y se registra una única vez en el log del primer arranque (`[SEED] Administrador inicial creado...`) — revisar ese log si no se fija explícitamente. |

### Estado del despliegue

La aplicación está funcionalmente completa y validada con datos de prueba, **pendiente de despliegue en infraestructura institucional**. Hasta que se resuelvan los tres puntos siguientes no debe tratar datos de pacientes reales:

| Punto | Situación | Qué lo resuelve |
|---|---|---|
| **Alojamiento** | Ejecución local, fuera de infraestructura gestionada. | Máquina gestionada por la institución, con copias de seguridad, segmentación de red y acceso restringido al personal del laboratorio. |
| **Cifrado en reposo** (N-6) | La instancia sigue en SQLite en fichero (`minilis.db`), sin cifrado propio — a diferencia de las copias de seguridad, que sí van cifradas en AES-256 (A-7). | Migración a SQL Server con TDE, o bien mantener SQLite sobre un volumen cifrado (BitLocker/LUKS). El fichero **no debe alojar datos reales de paciente sin una de las dos medidas**. |
| **Identidad corporativa** (M-1) | Autenticación local propia (ASP.NET Core Identity), transitoria por diseño. | Integración con el directorio corporativo (LDAP/AD/SSO) cuando la institución la facilite. |

Migrar a SQL Server no es un requisito funcional —el volumen de una unidad de citometría está muy por debajo de lo que SQLite soporta—, sino operativo: resuelve el cifrado en reposo e integra la base en las herramientas de respaldo y administración de la institución. El cambio está acotado: proveedor de acceso a datos, paquete correspondiente y regeneración de las migraciones (no son portables entre motores). El control de concurrencia se genera en código (`ApplicationDbContext`), no en el motor, por lo que es directamente compatible.

## Licencia

Uso interno de la unidad. Todos los derechos reservados.
