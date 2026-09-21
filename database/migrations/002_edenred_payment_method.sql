BEGIN;

CREATE TABLE IF NOT EXISTS schema_migrations (
    id VARCHAR(100) PRIMARY KEY,
    aplicado_en TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE pagos DROP CONSTRAINT IF EXISTS ck_pago_metodo;
ALTER TABLE pagos ADD CONSTRAINT ck_pago_metodo
    CHECK (metodo IN ('efectivo', 'transferencia', 'tarjeta', 'edenred'));

INSERT INTO schema_migrations (id)
VALUES ('002_edenred_payment_method')
ON CONFLICT (id) DO NOTHING;

COMMIT;
