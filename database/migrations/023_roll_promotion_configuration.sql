BEGIN;

ALTER TABLE productos ADD COLUMN IF NOT EXISTS cantidad_rolls_promocion INTEGER NOT NULL DEFAULT 0;
ALTER TABLE productos ADD COLUMN IF NOT EXISTS cantidad_salsas_promocion INTEGER NOT NULL DEFAULT 0;
ALTER TABLE productos DROP CONSTRAINT IF EXISTS ck_producto_promocion_rolls;
ALTER TABLE productos ADD CONSTRAINT ck_producto_promocion_rolls CHECK (cantidad_rolls_promocion BETWEEN 0 AND 20);
ALTER TABLE productos DROP CONSTRAINT IF EXISTS ck_producto_promocion_salsas;
ALTER TABLE productos ADD CONSTRAINT ck_producto_promocion_salsas CHECK (cantidad_salsas_promocion BETWEEN 0 AND 20);

UPDATE productos SET cantidad_rolls_promocion=4,cantidad_salsas_promocion=2 WHERE nombre_producto='Promoción 40 cortes';
UPDATE productos SET cantidad_rolls_promocion=5,cantidad_salsas_promocion=3 WHERE nombre_producto='Promoción 50 cortes';
UPDATE productos SET cantidad_rolls_promocion=6,cantidad_salsas_promocion=4 WHERE nombre_producto='Promoción 60 cortes';
UPDATE productos SET cantidad_rolls_promocion=7,cantidad_salsas_promocion=5 WHERE nombre_producto='Promoción 70 cortes';
UPDATE productos SET cantidad_rolls_promocion=8,cantidad_salsas_promocion=6 WHERE nombre_producto='Promoción 80 cortes';

INSERT INTO schema_migrations(id) VALUES('023_roll_promotion_configuration') ON CONFLICT(id) DO NOTHING;
COMMIT;
