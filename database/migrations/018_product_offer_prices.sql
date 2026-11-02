BEGIN;

ALTER TABLE promociones_admin
    ADD COLUMN IF NOT EXISTS precio_oferta NUMERIC(12,2);

ALTER TABLE promociones_admin
    DROP CONSTRAINT IF EXISTS ck_promocion_precio_oferta;

ALTER TABLE promociones_admin
    ADD CONSTRAINT ck_promocion_precio_oferta
    CHECK (precio_oferta IS NULL OR precio_oferta > 0);

INSERT INTO schema_migrations(id)
VALUES ('018_product_offer_prices')
ON CONFLICT (id) DO NOTHING;

COMMIT;
