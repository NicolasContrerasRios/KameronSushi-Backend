BEGIN;

CREATE TABLE IF NOT EXISTS historial_estados_pedido (
    id_cambio BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_pedido BIGINT NOT NULL REFERENCES pedidos(id_pedido) ON DELETE RESTRICT,
    estado_anterior VARCHAR(30),
    estado_nuevo VARCHAR(30) NOT NULL,
    motivo VARCHAR(500),
    origen VARCHAR(30) NOT NULL DEFAULT 'sistema',
    id_usuario BIGINT REFERENCES usuarios(id_usuario) ON DELETE SET NULL,
    cambiado_en TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_historial_estados_pedido
    ON historial_estados_pedido(id_pedido,cambiado_en,id_cambio);

CREATE OR REPLACE FUNCTION validar_transicion_estado_pedido()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.estado = OLD.estado THEN RETURN NEW; END IF;
    IF NOT (
        (OLD.estado='borrador' AND NEW.estado IN ('pendiente_pago','confirmado','cancelado')) OR
        (OLD.estado='pendiente_pago' AND NEW.estado IN ('confirmado','cancelado')) OR
        (OLD.estado='confirmado' AND NEW.estado IN ('en_preparacion','cancelado')) OR
        (OLD.estado='en_preparacion' AND NEW.estado IN ('listo','cancelado'))
    ) THEN
        RAISE EXCEPTION 'Transición de pedido no permitida: % -> %', OLD.estado, NEW.estado;
    END IF;
    IF NEW.estado='listo' THEN NEW.listo_en := COALESCE(NEW.listo_en,NOW()); END IF;
    RETURN NEW;
END; $$;

DROP TRIGGER IF EXISTS trg_pedidos_validar_transicion ON pedidos;
CREATE TRIGGER trg_pedidos_validar_transicion
    BEFORE UPDATE OF estado ON pedidos
    FOR EACH ROW EXECUTE FUNCTION validar_transicion_estado_pedido();

CREATE OR REPLACE FUNCTION auditar_estado_pedido()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE
    v_usuario TEXT := current_setting('kameron.usuario_id', TRUE);
    v_motivo TEXT := current_setting('kameron.motivo', TRUE);
    v_origen TEXT := current_setting('kameron.origen', TRUE);
BEGIN
    IF TG_OP='INSERT' THEN
        INSERT INTO historial_estados_pedido(id_pedido,estado_anterior,estado_nuevo,motivo,origen,id_usuario)
        VALUES(NEW.id_pedido,NULL,NEW.estado,NULL,COALESCE(NULLIF(v_origen,''),'sistema'),NULLIF(v_usuario,'')::BIGINT);
    ELSIF NEW.estado IS DISTINCT FROM OLD.estado THEN
        INSERT INTO historial_estados_pedido(id_pedido,estado_anterior,estado_nuevo,motivo,origen,id_usuario)
        VALUES(NEW.id_pedido,OLD.estado,NEW.estado,NULLIF(v_motivo,''),COALESCE(NULLIF(v_origen,''),'sistema'),NULLIF(v_usuario,'')::BIGINT);
    END IF;
    RETURN NEW;
END; $$;

DROP TRIGGER IF EXISTS trg_pedidos_auditar_estado ON pedidos;
CREATE TRIGGER trg_pedidos_auditar_estado
    AFTER INSERT OR UPDATE OF estado ON pedidos
    FOR EACH ROW EXECUTE FUNCTION auditar_estado_pedido();

INSERT INTO historial_estados_pedido(id_pedido,estado_anterior,estado_nuevo,motivo,origen,cambiado_en)
SELECT p.id_pedido,NULL,p.estado,'Estado existente al habilitar el historial','migracion',p.creado_en
FROM pedidos p
WHERE NOT EXISTS(SELECT 1 FROM historial_estados_pedido h WHERE h.id_pedido=p.id_pedido);

COMMIT;
