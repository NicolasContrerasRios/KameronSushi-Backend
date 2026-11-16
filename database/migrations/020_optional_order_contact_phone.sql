BEGIN;

ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS telefono_contacto VARCHAR(20);
ALTER TABLE pedidos DROP CONSTRAINT IF EXISTS ck_pedido_telefono_contacto;
ALTER TABLE pedidos ADD CONSTRAINT ck_pedido_telefono_contacto
    CHECK (telefono_contacto IS NULL OR BTRIM(telefono_contacto) <> '');

INSERT INTO schema_migrations(id)
VALUES ('020_optional_order_contact_phone')
ON CONFLICT (id) DO NOTHING;

COMMIT;
