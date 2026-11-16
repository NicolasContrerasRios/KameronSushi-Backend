BEGIN;

ALTER TABLE turnos
    ADD COLUMN IF NOT EXISTS es_prueba BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE pedidos
    ADD COLUMN IF NOT EXISTS es_prueba BOOLEAN NOT NULL DEFAULT FALSE;

UPDATE pedidos p
   SET es_prueba = t.es_prueba
  FROM turnos t
 WHERE p.id_turno = t.id_turno
   AND p.es_prueba IS DISTINCT FROM t.es_prueba;

CREATE INDEX IF NOT EXISTS ix_turnos_prueba_abierto
    ON turnos(es_prueba, abierto_en DESC);

CREATE INDEX IF NOT EXISTS ix_pedidos_prueba_creado
    ON pedidos(es_prueba, creado_en DESC);

INSERT INTO schema_migrations(id)
VALUES ('021_test_data_markers')
ON CONFLICT (id) DO NOTHING;

COMMIT;

