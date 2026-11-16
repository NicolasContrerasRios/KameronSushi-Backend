ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS numero_turno INTEGER;

WITH numerados AS (
    SELECT id_pedido,
           ROW_NUMBER() OVER (PARTITION BY id_turno ORDER BY creado_en, id_pedido)::INTEGER AS numero
      FROM pedidos
     WHERE id_turno IS NOT NULL
)
UPDATE pedidos p
   SET numero_turno = n.numero
  FROM numerados n
 WHERE p.id_pedido = n.id_pedido
   AND p.numero_turno IS NULL;

ALTER TABLE pedidos DROP CONSTRAINT IF EXISTS ck_pedido_numero_turno;
ALTER TABLE pedidos ADD CONSTRAINT ck_pedido_numero_turno
    CHECK (numero_turno IS NULL OR numero_turno > 0);

CREATE UNIQUE INDEX IF NOT EXISTS uq_pedido_numero_por_turno
    ON pedidos(id_turno, numero_turno)
    WHERE id_turno IS NOT NULL AND numero_turno IS NOT NULL;

CREATE OR REPLACE FUNCTION asignar_numero_pedido_turno()
RETURNS TRIGGER AS $$
BEGIN
    IF NEW.id_turno IS NOT NULL AND NEW.numero_turno IS NULL THEN
        PERFORM pg_advisory_xact_lock(NEW.id_turno);
        SELECT COALESCE(MAX(numero_turno), 0) + 1
          INTO NEW.numero_turno
          FROM pedidos
         WHERE id_turno = NEW.id_turno;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_asignar_numero_pedido_turno ON pedidos;
CREATE TRIGGER trg_asignar_numero_pedido_turno
    BEFORE INSERT OR UPDATE OF id_turno ON pedidos
    FOR EACH ROW EXECUTE FUNCTION asignar_numero_pedido_turno();
