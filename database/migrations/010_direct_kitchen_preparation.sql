BEGIN;

SELECT set_config('kameron.origen','migracion',true),
       set_config('kameron.motivo','Ingreso directo a preparación',true);
UPDATE pedidos SET estado='en_preparacion' WHERE estado='confirmado';

CREATE OR REPLACE FUNCTION validar_transicion_estado_pedido()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.estado = OLD.estado THEN RETURN NEW; END IF;
    IF NOT (
        (OLD.estado='borrador' AND NEW.estado IN ('pendiente_pago','en_preparacion','cancelado')) OR
        (OLD.estado='pendiente_pago' AND NEW.estado IN ('en_preparacion','cancelado')) OR
        (OLD.estado='confirmado' AND NEW.estado IN ('en_preparacion','cancelado')) OR
        (OLD.estado='en_preparacion' AND NEW.estado IN ('listo','cancelado'))
    ) THEN
        RAISE EXCEPTION 'Transición de pedido no permitida: % -> %', OLD.estado, NEW.estado;
    END IF;
    IF NEW.estado='listo' THEN NEW.listo_en := COALESCE(NEW.listo_en,NOW()); END IF;
    RETURN NEW;
END; $$;

CREATE OR REPLACE FUNCTION encolar_impresion_pedido()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.estado = 'cancelado' THEN
        NEW.pendiente_impresion := FALSE;
        NEW.impresion_token := NULL;
        NEW.impresion_tomada_en := NULL;
        NEW.impresion_tomada_por := NULL;
    ELSIF TG_OP = 'INSERT' THEN
        IF NEW.estado = 'en_preparacion' AND NEW.impreso_en IS NULL THEN
            NEW.pendiente_impresion := TRUE;
            NEW.impresion_reintentar_en := NOW();
        END IF;
    ELSIF NEW.estado = 'en_preparacion'
          AND OLD.estado IS DISTINCT FROM NEW.estado
          AND NEW.impreso_en IS NULL THEN
        NEW.pendiente_impresion := TRUE;
        NEW.impresion_reintentar_en := NOW();
    END IF;
    RETURN NEW;
END; $$;

COMMIT;
