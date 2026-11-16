-- REINICIO OPERATIVO MANUAL DE KAMERON SUSHI
--
-- Este script ELIMINA pedidos, turnos, pagos, movimientos, clientes,
-- sesiones, puntos, conversaciones y archivos históricos.
-- Conserva catálogo, productos, precios, promociones, publicidad,
-- configuración operativa, usuarios del personal, roles y migraciones.
--
-- No se ejecuta automáticamente durante un deploy.

BEGIN;

TRUNCATE TABLE
    archivos_mensuales,
    mensajes_whatsapp,
    conversaciones_whatsapp,
    eventos_pagos,
    movimiento_puntos,
    cuentas_fidelizacion,
    historial_estados_pedido,
    entregas_pedido,
    configuracion_roll_pedido,
    detalle_pedidos,
    pagos,
    checkouts,
    movimientos_caja,
    pedidos,
    turnos,
    sesiones_cliente_app,
    cuentas_cliente_app,
    clientes,
    sesiones_usuario
RESTART IDENTITY CASCADE;

COMMIT;

-- Validación esperada: todos los contadores deben quedar en cero.
SELECT
    (SELECT COUNT(*) FROM turnos) AS turnos,
    (SELECT COUNT(*) FROM pedidos) AS pedidos,
    (SELECT COUNT(*) FROM pagos) AS pagos,
    (SELECT COUNT(*) FROM clientes) AS clientes,
    (SELECT COUNT(*) FROM movimiento_puntos) AS movimientos_puntos;

