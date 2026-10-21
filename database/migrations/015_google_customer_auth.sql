ALTER TABLE cuentas_cliente_app ALTER COLUMN pin_hash DROP NOT NULL;
ALTER TABLE cuentas_cliente_app ADD COLUMN IF NOT EXISTS google_subject VARCHAR(100);
CREATE UNIQUE INDEX IF NOT EXISTS ux_cuentas_cliente_app_google
    ON cuentas_cliente_app(google_subject) WHERE google_subject IS NOT NULL;
ALTER TABLE cuentas_cliente_app DROP CONSTRAINT IF EXISTS ck_cuenta_cliente_autenticacion;
ALTER TABLE cuentas_cliente_app ADD CONSTRAINT ck_cuenta_cliente_autenticacion
    CHECK(pin_hash IS NOT NULL OR google_subject IS NOT NULL);
