BEGIN;

CREATE TABLE IF NOT EXISTS archivos_mensuales (
    periodo_inicio DATE PRIMARY KEY,
    periodo_fin DATE NOT NULL,
    sha256 CHAR(64) NOT NULL,
    tamano_bytes BIGINT NOT NULL CHECK(tamano_bytes>=0),
    preparado_en TIMESTAMPTZ NOT NULL,
    confirmado_en TIMESTAMPTZ,
    confirmado_por BIGINT REFERENCES usuarios(id_usuario) ON DELETE SET NULL,
    purgado_en TIMESTAMPTZ,
    CONSTRAINT ck_archivo_periodo CHECK(periodo_fin=periodo_inicio+INTERVAL '1 month')
);

CREATE INDEX IF NOT EXISTS ix_pedidos_archivo_creado ON pedidos(creado_en);
CREATE INDEX IF NOT EXISTS ix_mensajes_archivo_creado ON mensajes_whatsapp(creado_en);
CREATE INDEX IF NOT EXISTS ix_turnos_archivo_cerrado ON turnos(cerrado_en) WHERE cerrado_en IS NOT NULL;

INSERT INTO schema_migrations(id) VALUES('012_monthly_archives') ON CONFLICT(id) DO NOTHING;

COMMIT;
