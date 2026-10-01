# Entorno de configuración

Guía para dejar un PC listo para desarrollar StarSeaPOS: herramientas, compilación, base de datos, hardware y Verifactu.

Para saber qué es el proyecto y cómo está organizado, ver el [README](README.md).

---

## Índice

1. [Resumen rápido](#1-resumen-rápido)
2. [Requisitos del equipo](#2-requisitos-del-equipo)
3. [Instalar las herramientas](#3-instalar-las-herramientas)
4. [Editor o IDE](#4-editor-o-ide)
5. [Clonar, compilar y ejecutar](#5-clonar-compilar-y-ejecutar)
6. [Base de datos local](#6-base-de-datos-local)
7. [Idiomas](#7-idiomas)
8. [Hardware para desarrollo](#8-hardware-para-desarrollo)
9. [Verifactu y certificado digital](#9-verifactu-y-certificado-digital)
10. [Secretos y ficheros que no se suben](#10-secretos-y-ficheros-que-no-se-suben)
11. [Problemas frecuentes](#11-problemas-frecuentes)
12. [Lista de comprobación](#12-lista-de-comprobación)

---

## 1. Resumen rápido

En Windows, con todo por instalar:

```powershell
winget install Git.Git
winget install Microsoft.DotNet.SDK.8
winget install Microsoft.VisualStudioCode   # o Visual Studio 2022 / Rider

# Cerrar y volver a abrir la terminal para que se cargue el PATH
git clone <url-del-repo> StarSeaPOS
cd StarSeaPOS
dotnet --list-sdks          # debe salir una versión 8.0.x
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Pos.App
```

Si todo va bien se abre la app a pantalla completa con el selector de idioma. Para salir, botón **Salir** de la barra superior.

## 2. Requisitos del equipo

| | Mínimo | Recomendado |
|---|---|---|
| Sistema | **Windows 11 de 64 bits** (único sistema soportado) | Windows 11 24H2 o superior |
| Pantalla | 1366×768 | 1920×1080 + un segundo monitor para la pantalla de cliente |
| Memoria | 8 GB | 16 GB |
| Disco | 5 GB libres (SDK, paquetes NuGet, IDE) | SSD |

## 3. Instalar las herramientas

### 3.1 Obligatorias

| Herramienta | Versión | Para qué |
|---|---|---|
| [Git](https://git-scm.com/) | 2.40 o superior | Control de versiones |
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | 8.0.100 o superior (8.0.x) | Compilar, ejecutar y probar |

> **Ojo:** hace falta el **SDK**, no solo el *runtime*. Con solo el runtime, `dotnet build` falla con *"No .NET SDKs were found"*.

El fichero [global.json](global.json) fija el SDK 8.0 y acepta cualquier 8.0.x más reciente (`rollForward: latestFeature`). Si tienes instalado solo el SDK 9 o 10, también falla: instala además el 8.

```powershell
winget install Git.Git
winget install Microsoft.DotNet.SDK.8
```

Sin permisos de administrador, el SDK se puede instalar solo para el usuario con el script oficial (queda en `%USERPROFILE%\.dotnet`, que hay que añadir al PATH):

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
.\dotnet-install.ps1 -Channel 8.0 -InstallDir "$env:USERPROFILE\.dotnet"
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
```

#### Comprobar

```powershell
git --version
dotnet --list-sdks   # debe aparecer 8.0.xxx
```

### 3.2 Herramienta de migraciones (EF Core)

Es una herramienta **local** del repositorio: su versión está fijada en [.config/dotnet-tools.json](.config/dotnet-tools.json). Desde la raíz del proyecto:

```powershell
dotnet tool restore
dotnet ef --version   # 8.0.x
```

### 3.3 Opcionales

```powershell
# Plantillas de Avalonia (para crear ventanas y controles nuevos)
dotnet new install Avalonia.Templates
```

## 4. Editor o IDE

Cualquiera de estos sirve:

| IDE | Versión mínima | Notas |
|---|---|---|
| **Visual Studio 2022** | 17.8 | Carga de trabajo *Desarrollo de escritorio de .NET*. Añadir la extensión *Avalonia for Visual Studio* para la vista previa de XAML |
| **JetBrains Rider** | 2023.3 | Instalar el plugin *AvaloniaRider* |
| **VS Code** | Reciente | Extensiones `ms-dotnettools.csdevkit` y `AvaloniaTeam.vscode-avalonia` |

En VS Code:

```powershell
code --install-extension ms-dotnettools.csdevkit
code --install-extension AvaloniaTeam.vscode-avalonia
```

Abrir siempre la carpeta raíz (o `StarSeaPOS.sln`), no un proyecto suelto, para que se apliquen [Directory.Build.props](Directory.Build.props) y [Directory.Packages.props](Directory.Packages.props).

### Convenciones que el IDE debe respetar

- **Codificación UTF-8** en todos los ficheros, sobre todo en `locales/*.json`.
- **Finales de línea:** los gestiona [.gitattributes](.gitattributes); no hace falta tocar `core.autocrlf`.
- **Versiones de NuGet:** se añaden solo en `Directory.Packages.props`. En los `.csproj` va `<PackageReference Include="Paquete" />` sin versión.

## 5. Clonar, compilar y ejecutar

```powershell
git clone <url-del-repo> StarSeaPOS
cd StarSeaPOS
dotnet tool restore
dotnet build
```

| Acción | Comando |
|---|---|
| Ejecutar la app (pantalla completa, como en la tienda) | `dotnet run --project src/Pos.App` |
| Ejecutar la app en ventana (para desarrollar) | `dotnet run --project src/Pos.App -- --windowed` |
| Pasar los tests | `dotnet test` |
| Sacar capturas de las pantallas | `$env:STARSEAPOS_SCREENSHOTS = "<carpeta>"; dotnet test tests/Pos.App.Tests --filter ScreenshotTests` |
| Compilar en Release | `dotnet build -c Release` |
| Publicar para Windows (un ejecutable) | `dotnet publish src/Pos.App -c Release -r win-x64 --self-contained` |

El ejecutable se llama `StarSeaPOS.exe`. Al compilar se copian los idiomas a `bin/.../locales/`.

## 6. Base de datos local

- Motor: **SQLite cifrado con SQLCipher**, a través de EF Core 8.
- Ubicación: `%LOCALAPPDATA%\StarSeaPOS\` (`pos.db` + `pos.key`).
- Clave: aleatoria, creada en el primer arranque y guardada en `pos.key`, protegida con **DPAPI** (solo la misma cuenta de Windows en el mismo PC la puede leer).
- Las migraciones pendientes se aplican **solas al arrancar** la app.
- La base de datos **siempre** se abre con clave (`PosDatabase.CreateOptions`); sin clave lanza un error.
- Los ficheros `*.db` están en `.gitignore`: nunca se suben, porque contendrán ventas y facturas.

### BD de desarrollo aparte

Para no mezclar pruebas con la BD normal, se puede usar otra carpeta con la variable `STARSEAPOS_DATA_DIR`:

```powershell
$env:STARSEAPOS_DATA_DIR = "C:\temp\starseapos-dev"
dotnet run --project src/Pos.App -- --windowed
```

Con la carpeta vacía, la app arranca en **primer arranque** y pide crear el administrador. Para empezar de cero, borrar la carpeta.

> **Ojo:** si se borra `pos.key`, `pos.db` ya no se puede abrir. Si se cambia de cuenta de Windows, tampoco.

### Migraciones

```powershell
# Crear una migración
dotnet ef migrations add <NombreDescriptivo> --project src/Pos.Data

# Ver las migraciones existentes
dotnet ef migrations list --project src/Pos.Data
```

Las migraciones usan `DesignTimeDbContextFactory`, que crea un `pos-design.db` temporal con una clave de prueba. Ese fichero se puede borrar sin problema.

## 7. Idiomas

Los textos están en [locales/](locales/), un JSON por idioma (`es`, `ca`, `en`, `zh`, `de`, `fr`).

**Añadir un texto nuevo:**

1. Añadir la clave a `es.json`.
2. Añadir la misma clave a **todos** los demás ficheros.
3. Usarla en XAML con `{Binding L[MiClave]}`.
4. Ejecutar `dotnet test`: un test falla si algún idioma tiene claves de más o de menos, si hay textos vacíos o si el código usa una clave que no existe en `es.json`.

**Añadir un idioma:** copiar `es.json` como `<código>.json` (por ejemplo `it.json`), traducir los valores y poner su nombre en `LanguageName`. No hace falta recompilar: basta con dejar el fichero en la carpeta `locales/` junto al ejecutable.

## 8. Hardware para desarrollo

No hace falta hardware para compilar ni para pasar los tests. Para probar los periféricos:

| Periférico | Cómo probar | Sin el aparato |
|---|---|---|
| **Lector de códigos** (HW-04) | Lector USB configurado en **modo teclado** (HID) y con sufijo **Enter** | Escribir el código en el campo de búsqueda y pulsar Enter: es exactamente lo que hace el lector |
| **Impresora térmica** (HW-01) | ESC/POS por USB (instalada en Windows, también con el driver «Generic / Text Only»), puerto COM o red (puerto 9100), papel de 58 u 80 mm. Ajustes > Impresora > Imprimir prueba | Conexión «Sin impresora»: cada ticket se guarda como `.bin` en `%LOCALAPPDATA%StarSeaPOS	ickets`. Se puede mandar a una impresora real con `copy /b ticket.bin \PCImpresora` |
| **Impresora de etiquetas** (BAZ-01) | Depende del modelo (ESC/POS, ZPL o TSPL): **modelo pendiente de decidir** | — |
| **Cajón portamonedas** (HW-03) | Conectado a la impresora; se abre con un pulso ESC/POS | — |
| **Pantalla de cliente** (HW-02) | Segundo monitor, o visor por puerto COM | Ventana normal en el mismo monitor |

### Puertos serie (COM)

- Ver el número de puerto en *Administrador de dispositivos → Puertos (COM y LPT)*. Muchas impresoras Bluetooth también aparecen ahí como puerto COM virtual.

## 9. Verifactu y certificado digital

Solo hace falta a partir del **Sprint 5**.

- Se necesita el **certificado digital del negocio** en formato `.pfx` (o `.p12`) con su contraseña.
- Toda prueba de desarrollo se hace contra el **entorno de pruebas de la AEAT**, nunca contra producción. La app tendrá un selector pruebas / producción (VFA-01).
- El certificado **nunca** se sube al repositorio ni se copia en carpetas compartidas. Dentro de la app se guardará cifrado (VFA-01).
- Las direcciones de los servicios web se toman de la documentación técnica oficial de Verifactu en la sede electrónica de la AEAT, y se guardarán en la configuración, no en el código.

## 10. Secretos y ficheros que no se suben

El [.gitignore](.gitignore) ya excluye:

| Qué | Patrón |
|---|---|
| Compilación | `bin/`, `obj/`, `publish/`, `*.msi` |
| Bases de datos y copias de seguridad | `*.db`, `*.db-shm`, `*.db-wal`, `*.sqlite`, `backups/` |
| Certificados | `*.pfx`, `*.p12` |
| Configuración local y secretos | `appsettings.*.local.json`, `.env*` |
| IDE | `.vs/`, `.idea/`, `*.user` |

Reglas:

- Nada de contraseñas, claves de la BD ni certificados en el código ni en commits.
- Para datos de prueba, usar siempre datos inventados, nunca ventas o NIF reales de la tienda.

## 11. Problemas frecuentes

| Síntoma | Causa | Solución |
|---|---|---|
| `No .NET SDKs were found` | Solo está el runtime | `winget install Microsoft.DotNet.SDK.8` y abrir una terminal nueva |
| `A compatible .NET SDK was not found` / error de `global.json` | Solo hay SDK 9 o 10 | Instalar también el SDK 8 |
| `dotnet` no se reconoce | PATH sin recargar | Cerrar y abrir la terminal (o el IDE) |
| Error NU1008 al restaurar | Un `.csproj` lleva versión en `PackageReference` | Quitar la versión y ponerla en `Directory.Packages.props` |
| Error NU1010 al restaurar | El paquete no está en `Directory.Packages.props` | Añadir allí su `PackageVersion` |
| La app abre pero sin textos (salen las claves) | No se copió `locales/` al compilar | `dotnet build` de nuevo; comprobar que existe `bin/.../locales/es.json` |
| `file is not a database` al abrir la BD | Clave incorrecta, o BD creada sin cifrar | Borrar la BD de desarrollo y volver a crearla |
| La app tapa toda la pantalla y no sé salir | Arranca a pantalla completa | Botón **Salir**, o Alt+F4 |

## 12. Lista de comprobación

- [ ] `git --version` funciona
- [ ] `dotnet --list-sdks` muestra un 8.0.x
- [ ] `dotnet ef --version` muestra un 8.x
- [ ] IDE instalado con la extensión de Avalonia
- [ ] `dotnet build` termina sin errores
- [ ] `dotnet test` pasa todos los tests
- [ ] `dotnet run --project src/Pos.App` abre la app y el selector cambia el idioma
- [ ] (Si se trabaja con hardware) el lector escribe el código y pulsa Enter en un bloc de notas
