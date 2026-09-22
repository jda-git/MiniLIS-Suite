# Instalación de MiniLIS Suite

Guía para instalar MiniLIS en un **servidor** (los usuarios entran desde sus equipos con el
navegador) o en un **puesto único** (se usa solo en ese PC). El mismo instalador sirve para
los dos casos, para **actualizar** una instalación existente y para **desinstalar**.

---

## 1. Qué hace el instalador

`MiniLIS-Suite-<versión>-instalador.exe` es un asistente de Windows que:

1. **Comprueba los requisitos** del equipo y muestra el resultado antes de instalar nada.
2. **Copia el programa** (con su propio .NET 9 incluido: no hay que instalar nada más).
3. **Prepara el certificado HTTPS**: importa el del hospital (`.pfx`) o genera uno
   autofirmado.
4. **Escribe la configuración** de la instalación en la carpeta de datos, con una **clave
   nueva para cifrar las copias de seguridad**.
5. **Registra MiniLIS como servicio de Windows**. Arranca solo con el equipo, sin que nadie
   tenga que iniciar sesión, y se reinicia solo si se detiene por un fallo.
6. **Ajusta los permisos**: la carpeta de datos solo es accesible para administradores y
   para el propio servicio.
7. **Abre el puerto en el cortafuegos** (solo en modo servidor).
8. **Arranca el servicio y comprueba que responde**. La primera vez crea la base de datos y
   el usuario administrador inicial.

No necesita SQL Server, IIS, .NET ni ningún otro programa: la base de datos es SQLite y va
incluida.

---

## 2. Requisitos

| | Mínimo | Recomendado |
|---|---|---|
| Sistema | Windows 10 (1607) / Windows Server 2016, 64 bits | Windows Server 2019 o posterior |
| Memoria | 2 GB | 4 GB o más |
| Disco | 1 GB libre | 5 GB o más (base de datos y copias) |
| Permisos | Administrador del equipo, para instalar | — |
| Red (servidor) | Un puerto libre (443 por defecto) | Nombre DNS propio, p. ej. `minilis.hospital.local` |
| Cifrado | — | **Volumen de datos cifrado (BitLocker)**: obligatorio antes de tratar datos reales de pacientes |

El instalador comprueba todo esto. Marca como **ERROR** lo que impide instalar y como
**AVISO** lo que conviene resolver.

Los usuarios solo necesitan un navegador actual (Edge, Chrome o Firefox).

### Antes de instalar en un servidor

Pida al Servicio de Informática:

- **El nombre DNS** con el que se accederá (por ejemplo `minilis.hospital.local`).
- **Un certificado de servidor** para ese nombre, en formato `.pfx` y con su contraseña. Con
  un certificado autofirmado MiniLIS funciona, pero cada equipo mostrará un aviso de seguridad
  al entrar.
- **Una carpeta en un volumen cifrado** para los datos.
- **Una ubicación para las copias de seguridad**, idealmente otra unidad o una carpeta de red.

---

## 3. Generar el instalador

Esto se hace en el equipo de desarrollo, no en el servidor, y una sola vez por versión.

1. Instale **Inno Setup 6** (gratuito): <https://jrsoftware.org/isdl.php>, o con:
   ```
   winget install JRSoftware.InnoSetup
   ```
2. Desde la carpeta del proyecto, ejecute:
   ```
   powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1
   ```
3. El resultado queda en `installer\out\`:
   - `MiniLIS-Suite-<versión>-instalador.exe`: el instalador, unos 45 MB.
   - `MiniLIS-Suite-<versión>-instalador.exe.sha256`: su huella. Anótela en el registro de
     cambios del SGC; así se puede demostrar qué instalador exacto se usó.

La versión se toma de `Directory.Build.props`, igual que la del programa. El script comprueba
que en el paquete no entra ninguna base de datos ni configuración de desarrollo.

---

## 4. Instalar paso a paso

Copie el instalador al equipo, pulse con el botón derecho → **Ejecutar como administrador** y
siga el asistente.

| Paso | Qué se indica |
|---|---|
| **Carpeta del programa** | Por defecto `C:\Program Files\MiniLIS`. |
| **Tipo de instalación** | **Servidor** (acceso desde la red) o **Puesto único** (solo este equipo, `https://localhost`). |
| **Acceso** | Puerto HTTPS (443 por defecto) y nombres de acceso separados por `;`. Se proponen los del equipo. En puesto único el nombre es siempre `localhost`. |
| **Certificado HTTPS** (solo servidor) | El del hospital (`.pfx`) o uno autofirmado para pruebas. Si se elige el del hospital, se comprueban al momento la contraseña, la caducidad, el uso para servidor y que cubra los nombres de acceso. |
| **Administrador inicial** | Usuario (correo) y contraseña: 12 caracteres o más, con mayúsculas, minúsculas, números y algún símbolo. **En el primer acceso se pedirá cambiarla.** |
| **Carpeta de datos** | Por defecto `C:\ProgramData\MiniLIS`. Para datos reales, en un volumen cifrado. |
| **Comprobación de requisitos** | Resultado de las comprobaciones. Con algún ERROR no se puede continuar: corríjalo y pulse *Atrás* y *Siguiente* para repetirla. |
| **Instalar** | Copia, configura y arranca. Tarda uno o dos minutos. |
| **Resultado** | Dirección de acceso, usuario administrador, carpeta de datos y **clave de las copias de seguridad** (ver abajo). |

> **Clave de las copias de seguridad.** Al terminar se muestra una clave como
> `k3J9…Qw=`. **Guárdela fuera del equipo**, en el gestor de claves del Servicio de
> Informática o en un sobre cerrado en el laboratorio. Las copias van cifradas con ella: si
> el servidor se pierde y no se tiene la clave, **las copias no se pueden restaurar**.
> También está en `minilis.settings.json`, dentro de la carpeta de datos.

---

## 5. Qué queda instalado

| Elemento | Dónde |
|---|---|
| Programa | `C:\Program Files\MiniLIS` (se sustituye al actualizar) |
| Base de datos | `<datos>\db\minilis.db` |
| Configuración de la instalación | `<datos>\minilis.settings.json` |
| Copias previas a importar configuración | `<datos>\config-backups` |
| Copias de la base de datos antes de actualizar | `<datos>\backups` |
| Claves de sesión (cifradas) | `<datos>\keys` |
| Registro del instalador | `<datos>\logs\instalacion.log` |
| Servicio de Windows | **MiniLIS Suite** (`MiniLIS`), cuenta `NT SERVICE\MiniLIS`, inicio automático |
| Cortafuegos (servidor) | Regla *MiniLIS Suite (HTTPS 443)*, redes de dominio y privadas |
| Certificado | Almacén *Equipo local → Personal* (el autofirmado también en *Entidades raíz de confianza* de ese equipo) |
| Acceso directo | Menú Inicio → *MiniLIS Suite* (abre la dirección en el navegador) |

`<datos>` es la carpeta de datos elegida; por defecto `C:\ProgramData\MiniLIS`.

El servicio usa una **cuenta virtual sin privilegios** (`NT SERVICE\MiniLIS`), no la de
administrador ni *SYSTEM*. Solo puede leer el programa y escribir en la carpeta de datos.

---

## 6. Primeros pasos después de instalar

1. Abra la dirección (acceso directo *MiniLIS Suite*) y entre con el administrador inicial.
   Se le pedirá **cambiar la contraseña**.
2. **Usuarios**: cree los usuarios del laboratorio con su rol (Técnico, Facultativo o
   Administrador).
3. **Copias de seguridad** (`/backup`): indique la carpeta de las copias, a ser posible en
   otra unidad o en red, y la frecuencia. **La cuenta del servicio tiene que poder escribir
   en ella**: dé permiso de *Modificar* a `NT SERVICE\MiniLIS` (carpeta local) o a la cuenta
   del equipo `DOMINIO\SERVIDOR$` (carpeta de red). Haga una primera copia y compruebe que
   aparece.
4. **Configuración**, si ya existe una configuración funcional en otro equipo: en el equipo
   de origen, *Configuración → Copia config. → Guardar configuración en un fichero*; aquí,
   *Importar* ese fichero. Así se cargan de una vez marcadores, plantillas, paneles, etiquetas,
   motivos, citómetros, cabecera y firmas. Si no, configúrelo pestaña a pestaña.
5. **Carpeta FCS** (Configuración → FCS), si se enlazan los ficheros del citómetro. La cuenta
   del servicio debe poder leerla (ver *Solución de problemas*).
6. Imprima una etiqueta y genere un informe de prueba para comprobar la impresora y la
   cabecera.

---

## 7. Uso diario

No hay que hacer nada: el servicio arranca con el equipo. Para detenerlo o reiniciarlo,
abra *Servicios* (`services.msc`) → **MiniLIS Suite**, o use PowerShell como administrador:

```
Restart-Service MiniLIS
```

Los avisos y errores del programa van al **Visor de eventos → Registros de Windows →
Aplicación**, origen **MiniLIS**.

---

## 8. Actualizar a una versión nueva

Ejecute como administrador el instalador de la versión nueva. Detecta la instalación
existente y **no vuelve a preguntar nada**:

1. Detiene el servicio.
2. **Copia la base de datos** en `<datos>\backups\antes-de-actualizar-<fecha>-v<versión anterior>`.
3. Sustituye el programa. **No toca la carpeta de datos ni la configuración.**
4. Arranca el servicio. La base de datos se actualiza sola al arrancar.

Si instala una versión **más antigua** que la que hay, el asistente avisa y pide
confirmación: los cambios de la base de datos no se deshacen.

**Volver atrás** si la versión nueva da problemas:

1. Detenga el servicio: `Stop-Service MiniLIS`.
2. Copie los ficheros de `<datos>\backups\antes-de-actualizar-…` sobre `<datos>\db\`.
3. Ejecute el instalador de la versión anterior.

Según el esquema de versiones (ver `CHANGELOG.md`), una versión **MAYOR** exige
revalidación documentada antes de usarla. Una **MENOR** se notifica al Servicio de
Informática antes de desplegarla.

---

## 9. Cambiar la configuración de la instalación

Todo está en `<datos>\minilis.settings.json` (edítelo como administrador y después
`Restart-Service MiniLIS`):

| Clave | Para qué |
|---|---|
| `AllowedHosts` | Nombres de acceso permitidos, separados por `;`. Una petición con otro nombre se rechaza. |
| `Kestrel.Endpoints.Https.Url` | Puerto (`https://*:443`). Si lo cambia, cambie también la regla del cortafuegos. |
| `Kestrel.Endpoints.Https.Certificate.Subject` | Nombre del certificado en el almacén *Equipo local → Personal*. |
| `ConnectionStrings.DefaultConnection` | Ruta de la base de datos. |
| `Backup.EncryptionKey` | Clave de las copias. **No la cambie**: las copias anteriores dejarían de poder restaurarse. |

**Cambiar el certificado** (p. ej. al renovarlo):

1. Importe el `.pfx` nuevo en *Equipo local → Personal* (doble clic en el fichero →
   *Equipo local*).
2. Dé permiso de lectura de la clave privada a `NT SERVICE\MiniLIS`: en `certlm.msc`,
   botón derecho sobre el certificado → *Todas las tareas → Administrar claves privadas*.
3. Si el nombre del certificado cambia, actualice `Certificate.Subject`.
4. `Restart-Service MiniLIS`.

---

## 10. Desinstalar

*Configuración de Windows → Aplicaciones → MiniLIS Suite → Desinstalar*. Se eliminan el
programa, el servicio, la regla del cortafuegos y el acceso directo.

**La carpeta de datos no se borra**: contiene registros asistenciales con plazo de
conservación. Si se reinstala indicando la misma carpeta de datos, se reutiliza la base de
datos y se conserva la clave de las copias. Si de verdad hay que eliminarla, hágalo a mano
cuando se haya cumplido el plazo de conservación y quede constancia en el SGC.

---

## 11. Instalación sin asistente

Para el Servicio de Informática (despliegue automatizado): el trabajo lo hace
`installer\scripts\Install-MiniLIS.ps1`, que el asistente instala en
`C:\Program Files\MiniLIS\instalacion`. Con el programa ya copiado (contenido de
`installer\out\app`):

```
# secretos.json (se borra al leerlo): { "adminPassword": "...", "certPassword": "..." }
powershell -ExecutionPolicy Bypass -File Install-MiniLIS.ps1 -Action Check -Mode Servidor -Port 443
powershell -ExecutionPolicy Bypass -File Install-MiniLIS.ps1 -Action Configure `
    -InstallDir "C:\Program Files\MiniLIS" -DataDir "E:\MiniLIS" -Mode Servidor -Port 443 `
    -HostNames "minilis.hospital.local;minilis" -CertPfxPath "D:\certs\minilis.pfx" `
    -SecretsFile "D:\temp\secretos.json"
```

Las contraseñas nunca van como argumentos (serían visibles en la lista de procesos), sino en
el fichero de secretos.

---

## 12. Solución de problemas

| Síntoma | Causa probable y solución |
|---|---|
| La comprobación marca **ERROR en el puerto** | Otro programa (IIS, otro servicio web) usa ese puerto. Elija otro, p. ej. 8443, o detenga el otro programa. |
| El navegador avisa de **conexión no segura** | Certificado autofirmado, o el nombre usado no está en el certificado. Instale el del hospital (apartado 9). |
| Error **400 Bad Request** | Se ha entrado con un nombre que no está en `AllowedHosts`. Añádalo (apartado 9). |
| El servicio **no arranca** | Visor de eventos → Aplicación → origen MiniLIS. Lo más habitual: el certificado no se encuentra o el servicio no puede leer su clave privada (apartado 9). |
| Todos los usuarios **pierden la sesión** tras reiniciar | La carpeta `<datos>\keys` no es accesible para el servicio. Vuelva a ejecutar el instalador (repara los permisos). |
| La **copia de seguridad falla** con acceso denegado | La cuenta del servicio no puede escribir en la carpeta de copias (ver apartado 6, paso 3). |
| No encuentra los **ficheros FCS** | La cuenta `NT SERVICE\MiniLIS` no tiene permiso de lectura en esa carpeta. Si es una carpeta de red, la cuenta virtual accede como la cuenta del equipo (`DOMINIO\SERVIDOR$`): el permiso hay que darlo a esa cuenta. |
| La instalación termina con errores | El resultado indica qué ha fallado. El detalle está en `<datos>\logs\instalacion.log` y en el registro del asistente (`%TEMP%\Setup Log *.txt`). Corregido el problema, vuelva a ejecutar el instalador. |

---

## 13. Seguridad (ENS / RGPD)

- **Cifrado en reposo.** La base de datos no va cifrada por sí misma. Antes de tratar datos
  reales de pacientes, la carpeta de datos debe estar en un volumen cifrado (BitLocker). El
  instalador avisa si no lo está.
- **Copias de seguridad.** Van cifradas (AES-256). Custodie la clave fuera del servidor
  (apartado 4).
- **Permisos.** La carpeta de datos solo es accesible para administradores, *SYSTEM* y el
  servicio. No añada otros usuarios.
- **Acceso.** Solo por HTTPS. En modo servidor el cortafuegos se abre solo para redes de
  dominio y privadas, nunca para redes públicas.
- **Identidad.** Los usuarios son locales de MiniLIS. La integración con el directorio
  corporativo (LDAP/AD) está pendiente (ver README, *Estado del despliegue*).
