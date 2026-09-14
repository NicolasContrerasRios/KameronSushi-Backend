BEGIN;

ALTER TABLE conversaciones_whatsapp
    ADD COLUMN IF NOT EXISTS contexto JSONB NOT NULL DEFAULT '{}'::jsonb;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_conversacion_contexto'
          AND conrelid = 'conversaciones_whatsapp'::regclass
    ) THEN
        ALTER TABLE conversaciones_whatsapp
            ADD CONSTRAINT ck_conversacion_contexto
            CHECK (JSONB_TYPEOF(contexto) = 'object');
    END IF;
END;
$$;

COMMIT;
