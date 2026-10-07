BEGIN;

ALTER TABLE archivos_mensuales
    ADD COLUMN IF NOT EXISTS version_esquema INTEGER NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS mensajes_purgados_en TIMESTAMPTZ;

UPDATE archivos_mensuales
   SET confirmado_en=NULL, confirmado_por=NULL
 WHERE version_esquema<2 AND purgado_en IS NULL;

CREATE INDEX IF NOT EXISTS ix_eventos_pagos_archivo_recibido
    ON eventos_pagos(recibido_en) WHERE id_pago IS NULL;

INSERT INTO schema_migrations(id)
VALUES('013_archive_retention_policy')
ON CONFLICT(id) DO NOTHING;

COMMIT;
