# MiniLIS Suite

**MiniLIS Suite** es un sistema de información de laboratorio (LIS) para el proceso de citometría de flujo e inmunología de una unidad hospitalaria: registro de muestras, adquisición, redacción y validación de informes, y trazabilidad ISO 15189 del ciclo de vida de cada estudio. Construido con **Blazor Server** y **.NET 9**.

## Estado y alcance

Sistema en uso real de la unidad, en desarrollo activo. **No es un producto sanitario conforme al Reglamento (UE) 2017/745** (dispositivos médicos) — es una herramienta de gestión de laboratorio, no interviene en el diagnóstico ni en el cálculo clínico automatizado. Trata datos de salud de categoría especial (RGPD art. 9); el control de acceso, la auditoría y las medidas del ENS existentes en el código reflejan esa exigencia, no son una funcionalidad opcional.

El módulo donde esta frontera se vigila activamente es la importación de resultados desde XML de Infinicyt (Editor de Informe → "Importar resultados Infinicyt"): transfiere texto de poblaciones ya calculado por Infinicyt para que el facultativo elija qué insertar en el informe, pero no calcula, deriva, compara ni clasifica ningún valor por su cuenta — es, de todos los puntos de la aplicación, el más próximo a cruzar esa línea, y el más probable candidato a recibir peticiones que la crucen ("que calcule el porcentaje sobre celularidad total", "que marque en rojo si supera un umbral", "que compare con el estudio previo"). Ver la cabecera de `InfinicytXmlParser.cs` para el detalle.

MiniLIS cubre el proceso de la muestra. Fuera de su alcance deliberadamente, y responsabilidad del sistema de gestión de calidad de la unidad (QMS Flow Doc): gestión documental y procedimientos normalizados, personal y competencias, equipos y su mantenimiento/calibración, reactivos y lotes, no conformidades formales, evaluación externa de la calidad (EQA), y control ambiental. MiniLIS solo almacena referencias en texto a esos registros, nunca los sustituye.

## Funcionalidades principales

- Registro transaccional de pacientes, peticiones y muestras, con numeración correlativa automática (`AA-NNNNN`) y modo de contingencia para caídas del sistema.
- Recepción con registro de aceptación/salvedad/rechazo y notificación al peticionario.
- Editor de informes con captura de intensidades/porcentajes de marcadores y síntesis de texto.
- Generación de PDF (QuestPDF) y ODT editable (LibreOffice).
- Cuadro de indicadores de calidad (TAT, % de rechazo/salvedad, actividad) según ISO 15189 cl. 8.8/8.9.
- Etiquetas con código de barras Code128, hoja de trabajo del citómetro, gestión de excedente/alícuotas, enlace con ficheros FCS.
- Auditoría de escritura y de consulta (búsquedas, lecturas de historial), paquete de evidencias para auditoría externa.
- Concurrencia optimista, backups cifrados y verificados, control de acceso por rol.

## Stack técnico

- **Frontend**: Blazor Server (ASP.NET Core 9, Interactive Server), Bootstrap 5, Bootstrap Icons.
- **Backend**: C#, Entity Framework Core sobre **SQLite**.
- **Documentos**: QuestPDF (PDF), manipulación OpenXML/ZIP (ODT).
- **Pruebas**: xUnit + FluentAssertions sobre Sqlite en memoria (`MiniLIS.Tests`).
- **Arquitectura**: Domain / Application / Infrastructure / Web.

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

La autenticación local (ASP.NET Core Identity) es una solución transitoria; debe migrar al directorio corporativo de la institución (LDAP/AD/SSO) cuando exista integración disponible.

**Base de datos (N-6, pendiente):** la instancia sigue en SQLite en fichero (`minilis.db`), sin cifrado propio — a diferencia de las copias de seguridad, que sí van cifradas en AES-256 (A-7). Mientras N-6 no esté hecho, el fichero de base de datos **no debe alojar datos reales de paciente sin cifrado a nivel de sistema de ficheros** (BitLocker/LUKS o equivalente) en el servidor donde resida, y el despliegue en producción con datos reales queda condicionado a resolver esa asimetría.

## Licencia

Uso interno de la unidad. Todos los derechos reservados.
