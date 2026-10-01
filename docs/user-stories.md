# User stories — ProyPOS (bazar)

60 historias en 12 módulos. Formato: *Como [rol], quiero [acción], para [beneficio]*, con criterios de aceptación (CA) verificables.

Prioridad: **M** = imprescindible para el MVP (34) · **S** = segunda versión · **C** = deseable.

Fuente: `App_POS_Bazar_Especificacion_User_Stories.pdf` (1 oct 2026, @Jiahao).

---

## 1. Ventas y cobro (VEN)

| ID | Rol | User story | Criterios de aceptación | Prior. |
|---|---|---|---|---|
| VEN-01 | Cajero | Quiero añadir productos al ticket por categoría, búsqueda o código de barras, para vender rápido | El producto aparece con precio e IVA; el total se recalcula al instante | M |
| VEN-02 | Cajero | Quiero cambiar cantidades y quitar líneas antes de cobrar, para corregir errores | Quitar una línea no deja rastro de venta; el total se actualiza | M |
| VEN-03 | Cajero | Quiero cobrar en efectivo y ver el cambio, para no calcularlo a mano | Introduzco lo entregado y la app muestra el cambio; no permite cobrar menos del total | M |
| VEN-04 | Cajero | Quiero cobrar con tarjeta o pago mixto, para aceptar cualquier forma de pago | Se registra el importe de cada método; la suma debe igualar el total | M |
| VEN-05 | Admin | Quiero aplicar descuentos por línea o total, para promociones | El cajero solo aplica descuentos hasta un límite que fija el admin | S |
| VEN-06 | Admin | Quiero hacer devoluciones de una venta cerrada, para reembolsar al cliente | Exige PIN de admin y motivo; devuelve el stock y genera factura rectificativa | S |

## 2. Productos y precios (PRE)

| ID | Rol | User story | Criterios de aceptación | Prior. |
|---|---|---|---|---|
| PRE-01 | Admin | Quiero crear y editar productos con nombre, precio, IVA, foto y código, para tener el catálogo | Campos obligatorios: nombre, precio, tipo de IVA; el código de barras es único | M |
| PRE-02 | Admin | Quiero organizar productos en categorías, para que el cajero los encuentre rápido | Las categorías salen como botones en la pantalla de venta | M |
| PRE-03 | Admin | Quiero cambiar precios de varios productos a la vez, para actualizar tarifas | Subida/bajada por % o importe sobre una categoría; vista previa antes de guardar | S |
| PRE-04 | Admin | Quiero ver el historial de precios de un producto, para controlar cambios | Cada cambio guarda fecha, usuario, precio anterior y nuevo | C |

## 3. Inventario (INV)

| ID | Rol | User story | Criterios de aceptación | Prior. |
|---|---|---|---|---|
| INV-01 | Sistema | Quiero que cada venta descuente stock automáticamente, para tener el inventario al día | Vender 3 unidades resta 3; una devolución las suma | M |
| INV-02 | Cajero | Quiero consultar el stock de un producto, para responder al cliente | Muestra unidades disponibles; el cajero no puede editarlas | M |
| INV-03 | Admin | Quiero registrar entradas de mercancía, para sumar stock al recibir pedidos | Indica proveedor, cantidad y coste; actualiza stock y coste medio | M |
| INV-04 | Admin | Quiero recibir alertas de stock mínimo, para reponer a tiempo | Aviso en el panel cuando el stock baja del mínimo configurado | S |
| INV-05 | Admin | Quiero ajustar stock con un motivo (merma, rotura, caducidad), para cuadrar el inventario | Motivo obligatorio; queda en el historial de movimientos | S |
| INV-06 | Admin | Quiero hacer un recuento físico, para corregir diferencias | Introduzco lo contado y la app muestra y registra la diferencia | C |

## 4. Impresión de tickets (IMP)

| ID | Rol | User story | Criterios de aceptación | Prior. |
|---|---|---|---|---|
| IMP-01 | Cajero | Quiero que el ticket se imprima al cobrar, para entregarlo sin pasos extra | Impresión automática configurable (sí/no/preguntar) | M |
| IMP-02 | Cajero | Quiero reimprimir un ticket anterior, para cuando el cliente lo pide | Búsqueda por número o fecha; marca "COPIA" en el ticket | M |
| IMP-03 | Admin | Quiero personalizar cabecera y pie (logo, NIF, dirección, mensaje), para cumplir y dar imagen | Vista previa antes de guardar; datos fiscales obligatorios | M |
| IMP-04 | Cajero | Quiero enviar el ticket por email o mostrar un QR, para clientes que no lo quieren en papel | Envío del PDF o QR con enlace al ticket | C |

## 5. Facturación (FAC)

| ID | Rol | User story | Criterios de aceptación | Prior. |
|---|---|---|---|---|
| FAC-01 | Sistema | Quiero que cada venta genere una factura simplificada numerada, para cumplir con Hacienda | Serie y número correlativos sin huecos; desglose de base, IVA y total | M |
| FAC-02 | Cajero | Quiero emitir factura completa con datos del cliente, para empresas y autónomos | Pide NIF, razón social y dirección; valida el formato del NIF | M |
| FAC-03 | Admin | Quiero emitir facturas rectificativas, para corregir o anular facturas | Serie propia (R) y referencia a la factura original; nunca se borra la original | S |
| FAC-04 | Sistema | Quiero generar registros Verifactu (huella encadenada y QR), para cumplir la normativa antifraude | Cada factura lleva QR y hash encadenado; envío a la AEAT configurable | M |
| FAC-05 | Admin | Quiero exportar facturas por periodo, para enviarlas a la gestoría | Exporta CSV/Excel y PDF con totales por tipo de IVA | S |
| FAC-06 | Cajero | Quiero facturar un ticket ya emitido cuando el cliente vuelve a pedir factura, para darle una factura completa sin repetir la venta | Busco el ticket por número, fecha o escaneando su QR; introduzco los datos fiscales; se genera una factura completa en sustitución de la simplificada, con referencia a ella; un ticket solo se puede facturar una vez; se imprime o se envía en PDF | M |

## 6. Caja (CAJ)

| ID | Rol | User story | Criterios de aceptación | Prior. |
|---|---|---|---|---|
| CAJ-01 | Cajero | Quiero abrir caja con un fondo inicial, para empezar el turno | No se puede vender con la caja cerrada | M |
| CAJ-02 | Cajero | Quiero cerrar caja con arqueo, para cuadrar el efectivo | Introduzco el efectivo contado; muestra el descuadre e imprime el cierre Z | M |
| CAJ-03 | Admin | Quiero ver las ventas del día por producto, método de pago y cajero, para controlar el negocio | Panel con totales; filtro por fecha | S |

## 7. Hardware (HW)

| ID | Rol | User story | Criterios de aceptación | Prior. |
|---|---|---|---|---|
| HW-01 | Admin | Quiero conectar una impresora térmica por Bluetooth, USB o red, para imprimir tickets | Compatible ESC/POS, papel de 58 y 80 mm; botón de prueba de impresión | M |
| HW-02 | Cliente / Admin | Quiero una pantalla de cliente (visor o segunda pantalla) que muestre producto y total, para que el cliente vea lo que paga | Muestra cada línea añadida y el total; mensaje de bienvenida en reposo | S |
| HW-03 | Cajero | Quiero que el cajón portamonedas se abra al cobrar en efectivo, para agilizar el cobro | Apertura mediante la impresora (pulso ESC/POS); también manual con PIN | S |
| HW-04 | Cajero | Quiero escanear códigos de barras con lector o cámara, para añadir productos sin buscar | Un escaneo añade 1 unidad; código desconocido muestra aviso | M |

## 8. Usuarios y seguridad (USR)

| ID | Rol | User story | Criterios de aceptación | Prior. |
|---|---|---|---|---|
| USR-01 | Cajero | Quiero entrar con un PIN de 4 dígitos, para cambiar de usuario rápido | Bloqueo tras 5 intentos fallidos; cierre de sesión por inactividad | M |
| USR-02 | Admin | Quiero crear usuarios con rol admin o cajero, para limitar lo que cada uno puede hacer | Las acciones de admin piden PIN de admin al cajero | M |
| USR-03 | Admin | Quiero un registro de acciones sensibles (anulaciones, descuentos, cambios de precio), para auditar | Guarda usuario, fecha, acción y valores; no editable | S |

## 9. Idiomas y extensibilidad (CFG)

| ID | Rol | User story | Criterios de aceptación | Prior. |
|---|---|---|---|---|
| CFG-01 | Admin | Quiero elegir el idioma de la interfaz (español, catalán, inglés, chino, alemán, francés…), para que todo el equipo la entienda | Cambia todos los textos sin reiniciar; ningún texto queda sin traducir | M |
| CFG-02 | Cajero | Quiero que la app recuerde mi idioma al entrar con mi PIN, para trabajar en mi lengua | Cada usuario tiene su idioma; el cambio no afecta a los demás | S |
| CFG-03 | Cajero | Quiero imprimir el ticket en el idioma del cliente, para turistas | Selector de idioma al cobrar; los datos fiscales no cambian | S |
| CFG-04 | Admin | Quiero configurar moneda, formato de fecha y números según la región, para que se vean correctos | Euro por defecto; separadores decimales según el idioma | M |
| CFG-05 | Admin | Quiero añadir nuevos idiomas o módulos (fidelización, venta online, multi-tienda…) sin reinstalar, para hacer crecer la app | Un idioma nuevo = un fichero de traducción; los módulos se activan desde Ajustes | C |
| CFG-06 | Admin | Quiero que la app se actualice automáticamente, para tener mejoras sin perder datos | Aviso de nueva versión; copia de seguridad antes de actualizar; migración automática de la base de datos | S |

## 10. Conexión con Verifactu (VFA)

Obligatorio desde el 1 de enero de 2027 para sociedades y el 1 de julio de 2027 para autónomos (RDL 15/2025). Amplía FAC-04 con la conexión real a la AEAT.

| ID | Rol | User story | Criterios de aceptación | Prior. |
|---|---|---|---|---|
| VFA-01 | Admin | Quiero cargar el certificado digital del negocio y probar la conexión con la AEAT, para poder enviar registros | Certificado (.pfx) guardado cifrado; botón de prueba contra el entorno de pruebas y el de producción | M |
| VFA-02 | Sistema | Quiero enviar a la AEAT cada registro de alta y de anulación de factura, para cumplir Verifactu | Envío por el servicio web de la AEAT; se guarda la respuesta (aceptado, aceptado con errores, rechazado) | M |
| VFA-03 | Sistema | Quiero encolar los envíos cuando no hay internet y reenviarlos después, para no parar de vender | Cola persistente; reintento automático respetando el tiempo de espera que indica la AEAT | M |
| VFA-04 | Admin | Quiero elegir la modalidad VERI\*FACTU o no VERI\*FACTU, para adaptarme al negocio | En no VERI\*FACTU cada registro se firma y se guarda un registro de eventos; el cambio queda registrado | S |
| VFA-05 | Admin | Quiero un panel con envíos pendientes, aceptados y rechazados, para corregir errores | Filtro por estado; reenvío de registros de subsanación | S |
| VFA-06 | Admin | Quiero ver la declaración responsable del software en "Acerca de", para acreditar que cumple la norma | Texto con versión, fabricante y fecha, accesible desde la app | M |

## 11. Importar y exportar datos (DAT)

| ID | Rol | User story | Criterios de aceptación | Prior. |
|---|---|---|---|---|
| DAT-01 | Admin | Quiero importar productos y clientes desde CSV o Excel, para no darlos de alta uno a uno | Plantilla descargable; vista previa con errores por fila; los códigos duplicados no se importan | M |
| DAT-02 | Admin | Quiero exportar productos, stock, ventas y clientes a CSV o Excel, para analizarlos fuera | Filtro por fechas; exporta con los nombres de columna en el idioma de la interfaz | S |
| DAT-03 | Admin | Quiero hacer una copia completa de la base de datos y restaurarla, para no perder nada si falla el PC | Archivo cifrado con contraseña a disco, USB o nube; restaurar pide confirmación y guarda antes una copia del estado actual | M |
| DAT-04 | Admin | Quiero exportar el registro de facturación completo, para una inspección de Hacienda o la gestoría | Exporta facturas y registros con su huella; el archivo no se puede alterar sin detectarse | S |
| DAT-05 | Admin | Quiero migrar los datos desde mi TPV anterior, para no empezar de cero | Importación por CSV con asignación de columnas; informe de lo importado | C |

## 12. Funciones de bazar (BAZ)

| ID | Rol | User story | Criterios de aceptación | Prior. |
|---|---|---|---|---|
| BAZ-01 | Admin | Quiero imprimir etiquetas de precio con código de barras, para los productos que llegan sin código o sin precio | Impresora de etiquetas (térmica o ESC/POS); elijo cantidad por producto; imprime nombre, precio y código | M |
| BAZ-02 | Cajero | Quiero vender un artículo genérico tecleando solo el importe y la sección, para cuando el producto no está dado de alta | Botones por sección (hogar, papelería, juguetes…) con precio libre; aparece en los informes de ventas por sección | M |
| BAZ-03 | Cajero / Admin | Quiero dar de alta un producto al escanear un código que no existe, para no parar la venta ni la recepción | Formulario rápido con nombre, precio y sección; el código escaneado se rellena solo; el cajero lo crea pendiente de revisión por el admin | M |
| BAZ-04 | Admin | Quiero recibir mercancía por cajas y vender por unidades, para comprar al mayorista y vender suelto | Unidades por caja en la ficha; recibir 3 cajas de 12 suma 36 unidades; coste unitario calculado | M |
| BAZ-05 | Admin | Quiero productos con variantes (talla, color, modelo), para controlar el stock de cada una | Un producto padre con variantes, cada una con su código y stock; precio común o por variante | S |
| BAZ-06 | Cajero | Quiero consultar el precio escaneando, sin añadirlo al ticket, para responder rápido al cliente | Modo verificador de precios; muestra precio, stock y ubicación en la tienda | S |
| BAZ-07 | Cajero | Quiero imprimir un ticket regalo y hacer cambios de producto, para las compras de regalo | Ticket regalo sin precios; el cambio devuelve un producto y vende otro en la misma operación, cobrando o devolviendo la diferencia | S |
