BEGIN;

CREATE TABLE IF NOT EXISTS schema_migrations (
    id VARCHAR(100) PRIMARY KEY,
    aplicado_en TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE turnos ADD COLUMN IF NOT EXISTS monto_inicial NUMERIC(12,0) NOT NULL DEFAULT 0;
ALTER TABLE turnos ADD COLUMN IF NOT EXISTS efectivo_contado NUMERIC(12,0);
ALTER TABLE turnos ADD COLUMN IF NOT EXISTS efectivo_esperado NUMERIC(12,0);
ALTER TABLE turnos ADD COLUMN IF NOT EXISTS diferencia_efectivo NUMERIC(12,0);

CREATE TABLE IF NOT EXISTS movimientos_caja (
    id_movimiento BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_turno BIGINT NOT NULL REFERENCES turnos(id_turno) ON DELETE RESTRICT,
    tipo VARCHAR(20) NOT NULL,
    monto NUMERIC(12,0) NOT NULL,
    motivo VARCHAR(250) NOT NULL,
    creado_en TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_movimiento_caja_tipo CHECK (tipo IN ('ingreso', 'retiro', 'gasto')),
    CONSTRAINT ck_movimiento_caja_monto CHECK (monto > 0),
    CONSTRAINT ck_movimiento_caja_motivo CHECK (btrim(motivo) <> '')
);

CREATE INDEX IF NOT EXISTS ix_movimientos_caja_turno
    ON movimientos_caja (id_turno, creado_en);

INSERT INTO schema_migrations (id)
VALUES ('003_cash_shift_accounting')
ON CONFLICT (id) DO NOTHING;

COMMIT;
