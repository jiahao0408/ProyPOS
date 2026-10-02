# StarSeaPOS — TPV de escritorio para bazar

TPV (punto de venta) de escritorio para PC, pensado para una **tienda de bazar**. Gestiona **ventas, cobro, productos y precios, tickets, facturas, caja y Verifactu**. **No lleva stock:** la tienda no depende de las existencias. Es multidioma y modular, funciona **sin conexión** y sincroniza con un backend opcional cuando hay red.

> **Estado:** **MVP completo: secciones 1 a 5** (Sprints 1 a 5, las 34 historias M): login con PIN, roles, catálogo, caja, idiomas y formatos regionales; venta con escáner, ticket, cobro en efectivo, tarjeta o mixto, artículo genérico y alta rápida. Impresora térmica ESC/POS, tickets con reimpresión, factura simplificada y completa, facturar un ticket ya emitido y PDF. Cierre Z, etiquetas, importación de catálogo y copias de seguridad. Verifactu (huella encadenada, QR, certificado, envío a la AEAT y cola sin conexión), declaración responsable e instalador (`StarSeaPOS-Setup.exe` y MSI). **Versión 2 en curso:** sección 6 (descuentos, devoluciones, rectificativas, cambios, auditoría) sección 7 (cambio masivo de precios, variantes, verificador de precios) sección 8 (informe de ventas, exportaciones y registro de facturación), sección 9 (pantalla de cliente y cajón) y sección 10 (idioma por usuario y por ticket, modalidad VERI\*FACTU / No VERI\*FACTU, panel de Verifactu con subsanaciones y actualizaciones automáticas) hechas: **versión 2 completa**. 365 tests. Pendiente de homologar contra el entorno de pruebas de la AEAT. Especificación completa en [docs/user-stories.md](docs/user-stories.md).

---

## Índice

1. [Alcance del MVP](#alcance-del-mvp)
2. [Roles](#roles)
3. [Módulos](#módulos)
4. [Arquitectura](#arquitectura)
5. [Stack tecnológico](#stack-tecnológico)
6. [Estructura del proyecto](#estructura-del-proyecto)
7. [Primeros pasos](#primeros-pasos)
8. [Idiomas](#idiomas)
9. [Verifactu](#verifactu)
10. [Requisitos no funcionales](#requisitos-no-funcionales)
11. [Roadmap](#roadmap)
12. [Decisiones abiertas](#decisiones-abiertas)
13. [Contribuir](#contribuir)

---

## Alcance del MVP

**Dentro:** vender, cobrar y cerrar caja desde el PC del mostrador, en el idioma de cada usuario. Incluye las funciones propias de un bazar (escáner, artículo genérico, alta rápida, etiquetas) y el envío de facturas a la AEAT con Verifactu.

**Fuera del MVP:** tienda online, fidelización avanzada y multi-local.

**Sin stock:** la tienda no controla existencias. Se han quitado el módulo de inventario (INV-01 … INV-06), las entradas de mercancía, los proveedores y la compra por cajas (BAZ-04). Las tablas y columnas antiguas siguen en la base de datos sin uso, para no perder datos de instalaciones anteriores.

El MVP son las **34 historias con prioridad M**, repartidas en **5 sprints de 2 semanas**.

## Roles

| Rol | Qué hace | Permisos clave |
|---|---|---|
| **Administrador** | Configura negocio, productos, precios, impresora y pantalla; ve reportes | Todo: usuarios, precios, impuestos, devoluciones, facturas rectificativas |
| **Cajero** | Vende, cobra, imprime tickets y emite facturas | Vender, cobrar, imprimir, consultar precios. **No** cambia precios ni anula ventas cerradas |

Cuando un cajero intenta una acción de administrador, la app pide el PIN de un administrador (USR-02).

## Módulos

| # | Módulo | Prefijo | Historias | MVP (M) |
|---|---|---|---|---|
| 1 | Ventas y cobro | `VEN` | 6 | VEN-01 … VEN-04 |
| 2 | Productos y precios | `PRE` | 4 | PRE-01, PRE-02 |
| 3 | ~~Inventario~~ (quitado: no se lleva stock) | `INV` | 6 | — |
| 4 | Impresión de tickets | `IMP` | 4 | IMP-01 … IMP-03 |
| 5 | Facturación | `FAC` | 6 | FAC-01, FAC-02, FAC-04, FAC-06 |
| 6 | Caja | `CAJ` | 3 | CAJ-01, CAJ-02 |
| 7 | Hardware | `HW` | 4 | HW-01, HW-04 |
| 8 | Usuarios y seguridad | `USR` | 3 | USR-01, USR-02 |
| 9 | Idiomas y extensibilidad | `CFG` | 6 | CFG-01, CFG-04 |
| 10 | Conexión con Verifactu | `VFA` | 6 | VFA-01, VFA-02, VFA-03, VFA-06 |
| 11 | Importar y exportar datos | `DAT` | 5 | DAT-01, DAT-03 |
| 12 | Funciones de bazar | `BAZ` | 7 | BAZ-01 … BAZ-03 (BAZ-04 quitada) |
| | **Total** | | **60** | **34** |

Prioridades: **M** = imprescindible para el MVP · **S** = segunda versión · **C** = deseable.

Detalle completo con criterios de aceptación: [docs/user-stories.md](docs/user-stories.md).

## Arquitectura

La app funciona sola en el PC; la nube es opcional. **La base de datos local es la fuente de verdad** en el dispositivo; el backend solo replica y envía los registros de facturación a la AEAT.

```mermaid
flowchart TB
    subgraph APP["App de escritorio (C# .NET 8 + Avalonia)"]
        UI["Interfaz gráfica multidioma<br/>Venta · Cobro · Productos · Precios · Facturas · Caja"]
        BL["Lógica de negocio (MVVM + módulos)<br/>Precios e IVA · numeración de facturas · permisos por rol"]
        DB[("Base de datos local<br/>SQLite + EF Core + SQLCipher")]
        UI --> BL --> DB
    end

    subgraph HW["Hardware"]
        PRN["Impresora térmica<br/>ESC/POS · USB, COM o red"]
        CDS["Pantalla de cliente<br/>2º monitor o visor COM"]
        DRW["Cajón portamonedas<br/>vía impresora"]
        SCN["Lector de códigos<br/>USB modo teclado"]
    end

    BL -- drivers --> HW
    DB <-. "sincroniza cuando hay red" .-> API["Backend opcional<br/>API REST + PostgreSQL"]
    API --> AEAT["AEAT · Verifactu"]
```

Tres capas (UI → lógica → datos) y cuatro periféricos.

## Stack tecnológico

| Pieza | Tecnología |
|---|---|
| Lenguaje y UI | C# + .NET 8, [Avalonia UI](https://avaloniaui.net/), patrón MVVM |
| Idiomas | Ficheros JSON por idioma, cargados en tiempo de ejecución |
| Módulos | Un proyecto por área con interfaces comunes; inyección de dependencias |
| Base de datos | SQLite + Entity Framework Core con migraciones; cifrado con SQLCipher |
| Verifactu | Cliente del servicio web SOAP de la AEAT con certificado X.509; huella SHA-256 encadenada; QR de cotejo; cola de envíos en la BD |
| Importar / exportar | CSV y Excel (ClosedXML o similar); copia de seguridad = archivo SQLite cifrado y comprimido |
| Impresora | Librería ESC/POS para .NET (USB, serie COM o red) |
| Pantalla de cliente | Segunda ventana en monitor secundario, o visor por COM |
| Códigos de barras | Lector USB en modo teclado |
| Backend (opcional) | ASP.NET Core o Node.js + PostgreSQL |
| Entrega | Instalador `.exe` (y MSI) para Windows 11 con actualización automática |

**Alternativa web:** Tauri (Rust) + React con i18next. Más ligera, pero con más trabajo para acceder a puertos serie e impresoras.

## Estructura del proyecto

```text
StarSeaPOS/
├── StarSeaPOS.sln
├── Directory.Build.props        # net8.0, nullable, usings implícitos para todos los proyectos
├── Directory.Packages.props     # Versiones de NuGet centralizadas
├── global.json                  # SDK .NET 8 o más nuevo
├── src/
│   ├── Pos.App/                 # Shell Avalonia: ventanas, MVVM, DI (Composition.cs), temas
│   ├── Pos.Core/                # Dominio, IModule, ILocalizer, PinHasher, cálculo de IVA
│   ├── Pos.Data/                # EF Core, PosDbContext, migraciones, SQLCipher
│   ├── Pos.Localization/        # JsonLocalizer (locales/*.json), formatos regionales e idioma de impresión (CFG)
│   └── Modules/
│       ├── Pos.Modules.Sales/        # VEN
│       ├── Pos.Modules.Products/     # PRE
│       ├── Pos.Modules.Printing/     # IMP
│       ├── Pos.Modules.Invoicing/    # FAC
│       ├── Pos.Modules.CashRegister/ # CAJ
│       ├── Pos.Modules.Hardware/     # HW
│       ├── Pos.Modules.Users/        # USR
│       ├── Pos.Modules.Verifactu/    # VFA
│       ├── Pos.Modules.DataTransfer/ # DAT
│       └── Pos.Modules.Bazaar/       # BAZ
├── locales/                     # es.json, ca.json, en.json, zh.json, de.json, fr.json
├── tests/
│   ├── Pos.Core.Tests/          # PIN, IVA, traducciones completas (incluidas las claves usadas en el código)
│   ├── Pos.Modules.Tests/       # Servicios de negocio sobre SQLite real (en memoria y cifrada en disco)
│   └── Pos.App.Tests/           # Recorridos de interfaz headless (Avalonia) y capturas de pantalla
├── docs/
│   └── user-stories.md
└── README.md
```

Regla de dependencias: los módulos dependen de `Pos.Core`, `Pos.Data` y `Pos.Localization`, nunca entre sí ni de `Pos.App`. Cuando un módulo necesita algo de otro, se comunica por una interfaz del núcleo (`IRawPrinter`, `IInvoiceDocuments`) o un punto de extensión de datos (`ISaleHook`: la facturación se engancha al cobro sin que el módulo de ventas la conozca). Cada módulo registra sus servicios en `ConfigureServices`; las vistas y ViewModels viven en `Pos.App`. CFG no es un módulo aparte: vive en `Pos.Localization` y en la página de Ajustes.

### Cómo está hecha la sección 1

| Historia | Dónde | Notas |
|---|---|---|
| USR-01 PIN de 4 dígitos | `UserService.SignIn`, `LoginView`, `PinPadView` | Tocar el nombre y teclear el PIN (táctil o teclado). Tras 5 fallos, bloqueo de 5 minutos; un admin puede desbloquear antes. Cierre de sesión por inactividad configurable (5 min por defecto) |
| USR-02 Roles | `WorkspaceViewModel.Navigate`, `AdminPinPromptView` | Productos, Categorías, Usuarios y Ajustes son de admin: a un cajero se le pide el PIN de un admin para esa visita. No se puede quitar el último admin |
| PRE-01 Productos | `CatalogService`, `ProductsPageView` | Nombre, precio con IVA incluido, IVA (21/10/4/0 %), código único, categoría, foto, unidades por caja. Los productos no se borran: se desactivan |
| PRE-02 Categorías | `CatalogService`, `CategoriesPageView`, `SalePageView` | Botones de color en la venta. Una categoría puede marcarse como sección de artículo genérico (BAZ-02) |
| CAJ-01 Abrir caja | `CashRegisterService`, `SalePageView` | Con la caja cerrada, la pantalla de venta solo deja abrirla |
| CFG-01 Idioma | `JsonLocalizer`, `SettingsPageView` | Cambia todos los textos sin reiniciar. Un test falla si el código usa una clave que no existe |
| CFG-04 Región | `RegionFormatter` | Euro por defecto; separadores según el idioma; formato de fecha elegible. `12.50` y `12,50` se leen igual al teclear importes |

Primer arranque: con la BD vacía, la app pide crear el administrador.

### Cómo está hecha la sección 2

| Historia | Dónde | Notas |
|---|---|---|
| VEN-01 Añadir productos | `SalePageView`, `CatalogService.Search` | Por categoría (botones), búsqueda por nombre o código de barras. `3*código` añade 3 unidades |
| VEN-02 Cantidades y líneas | `Ticket` (módulo Sales) | El ticket vive en memoria hasta cobrar: quitar una línea no deja rastro. Botones −, + y ✕ |
| VEN-03 Efectivo | `PaymentView`, `SalesService.Checkout` | Muestra el cambio al teclear; botones de importe justo y billetes; vacío = importe justo. No deja cobrar menos del total |
| VEN-04 Tarjeta y mixto | `PaymentRequest` | Se guarda el importe de cada método; la suma es siempre el total |
| HW-04 Escáner | `SalePageView.axaml.cs` | Lector USB en modo teclado: todo lo tecleado fuera de un cuadro de texto va al buscador, así no se pierde ningún escaneo. Código desconocido = aviso + alta rápida |
| BAZ-02 Artículo genérico | Botones de sección | Categorías marcadas como sección: importe libre, IVA general (21 %), cuenta en las ventas por sección |
| BAZ-03 Alta rápida | `QuickCreateView`, `CatalogService.QuickCreate` | Nombre, precio y sección con el código ya relleno. Si lo crea un cajero queda pendiente de revisión (filtro en Productos) |

Atajos: **F12** cobrar · **Esc** cerrar la ventana de cobro / alta rápida · **Enter** confirmar.

**Seguridad de los datos de venta:**
- Venta, líneas, pagos y factura se guardan en **una sola transacción**: o se guarda todo o nada.
- SQLite con `synchronous = FULL`: cuando la app dice "cobrada", la venta ya está en disco aunque se vaya la luz.
- Triggers en la BD impiden **modificar o borrar** ventas, líneas y pagos (datos de facturación).
- La línea guarda nombre, precio e IVA del momento: cambiar el producto después no altera ventas pasadas.

### Cómo está hecha la sección 3

| Historia | Dónde | Notas |
|---|---|---|
| FAC-01 Factura simplificada | `InvoiceSaleHook`, `InvoiceIssuer` | Cada venta genera su factura **en la misma transacción**. Serie por tipo y año (`T2026`), número correlativo sin huecos (un cobro fallido no gasta número; índice único serie+número). Desglose de base, cuota y total por tipo de IVA |
| FAC-02 Factura completa | Casilla en la ventana de cobro | NIF validado con su dígito de control (DNI, NIE, CIF), razón social y dirección. Serie `F2026`. El cliente se guarda y se rellena solo al teclear su NIF |
| FAC-06 Facturar un ticket | Página **Tickets** | Busca por número, fecha o escaneando el QR del ticket. Emite una factura completa que sustituye y referencia a la simplificada; un ticket solo se factura una vez (índice único). Se imprime y se puede guardar en PDF |
| IMP-01 Imprimir al cobrar | Ajustes > Impresora | Sí / no / preguntar. Se imprime en segundo plano: si la impresora falla, la venta ya está guardada y solo se avisa |
| IMP-02 Reimprimir | Página **Tickets** | Por número o fecha; la reimpresión lleva `*** COPIA ***` |
| IMP-03 Cabecera y pie | Ajustes > Negocio y ticket | Logo, nombre, NIF, dirección, teléfono y mensaje, con vista previa en vivo. Sin datos fiscales válidos no se puede cobrar |
| HW-01 Impresora térmica | `RawPrinter`, `EscPosEncoder` | ESC/POS por **USB** (impresora instalada en Windows, envío RAW), **COM** (también Bluetooth emparejado, que Windows expone como COM), **red** (puerto 9100) o **fichero** (sin impresora). Papel de 58 u 80 mm. Juego de caracteres occidental (PC858: €, ñ, acentos) o chino (GB18030). Botón de prueba |

Los datos de facturación (facturas y desgloses) tampoco se pueden modificar ni borrar: triggers en la BD.

### Cómo está hecha la sección 4

| Historia | Dónde | Notas |
|---|---|---|
| CAJ-02 Cierre con arqueo | Botón **Cerrar caja** en Venta | Muestra ventas, efectivo y tarjeta, efectivo esperado (fondo + cobros en efectivo) y el descuadre en vivo al teclear lo contado. Guarda una foto de los totales con número Z correlativo e imprime el cierre Z. No deja cerrar con un ticket a medias |
| BAZ-01 Etiquetas | Página **Etiquetas**, `LabelService` | Cantidad por producto; imprime nombre, precio y código de barras (EAN-13 o CODE128). Los productos sin código reciben uno interno EAN-13 que empieza por **29** (rango reservado para uso interno) y ya se pueden escanear. Impresora de etiquetas propia o la de tickets |
| DAT-01 Importar | Página **Datos y copias** | Productos (nombre, precio, IVA, código, categoría) y clientes. Las columnas de más (por ejemplo, el stock de una plantilla antigua) se ignoran. CSV (`;` o `,`, UTF-8 o el Windows-1252 de Excel en español) o Excel. Plantilla descargable, vista previa con el error de cada fila; los códigos o NIF duplicados no se importan; las categorías que no existen se crean |
| DAT-03 Copia y restauración | Página **Datos y copias**, `BackupService` | Fichero `.sspos` cifrado con contraseña (AES-256-GCM, PBKDF2 600.000 iteraciones): se puede guardar en un USB o en OneDrive y **restaurar en otro PC**. Restaurar pide confirmación y antes guarda una copia del estado actual |
| Copia diaria automática | Al arrancar | Copia cifrada en `%LOCALAPPDATA%\StarSeaPOS\backups`, una al día, se guardan las 7 últimas |

**Rendimiento de la BD cifrada:** la clave del PC son 32 bytes aleatorios, así que se abre con la clave "en bruto" de SQLCipher (`x'…'`), sin la derivación PBKDF2 que está pensada para contraseñas humanas. Cada conexión pasa de ~1 s a milisegundos.

### Cómo está hecha la sección 5 (Verifactu)

| Historia | Dónde | Notas |
|---|---|---|
| FAC-04 Huella y QR | `VerifactuHash`, `VerifactuRecorder`, `VerifactuQr` | Cada factura genera, en la misma transacción, su registro de alta con huella SHA-256 encadenada a la anterior (algoritmo de la Orden HAC/1177/2024; un test lo comprueba contra el ejemplo oficial de la AEAT). Tipo F2 (ticket), F1 (factura completa) o F3 (completa que sustituye a un ticket, FAC-06). Registros y huellas no se pueden modificar ni borrar (triggers). El ticket lleva el QR de cotejo de la AEAT y, con el envío activado, la leyenda VERI\*FACTU |
| VFA-01 Certificado | Página **Verifactu** | Carga del `.pfx` con su contraseña; se guarda cifrado con DPAPI dentro de la BD cifrada. Muestra titular, NIF y caducidad. Entorno de pruebas o producción y botón **Probar conexión** (TLS con el certificado contra el servicio web de la AEAT) |
| VFA-02 Envío | `VerifactuXml`, `VerifactuSender` | Mensaje SOAP `RegFactuSistemaFacturacion` (hasta 1000 registros) con certificado de cliente. Se guarda la respuesta de cada registro: aceptado, aceptado con errores o rechazado, con su código de error |
| VFA-03 Cola sin conexión | `VerifactuQueue` | Cola persistente en la BD; un proceso en segundo plano intenta enviar cada 30 s. Respeta el `TiempoEsperaEnvio` de la AEAT; sin conexión reintenta con esperas crecientes (hasta 1 h). La venta nunca espera a la AEAT |
| VFA-06 Declaración responsable | Página **Acerca de** | Texto con sistema, versión, productor, NIF, dirección, fecha y lugar (art. 13 de la Orden HAC/1177/2024). Los datos del productor se leen de `producer.json` junto al ejecutable |
| Instalador | `installer/` | `StarSeaPOS-Setup-<versión>.exe` (bundle WiX Burn: comprueba Windows 11 x64, instala y ofrece abrir la app) y el MSI que lleva dentro (WiX 5) por máquina, solo Windows 11, con la app *self-contained* (no hace falta instalar .NET), accesos directos en el menú Inicio y el escritorio. Desinstalar no borra los datos. `.\installer\build-installer.ps1 -Version 1.0.0` |

**Antes de usar Verifactu en producción:**
1. Completar `src\Pos.App\producer.json` con los datos reales del productor del software (la página Acerca de avisa mientras falten). Ese fichero **no se sube a git** (lleva NIF y dirección personales): en el repositorio solo está la plantilla `producer.example.json`, que se copia como `producer.json` en cada PC donde se compile el instalador.
2. Cargar el certificado y probar en el **entorno de pruebas** de la AEAT: enviar facturas de prueba y revisar que se aceptan. El XML sigue los esquemas `SuministroLR.xsd` / `SuministroInformacion.xsd`, pero no se ha podido validar contra el servidor real sin certificado.
3. Activar el envío y pasar a producción.

### Cómo está hecha la sección 6 (devoluciones, descuentos y cambios)

| Historia | Dónde | Notas |
|---|---|---|
| VEN-05 Descuentos | Botón **%** en cada línea y **Descuento** en Venta | Por línea (porcentaje o importe) y sobre el total del ticket. El cajero puede llegar al límite configurado en Ajustes (10 % por defecto); por encima hace falta el PIN de un administrador, y si se cancela se deshace el descuento. El ticket impreso muestra el precio de tarifa y una línea "Dto." |
| VEN-06 Devoluciones | Botón **Devolver** en Tickets, `SalesService.Return` | Devolución total o parcial con motivo obligatorio. Reembolso en efectivo o con tarjeta. El cajero necesita el PIN de un administrador. No deja devolver más unidades de las vendidas, aunque se haga en varias veces. Con descuentos, la última unidad devuelve exactamente lo que quedaba por devolver (sin céntimos de más ni de menos) |
| FAC-03 Rectificativas | `InvoiceSaleHook`, serie **R** | Cada devolución emite una factura rectificativa por diferencias con número correlativo propio (R2026-000001…), que indica la factura original y el motivo. Lleva el mismo cliente que la original. En Verifactu va como **R5** si rectifica un ticket y como **R1** si rectifica una factura completa, con `TipoRectificativa` "I" y la referencia a la factura rectificada |
| BAZ-07 Cambios y ticket regalo | Botones **Cambio** y **Ticket regalo** en Tickets | Un cambio es una devolución más una venta nueva. Lo devuelto se paga como **vale** en la venta nueva. Si el cliente debe dinero, lo paga; si la tienda debe dinero, hace falta el PIN de un administrador si es un cajero. El ticket regalo no lleva precios ni el QR de la AEAT (que lleva el importe), pero sí el número de factura en un QR para encontrar la venta |
| USR-03 Auditoría | Página **Auditoría** (solo administrador), `AuditLog` | Registra los descuentos, las devoluciones y los cambios con el usuario y quién autorizó. Las entradas no se pueden modificar ni borrar (triggers) |

**Cuidado con las migraciones:** cuando EF Core tiene que reconstruir una tabla en SQLite (por ejemplo, para añadir una clave ajena), la borra y la vuelve a crear, y **los triggers de inmutabilidad se pierden sin aviso**. EF hace esas reconstrucciones al final de la migración, así que recrear los triggers en la misma migración no basta. Hay que hacerlo en una migración posterior con `BillingTriggers.Recreate`. El test `MigrationSafetyTests` aplica todas las migraciones y comprueba que cada tabla de facturación sigue protegida.

### Cómo está hecha la sección 7 (precios, variantes y verificador)

| Historia | Dónde | Notas |
|---|---|---|
| PRE-03 Cambio masivo de precios | Página **Precios** (solo administrador), `PriceService` | Categoría o todo el catálogo; por porcentaje o por importe; un valor negativo baja los precios. Primero la **vista previa** (precio actual → nuevo) y luego **Aplicar**. Redondeo a céntimos; no deja ningún precio por debajo de cero. Queda en la auditoría |
| BAZ-05 Variantes | Ficha del producto (sección **Variantes**), `CatalogService.SaveVariant` | Talla, color o modelo. Cada variante es un producto con su código de barras y, si se indica, su propio precio; si no, el del producto. Comparten IVA, categoría, foto y ubicación. En la venta el producto sale una vez y al tocarlo se elige la variante; escanear el código de una variante la añade directamente. En el ticket y la factura sale con su nombre completo ("Camiseta · Roja XL") |
| BAZ-06 Verificador de precios | Botón **Consultar precio (F9)** en Venta | Se escanea o se busca el producto y se ve el precio, la **ubicación en la tienda** (campo nuevo en la ficha), la categoría y las variantes con su precio. No añade nada al ticket |
| Idioma de impresión | **Ajustes** → Idioma de impresión de los tickets, `PrintLocalization` | Los tickets, las facturas en PDF y el cierre Z se imprimen en su propio idioma, distinto del de la aplicación (por ejemplo, la app en chino y los tickets en español). Sin elegir, se imprime en el idioma de la aplicación. La moneda y el formato de fecha son los mismos |

Al cambiar el precio del producto, las variantes que tenían el mismo precio lo siguen; las de precio propio lo mantienen. Desactivar el producto desactiva sus variantes.

### Cómo está hecha la sección 8 (informes y exportaciones)

| Historia | Dónde | Notas |
|---|---|---|
| CAJ-03 Informe de ventas | Página **Informe de ventas** (solo administrador), `SalesReportService` | Hoy, ayer, este mes o dos fechas cualesquiera. Tickets, vendido, devoluciones, neto y ticket medio; ventas por producto (unidades e importe), por forma de pago y por cajero. Las devoluciones restan; el vale de un cambio no cuenta como dinero |
| DAT-02 Exportar datos | **Datos y copias** → Exportar, `ExportService` | Productos, clientes y ventas (una fila por línea, con filtro de fechas) a Excel o CSV, con los encabezados en el idioma de la aplicación. En Excel los importes son números (se pueden sumar). Lo exportado de productos y clientes se puede volver a importar. Sin stock: la tienda no lo lleva |
| FAC-05 Facturas para la gestoría | **Datos y copias** → Exportar, `ExportService`, `InvoiceBookPdf` | Excel/CSV con una fila por factura (base y cuota de cada tipo de IVA) y una fila de totales; y el **libro de facturas emitidas** en PDF con los totales por tipo de IVA. Un ticket que luego se facturó (FAC-06) sale tachado y no suma: si no, se contaría dos veces |
| DAT-04 Registro de facturación | **Datos y copias** → Registro de facturación, `BillingRecordExport` | ZIP con todas las facturas, todos los registros de Verifactu con su huella, un manifiesto con la SHA-256 de cada fichero y un LEEME. **Verificar exportación** comprueba que ningún fichero ha cambiado, recalcula cada huella y el encadenamiento, compara los importes con las facturas y comprueba que la cadena es la de esta base de datos |

**Por qué no se puede alterar sin que se note (DAT-04):** cambiar un importe rompe la huella de ese registro y la de todos los siguientes. Si alguien recalcula la cadena entera, la huella final ya no coincide con la de la base de datos (ni con la que tiene la AEAT, que recibió cada registro). La huella final sale en pantalla al exportar para poder anotarla.

### Cómo está hecha la sección 9 (hardware)

| Historia | Dónde | Notas |
|---|---|---|
| HW-02 Pantalla de cliente | `CustomerDisplayViewModel`, `CustomerDisplayWindow`; se activa en **Impresora y cajón** | Ventana a pantalla completa en el **segundo monitor** (si solo hay uno, ventana normal para arrastrarla al visor). Muestra cada línea y el total mientras se vende, "¡Gracias!" con el total y el cambio al cobrar, y el nombre de la tienda con un mensaje de bienvenida en reposo. Los textos salen en el idioma de impresión (el del cliente) |
| HW-03 Cajón portamonedas | `PrintService.OpenDrawerAsync`, botón **Abrir cajón** en Venta | Pulso ESC/POS `ESC p 0 25 250` por la impresora de tickets (conector RJ11). Se abre solo al cobrar algo en efectivo (opción en Impresora y cajón; con tarjeta no). A mano, sin venta: el cajero necesita el PIN de un administrador y queda en la auditoría. Botón **Probar cajón** |

### Cómo está hecha la sección 10 (idiomas, Verifactu y actualizaciones)

| Historia | Dónde | Notas |
|---|---|---|
| CFG-02 Idioma por usuario | Selector de idioma en la barra superior, `UserLanguage` | Cada usuario elige su idioma y se guarda en su ficha; al entrar con su PIN la interfaz cambia a ese idioma y al salir vuelve al de la tienda (Ajustes). El cambio de uno no afecta a los demás |
| CFG-03 Ticket en el idioma del cliente | Selector **Idioma del ticket** en la ventana de cobro | Por defecto, el idioma de impresión de Ajustes. Solo cambian los textos del ticket: los datos fiscales (emisor, NIF, número, importes) son los mismos |
| VFA-04 Modalidad | Página **Verifactu**, `VerifactuModeService`, `VerifactuSigner`, `XadesSigner` | **VERI\*FACTU:** los registros se envían a la AEAT. **No VERI\*FACTU:** no se envían; cada registro se firma con el certificado (XAdES enveloped, RSA-SHA256, con hora de firma y huella del certificado) y se lleva un **registro de eventos** encadenado (arranques, cambios de modalidad, exportaciones, anomalías como registros sin firmar o exportaciones alteradas). Firmas y eventos no se pueden modificar ni borrar (triggers). El cambio de modalidad queda en la auditoría y en los eventos; si este año ya se ha enviado algo en VERI\*FACTU no se puede volver a No VERI\*FACTU hasta el 1 de enero. El registro de facturación exportado (DAT-04) incluye los eventos |
| VFA-05 Panel de envíos | Página **Verifactu** | Filtro por estado (pendientes, aceptados, con errores, rechazados, no enviados). Los rechazados y los aceptados con errores tienen **Subsanar y reenviar**: se genera un registro nuevo de la misma factura con `Subsanacion = S` (y `RechazoPrevio = S` si fue rechazado), encadenado al último y en la cola. El original no se toca |
| CFG-06 Actualizaciones automáticas | `UpdateService`, **Acerca de**, aviso en la barra superior | Al arrancar mira `update.json` (versión, dirección del MSI y su SHA-256) y avisa si hay versión nueva. Un administrador la instala: se descarga el MSI, se comprueba su SHA-256 (si no coincide, no se instala), se hace una **copia de seguridad** de la base de datos y se lanza el instalador, que sustituye la versión anterior sin tocar los datos. La versión nueva aplica las migraciones al arrancar. `build-installer.ps1` genera el `update.json` junto al MSI |

**Ojo con la modalidad por defecto:** mientras no se elija VERI\*FACTU (con el certificado cargado), la aplicación funciona como **No VERI\*FACTU**: los registros no se envían, se firman en cuanto haya certificado y quedan guardados. Los registros generados en una modalidad se quedan en ella: pasar a VERI\*FACTU no envía los anteriores.

### Versión 1.1: compatibilidad de periféricos (rama `v1.1`)

| Periférico | Qué se ha añadido | Dónde |
|---|---|---|
| Impresora térmica | **Modelo** con ajustes de compatibilidad: Epson TM y compatibles, genérica de 80 mm (Xprinter, HPRT…), genérica de 58 mm sin cortador o personalizada. **Tabla de caracteres** (PC858, Windows-1252, PC850, PC437): lo que no existe en la tabla se sustituye (€ → EUR, á → a) en vez de salir como «?». **Corte** parcial, total o sin cortador. **QR como imagen** para impresoras sin el comando de QR. **Control de flujo** (XON/XOFF, RTS/CTS, DTR/DSR) y envío por bloques en impresoras COM. La prueba de impresión muestra los ajustes | Impresora, `EscPosEncoder`, `QrRaster`, `RawPrinter` |
| Cajón | **Pin 2 o 5** y cajón con **puerto COM propio** (disparadores USB/serie) | Impresora > Cajón, `PrintService.OpenDrawerAsync` |
| Visor de cliente | **Visor de 2 × 20** por puerto serie o USB-COM, protocolo **ESC/POS** (Epson DM-D) o **CD5220**: último producto y total, total y cambio al cobrar, bienvenida en reposo. Sin acentos ni € (solo ASCII es fiable en un VFD). Botón **Probar visor** | Impresora > Visor de cliente, `PoleDisplay` |
| Lector de códigos | Lectores **serie o USB-COM** (los de teclado siguen sin configurar). En el verificador de precios el código va al verificador. En modo teclado, el código también puede terminar en **Tab** | Impresora > Lector de códigos, `SerialScanner` |

Todo lo nuevo tiene valores por defecto que dejan el comportamiento de la versión 1.0 igual.

### Versión 1.1.5: atajos de teclado y modo teclado (rama `v1.1.5`)

**Atajos de la pantalla de venta** (por defecto; se cambian en **Ajustes > Atajos de teclado**, con «Restaurar los atajos de fábrica»). **F1** o el botón «Atajos» los muestran.

| Tecla | Acción |
|---|---|
| **Enter** (buscador vacío) / **F12** | Cobrar en efectivo; Enter otra vez = importe justo |
| **+** (buscador vacío) | Cobrar con tarjeta; Enter confirma |
| **Insert** | Con la caja cerrada, abrirla (fondo vacío = 0 €); con la caja abierta, abrir el cajón (el cajero necesita PIN de administrador) |
| **Supr** (buscador vacío) | Quitar la última línea |
| **F8** | Vaciar el ticket |
| **F2** / **F3** | Descuento del ticket / de la última línea |
| **F9** | Consultar precio |
| **F10** | Cambiar modo táctil / teclado |
| **F1** | Ver los atajos |

**En el buscador** (y Enter): `n*` añade n unidades a la última línea; `-n*` las quita (quitarlas todas quita la línea); `2,50` o `2.50` añade un «Artículo» de ese precio (IVA general); `n*2,50` n artículos de ese precio; `n*código` sigue igual.

Las teclas que también sirven para escribir (Enter, Supr, «+») solo actúan con el buscador vacío: el lector de códigos (código + Enter) funciona igual que antes. Los atajos no admiten letras ni números sin Ctrl/Alt ni los caracteres `* - . ,` (son de la sintaxis del buscador).

**Modo de la pantalla de venta** (Ajustes, botón en la venta o **F10**; se recuerda): **táctil** (categorías y botones de productos, como hasta ahora) o **teclado** (ticket grande y leyenda de atajos; los productos solo aparecen al buscar por nombre).

## Primeros pasos

Guía completa para preparar el PC (herramientas, IDE, base de datos, hardware, problemas frecuentes): [entornoDeConfiguracion.md](entornoDeConfiguracion.md).

### Requisitos

- **Windows 11 de 64 bits** (único sistema soportado)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Pantalla de al menos 1366×768, táctil o ratón y teclado
- Opcional: impresora térmica ESC/POS (58 u 80 mm), impresora de etiquetas, lector de códigos USB
- Para Verifactu: certificado digital del negocio (.pfx)

### Compilar y ejecutar

```powershell
git clone <url-del-repo> StarSeaPOS
cd StarSeaPOS
dotnet tool restore          # dotnet-ef, versión fijada en .config/dotnet-tools.json
dotnet build
dotnet run --project src/Pos.App -- --windowed
```

- `--windowed`: ventana maximizada en lugar de pantalla completa (cómodo para desarrollar).
- `$env:STARSEAPOS_DATA_DIR = "<carpeta>"`: usa otra carpeta de datos en vez de `%LOCALAPPDATA%\StarSeaPOS` (útil para empezar con una BD vacía).

### Tests

```powershell
dotnet test

# Además, capturas PNG de las pantallas para revisarlas a ojo:
$env:STARSEAPOS_SCREENSHOTS = "<carpeta>"; dotnet test tests/Pos.App.Tests --filter ScreenshotTests
```

### Base de datos

- Fichero: `%LOCALAPPDATA%\StarSeaPOS\pos.db`, cifrado con SQLCipher.
- Clave: aleatoria, guardada en `pos.key` junto a la BD y protegida con DPAPI (solo esa cuenta de Windows en ese PC puede usarla). Copiar `pos.db` a otro PC no sirve sin la clave: las copias de seguridad (DAT-03) tendrán su propia contraseña.
- Las migraciones se aplican solas al arrancar.
- Copia automática diaria en `backups\` (se guardan 7). Errores no controlados en `logs\errores.log`.

```powershell
# Crear una migración nueva (usa DesignTimeDbContextFactory, no necesita la clave real)
dotnet ef migrations add <Nombre> --project src/Pos.Data
```

## Idiomas

- Todos los textos de la interfaz viven en `locales/<código>.json`, **nunca en el código**. En XAML: `{Binding L[Clave]}`.
- Cada fichero lleva la clave `LanguageName` con el nombre del idioma en su propia lengua (sale en el selector).
- Un test comprueba que todos los idiomas tienen exactamente las mismas claves que `es.json`.
- Añadir un idioma = añadir un fichero; **no hace falta recompilar** (CFG-05).
- El cambio de idioma se aplica **sin reiniciar** (CFG-01).
- Codificación UTF-8 obligatoria (alfabetos como el chino).
- Moneda, fecha y separadores decimales según la región; euro por defecto (CFG-04).
- El **idioma de impresión** de los tickets se elige aparte en Ajustes; sin elegir, es el de la aplicación.
- Los datos fiscales del ticket **no se traducen** (CFG-03).

Idiomas previstos: español, catalán, inglés, chino, alemán y francés.

## Verifactu

Obligatorio desde el **1 de enero de 2027** para sociedades y el **1 de julio de 2027** para autónomos (RDL 15/2025).

- Cada factura lleva **huella SHA-256 encadenada** con la anterior y **QR de cotejo** (FAC-04).
- Los registros de alta y anulación se envían al servicio web SOAP de la AEAT con el certificado del negocio (VFA-01, VFA-02).
- Sin internet, los envíos van a una **cola persistente** en la BD y se reintentan respetando el tiempo de espera que indica la AEAT (VFA-03). **Nunca se deja de vender.**
- La declaración responsable del software se ve en "Acerca de" (VFA-06).
- El MVP solo implementa la modalidad VERI\*FACTU; la modalidad no VERI\*FACTU (VFA-04) es de la versión 2.

## Requisitos no funcionales

| Área | Requisito |
|---|---|
| Plataforma | **Solo Windows 11 de 64 bits.** La especificación pedía dejarla preparada para Linux y macOS; se descartó el 1 oct 2026 |
| Interfaz | Pantalla completa, botones grandes para uso táctil, tema claro/oscuro, atajos F1–F12 para cobrar e imprimir |
| Rendimiento | Añadir producto < 200 ms · cobro + impresión < 3 s · **30.000 productos con búsqueda instantánea por nombre o código** |
| Offline | Ventas, cobro, impresión y facturas funcionan sin red; sincroniza al reconectar |
| Seguridad | PIN con hash · BD local cifrada · los datos de facturación no se pueden borrar |
| Cumplimiento | Factura simplificada y completa según el reglamento español; Verifactu |
| Fiabilidad | Copia de seguridad diaria automática · ninguna venta se pierde si el PC se apaga a mitad |
| Extensibilidad | Módulos con interfaces internas · migraciones versionadas · módulos nuevos sin tocar el núcleo |

## Roadmap

| Fase | Contenido | Historias |
|---|---|---|
| **Sprint 1 — Base** | Login, roles, catálogo, apertura de caja, sistema de idiomas | USR-01, USR-02, PRE-01, PRE-02, CAJ-01, CFG-01, CFG-04 |
| **Sprint 2 — Vender y cobrar** | Venta por escáner, artículo genérico, alta rápida y cobro | VEN-01 … VEN-04, HW-04, BAZ-02, BAZ-03 |
| **Sprint 3 — Imprimir y facturar** | Impresora térmica, tickets, facturas y facturar un ticket ya emitido | HW-01, IMP-01 … IMP-03, FAC-01, FAC-02, FAC-06 |
| **Sprint 4 — Cierre y datos** | Etiquetas de precio, cierre Z, importación de catálogo, copias de seguridad | CAJ-02, DAT-01, DAT-03, BAZ-01 |
| **Sprint 5 — Verifactu** | Huella y QR, certificado, envío a la AEAT, cola sin conexión, pruebas e instalador para Windows | FAC-04, VFA-01, VFA-02, VFA-03, VFA-06 |
| **Sección 6 — Devoluciones y descuentos** ✅ | Descuentos con límite de cajero, devoluciones con rectificativa, cambios, ticket regalo, auditoría | VEN-05, VEN-06, FAC-03, BAZ-07, USR-03 |
| **Sección 7 — Precios y variantes** ✅ | Cambio masivo de precios, variantes por talla y color, verificador de precios, idioma de impresión | PRE-03, BAZ-05, BAZ-06 |
| **Sección 8 — Informes y exportaciones** ✅ | Informe de ventas, exportar datos y facturas, registro de facturación | CAJ-03, DAT-02, FAC-05, DAT-04 |
| **Sección 9 — Hardware** ✅ | Pantalla de cliente, cajón portamonedas | HW-02, HW-03 |
| **Sección 10 — Idiomas, Verifactu y actualizaciones** ✅ | Idioma por usuario, ticket en el idioma del cliente, modalidad y panel Verifactu, actualizaciones automáticas | CFG-02, CFG-03, VFA-04, VFA-05, CFG-06 |
| **Versión 3** | Historial de precios, ticket digital, migración desde otro TPV, módulos ampliables (fidelización, venta online, multi-tienda) | Historias **C** |

## Decisiones abiertas

Puntos de la especificación que conviene cerrar antes de empezar:

- **Sección vs. categoría (BAZ-02, BAZ-03):** las historias de bazar hablan de "secciones" (hogar, papelería, juguetes…) y PRE-02 de "categorías". Propuesta: que sean lo mismo, con una marca para las que admiten artículo genérico.
- **Clientes (DAT-01):** se importan clientes, pero ninguna historia los gestiona. FAC-02 y FAC-06 piden datos fiscales: propuesta de guardar los clientes de esas facturas y reutilizarlos.
- **Búsqueda en 30.000 productos:** un `LIKE '%texto%'` no usa índices. Propuesta: índice FTS5 de SQLite para nombre y búsqueda exacta indexada para el código.
- **Bluetooth (HW-01):** el stack solo cubre USB, serie COM y red. En Windows muchas impresoras Bluetooth se exponen como puerto COM virtual; hay que confirmarlo con el modelo real.
- **Escáner por cámara (HW-04):** implementado el lector USB en modo teclado; la lectura con cámara queda para la versión 2 (exige una librería de visión). Confirmar que basta con el lector.
- **IVA del artículo genérico y del alta rápida (BAZ-02, BAZ-03):** se usa el 21 %. Si alguna sección vende con otro tipo (por ejemplo libros al 4 %), habría que añadir un IVA por sección.
- **Series:** `T` (simplificadas) y `F` (completas) más el año, numeración que vuelve a 1 cada año. Confirmar con la gestoría.
- **Precio sin IVA en la factura completa:** se redondea a 2 decimales por línea; el desglose por tipo (base y cuota) es el que vale fiscalmente.
- **PDF:** QuestPDF con licencia Community (gratuita para empresas con menos de 1 M$ de ingresos anuales).
- **Clientes extranjeros:** la factura completa solo acepta NIF español (DNI, NIE, CIF). Para clientes de otros países haría falta un NIF-IVA europeo u otro identificador.
- **Impresora de etiquetas (BAZ-01):** las etiquetas salen en ESC/POS (nombre, precio, código de barras y corte), que funciona en impresoras térmicas de tickets y en muchas de etiquetas en modo ESC/POS. Las impresoras de etiquetas que solo hablan ZPL o TSPL necesitarían otro formato: confirmar el modelo.
- **Importación:** las columnas se leen por posición (el orden de la plantilla), no por nombre.
- **Copias:** sin la contraseña de la copia no hay forma de recuperarla. Conviene apuntarla en un lugar seguro.
- **Verifactu, homologación:** la huella coincide con el ejemplo oficial de la AEAT, pero el mensaje SOAP completo no se ha probado contra el servidor (hace falta un certificado real). Puntos a confirmar en el entorno de pruebas: formato del importe en el QR (`17.40`), código de "registro duplicado" (se ha supuesto el 3000, que se trata como aceptado) y, si el certificado es de sello, la dirección del servicio (`www10`).
- **Registros de anulación:** el algoritmo está hecho (`VerifactuHash.ForAnulacion`), pero el MVP no anula facturas (las rectificativas, FAC-03, son de la versión 2), así que no se generan.
- **QR en entorno de pruebas:** mientras Verifactu esté en "Pruebas", el QR del ticket apunta al servidor de pruebas de la AEAT. Al pasar a producción apunta al real.
- **No VERI\*FACTU, formato de la firma:** se firma cada RegistroAlta con XAdES enveloped (RSA-SHA256, `SigningTime` y `SigningCertificate`). Hay que validarlo con la AEAT o con un validador XAdES, por si exigen una política de firma (XAdES-EPES) concreta.
- **Subsanaciones (VFA-05):** se envía `Subsanacion = S` y, si el registro fue rechazado, `RechazoPrevio = S`. Confirmar en el entorno de pruebas de la AEAT los valores exactos (la especificación también contempla `RechazoPrevio = X`).
- **Actualizaciones (CFG-06):** por defecto se busca `update.json` en la última publicación (release) del repositorio de GitHub; hay que publicar ahí el MSI y el `update.json` que genera `build-installer.ps1`, o cambiar la dirección en Ajustes.
- **Backend:** ASP.NET Core o Node.js. ASP.NET Core permite compartir modelos con la app en C#.

## Contribuir

- Una rama por historia: `feature/VEN-01-anadir-productos`.
- Mensajes de commit que referencien el ID: `VEN-01: búsqueda por nombre en el ticket`.
- Cada historia se da por terminada cuando cumple **todos** sus criterios de aceptación y tiene tests.
- Ningún texto visible en el código: siempre clave de traducción.

---

Especificación original: [App_POS_Bazar_Especificacion_User_Stories.pdf](App_POS_Bazar_Especificacion_User_Stories.pdf), 1 oct 2026, @Jiahao.
