# User stories — ProyPOS

41 historias en 9 módulos. Formato: *Como [rol], quiero [acción], para [beneficio]*, con criterios de aceptación (CA) verificables.

Prioridad: **M** = imprescindible para el MVP · **S** = segunda versión · **C** = deseable.

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
| FAC-04 | Sistema | Quiero generar registros Verifactu (huella encadenada y QR), para cumplir la normativa antifraude | Cada factura lleva QR y hash encadenado; envío a la AEAT configurable | S |
| FAC-05 | Admin | Quiero exportar facturas por periodo, para enviarlas a la gestoría | Exporta CSV/Excel y PDF con totales por tipo de IVA | S |

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
| HW-04 | Cajero | Quiero escanear códigos de barras con lector o cámara, para añadir productos sin buscar | Un escaneo añade 1 unidad; código desconocido muestra aviso | S |

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
| CFG-05 | Admin | Quiero añadir nuevos idiomas o módulos (reservas, fidelización, cocina…) sin reinstalar, para hacer crecer la app | Un idioma nuevo = un fichero de traducción; los módulos se activan desde Ajustes | C |
| CFG-06 | Admin | Quiero que la app se actualice automáticamente, para tener mejoras sin perder datos | Aviso de nueva versión; copia de seguridad antes de actualizar; migración automática de la base de datos | S |
