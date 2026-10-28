BEGIN;

CREATE TABLE IF NOT EXISTS dispositivos_notificacion_cliente (
    id_dispositivo BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_cliente BIGINT NOT NULL REFERENCES clientes(id_cliente) ON DELETE CASCADE,
    token_fcm VARCHAR(4096) NOT NULL UNIQUE,
    plataforma VARCHAR(20) NOT NULL CHECK(plataforma IN ('android')),
    nombre_dispositivo VARCHAR(150),
    activo BOOLEAN NOT NULL DEFAULT TRUE,
    registrado_en TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ultimo_uso_en TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_dispositivos_notificacion_cliente_activos
    ON dispositivos_notificacion_cliente(id_cliente,ultimo_uso_en DESC)
    WHERE activo=TRUE;

ALTER TABLE pedidos DROP CONSTRAINT IF EXISTS ck_pedido_estado;
ALTER TABLE pedidos ADD CONSTRAINT ck_pedido_estado CHECK (estado IN (
    'borrador','pendiente_pago','confirmado','en_preparacion','listo','en_reparto','cancelado'));

CREATE OR REPLACE FUNCTION validar_transicion_estado_pedido()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.estado = OLD.estado THEN RETURN NEW; END IF;
    IF NOT (
        (OLD.estado='borrador' AND NEW.estado IN ('pendiente_pago','en_preparacion','cancelado')) OR
        (OLD.estado='pendiente_pago' AND NEW.estado IN ('en_preparacion','cancelado')) OR
        (OLD.estado='confirmado' AND NEW.estado IN ('en_preparacion','cancelado')) OR
        (OLD.estado='en_preparacion' AND NEW.estado IN ('listo','cancelado')) OR
        (OLD.estado='listo' AND NEW.estado='en_reparto' AND NEW.tipo_entrega='delivery')
    ) THEN
        RAISE EXCEPTION 'Transición de pedido no permitida: % -> %', OLD.estado, NEW.estado;
    END IF;
    IF NEW.estado='listo' THEN NEW.listo_en := COALESCE(NEW.listo_en,NOW()); END IF;
    RETURN NEW;
END; $$;

INSERT INTO schema_migrations(id) VALUES('017_mobile_notifications_and_delivery')
ON CONFLICT(id) DO NOTHING;

COMMIT;
