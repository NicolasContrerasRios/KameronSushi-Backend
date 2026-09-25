BEGIN;

CREATE TABLE IF NOT EXISTS schema_migrations (
    id VARCHAR(100) PRIMARY KEY,
    aplicado_en TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS pendiente_impresion BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS impreso_en TIMESTAMPTZ;
ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS impreso_por VARCHAR(150);
ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS impresion_token UUID;
ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS impresion_tomada_en TIMESTAMPTZ;
ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS impresion_tomada_por VARCHAR(150);
ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS impresion_reintentar_en TIMESTAMPTZ NOT NULL DEFAULT NOW();
ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS intentos_impresion INTEGER NOT NULL DEFAULT 0;
ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS ultimo_error_impresion VARCHAR(500);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'ck_pedido_intentos_impresion'
           AND conrelid = 'pedidos'::regclass
    ) THEN
        ALTER TABLE pedidos ADD CONSTRAINT ck_pedido_intentos_impresion
            CHECK (intentos_impresion >= 0);
    END IF;
END;
$$;

CREATE INDEX IF NOT EXISTS ix_pedidos_impresion_pendiente
    ON pedidos (impresion_reintentar_en, creado_en)
    WHERE pendiente_impresion = TRUE AND impreso_en IS NULL;

CREATE OR REPLACE FUNCTION encolar_impresion_pedido()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.estado = 'cancelado' THEN
        NEW.pendiente_impresion := FALSE;
        NEW.impresion_token := NULL;
        NEW.impresion_tomada_en := NULL;
        NEW.impresion_tomada_por := NULL;
    ELSIF TG_OP = 'INSERT' THEN
        IF NEW.estado IN ('confirmado', 'en_preparacion') AND NEW.impreso_en IS NULL THEN
            NEW.pendiente_impresion := TRUE;
            NEW.impresion_reintentar_en := NOW();
        END IF;
    ELSIF NEW.estado IN ('confirmado', 'en_preparacion')
          AND OLD.estado IS DISTINCT FROM NEW.estado
          AND NEW.impreso_en IS NULL THEN
        NEW.pendiente_impresion := TRUE;
        NEW.impresion_reintentar_en := NOW();
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_pedidos_encolar_impresion ON pedidos;
CREATE TRIGGER trg_pedidos_encolar_impresion
    BEFORE INSERT OR UPDATE OF estado ON pedidos
    FOR EACH ROW EXECUTE FUNCTION encolar_impresion_pedido();

INSERT INTO schema_migrations (id)
VALUES ('007_kitchen_print_queue')
ON CONFLICT (id) DO NOTHING;

COMMIT;
