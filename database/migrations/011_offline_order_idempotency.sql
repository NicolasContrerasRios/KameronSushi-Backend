BEGIN;

ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS id_operacion_cliente UUID;

CREATE UNIQUE INDEX IF NOT EXISTS ux_pedidos_operacion_cliente
    ON pedidos(id_operacion_cliente)
    WHERE id_operacion_cliente IS NOT NULL;

INSERT INTO schema_migrations(id)
VALUES('011_offline_order_idempotency')
ON CONFLICT(id) DO NOTHING;

COMMIT;
