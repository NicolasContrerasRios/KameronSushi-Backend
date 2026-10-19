-- Kameron Sushi - esquema PostgreSQL consolidado y catálogo inicial
-- Fuente del catálogo: CARTA.md (13 de septiembre de 2026)
-- Modelo: BASE_DE_DATOS.md y KameronSushi.drawio (8).png
-- Incluye las migraciones del backend hasta 013_archive_retention_policy.
--
-- Este archivo se ejecuta dentro de una base de datos vacía.
-- Ejemplo con psql:
--   createdb -E UTF8 kameron_sushi
--   psql -v ON_ERROR_STOP=1 -d kameron_sushi -f KameronSushi.sql
--
-- El script no elimina objetos ni datos existentes. Si ya fue ejecutado,
-- fallará al encontrar las tablas; esto evita sobrescrituras accidentales.

BEGIN;

SET LOCAL TIME ZONE 'America/Santiago';

-- El backend usa esta tabla para no volver a ejecutar cambios ya incorporados
-- en esta instalación consolidada.
CREATE TABLE schema_migrations (
    id           VARCHAR(100) PRIMARY KEY,
    aplicado_en  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE categorias (
    id_categoria       BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    nombre_categoria   VARCHAR(100) NOT NULL UNIQUE,
    descripcion_categoria TEXT,
    orden              INTEGER NOT NULL DEFAULT 0 CHECK (orden >= 0),
    activa             BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE productos (
    id_producto            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_categoria           BIGINT NOT NULL REFERENCES categorias(id_categoria) ON DELETE RESTRICT,
    nombre_producto        VARCHAR(150) NOT NULL,
    descripcion            TEXT,
    precio                 NUMERIC(12,0) NOT NULL CHECK (precio >= 0),
    imagen_url             TEXT,
    activo                 BOOLEAN NOT NULL DEFAULT TRUE,
    requiere_configuracion BOOLEAN NOT NULL DEFAULT FALSE,
    disponible             BOOLEAN NOT NULL DEFAULT TRUE,
    orden                  INTEGER NOT NULL DEFAULT 0 CHECK (orden >= 0),
    creado_en              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    actualizado_en         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_producto_categoria_nombre UNIQUE (id_categoria, nombre_producto),
    CONSTRAINT ck_producto_nombre CHECK (BTRIM(nombre_producto) <> '')
);

CREATE INDEX ix_productos_categoria ON productos(id_categoria);
CREATE INDEX ix_productos_menu
    ON productos(id_categoria, activo, disponible);

CREATE TABLE envolturas (
    id_envoltura   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    nombre         VARCHAR(100) NOT NULL UNIQUE,
    activa         BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT ck_envoltura_nombre CHECK (BTRIM(nombre) <> '')
);

CREATE TABLE salsas (
    id_salsa       BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    nombre         VARCHAR(100) NOT NULL UNIQUE,
    descripcion    TEXT,
    activa         BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT ck_salsa_nombre CHECK (BTRIM(nombre) <> '')
);

CREATE TABLE producto_envolturas (
    id_producto_envoltura BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_envoltura          BIGINT NOT NULL REFERENCES envolturas(id_envoltura) ON DELETE RESTRICT,
    id_producto           BIGINT NOT NULL REFERENCES productos(id_producto) ON DELETE RESTRICT,
    precio_adicional      NUMERIC(12,0) NOT NULL DEFAULT 0 CHECK (precio_adicional >= 0),
    predeterminada        BOOLEAN NOT NULL DEFAULT FALSE,
    activa                BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT uq_producto_envoltura UNIQUE (id_producto, id_envoltura),
    CONSTRAINT uq_producto_envoltura_id_producto UNIQUE (id_producto_envoltura, id_producto)
);

CREATE UNIQUE INDEX uq_producto_envoltura_predeterminada
    ON producto_envolturas(id_producto)
    WHERE predeterminada = TRUE;

CREATE INDEX ix_producto_envolturas_envoltura
    ON producto_envolturas(id_envoltura);

CREATE TABLE opciones_roll (
    id_opcion_roll                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_producto                     BIGINT NOT NULL REFERENCES productos(id_producto) ON DELETE RESTRICT,
    id_producto_envoltura_fija      BIGINT,
    numero_opcion                   SMALLINT NOT NULL CHECK (numero_opcion > 0),
    nombre                          VARCHAR(100) NOT NULL,
    ingredientes                    TEXT NOT NULL,
    activa                          BOOLEAN NOT NULL DEFAULT TRUE,
    creado_en                       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    actualizado_en                  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_opcion_roll_producto_numero UNIQUE (id_producto, numero_opcion),
    CONSTRAINT ck_opcion_roll_nombre CHECK (BTRIM(nombre) <> ''),
    CONSTRAINT ck_opcion_roll_ingredientes CHECK (BTRIM(ingredientes) <> ''),
    CONSTRAINT fk_opcion_envoltura_fija_producto
        FOREIGN KEY (id_producto_envoltura_fija, id_producto)
        REFERENCES producto_envolturas(id_producto_envoltura, id_producto)
        ON DELETE RESTRICT
);

CREATE INDEX ix_opciones_roll_producto ON opciones_roll(id_producto);
CREATE INDEX ix_opciones_roll_envoltura_fija
    ON opciones_roll(id_producto_envoltura_fija)
    WHERE id_producto_envoltura_fija IS NOT NULL;

CREATE TABLE productos_canje (
    id_producto_canje BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_producto       BIGINT NOT NULL UNIQUE REFERENCES productos(id_producto) ON DELETE RESTRICT,
    coste_puntos      INTEGER NOT NULL CHECK (coste_puntos > 0),
    activo            BOOLEAN NOT NULL DEFAULT TRUE,
    stock_disponible  INTEGER CHECK (stock_disponible IS NULL OR stock_disponible >= 0),
    limite_por_pedido INTEGER CHECK (limite_por_pedido IS NULL OR limite_por_pedido > 0),
    vigente_desde     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    vigente_hasta     TIMESTAMPTZ,
    creado_en         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    actualizado_en    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_producto_canje_vigencia
        CHECK (vigente_hasta IS NULL OR vigente_hasta > vigente_desde)
);

CREATE TABLE promociones_admin (
    id_promocion          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_producto           BIGINT NOT NULL UNIQUE
        REFERENCES productos(id_producto) ON DELETE RESTRICT,
    productos_incluidos   TEXT,
    salsas_incluidas      INTEGER NOT NULL DEFAULT 0 CHECK (salsas_incluidas >= 0),
    vigente_desde         TIMESTAMPTZ,
    vigente_hasta         TIMESTAMPTZ,
    habilitada_retiro     BOOLEAN NOT NULL DEFAULT TRUE,
    habilitada_delivery   BOOLEAN NOT NULL DEFAULT TRUE,
    activa                BOOLEAN NOT NULL DEFAULT TRUE,
    actualizado_en        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_promocion_vigencia CHECK (
        vigente_hasta IS NULL OR vigente_desde IS NULL OR vigente_hasta > vigente_desde
    ),
    CONSTRAINT ck_promocion_canal CHECK (habilitada_retiro OR habilitada_delivery)
);

CREATE TABLE configuracion_operativa (
    clave           VARCHAR(100) PRIMARY KEY,
    valor           VARCHAR(500) NOT NULL,
    actualizado_en  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_configuracion_operativa_clave CHECK (BTRIM(clave) <> '')
);

CREATE TABLE clientes (
    id_cliente       BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    nombre           VARCHAR(150) NOT NULL,
    telefono         VARCHAR(20) NOT NULL,
    email            VARCHAR(254),
    creado_en        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    actualizado_en   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_cliente_nombre CHECK (BTRIM(nombre) <> ''),
    CONSTRAINT ck_cliente_telefono CHECK (BTRIM(telefono) <> '')
);

CREATE UNIQUE INDEX uq_clientes_email
    ON clientes(LOWER(email))
    WHERE email IS NOT NULL;
CREATE INDEX ix_clientes_telefono ON clientes(telefono);

CREATE TABLE conversaciones_whatsapp (
    id_conversacion             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_cliente                  BIGINT REFERENCES clientes(id_cliente) ON DELETE SET NULL,
    wa_id                       VARCHAR(50) NOT NULL UNIQUE,
    telefono                    VARCHAR(20) NOT NULL,
    estado                      VARCHAR(20) NOT NULL DEFAULT 'activa',
    paso_actual                 VARCHAR(40) NOT NULL DEFAULT 'inicio',
    contexto                    JSONB NOT NULL DEFAULT '{}'::jsonb,
    ultimo_mensaje_cliente_en   TIMESTAMPTZ,
    creado_en                   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    actualizado_en              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_conversacion_estado
        CHECK (estado IN ('activa', 'finalizada', 'cancelada')),
    CONSTRAINT ck_conversacion_paso
        CHECK (paso_actual IN (
            'inicio',
            'seleccionando_categoria',
            'seleccionando_producto',
            'seleccionando_opcion_roll',
            'seleccionando_envoltura',
            'seleccionando_salsa',
            'seleccionando_cantidad',
            'seleccionando_entrega',
            'ingresando_direccion',
            'revisando_carrito',
            'confirmando_pedido',
            'seleccionando_pago',
            'esperando_pago',
            'finalizado'
        )),
    CONSTRAINT ck_conversacion_wa_id CHECK (BTRIM(wa_id) <> ''),
    CONSTRAINT ck_conversacion_telefono CHECK (BTRIM(telefono) <> ''),
    CONSTRAINT ck_conversacion_contexto CHECK (JSONB_TYPEOF(contexto) = 'object')
);

CREATE INDEX ix_conversaciones_whatsapp_cliente
    ON conversaciones_whatsapp(id_cliente);

CREATE TABLE pedidos (
    id_pedido                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_operacion_cliente       UUID,
    id_turno                   BIGINT,
    id_cliente                 BIGINT REFERENCES clientes(id_cliente) ON DELETE SET NULL,
    id_conversacion_whatsapp   BIGINT REFERENCES conversaciones_whatsapp(id_conversacion) ON DELETE SET NULL,
    estado                     VARCHAR(30) NOT NULL DEFAULT 'borrador',
    subtotal                   NUMERIC(12,0) NOT NULL DEFAULT 0 CHECK (subtotal >= 0),
    descuento                  NUMERIC(12,0) NOT NULL DEFAULT 0 CHECK (descuento >= 0),
    tipo_entrega               VARCHAR(20) NOT NULL,
    canal_origen               VARCHAR(20) NOT NULL,
    costo_envio                NUMERIC(12,0) NOT NULL DEFAULT 0 CHECK (costo_envio >= 0),
    total                      NUMERIC(12,0)
        GENERATED ALWAYS AS (subtotal - descuento + costo_envio) STORED,
    observaciones              TEXT,
    creado_en                  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    actualizado_en             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    listo_en                   TIMESTAMPTZ,
    pendiente_impresion        BOOLEAN NOT NULL DEFAULT FALSE,
    impreso_en                 TIMESTAMPTZ,
    impreso_por                VARCHAR(150),
    impresion_token            UUID,
    impresion_tomada_en        TIMESTAMPTZ,
    impresion_tomada_por       VARCHAR(150),
    impresion_reintentar_en    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    intentos_impresion         INTEGER NOT NULL DEFAULT 0,
    ultimo_error_impresion     VARCHAR(500),
    CONSTRAINT ck_pedido_estado CHECK (estado IN (
        'borrador', 'pendiente_pago', 'confirmado', 'en_preparacion',
        'listo', 'cancelado'
    )),
    CONSTRAINT ck_pedido_tipo_entrega CHECK (tipo_entrega IN ('retiro', 'delivery')),
    CONSTRAINT ck_pedido_canal CHECK (canal_origen IN ('web', 'whatsapp', 'local', 'telefono')),
    CONSTRAINT ck_pedido_descuento CHECK (descuento <= subtotal),
    CONSTRAINT ck_pedido_intentos_impresion CHECK (intentos_impresion >= 0),
    CONSTRAINT ck_pedido_retiro_sin_envio
        CHECK (tipo_entrega <> 'retiro' OR costo_envio = 0)
);

CREATE INDEX ix_pedidos_cliente ON pedidos(id_cliente);
CREATE INDEX ix_pedidos_conversacion ON pedidos(id_conversacion_whatsapp);
CREATE INDEX ix_pedidos_estado_creado ON pedidos(estado, creado_en);
CREATE UNIQUE INDEX ux_pedidos_operacion_cliente
    ON pedidos(id_operacion_cliente)
    WHERE id_operacion_cliente IS NOT NULL;
CREATE INDEX ix_pedidos_impresion_pendiente
    ON pedidos(impresion_reintentar_en, creado_en)
    WHERE pendiente_impresion = TRUE AND impreso_en IS NULL;

CREATE TABLE detalle_pedidos (
    id_detalle         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_pedido          BIGINT NOT NULL REFERENCES pedidos(id_pedido) ON DELETE CASCADE,
    id_producto        BIGINT NOT NULL REFERENCES productos(id_producto) ON DELETE RESTRICT,
    nombre_producto    VARCHAR(150) NOT NULL,
    cantidad           INTEGER NOT NULL CHECK (cantidad > 0),
    precio_unitario    NUMERIC(12,0) NOT NULL CHECK (precio_unitario >= 0),
    puntos_unitarios   INTEGER NOT NULL DEFAULT 0 CHECK (puntos_unitarios >= 0),
    subtotal_puntos    INTEGER
        GENERATED ALWAYS AS (cantidad * puntos_unitarios) STORED,
    tipo_item          VARCHAR(20) NOT NULL DEFAULT 'compra',
    subtotal           NUMERIC(12,0)
        GENERATED ALWAYS AS (cantidad * precio_unitario) STORED,
    observaciones      TEXT,
    CONSTRAINT ck_detalle_nombre CHECK (BTRIM(nombre_producto) <> ''),
    CONSTRAINT ck_detalle_tipo_item CHECK (tipo_item IN ('compra', 'canje')),
    CONSTRAINT ck_detalle_compra_o_canje CHECK (
        (tipo_item = 'compra' AND puntos_unitarios = 0)
        OR
        (tipo_item = 'canje' AND precio_unitario = 0 AND puntos_unitarios > 0)
    )
);

CREATE INDEX ix_detalle_pedidos_pedido ON detalle_pedidos(id_pedido);
CREATE INDEX ix_detalle_pedidos_producto ON detalle_pedidos(id_producto);

CREATE TABLE configuracion_roll_pedido (
    id_configuracion          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_detalle                BIGINT NOT NULL UNIQUE
        REFERENCES detalle_pedidos(id_detalle) ON DELETE CASCADE,
    id_opcion_roll            BIGINT NOT NULL
        REFERENCES opciones_roll(id_opcion_roll) ON DELETE RESTRICT,
    id_producto_envoltura     BIGINT NOT NULL
        REFERENCES producto_envolturas(id_producto_envoltura) ON DELETE RESTRICT,
    id_salsa                  BIGINT NOT NULL
        REFERENCES salsas(id_salsa) ON DELETE RESTRICT,
    configuracion_snapshot    JSONB NOT NULL,
    CONSTRAINT ck_configuracion_snapshot_objeto
        CHECK (JSONB_TYPEOF(configuracion_snapshot) = 'object')
);

CREATE INDEX ix_configuracion_roll_opcion
    ON configuracion_roll_pedido(id_opcion_roll);
CREATE INDEX ix_configuracion_roll_producto_envoltura
    ON configuracion_roll_pedido(id_producto_envoltura);
CREATE INDEX ix_configuracion_roll_salsa
    ON configuracion_roll_pedido(id_salsa);

CREATE TABLE entregas_pedido (
    id_entrega         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_pedido          BIGINT NOT NULL UNIQUE REFERENCES pedidos(id_pedido) ON DELETE CASCADE,
    nombre_receptor    VARCHAR(150) NOT NULL,
    telefono_receptor  VARCHAR(20) NOT NULL,
    calle              VARCHAR(150) NOT NULL,
    numero             VARCHAR(20) NOT NULL,
    departamento       VARCHAR(30),
    comuna             VARCHAR(100) NOT NULL,
    referencia         TEXT,
    estado             VARCHAR(30) NOT NULL DEFAULT 'pendiente',
    despachado_en      TIMESTAMPTZ,
    entregado_en       TIMESTAMPTZ,
    creado_en          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    actualizado_en     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_entrega_estado CHECK (estado IN (
        'pendiente', 'asignada', 'en_camino', 'entregada', 'fallida', 'cancelada'
    )),
    CONSTRAINT ck_entrega_nombre CHECK (BTRIM(nombre_receptor) <> ''),
    CONSTRAINT ck_entrega_telefono CHECK (BTRIM(telefono_receptor) <> ''),
    CONSTRAINT ck_entrega_calle CHECK (BTRIM(calle) <> ''),
    CONSTRAINT ck_entrega_numero CHECK (BTRIM(numero) <> ''),
    CONSTRAINT ck_entrega_comuna CHECK (BTRIM(comuna) <> ''),
    CONSTRAINT ck_entrega_fechas CHECK (
        entregado_en IS NULL
        OR despachado_en IS NULL
        OR entregado_en >= despachado_en
    )
);

CREATE TABLE checkouts (
    id_checkout          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_pedido            BIGINT NOT NULL REFERENCES pedidos(id_pedido) ON DELETE CASCADE,
    id_preferencia       TEXT NOT NULL UNIQUE,
    referencia_comercio  VARCHAR(100) NOT NULL UNIQUE,
    url_pago             TEXT NOT NULL,
    monto                NUMERIC(12,0) NOT NULL CHECK (monto > 0),
    moneda               CHAR(3) NOT NULL DEFAULT 'CLP' CHECK (moneda ~ '^[A-Z]{3}$'),
    creado_en            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    vence_en             TIMESTAMPTZ,
    CONSTRAINT ck_checkout_vigencia CHECK (vence_en IS NULL OR vence_en > creado_en),
    CONSTRAINT ck_checkout_url CHECK (BTRIM(url_pago) <> '')
);

CREATE INDEX ix_checkouts_pedido ON checkouts(id_pedido);

CREATE TABLE pagos (
    id_pago             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_pedido           BIGINT NOT NULL REFERENCES pedidos(id_pedido) ON DELETE RESTRICT,
    id_checkout         BIGINT REFERENCES checkouts(id_checkout) ON DELETE SET NULL,
    monto               NUMERIC(12,0) NOT NULL CHECK (monto > 0),
    metodo              VARCHAR(30) NOT NULL,
    estado              VARCHAR(30) NOT NULL DEFAULT 'pendiente',
    id_pago_proveedor   TEXT,
    creado_en           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    pagado_en           TIMESTAMPTZ,
    actualizado_en      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    proveedor           VARCHAR(30) NOT NULL,
    moneda              CHAR(3) NOT NULL DEFAULT 'CLP' CHECK (moneda ~ '^[A-Z]{3}$'),
    estado_proveedor    VARCHAR(50),
    detalle_estado      VARCHAR(100),
    CONSTRAINT uq_pago_proveedor UNIQUE (proveedor, id_pago_proveedor),
    CONSTRAINT ck_pago_metodo CHECK (metodo IN ('efectivo', 'transferencia', 'tarjeta', 'edenred')),
    CONSTRAINT ck_pago_estado CHECK (estado IN (
        'pendiente', 'aprobado', 'rechazado', 'cancelado', 'reembolsado'
    )),
    CONSTRAINT ck_pago_proveedor CHECK (proveedor IN ('manual', 'mercadopago')),
    CONSTRAINT ck_pago_fecha CHECK (pagado_en IS NULL OR pagado_en >= creado_en)
);

CREATE INDEX ix_pagos_pedido ON pagos(id_pedido);
CREATE INDEX ix_pagos_checkout ON pagos(id_checkout) WHERE id_checkout IS NOT NULL;
CREATE INDEX ix_pagos_estado ON pagos(estado);

CREATE TABLE eventos_pagos (
    id_evento             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_pago               BIGINT REFERENCES pagos(id_pago) ON DELETE SET NULL,
    proveedor             VARCHAR(30) NOT NULL,
    id_evento_proveedor   TEXT NOT NULL,
    tipo                  VARCHAR(100) NOT NULL,
    id_recurso_proveedor  TEXT NOT NULL,
    estado_procesamiento  VARCHAR(20) NOT NULL DEFAULT 'pendiente',
    intentos              INTEGER NOT NULL DEFAULT 0 CHECK (intentos >= 0),
    ultimo_error          TEXT,
    recibido_en           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    procesado_en          TIMESTAMPTZ,
    CONSTRAINT uq_evento_pago_proveedor UNIQUE (proveedor, id_evento_proveedor),
    CONSTRAINT ck_evento_pago_estado
        CHECK (estado_procesamiento IN ('pendiente', 'procesado', 'error')),
    CONSTRAINT ck_evento_pago_fecha
        CHECK (procesado_en IS NULL OR procesado_en >= recibido_en)
);

CREATE INDEX ix_eventos_pagos_pago ON eventos_pagos(id_pago);
CREATE INDEX ix_eventos_pagos_pendientes
    ON eventos_pagos(estado_procesamiento, recibido_en)
    WHERE estado_procesamiento IN ('pendiente', 'error');

CREATE TABLE reglas_fidelizacion (
    id_regla          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    nombre            VARCHAR(100) NOT NULL,
    monto_por_punto   NUMERIC(12,0) NOT NULL CHECK (monto_por_punto > 0),
    compra_minima     NUMERIC(12,0) NOT NULL DEFAULT 0 CHECK (compra_minima >= 0),
    dias_vigencia     INTEGER CHECK (dias_vigencia IS NULL OR dias_vigencia > 0),
    activa            BOOLEAN NOT NULL DEFAULT TRUE,
    vigente_desde     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    vigente_hasta     TIMESTAMPTZ,
    CONSTRAINT ck_regla_fidelizacion_nombre CHECK (BTRIM(nombre) <> ''),
    CONSTRAINT ck_regla_fidelizacion_vigencia
        CHECK (vigente_hasta IS NULL OR vigente_hasta > vigente_desde)
);

CREATE TABLE cuentas_fidelizacion (
    id_cuenta          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_cliente         BIGINT NOT NULL UNIQUE REFERENCES clientes(id_cliente) ON DELETE RESTRICT,
    saldo_puntos       INTEGER NOT NULL DEFAULT 0 CHECK (saldo_puntos >= 0),
    puntos_acumulados  INTEGER NOT NULL DEFAULT 0 CHECK (puntos_acumulados >= 0),
    puntos_canjeados   INTEGER NOT NULL DEFAULT 0 CHECK (puntos_canjeados >= 0),
    estado             VARCHAR(20) NOT NULL DEFAULT 'activa',
    actualizado_en     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    creado_en          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_cuenta_fidelizacion_estado
        CHECK (estado IN ('activa', 'suspendida', 'cerrada'))
);

CREATE TABLE movimiento_puntos (
    id_movimiento      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_cuenta          BIGINT NOT NULL REFERENCES cuentas_fidelizacion(id_cuenta) ON DELETE RESTRICT,
    id_pedido          BIGINT REFERENCES pedidos(id_pedido) ON DELETE RESTRICT,
    id_regla           BIGINT REFERENCES reglas_fidelizacion(id_regla) ON DELETE RESTRICT,
    tipo               VARCHAR(30) NOT NULL,
    puntos             INTEGER NOT NULL CHECK (puntos <> 0),
    saldo_anterior     INTEGER NOT NULL CHECK (saldo_anterior >= 0),
    saldo_resultante   INTEGER NOT NULL CHECK (saldo_resultante >= 0),
    descripcion        TEXT,
    vence_en           TIMESTAMPTZ,
    creado_en          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_movimiento_puntos_tipo CHECK (tipo IN (
        'acumulacion', 'canje', 'vencimiento', 'ajuste', 'reversion', 'bienvenida'
    )),
    CONSTRAINT ck_movimiento_puntos_saldo
        CHECK (saldo_resultante = saldo_anterior + puntos),
    CONSTRAINT ck_movimiento_puntos_signo CHECK (
        (tipo IN ('acumulacion', 'bienvenida') AND puntos > 0)
        OR (tipo IN ('canje', 'vencimiento') AND puntos < 0)
        OR tipo IN ('ajuste', 'reversion')
    )
);

CREATE INDEX ix_movimiento_puntos_cuenta
    ON movimiento_puntos(id_cuenta, creado_en);
CREATE INDEX ix_movimiento_puntos_pedido
    ON movimiento_puntos(id_pedido) WHERE id_pedido IS NOT NULL;
CREATE INDEX ix_movimiento_puntos_regla
    ON movimiento_puntos(id_regla) WHERE id_regla IS NOT NULL;

CREATE TABLE mensajes_whatsapp (
    id_mensaje            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_conversacion       BIGINT NOT NULL
        REFERENCES conversaciones_whatsapp(id_conversacion) ON DELETE RESTRICT,
    id_pedido             BIGINT REFERENCES pedidos(id_pedido) ON DELETE SET NULL,
    id_mensaje_proveedor  TEXT UNIQUE,
    direccion             VARCHAR(20) NOT NULL,
    tipo                  VARCHAR(30) NOT NULL,
    contenido             TEXT,
    nombre_plantilla      VARCHAR(150),
    estado                VARCHAR(20) NOT NULL,
    opcion_seleccionada   VARCHAR(100),
    detalle_error         TEXT,
    enviado_en            TIMESTAMPTZ,
    entregado_en          TIMESTAMPTZ,
    leido_en              TIMESTAMPTZ,
    creado_en             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_mensaje_whatsapp_direccion
        CHECK (direccion IN ('entrante', 'saliente')),
    CONSTRAINT ck_mensaje_whatsapp_estado
        CHECK (estado IN ('pendiente', 'recibido', 'enviado', 'entregado', 'leido', 'fallido')),
    CONSTRAINT ck_mensaje_whatsapp_fechas CHECK (
        (entregado_en IS NULL OR enviado_en IS NULL OR entregado_en >= enviado_en)
        AND (leido_en IS NULL OR entregado_en IS NULL OR leido_en >= entregado_en)
    )
);

CREATE INDEX ix_mensajes_whatsapp_conversacion
    ON mensajes_whatsapp(id_conversacion, creado_en);
CREATE INDEX ix_mensajes_whatsapp_pedido
    ON mensajes_whatsapp(id_pedido) WHERE id_pedido IS NOT NULL;

CREATE TABLE usuarios (
    id_usuario       BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    nombre           VARCHAR(150) NOT NULL,
    email            VARCHAR(254) NOT NULL,
    password_hash    TEXT NOT NULL,
    estado           VARCHAR(20) NOT NULL DEFAULT 'activo',
    creado_en        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    actualizado_en   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_usuario_nombre CHECK (BTRIM(nombre) <> ''),
    CONSTRAINT ck_usuario_email CHECK (BTRIM(email) <> ''),
    CONSTRAINT ck_usuario_password_hash CHECK (BTRIM(password_hash) <> ''),
    CONSTRAINT ck_usuario_estado CHECK (estado IN ('activo', 'bloqueado', 'deshabilitado'))
);

CREATE UNIQUE INDEX uq_usuarios_email ON usuarios(LOWER(email));

CREATE TABLE roles (
    id_rol       SMALLINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    codigo       VARCHAR(30) NOT NULL UNIQUE,
    nombre       VARCHAR(50) NOT NULL,
    descripcion  TEXT,
    CONSTRAINT ck_rol_codigo CHECK (BTRIM(codigo) <> ''),
    CONSTRAINT ck_rol_nombre CHECK (BTRIM(nombre) <> '')
);

CREATE TABLE usuario_roles (
    id_usuario_rol BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_usuario     BIGINT NOT NULL REFERENCES usuarios(id_usuario) ON DELETE CASCADE,
    id_rol         SMALLINT NOT NULL REFERENCES roles(id_rol) ON DELETE RESTRICT,
    asignado_en    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_usuario_rol UNIQUE (id_usuario, id_rol)
);

CREATE INDEX ix_usuario_roles_rol ON usuario_roles(id_rol);

CREATE TABLE sesiones_usuario (
    id_sesion    BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_usuario   BIGINT NOT NULL REFERENCES usuarios(id_usuario) ON DELETE CASCADE,
    token_hash   CHAR(64) NOT NULL UNIQUE,
    dispositivo  VARCHAR(150),
    creado_en    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expira_en    TIMESTAMPTZ NOT NULL,
    revocada_en  TIMESTAMPTZ,
    CONSTRAINT ck_sesion_expiracion CHECK (expira_en > creado_en)
);

CREATE INDEX ix_sesiones_usuario_vigente
    ON sesiones_usuario(token_hash, expira_en)
    WHERE revocada_en IS NULL;

CREATE TABLE turnos (
    id_turno             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_usuario_apertura  BIGINT NOT NULL REFERENCES usuarios(id_usuario) ON DELETE RESTRICT,
    id_usuario_cierre    BIGINT REFERENCES usuarios(id_usuario) ON DELETE RESTRICT,
    estado               VARCHAR(20) NOT NULL DEFAULT 'abierto',
    abierto_en           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    cerrado_en           TIMESTAMPTZ,
    monto_inicial        NUMERIC(12,0) NOT NULL DEFAULT 0,
    efectivo_contado     NUMERIC(12,0),
    efectivo_esperado    NUMERIC(12,0),
    diferencia_efectivo  NUMERIC(12,0),
    observaciones        TEXT,
    creado_en            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    actualizado_en       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_turno_estado CHECK (estado IN ('abierto', 'cerrado')),
    CONSTRAINT ck_turno_apertura_cierre CHECK (
        (estado = 'abierto' AND cerrado_en IS NULL AND id_usuario_cierre IS NULL)
        OR
        (estado = 'cerrado' AND cerrado_en IS NOT NULL AND id_usuario_cierre IS NOT NULL)
    ),
    CONSTRAINT ck_turno_fechas CHECK (cerrado_en IS NULL OR cerrado_en >= abierto_en),
    CONSTRAINT ck_turno_monto_inicial CHECK (monto_inicial >= 0),
    CONSTRAINT ck_turno_efectivo_contado CHECK (
        efectivo_contado IS NULL OR efectivo_contado >= 0
    ),
    CONSTRAINT ck_turno_efectivo_esperado CHECK (
        efectivo_esperado IS NULL OR efectivo_esperado >= 0
    )
);

CREATE UNIQUE INDEX uq_turno_abierto
    ON turnos(estado)
    WHERE estado = 'abierto';
CREATE INDEX ix_turnos_usuario_apertura ON turnos(id_usuario_apertura);
CREATE INDEX ix_turnos_usuario_cierre
    ON turnos(id_usuario_cierre) WHERE id_usuario_cierre IS NOT NULL;
CREATE INDEX ix_turnos_abierto_en ON turnos(abierto_en DESC);

CREATE TABLE movimientos_caja (
    id_movimiento  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_turno       BIGINT NOT NULL REFERENCES turnos(id_turno) ON DELETE RESTRICT,
    tipo           VARCHAR(20) NOT NULL,
    monto          NUMERIC(12,0) NOT NULL,
    motivo         VARCHAR(250) NOT NULL,
    creado_en      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_movimiento_caja_tipo CHECK (tipo IN ('ingreso', 'retiro', 'gasto')),
    CONSTRAINT ck_movimiento_caja_monto CHECK (monto > 0),
    CONSTRAINT ck_movimiento_caja_motivo CHECK (BTRIM(motivo) <> '')
);

CREATE INDEX ix_movimientos_caja_turno
    ON movimientos_caja(id_turno, creado_en);

ALTER TABLE pedidos
    ADD CONSTRAINT fk_pedidos_turnos
    FOREIGN KEY (id_turno) REFERENCES turnos(id_turno) ON DELETE RESTRICT;

CREATE INDEX ix_pedidos_turno ON pedidos(id_turno) WHERE id_turno IS NOT NULL;

CREATE TABLE historial_estados_pedido (
    id_cambio       BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id_pedido       BIGINT NOT NULL REFERENCES pedidos(id_pedido) ON DELETE RESTRICT,
    estado_anterior VARCHAR(30),
    estado_nuevo    VARCHAR(30) NOT NULL,
    motivo          VARCHAR(500),
    origen          VARCHAR(30) NOT NULL DEFAULT 'sistema',
    id_usuario      BIGINT REFERENCES usuarios(id_usuario) ON DELETE SET NULL,
    cambiado_en     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX ix_historial_estados_pedido
    ON historial_estados_pedido(id_pedido, cambiado_en, id_cambio);

CREATE TABLE archivos_mensuales (
    periodo_inicio        DATE PRIMARY KEY,
    periodo_fin           DATE NOT NULL,
    sha256                CHAR(64) NOT NULL,
    tamano_bytes          BIGINT NOT NULL CHECK (tamano_bytes >= 0),
    preparado_en          TIMESTAMPTZ NOT NULL,
    confirmado_en         TIMESTAMPTZ,
    confirmado_por        BIGINT REFERENCES usuarios(id_usuario) ON DELETE SET NULL,
    purgado_en            TIMESTAMPTZ,
    version_esquema       INTEGER NOT NULL DEFAULT 2 CHECK (version_esquema > 0),
    mensajes_purgados_en  TIMESTAMPTZ,
    CONSTRAINT ck_archivo_periodo
        CHECK (periodo_fin = (periodo_inicio + INTERVAL '1 month')::DATE),
    CONSTRAINT ck_archivo_sha256 CHECK (sha256 ~ '^[0-9a-fA-F]{64}$')
);

CREATE INDEX ix_pedidos_archivo_creado ON pedidos(creado_en);
CREATE INDEX ix_mensajes_archivo_creado ON mensajes_whatsapp(creado_en);
CREATE INDEX ix_turnos_archivo_cerrado
    ON turnos(cerrado_en) WHERE cerrado_en IS NOT NULL;
CREATE INDEX ix_eventos_pagos_archivo_recibido
    ON eventos_pagos(recibido_en) WHERE id_pago IS NULL;

-- Mantiene actualizado_en sin depender de que cada cliente recuerde modificarlo.
CREATE FUNCTION establecer_actualizado_en()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.actualizado_en := NOW();
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_productos_actualizado_en
    BEFORE UPDATE ON productos
    FOR EACH ROW EXECUTE FUNCTION establecer_actualizado_en();
CREATE TRIGGER trg_opciones_roll_actualizado_en
    BEFORE UPDATE ON opciones_roll
    FOR EACH ROW EXECUTE FUNCTION establecer_actualizado_en();
CREATE TRIGGER trg_productos_canje_actualizado_en
    BEFORE UPDATE ON productos_canje
    FOR EACH ROW EXECUTE FUNCTION establecer_actualizado_en();
CREATE TRIGGER trg_promociones_admin_actualizado_en
    BEFORE UPDATE ON promociones_admin
    FOR EACH ROW EXECUTE FUNCTION establecer_actualizado_en();
CREATE TRIGGER trg_clientes_actualizado_en
    BEFORE UPDATE ON clientes
    FOR EACH ROW EXECUTE FUNCTION establecer_actualizado_en();
CREATE TRIGGER trg_conversaciones_actualizado_en
    BEFORE UPDATE ON conversaciones_whatsapp
    FOR EACH ROW EXECUTE FUNCTION establecer_actualizado_en();
CREATE TRIGGER trg_pedidos_actualizado_en
    BEFORE UPDATE ON pedidos
    FOR EACH ROW EXECUTE FUNCTION establecer_actualizado_en();
CREATE TRIGGER trg_entregas_actualizado_en
    BEFORE UPDATE ON entregas_pedido
    FOR EACH ROW EXECUTE FUNCTION establecer_actualizado_en();
CREATE TRIGGER trg_pagos_actualizado_en
    BEFORE UPDATE ON pagos
    FOR EACH ROW EXECUTE FUNCTION establecer_actualizado_en();
CREATE TRIGGER trg_cuentas_fidelizacion_actualizado_en
    BEFORE UPDATE ON cuentas_fidelizacion
    FOR EACH ROW EXECUTE FUNCTION establecer_actualizado_en();
CREATE TRIGGER trg_usuarios_actualizado_en
    BEFORE UPDATE ON usuarios
    FOR EACH ROW EXECUTE FUNCTION establecer_actualizado_en();
CREATE TRIGGER trg_turnos_actualizado_en
    BEFORE UPDATE ON turnos
    FOR EACH ROW EXECUTE FUNCTION establecer_actualizado_en();

-- Una venta se asigna al turno abierto cuando deja de ser borrador. Los
-- pedidos históricos anteriores a esta tabla pueden conservar id_turno NULL.
CREATE FUNCTION validar_turno_del_pedido()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_turno_abierto BOOLEAN;
    v_inicia_venta  BOOLEAN;
    v_validar_turno BOOLEAN;
BEGIN
    IF TG_OP = 'UPDATE' THEN
        IF OLD.id_turno IS NOT NULL
           AND NEW.id_turno IS DISTINCT FROM OLD.id_turno THEN
            RAISE EXCEPTION 'No se puede cambiar el turno del pedido % una vez asignado',
                OLD.id_pedido;
        END IF;

        v_inicia_venta :=
            OLD.estado = 'borrador'
            AND NEW.estado NOT IN ('borrador', 'cancelado');
        v_validar_turno :=
            NEW.id_turno IS DISTINCT FROM OLD.id_turno OR v_inicia_venta;
    ELSE
        v_inicia_venta := NEW.estado NOT IN ('borrador', 'cancelado');
        v_validar_turno := TRUE;
    END IF;

    IF v_inicia_venta AND NEW.id_turno IS NULL THEN
        RAISE EXCEPTION 'El pedido % necesita un turno abierto para iniciar la venta',
            NEW.id_pedido;
    END IF;

    IF NEW.id_turno IS NOT NULL AND v_validar_turno THEN
        SELECT EXISTS (
            SELECT 1
            FROM turnos t
            WHERE t.id_turno = NEW.id_turno
              AND t.estado = 'abierto'
              AND t.cerrado_en IS NULL
        ) INTO v_turno_abierto;

        IF NOT v_turno_abierto THEN
            RAISE EXCEPTION 'El turno % no está abierto', NEW.id_turno;
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_validar_turno_del_pedido
    BEFORE INSERT OR UPDATE OF id_turno, estado ON pedidos
    FOR EACH ROW EXECUTE FUNCTION validar_turno_del_pedido();

-- El flujo operativo actual ingresa directamente a preparación. El estado
-- confirmado se conserva para compatibilidad con datos históricos.
CREATE FUNCTION validar_transicion_estado_pedido()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.estado = OLD.estado THEN
        RETURN NEW;
    END IF;

    IF NOT (
        (OLD.estado = 'borrador'
            AND NEW.estado IN ('pendiente_pago', 'en_preparacion', 'cancelado'))
        OR (OLD.estado = 'pendiente_pago'
            AND NEW.estado IN ('en_preparacion', 'cancelado'))
        OR (OLD.estado = 'confirmado'
            AND NEW.estado IN ('en_preparacion', 'cancelado'))
        OR (OLD.estado = 'en_preparacion'
            AND NEW.estado IN ('listo', 'cancelado'))
    ) THEN
        RAISE EXCEPTION 'Transición de pedido no permitida: % -> %',
            OLD.estado, NEW.estado;
    END IF;

    IF NEW.estado = 'listo' THEN
        NEW.listo_en := COALESCE(NEW.listo_en, NOW());
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_pedidos_validar_transicion
    BEFORE UPDATE OF estado ON pedidos
    FOR EACH ROW EXECUTE FUNCTION validar_transicion_estado_pedido();

CREATE FUNCTION auditar_estado_pedido()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_usuario TEXT := current_setting('kameron.usuario_id', TRUE);
    v_motivo  TEXT := current_setting('kameron.motivo', TRUE);
    v_origen  TEXT := current_setting('kameron.origen', TRUE);
BEGIN
    IF TG_OP = 'INSERT' THEN
        INSERT INTO historial_estados_pedido (
            id_pedido, estado_anterior, estado_nuevo, motivo, origen, id_usuario
        ) VALUES (
            NEW.id_pedido,
            NULL,
            NEW.estado,
            NULL,
            COALESCE(NULLIF(v_origen, ''), 'sistema'),
            NULLIF(v_usuario, '')::BIGINT
        );
    ELSIF NEW.estado IS DISTINCT FROM OLD.estado THEN
        INSERT INTO historial_estados_pedido (
            id_pedido, estado_anterior, estado_nuevo, motivo, origen, id_usuario
        ) VALUES (
            NEW.id_pedido,
            OLD.estado,
            NEW.estado,
            NULLIF(v_motivo, ''),
            COALESCE(NULLIF(v_origen, ''), 'sistema'),
            NULLIF(v_usuario, '')::BIGINT
        );
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_pedidos_auditar_estado
    AFTER INSERT OR UPDATE OF estado ON pedidos
    FOR EACH ROW EXECUTE FUNCTION auditar_estado_pedido();

-- La impresión se toma desde una cola persistente. Al entrar a preparación se
-- encola una sola comanda; una cancelación retira cualquier trabajo pendiente.
CREATE FUNCTION encolar_impresion_pedido()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.estado = 'cancelado' THEN
        NEW.pendiente_impresion := FALSE;
        NEW.impresion_token := NULL;
        NEW.impresion_tomada_en := NULL;
        NEW.impresion_tomada_por := NULL;
    ELSIF TG_OP = 'INSERT' THEN
        IF NEW.estado = 'en_preparacion' AND NEW.impreso_en IS NULL THEN
            NEW.pendiente_impresion := TRUE;
            NEW.impresion_reintentar_en := NOW();
        END IF;
    ELSIF NEW.estado = 'en_preparacion'
          AND OLD.estado IS DISTINCT FROM NEW.estado
          AND NEW.impreso_en IS NULL THEN
        NEW.pendiente_impresion := TRUE;
        NEW.impresion_reintentar_en := NOW();
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_pedidos_encolar_impresion
    BEFORE INSERT OR UPDATE OF estado ON pedidos
    FOR EACH ROW EXECUTE FUNCTION encolar_impresion_pedido();

-- Comprueba que opción y envoltura pertenezcan al producto del detalle.
CREATE FUNCTION validar_configuracion_roll()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_producto_detalle             BIGINT;
    v_requiere_configuracion       BOOLEAN;
    v_producto_opcion              BIGINT;
    v_envoltura_fija               BIGINT;
    v_producto_envoltura           BIGINT;
BEGIN
    SELECT d.id_producto, p.requiere_configuracion
      INTO v_producto_detalle, v_requiere_configuracion
      FROM detalle_pedidos d
      JOIN productos p ON p.id_producto = d.id_producto
     WHERE d.id_detalle = NEW.id_detalle;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El detalle % no existe', NEW.id_detalle;
    END IF;

    IF NOT v_requiere_configuracion THEN
        RAISE EXCEPTION 'El producto del detalle % no admite configuración de roll', NEW.id_detalle;
    END IF;

    SELECT id_producto, id_producto_envoltura_fija
      INTO v_producto_opcion, v_envoltura_fija
      FROM opciones_roll
     WHERE id_opcion_roll = NEW.id_opcion_roll
       AND activa = TRUE;

    IF NOT FOUND OR v_producto_opcion <> v_producto_detalle THEN
        RAISE EXCEPTION 'La opción % no pertenece al producto del detalle %',
            NEW.id_opcion_roll, NEW.id_detalle;
    END IF;

    SELECT id_producto
      INTO v_producto_envoltura
      FROM producto_envolturas
     WHERE id_producto_envoltura = NEW.id_producto_envoltura;

    IF NOT FOUND OR v_producto_envoltura <> v_producto_detalle THEN
        RAISE EXCEPTION 'La envoltura configurada no pertenece al producto del detalle %',
            NEW.id_detalle;
    END IF;

    IF v_envoltura_fija IS NOT NULL
       AND v_envoltura_fija <> NEW.id_producto_envoltura THEN
        RAISE EXCEPTION 'La opción % exige su envoltura fija', NEW.id_opcion_roll;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_validar_configuracion_roll
    BEFORE INSERT OR UPDATE ON configuracion_roll_pedido
    FOR EACH ROW EXECUTE FUNCTION validar_configuracion_roll();

-- Esta comprobación es diferida: detalle y configuración deben insertarse
-- dentro de la misma transacción.
CREATE FUNCTION comprobar_detalle_configurado(p_id_detalle BIGINT)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
    v_requiere BOOLEAN;
    v_tiene    BOOLEAN;
BEGIN
    SELECT p.requiere_configuracion
      INTO v_requiere
      FROM detalle_pedidos d
      JOIN productos p ON p.id_producto = d.id_producto
     WHERE d.id_detalle = p_id_detalle;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    SELECT EXISTS (
        SELECT 1
          FROM configuracion_roll_pedido c
         WHERE c.id_detalle = p_id_detalle
    ) INTO v_tiene;

    IF v_requiere AND NOT v_tiene THEN
        RAISE EXCEPTION 'El detalle % requiere configuración de roll', p_id_detalle;
    ELSIF NOT v_requiere AND v_tiene THEN
        RAISE EXCEPTION 'El detalle % corresponde a un producto simple', p_id_detalle;
    END IF;
END;
$$;

CREATE FUNCTION validar_detalle_configurado_desde_detalle()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM comprobar_detalle_configurado(NEW.id_detalle);
    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER trg_detalle_configurado
    AFTER INSERT OR UPDATE OF id_producto ON detalle_pedidos
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION validar_detalle_configurado_desde_detalle();

CREATE FUNCTION validar_detalle_configurado_desde_configuracion()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        PERFORM comprobar_detalle_configurado(OLD.id_detalle);
        RETURN OLD;
    END IF;

    PERFORM comprobar_detalle_configurado(NEW.id_detalle);

    IF TG_OP = 'UPDATE' AND OLD.id_detalle <> NEW.id_detalle THEN
        PERFORM comprobar_detalle_configurado(OLD.id_detalle);
    END IF;

    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER trg_configuracion_mantiene_detalle
    AFTER INSERT OR UPDATE OR DELETE ON configuracion_roll_pedido
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION validar_detalle_configurado_desde_configuracion();

-- Una dirección de entrega solo corresponde a pedidos delivery.
CREATE FUNCTION validar_entrega_delivery()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_tipo_entrega VARCHAR(20);
BEGIN
    SELECT tipo_entrega INTO v_tipo_entrega
      FROM pedidos
     WHERE id_pedido = NEW.id_pedido;

    IF v_tipo_entrega <> 'delivery' THEN
        RAISE EXCEPTION 'El pedido % no es delivery', NEW.id_pedido;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_validar_entrega_delivery
    BEFORE INSERT OR UPDATE OF id_pedido ON entregas_pedido
    FOR EACH ROW EXECUTE FUNCTION validar_entrega_delivery();

-- =========================
-- Datos iniciales de carta
-- =========================

INSERT INTO categorias (nombre_categoria, descripcion_categoria, orden) VALUES
    ('Rolls', 'Rolls recomendados, veganos, sin arroz y premium', 1),
    ('Hand rolls', 'Hand rolls con envoltura seleccionable', 2),
    ('Sushi y entradas', 'Nigiris, entradas y torta de sushi', 3),
    ('Promociones', 'Promociones de cortes y salsas', 4),
    ('Bebidas', 'Bebidas y acompañamientos', 5),
    ('Churrascos', 'Churrascos y sándwiches', 6),
    ('Chorrillanas', 'Chorrillanas y pichanga', 7),
    ('Completos', 'Completos y ass', 8),
    ('Empanadas', 'Empanadas', 9),
    ('Tacos XL', 'Tacos XL', 10),
    ('Quesadillas', 'Quesadillas', 11);

INSERT INTO envolturas (nombre) VALUES
    ('Nori'),
    ('Sésamo'),
    ('Ciboulette'),
    ('Merkén'),
    ('Panko'),
    ('Tempura'),
    ('Coco'),
    ('Palta'),
    ('Queso'),
    ('Chocolate'),
    ('Salmón'),
    ('Tempura merkén'),
    ('Queso ciboulette'),
    ('Palta ciboulette'),
    ('Queso frutilla'),
    ('Coco chocolate'),
    ('Chocolate nuez'),
    ('Queso nuez');

INSERT INTO salsas (nombre, descripcion) VALUES
    ('Soya', 'Salsa de soya incluida con los rolls'),
    ('Teriyaki', 'Salsa teriyaki incluida con los rolls');

INSERT INTO productos (
    id_categoria, nombre_producto, descripcion, precio,
    requiere_configuracion, activo, disponible
)
SELECT
    c.id_categoria,
    v.nombre_producto,
    v.descripcion,
    v.precio,
    v.requiere_configuracion,
    v.activo,
    v.disponible
FROM (VALUES
    ('Rolls', 'Rolls de pollo', 'Roll recomendado; elegir opción numerada, envoltura y salsa', 4000, TRUE, TRUE, TRUE),
    ('Rolls', 'Rolls de camarón', 'Roll recomendado; elegir opción numerada, envoltura y salsa', 4000, TRUE, TRUE, TRUE),
    ('Rolls', 'Rolls de kanikama', 'Roll recomendado; elegir opción numerada, envoltura y salsa', 4000, TRUE, TRUE, TRUE),
    ('Rolls', 'Rolls vegetarianos', 'Roll recomendado; elegir opción numerada, envoltura y salsa', 4000, TRUE, TRUE, TRUE),
    ('Rolls', 'Rolls dulces', 'Roll recomendado; elegir opción numerada, envoltura y salsa', 4000, TRUE, TRUE, TRUE),
    ('Rolls', 'Rolls veganos', 'Elegir opción numerada, envoltura y salsa', 4990, TRUE, TRUE, TRUE),
    ('Rolls', 'Rolls sin arroz', 'Cada opción posee una envoltura fija; elegir salsa', 5990, TRUE, TRUE, TRUE),
    ('Rolls', 'Premium salmón', 'Elegir opción numerada, envoltura y salsa', 5990, TRUE, TRUE, TRUE),
    ('Rolls', 'Premium queso', 'Envoltura fija de queso gauda derretido; elegir opción y salsa', 5990, TRUE, TRUE, TRUE),
    ('Rolls', 'Premium carne', 'Elegir opción numerada, envoltura y salsa', 5990, TRUE, TRUE, TRUE),

    ('Hand rolls', 'Hand Roll pollo teriyaki', 'Pollo teriyaki, cebollín y queso crema', 3000, TRUE, TRUE, TRUE),
    ('Hand rolls', 'Hand Roll vegetariano', 'Champiñón, pimentón y queso crema', 3000, TRUE, TRUE, TRUE),
    ('Hand rolls', 'Hand Roll camarón', 'Camarón, cebollín y queso crema', 3500, TRUE, TRUE, TRUE),
    ('Hand rolls', 'Hand Roll pollo palta', 'Pollo, palta y queso crema', 3500, TRUE, TRUE, TRUE),

    ('Sushi y entradas', 'Nigiri de salmón', '4 unidades', 4890, FALSE, TRUE, TRUE),
    ('Sushi y entradas', 'Nigiri de camarón', '4 unidades', 4590, FALSE, TRUE, TRUE),
    ('Sushi y entradas', 'Camarones furay', '6 unidades', 4290, FALSE, TRUE, TRUE),
    ('Sushi y entradas', 'Gyosas de pollo', '6 unidades', 4290, FALSE, TRUE, TRUE),
    ('Sushi y entradas', 'Gyosas de cerdo', '6 unidades', 4290, FALSE, TRUE, TRUE),
    ('Sushi y entradas', 'Gyosas de camarón', '6 unidades', 4290, FALSE, TRUE, TRUE),
    ('Sushi y entradas', 'Arrollados primavera', '6 unidades', 4290, FALSE, TRUE, TRUE),
    ('Sushi y entradas', 'Arrollados jamón-queso', '6 unidades', 4290, FALSE, TRUE, TRUE),
    ('Sushi y entradas', 'Torta de sushi', '120 cortes surtidos; requiere 24 horas de anticipación', 37990, FALSE, TRUE, TRUE),

    ('Promociones', 'Promoción 40 cortes', 'Cortes recomendados; no incluye premium; incluye 2 salsas', 13990, FALSE, TRUE, TRUE),
    ('Promociones', 'Promoción 50 cortes', 'Cortes recomendados; no incluye premium; incluye 3 salsas', 15990, FALSE, TRUE, TRUE),
    ('Promociones', 'Promoción 60 cortes', 'Cortes recomendados; no incluye premium; incluye 4 salsas', 17990, FALSE, TRUE, TRUE),
    ('Promociones', 'Promoción 70 cortes', 'Cortes recomendados; no incluye premium; incluye 5 salsas', 19990, FALSE, TRUE, TRUE),
    ('Promociones', 'Promoción 80 cortes', 'Cortes recomendados; no incluye premium; incluye 6 salsas', 22990, FALSE, TRUE, TRUE),
    ('Promociones', 'Salsa extra', 'Elegir soya o teriyaki', 500, FALSE, TRUE, TRUE),

    ('Bebidas', 'Bebida 1,5 litros', NULL, 2500, FALSE, TRUE, TRUE),
    ('Bebidas', 'Jugo 1,5 litros', NULL, 2000, FALSE, TRUE, TRUE),
    ('Bebidas', 'Teriyaki en lata', NULL, 1200, FALSE, TRUE, TRUE),
    ('Bebidas', 'Jengibre', NULL, 1000, FALSE, TRUE, TRUE),
    ('Bebidas', 'Wasabi', NULL, 1000, FALSE, TRUE, TRUE),

    ('Churrascos', 'Churrasco italiano', 'Carne de vacuno, tomate, palta y mayonesa', 6500, FALSE, TRUE, TRUE),
    ('Churrascos', 'Churrasco a lo pobre', 'Carne de vacuno, tocino, cebolla salteada, huevo y papas fritas', 6800, FALSE, TRUE, TRUE),
    ('Churrascos', 'Kameron', 'Lomito de cerdo, tocino, pepinillo, queso mantecoso y mostaza', 5500, FALSE, TRUE, TRUE),
    ('Churrascos', 'Chacarero', 'Carne de vacuno, tomate, porotos verdes, ají y mayonesa', 6500, FALSE, TRUE, TRUE),
    ('Churrascos', 'Mechada italiana', 'Carne mechada, tomate, palta y mayonesa', 6800, FALSE, TRUE, TRUE),
    ('Churrascos', 'Barros Luco', 'Carne de vacuno y queso fundido', 6500, FALSE, TRUE, TRUE),
    ('Churrascos', 'Pollo italiano', 'Pollo a la plancha, tomate, palta y mayonesa', 6500, FALSE, TRUE, TRUE),
    ('Churrascos', 'Pollo granjero', 'Pollo a la plancha, lechuga, tomate, palta y mayonesa', 6800, FALSE, TRUE, TRUE),
    ('Churrascos', 'Especial Luco pollo', 'Pollo a la plancha, champiñón y queso fundido', 6800, FALSE, TRUE, TRUE),
    ('Churrascos', 'Pollo Luco', 'Pollo a la plancha y queso fundido', 6500, FALSE, TRUE, TRUE),

    ('Chorrillanas', 'Chorrillana clásica', 'Papas fritas, carne de vacuno, cebolla salteada, chorizo, vienesa y huevo frito', 14990, FALSE, TRUE, TRUE),
    ('Chorrillanas', 'Chorrillana especial', 'Papas fritas, carne, pollo, cebolla salteada, chorizo, vienesa, tocino y huevo frito', 15990, FALSE, TRUE, TRUE),
    ('Chorrillanas', 'Chorrillana chicken', 'Papas fritas, pollo a la plancha, cebolla salteada, pimentón, champiñón y huevo frito', 14990, FALSE, TRUE, TRUE),
    ('Chorrillanas', 'Chorrillana vegetariana', 'Papas fritas, cebolla salteada, pimentón, champiñón, choclo, aceituna y queso fundido', 14990, FALSE, TRUE, TRUE),
    ('Chorrillanas', 'Pichanga chilena', 'Papas fritas, carne de vacuno, tomate, palta, aceituna, cebollita perla, pepinillo y huevo duro', 15990, FALSE, TRUE, TRUE),

    ('Completos', 'Completo italiano', 'Vienesa, tomate, palta y mayonesa', 2300, FALSE, TRUE, TRUE),
    ('Completos', 'Ass carne', 'Carne a la plancha, tomate, palta y mayonesa', 3600, FALSE, TRUE, TRUE),
    ('Completos', 'Ass pollo', 'Pollo a la plancha, tomate, palta y mayonesa', 3600, FALSE, TRUE, TRUE),
    ('Completos', 'Ass mixto', 'Carne y pollo a la plancha, tomate, palta y mayonesa', 3990, FALSE, TRUE, TRUE),
    ('Completos', 'Ass Luco', 'Carne a la plancha y queso fundido', 3600, FALSE, TRUE, TRUE),

    ('Empanadas', 'Empanada de queso', NULL, 2000, FALSE, TRUE, TRUE),
    ('Empanadas', 'Empanada camarón-queso', NULL, 3600, FALSE, TRUE, TRUE),
    ('Empanadas', 'Empanada napolitana', NULL, 2300, FALSE, TRUE, TRUE),

    ('Tacos XL', 'Taco italiano', 'Base de lechuga, carne salteada con cebollín, tomate, palta y mayonesa', 6500, FALSE, TRUE, TRUE),
    ('Tacos XL', 'Taco de pollo', 'Base de lechuga, pollo salteado con cebollín, champiñón, pimentón y mayonesa', 6500, FALSE, TRUE, TRUE),
    ('Tacos XL', 'Taco de camarón', 'Base de lechuga, camarón salteado con cebollín, palta, queso y aceituna', 6500, FALSE, TRUE, TRUE),
    ('Tacos XL', 'Taco vegetariano', 'Base de lechuga, choclo, champiñón, palmito y aceituna; precio pendiente de confirmar', 0, FALSE, TRUE, FALSE),

    ('Quesadillas', 'Quesadilla de carne', 'Carne a la plancha, pimentón y queso fundido', 3990, FALSE, TRUE, TRUE),
    ('Quesadillas', 'Quesadilla de pollo', 'Pollo a la plancha, aceituna y queso fundido', 3990, FALSE, TRUE, TRUE),
    ('Quesadillas', 'Quesadilla vegetariana', 'Choclo, champiñón y queso fundido', 3990, FALSE, TRUE, TRUE)
) AS v(
    categoria, nombre_producto, descripcion, precio,
    requiere_configuracion, activo, disponible
)
JOIN categorias c ON c.nombre_categoria = v.categoria;

-- Envolturas seleccionables para rolls regulares y hand rolls.
WITH productos_configurables(nombre_producto) AS (
    VALUES
        ('Rolls de pollo'),
        ('Rolls de camarón'),
        ('Rolls de kanikama'),
        ('Rolls vegetarianos'),
        ('Rolls dulces'),
        ('Rolls veganos'),
        ('Premium salmón'),
        ('Premium carne'),
        ('Hand Roll pollo teriyaki'),
        ('Hand Roll vegetariano'),
        ('Hand Roll camarón'),
        ('Hand Roll pollo palta')
), precios_envoltura(nombre, precio_adicional) AS (
    VALUES
        ('Nori', 0),
        ('Sésamo', 0),
        ('Ciboulette', 0),
        ('Merkén', 0),
        ('Panko', 0),
        ('Tempura', 0),
        ('Coco', 1000),
        ('Palta', 1000),
        ('Queso', 1500),
        ('Chocolate', 1500),
        ('Salmón', 2000),
        ('Tempura merkén', 2000),
        ('Queso ciboulette', 2000),
        ('Palta ciboulette', 2000),
        ('Queso frutilla', 2000),
        ('Coco chocolate', 2000),
        ('Chocolate nuez', 2000),
        ('Queso nuez', 2000)
)
INSERT INTO producto_envolturas (
    id_envoltura, id_producto, precio_adicional, predeterminada
)
SELECT
    e.id_envoltura,
    p.id_producto,
    pe.precio_adicional,
    e.nombre = 'Nori'
FROM productos_configurables pc
JOIN productos p ON p.nombre_producto = pc.nombre_producto
CROSS JOIN precios_envoltura pe
JOIN envolturas e ON e.nombre = pe.nombre;

-- Envolturas incluidas y fijas en rolls sin arroz y premium queso.
INSERT INTO producto_envolturas (
    id_envoltura, id_producto, precio_adicional, predeterminada
)
SELECT
    e.id_envoltura,
    p.id_producto,
    0,
    FALSE
FROM productos p
CROSS JOIN envolturas e
WHERE p.nombre_producto = 'Rolls sin arroz'
  AND e.nombre IN ('Panko', 'Queso', 'Salmón', 'Palta');

INSERT INTO producto_envolturas (
    id_envoltura, id_producto, precio_adicional, predeterminada
)
SELECT e.id_envoltura, p.id_producto, 0, TRUE
FROM productos p
JOIN envolturas e ON e.nombre = 'Queso'
WHERE p.nombre_producto = 'Premium queso';

INSERT INTO opciones_roll (
    id_producto, numero_opcion, nombre, ingredientes
)
SELECT p.id_producto, v.numero_opcion, v.nombre, v.ingredientes
FROM (VALUES
    ('Rolls de pollo', 1, 'N.º 1', 'Pollo, aceituna y queso crema'),
    ('Rolls de pollo', 2, 'N.º 2', 'Pollo, cebollín y queso crema'),
    ('Rolls de pollo', 3, 'N.º 3', 'Pollo, palta y queso crema'),
    ('Rolls de pollo', 4, 'N.º 4', 'Pollo, champiñón y queso crema'),
    ('Rolls de pollo', 5, 'N.º 5', 'Pollo, nuez y queso crema'),
    ('Rolls de pollo', 6, 'N.º 6', 'Pollo, pimentón y queso crema'),
    ('Rolls de pollo', 7, 'N.º 7', 'Pollo, palmito y queso crema'),
    ('Rolls de pollo', 8, 'N.º 8', 'Pollo, piña y queso crema'),

    ('Rolls de camarón', 9, 'N.º 9', 'Camarón, palta y queso crema'),
    ('Rolls de camarón', 10, 'N.º 10', 'Camarón, cebollín y queso crema'),
    ('Rolls de camarón', 11, 'N.º 11', 'Camarón, pepino y queso crema'),
    ('Rolls de camarón', 12, 'N.º 12', 'Camarón, aceituna y queso crema'),
    ('Rolls de camarón', 13, 'N.º 13', 'Camarón, piña y queso crema'),
    ('Rolls de camarón', 14, 'N.º 14', 'Camarón, nuez y queso crema'),

    ('Rolls de kanikama', 15, 'N.º 15', 'Kanikama, cebollín y queso crema'),
    ('Rolls de kanikama', 16, 'N.º 16', 'Kanikama, palta y queso crema'),
    ('Rolls de kanikama', 17, 'N.º 17', 'Kanikama, pimentón y queso crema'),
    ('Rolls de kanikama', 18, 'N.º 18', 'Kanikama, palmito y queso crema'),
    ('Rolls de kanikama', 19, 'N.º 19', 'Kanikama, pepino y queso crema'),
    ('Rolls de kanikama', 20, 'N.º 20', 'Kanikama, choclo y queso crema'),

    ('Rolls vegetarianos', 21, 'N.º 21', 'Palmito, pimentón y queso crema'),
    ('Rolls vegetarianos', 22, 'N.º 22', 'Palmito, cebollín y queso crema'),
    ('Rolls vegetarianos', 23, 'N.º 23', 'Palmito, palta y queso crema'),
    ('Rolls vegetarianos', 24, 'N.º 24', 'Palmito, pepino y queso crema'),
    ('Rolls vegetarianos', 25, 'N.º 25', 'Palmito, aceituna y queso crema'),
    ('Rolls vegetarianos', 26, 'N.º 26', 'Palmito, champiñón y queso crema'),

    ('Rolls dulces', 27, 'N.º 27', 'Piña, nuez y queso crema'),
    ('Rolls dulces', 28, 'N.º 28', 'Piña, frutilla y queso crema'),
    ('Rolls dulces', 29, 'N.º 29', 'Piña, papaya y queso crema'),
    ('Rolls dulces', 30, 'N.º 30', 'Frutilla, nuez y queso crema'),
    ('Rolls dulces', 31, 'N.º 31', 'Frutilla, papaya y queso crema'),
    ('Rolls dulces', 32, 'N.º 32', 'Papaya, nuez y queso crema'),

    ('Rolls veganos', 33, 'N.º 33', 'Aceituna, palmito y cebollín'),
    ('Rolls veganos', 34, 'N.º 34', 'Aceituna, palmito y champiñón'),
    ('Rolls veganos', 35, 'N.º 35', 'Aceituna, palta y pimentón'),
    ('Rolls veganos', 36, 'N.º 36', 'Choclo, palmito y palta'),
    ('Rolls veganos', 37, 'N.º 37', 'Champiñón, pimentón y aceituna'),
    ('Rolls veganos', 38, 'N.º 38', 'Pepino, palta y cebollín'),

    ('Rolls sin arroz', 39, 'N.º 39', 'Pollo furay, aceituna, cebollín, palta y queso crema'),
    ('Rolls sin arroz', 40, 'N.º 40', 'Carne, aceituna, cebollín, palta y queso crema'),
    ('Rolls sin arroz', 41, 'N.º 41', 'Camarón furay, cebollín, palta, pepino y queso crema'),
    ('Rolls sin arroz', 42, 'N.º 42', 'Kanikama, palta, palmito, champiñón y queso crema'),
    ('Rolls sin arroz', 43, 'N.º 43', 'Salmón, cebollín, palta, aceituna y queso crema'),
    ('Rolls sin arroz', 44, 'N.º 44', 'Frutilla, piña, nuez, coco y queso crema'),

    ('Premium salmón', 45, 'N.º 45', 'Salmón, palta y queso crema'),
    ('Premium salmón', 46, 'N.º 46', 'Salmón, camarón y queso crema'),
    ('Premium salmón', 47, 'N.º 47', 'Salmón, cebollín y queso crema'),
    ('Premium salmón', 48, 'N.º 48', 'Salmón, palmito y queso crema'),
    ('Premium salmón', 49, 'N.º 49', 'Salmón, aceituna y queso crema'),
    ('Premium salmón', 50, 'N.º 50', 'Salmón, pepino y queso crema'),

    ('Premium queso', 51, 'N.º 51', 'Pollo, cebollín y queso'),
    ('Premium queso', 52, 'N.º 52', 'Pollo, aceituna y queso'),
    ('Premium queso', 53, 'N.º 53', 'Pollo, champiñón y queso'),
    ('Premium queso', 54, 'N.º 54', 'Camarón, cebollín y queso'),
    ('Premium queso', 55, 'N.º 55', 'Camarón, aceituna y queso'),
    ('Premium queso', 56, 'N.º 56', 'Camarón, champiñón y queso'),

    ('Premium carne', 57, 'N.º 57', 'Carne, palta y queso crema'),
    ('Premium carne', 58, 'N.º 58', 'Carne, pepino y queso crema'),
    ('Premium carne', 59, 'N.º 59', 'Carne, aceituna y queso crema'),
    ('Premium carne', 60, 'N.º 60', 'Carne, pollo y queso crema'),
    ('Premium carne', 61, 'N.º 61', 'Carne, champiñón y queso crema'),
    ('Premium carne', 62, 'N.º 62', 'Carne, pimentón y queso crema'),

    ('Hand Roll pollo teriyaki', 1, 'Relleno fijo', 'Pollo teriyaki, cebollín y queso crema'),
    ('Hand Roll vegetariano', 1, 'Relleno fijo', 'Champiñón, pimentón y queso crema'),
    ('Hand Roll camarón', 1, 'Relleno fijo', 'Camarón, cebollín y queso crema'),
    ('Hand Roll pollo palta', 1, 'Relleno fijo', 'Pollo, palta y queso crema')
) AS v(producto, numero_opcion, nombre, ingredientes)
JOIN productos p ON p.nombre_producto = v.producto;

-- Relaciona cada opción de envoltura fija con la asociación del mismo producto.
UPDATE opciones_roll o
SET id_producto_envoltura_fija = pe.id_producto_envoltura
FROM productos p
JOIN producto_envolturas pe ON pe.id_producto = p.id_producto
JOIN envolturas e ON e.id_envoltura = pe.id_envoltura
WHERE o.id_producto = p.id_producto
  AND p.nombre_producto = 'Rolls sin arroz'
  AND e.nombre = CASE o.numero_opcion
      WHEN 39 THEN 'Panko'
      WHEN 40 THEN 'Queso'
      WHEN 41 THEN 'Salmón'
      WHEN 42 THEN 'Palta'
      WHEN 43 THEN 'Palta'
      WHEN 44 THEN 'Queso'
  END;

UPDATE opciones_roll o
SET id_producto_envoltura_fija = pe.id_producto_envoltura
FROM productos p
JOIN producto_envolturas pe ON pe.id_producto = p.id_producto
JOIN envolturas e ON e.id_envoltura = pe.id_envoltura
WHERE o.id_producto = p.id_producto
  AND p.nombre_producto = 'Premium queso'
  AND e.nombre = 'Queso';

INSERT INTO roles (codigo, nombre, descripcion) VALUES
    ('administrador', 'Administrador', 'Acceso completo a la administración'),
    ('caja', 'Caja', 'Ingreso de pedidos y cobros'),
    ('cocina', 'Cocina', 'Preparación y cambio de estado de pedidos');

INSERT INTO configuracion_operativa (clave, valor) VALUES
    ('tiempo_objetivo_cocina_minutos', '20');

-- Estas versiones ya forman parte de este archivo consolidado. Registrarlas
-- evita que el backend vuelva a aplicar sus ALTER TABLE sobre una base nueva.
INSERT INTO schema_migrations (id) VALUES
    ('002_edenred_payment_method'),
    ('003_cash_shift_accounting'),
    ('004_admin_catalog'),
    ('005_promotions'),
    ('006_admin_kitchen_settings'),
    ('007_kitchen_print_queue'),
    ('008_authentication'),
    ('009_order_lifecycle'),
    ('010_direct_kitchen_preparation'),
    ('011_offline_order_idempotency'),
    ('012_monthly_archives'),
    ('013_archive_retention_policy');

-- No se crean usuarios: una contraseña inicial debe generarse mediante el backend
-- con Argon2id o bcrypt y nunca guardarse directamente.
-- No se crean reglas ni productos de canje: sus valores de puntos aún no están definidos.

-- Validaciones del catálogo inicial. Si una carga queda incompleta, toda la
-- transacción se revierte.
DO $$
DECLARE
    v_categorias INTEGER;
    v_productos INTEGER;
    v_opciones INTEGER;
    v_envolturas INTEGER;
    v_salsas INTEGER;
BEGIN
    SELECT COUNT(*) INTO v_categorias FROM categorias;
    SELECT COUNT(*) INTO v_productos FROM productos;
    SELECT COUNT(*) INTO v_opciones FROM opciones_roll;
    SELECT COUNT(*) INTO v_envolturas FROM envolturas;
    SELECT COUNT(*) INTO v_salsas FROM salsas;

    IF v_categorias <> 11 THEN
        RAISE EXCEPTION 'Se esperaban 11 categorías y se cargaron %', v_categorias;
    END IF;
    IF v_productos <> 64 THEN
        RAISE EXCEPTION 'Se esperaban 64 productos y se cargaron %', v_productos;
    END IF;
    IF v_opciones <> 66 THEN
        RAISE EXCEPTION 'Se esperaban 66 opciones y se cargaron %', v_opciones;
    END IF;
    IF v_envolturas <> 18 THEN
        RAISE EXCEPTION 'Se esperaban 18 envolturas y se cargaron %', v_envolturas;
    END IF;
    IF v_salsas <> 2 THEN
        RAISE EXCEPTION 'Se esperaban 2 salsas y se cargaron %', v_salsas;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM productos p
        WHERE p.requiere_configuracion
          AND NOT EXISTS (
              SELECT 1 FROM opciones_roll o
              WHERE o.id_producto = p.id_producto AND o.activa
          )
    ) THEN
        RAISE EXCEPTION 'Hay productos configurables sin opciones activas';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM productos p
        WHERE p.requiere_configuracion
          AND NOT EXISTS (
              SELECT 1 FROM producto_envolturas pe
              WHERE pe.id_producto = p.id_producto
          )
    ) THEN
        RAISE EXCEPTION 'Hay productos configurables sin envolturas';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM opciones_roll o
        JOIN productos p ON p.id_producto = o.id_producto
        WHERE p.nombre_producto IN ('Rolls sin arroz', 'Premium queso')
          AND o.id_producto_envoltura_fija IS NULL
    ) THEN
        RAISE EXCEPTION 'Hay opciones de envoltura fija sin configurar';
    END IF;
END;
$$;

COMMIT;
