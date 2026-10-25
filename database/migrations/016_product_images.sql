BEGIN;

ALTER TABLE productos ADD COLUMN IF NOT EXISTS imagen_url TEXT;

INSERT INTO schema_migrations(id)
VALUES ('016_product_images')
ON CONFLICT (id) DO NOTHING;

COMMIT;
