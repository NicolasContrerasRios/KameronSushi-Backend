BEGIN;

INSERT INTO roles(codigo,nombre,descripcion) VALUES
    ('administrador','Administrador','Acceso completo a la administración'),
    ('caja','Caja','Ingreso de pedidos y cobros'),
    ('cocina','Cocina','Preparación y cambio de estado de pedidos')
ON CONFLICT(codigo) DO UPDATE SET nombre=EXCLUDED.nombre,descripcion=EXCLUDED.descripcion;
DELETE FROM roles r WHERE r.codigo='repartidor'
AND NOT EXISTS(SELECT 1 FROM usuario_roles ur WHERE ur.id_rol=r.id_rol);

CREATE TABLE IF NOT EXISTS sesiones_usuario (
    id_sesion BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_usuario BIGINT NOT NULL REFERENCES usuarios(id_usuario) ON DELETE CASCADE,
    token_hash CHAR(64) NOT NULL UNIQUE,
    dispositivo VARCHAR(150),
    creado_en TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expira_en TIMESTAMPTZ NOT NULL,
    revocada_en TIMESTAMPTZ,
    CONSTRAINT ck_sesion_expiracion CHECK (expira_en > creado_en)
);
CREATE INDEX IF NOT EXISTS ix_sesiones_usuario_vigente
    ON sesiones_usuario(token_hash,expira_en) WHERE revocada_en IS NULL;

UPDATE pedidos SET estado='listo',listo_en=COALESCE(listo_en,actualizado_en)
WHERE estado IN ('en_reparto','entregado');
ALTER TABLE pedidos DROP CONSTRAINT IF EXISTS ck_pedido_estado;
ALTER TABLE pedidos ADD CONSTRAINT ck_pedido_estado CHECK (estado IN (
    'borrador','pendiente_pago','confirmado','en_preparacion','listo','cancelado'));

COMMIT;
