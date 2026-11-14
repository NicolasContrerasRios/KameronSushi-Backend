BEGIN;

CREATE TABLE IF NOT EXISTS paneles_publicidad (
    id_panel BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    titulo VARCHAR(120) NOT NULL,
    descripcion VARCHAR(300),
    imagen_url TEXT,
    duracion_segundos INTEGER NOT NULL DEFAULT 8,
    orden INTEGER NOT NULL DEFAULT 0,
    activa BOOLEAN NOT NULL DEFAULT TRUE,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    actualizado_en TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_panel_publicidad_titulo CHECK (BTRIM(titulo) <> ''),
    CONSTRAINT ck_panel_publicidad_duracion CHECK (duracion_segundos BETWEEN 3 AND 120),
    CONSTRAINT ck_panel_publicidad_orden CHECK (orden >= 0)
);

CREATE INDEX IF NOT EXISTS ix_paneles_publicidad_reproduccion
    ON paneles_publicidad(activa, orden, id_panel);

INSERT INTO schema_migrations(id)
VALUES('019_advertising_panels')
ON CONFLICT(id) DO NOTHING;

COMMIT;
