CREATE TABLE IF NOT EXISTS cuentas_cliente_app (
    id_cuenta_app BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_cliente BIGINT NOT NULL UNIQUE REFERENCES clientes(id_cliente) ON DELETE CASCADE,
    telefono_normalizado VARCHAR(20) NOT NULL UNIQUE,
    pin_hash VARCHAR(255) NOT NULL,
    estado VARCHAR(20) NOT NULL DEFAULT 'activa' CHECK(estado IN ('activa','bloqueada','cerrada')),
    creado_en TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    actualizado_en TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS sesiones_cliente_app (
    id_sesion BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_cuenta_app BIGINT NOT NULL REFERENCES cuentas_cliente_app(id_cuenta_app) ON DELETE CASCADE,
    token_hash CHAR(64) NOT NULL UNIQUE,
    dispositivo VARCHAR(150),
    creado_en TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expira_en TIMESTAMPTZ NOT NULL,
    revocada_en TIMESTAMPTZ,
    CHECK(expira_en > creado_en)
);

CREATE INDEX IF NOT EXISTS ix_sesiones_cliente_app_vigente
    ON sesiones_cliente_app(token_hash,expira_en) WHERE revocada_en IS NULL;

INSERT INTO reglas_fidelizacion(nombre,monto_por_punto,compra_minima)
SELECT 'Regla general',1000,1000
WHERE NOT EXISTS (
    SELECT 1 FROM reglas_fidelizacion
     WHERE activa AND vigente_desde<=NOW() AND (vigente_hasta IS NULL OR vigente_hasta>NOW())
);
