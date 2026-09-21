# Kameron Sushi — Backend

Base de ASP.NET Core 10 con controladores y arquitectura limpia. Todos los proyectos usan `net10.0`.

## Estructura

```text
backend/
├── KameronSushi.Backend.slnx
├── global.json
└── src/
    ├── KameronSushi.Domain/
    │   ├── Entities/
    │   ├── ValueObjects/
    │   ├── Enums/
    │   └── Exceptions/
    ├── KameronSushi.Application/
    │   ├── Abstractions/
    │   ├── Features/
    │   └── Common/
    ├── KameronSushi.Infrastructure/
    │   ├── Persistence/
    │   ├── Services/
    │   └── Integrations/
    └── KameronSushi.Api/
        ├── Controllers/
        ├── Contracts/
        ├── Properties/
        └── Program.cs
```

Las carpetas vacías contienen `.gitkeep` para conservarlas en Git.

## Responsabilidades y dependencias

| Proyecto | Responsabilidad | Referencias a proyectos |
| --- | --- | --- |
| Domain | Entidades, objetos de valor y reglas de negocio; independiente de HTTP y persistencia. | Ninguna |
| Application | Casos de uso organizados por funcionalidad en `Features`; interfaces para acceso a datos y servicios externos en `Abstractions`. | Domain |
| Infrastructure | Implementaciones de las interfaces de Application, persistencia e integraciones externas. | Application |
| Api | Controladores HTTP, contratos de entrada/salida y composición de dependencias en `Program.cs`. | Application, Infrastructure |

Las dependencias apuntan hacia el núcleo. Application y Domain no deben referenciar Infrastructure ni Api. Los controladores delegarán las operaciones a Application; las implementaciones concretas se registrarán en el contenedor de inyección de dependencias desde la API. Infrastructure puede acceder a Domain mediante la referencia transitiva de Application.

La solución ya implementa persistencia PostgreSQL, el flujo inicial del bot de WhatsApp y las primeras operaciones para caja y cocina. La autenticación de operadores todavía está pendiente y debe agregarse antes de exponer estas rutas en producción.

## Configuración local sin filtrar secretos

El host y el nombre de la base están en `appsettings.json` porque no son credenciales. El usuario, la contraseña y las credenciales de Meta se guardan con Secret Manager, fuera del repositorio.

Desde `backend/src/KameronSushi.Api`:

```powershell
dotnet user-secrets set "Database:Username" "USUARIO_DE_RENDER"
dotnet user-secrets set "Database:Password" "CONTRASEÑA_DE_RENDER"
dotnet user-secrets set "WhatsApp:AccessToken" "TOKEN_DE_ACCESO_DE_META"
dotnet user-secrets set "WhatsApp:VerifyToken" "UN_TOKEN_LARGO_CREADO_POR_TI"
dotnet user-secrets set "WhatsApp:AppSecret" "APP_SECRET_DE_META"
```

Los valores no deben copiarse a `appsettings.json`, archivos `.http`, capturas ni commits. `.env` también está ignorado, pero Secret Manager es la opción usada por este proyecto para desarrollo. Puedes revisar únicamente los nombres configurados con `dotnet user-secrets list`; ese comando también imprime los valores, así que no compartas su salida.

En Render configura las mismas claves como variables de entorno, reemplazando `:` por `__`:

```text
Database__Username
Database__Password
WhatsApp__PhoneNumberId
WhatsApp__BusinessAccountId
WhatsApp__AccessToken
WhatsApp__VerifyToken
WhatsApp__AppSecret
```

La URL pública proporcionada se descompuso en `Host`, `Port=5432`, `Name=bd_kameron_sushi` y `SslMode=Require`. Npgsql arma internamente la conexión; la contraseña no aparece en la URL versionada.

## Migración necesaria

Si `KameronSushi.sql` ya fue ejecutado, aplica una vez `database/migrations/001_whatsapp_context.sql`. El script es idempotente y agrega el JSON que conserva el avance temporal de la conversación.

Con `psql`, deja que solicite la contraseña para no escribirla en el comando:

```powershell
psql -W "host=dpg-dak0d9gjo6nc73fi53mg-a.oregon-postgres.render.com port=5432 dbname=bd_kameron_sushi user=TU_USUARIO sslmode=require" -v ON_ERROR_STOP=1 -f database/migrations/001_whatsapp_context.sql
```

## Configurar Meta WhatsApp Cloud API

El webhook público es `https://TU-DOMINIO/webhooks/whatsapp`.

1. En la configuración de webhooks de la aplicación Meta, usa esa URL y el mismo valor guardado en `WhatsApp:VerifyToken`.
2. Suscribe el campo `messages` de la cuenta de WhatsApp Business.
3. El número de prueba ya tiene configurados `WhatsApp:PhoneNumberId` y `WhatsApp:BusinessAccountId` en `appsettings.json`. Guarda el token en `WhatsApp:AccessToken` y el secreto de la aplicación Meta en `WhatsApp:AppSecret`.
4. Meta firma los POST con `X-Hub-Signature-256`; el backend rechaza solicitudes sin una firma HMAC-SHA256 válida.

La integración saliente usa Graph API `v26.0`, configurable mediante `WhatsApp:ApiVersion`.

## Flujo disponible

- Webhook GET de verificación y POST de eventos.
- Validación de firma y deduplicación por `id_mensaje_proveedor`.
- Registro de mensajes entrantes, salientes y estados enviado/entregado/leído/fallido.
- Retiro o delivery; para delivery se pide `calle, número, comuna`.
- Categorías y productos desde PostgreSQL, con paginación por los límites de las listas de WhatsApp.
- Opciones, envolturas, recargos, salsa y cantidad para rolls configurables.
- Carrito persistido como pedido borrador y confirmación de pago en efectivo.
- Comandos de recuperación: `menú`, `pedido` y `finalizar`.
- API de caja para cargar el catálogo desde PostgreSQL y crear pedidos locales con sus detalles y pagos.
- API de cocina para consultar pedidos activos y marcarlos como listos.

El pago con tarjeta responde que todavía no está disponible hasta integrar Mercado Pago. La tarifa y cobertura de delivery todavía necesitan una regla comercial; antes de producción se debe configurar ese cálculo. Quitar líneas o cambiar cantidades y los avisos automáticos desde cocina también quedan para la siguiente etapa.

## Ejecutar

Desde la carpeta `backend`, con el SDK .NET 10.0.201 o una banda posterior de .NET 10:

```powershell
dotnet restore KameronSushi.Backend.slnx
dotnet build KameronSushi.Backend.slnx --no-restore
dotnet run --project src/KameronSushi.Api --launch-profile http
```

- Salud del proceso: `http://localhost:5102/health`. Incluye una consulta real a PostgreSQL y devuelve estado no saludable si faltan credenciales o la base no responde.
- Documento OpenAPI en Development: `http://localhost:5102/openapi/v1.json`. No incluye interfaz Swagger UI.
- Peticiones de ejemplo: `src/KameronSushi.Api/KameronSushi.Api.http`.

La aplicación de escritorio usa `https://kameronsushi-backend.onrender.com/`, configurado en su `appsettings.json`. Para trabajar con una API local se puede reemplazar temporalmente por `http://localhost:5102/` y ejecutar primero el backend:

```powershell
dotnet run --project ..\AplicacionEscritorio\KameronSushi.Desktop
```

La URL está en `AplicacionEscritorio/KameronSushi.Desktop/appsettings.json`. En otro equipo o en producción puede reemplazarse sin recompilar mediante la variable `KAMERONSUSHI_API_URL`. La aplicación de escritorio no recibe la contraseña de PostgreSQL; únicamente el backend la conoce.

Al iniciar, el backend registra y aplica sus migraciones pendientes en la tabla `schema_migrations`. La migración `002_edenred_payment_method` permite guardar pagos Edenred.

## API inicial de caja y cocina

| Método | Ruta | Función |
| --- | --- | --- |
| `GET` | `/api/pos/catalog` | Devuelve productos activos y sus selecciones configurables con el precio final. |
| `POST` | `/api/pos/orders` | Crea un pedido local, recalcula sus precios en el servidor y registra sus pagos. |
| `GET` | `/api/pos/orders?status=en_preparacion&limit=50` | Lista pedidos recientes para la caja; el estado es opcional. |
| `GET` | `/api/pos/orders/{orderId}` | Devuelve el encabezado, los ítems y los pagos de un pedido. |
| `GET` | `/api/kitchen/orders` | Lista pedidos confirmados o en preparación para cocina. |
| `PATCH` | `/api/kitchen/orders/{orderId}/ready` | Marca un pedido activo como listo. |

El cliente puede enviar los métodos `efectivo`, `tarjeta` o `edenred`. El backend valida productos, variantes y pagos contra PostgreSQL y nunca acepta el precio enviado por la caja. Los ejemplos completos están en `src/KameronSushi.Api/KameronSushi.Api.http`.

Para desarrollo con HTTPS:

```powershell
dotnet dev-certs https --trust
dotnet run --project src/KameronSushi.Api --launch-profile https
```

El perfil HTTPS utiliza `https://localhost:7227`. En Render, el proxy público realiza la redirección a HTTPS. La gestión de errores utiliza Problem Details. Guarda credenciales en secretos de usuario o variables de entorno, nunca en los archivos versionados de configuración.

## Despliegue Docker en Render

El `Dockerfile` publica la API con .NET 10 y ejecuta únicamente la imagen de runtime. En Render crea un Web Service con runtime Docker y deja el Dockerfile en la raíz del repositorio. La API escucha en `0.0.0.0` usando la variable `PORT` que proporciona Render; si no existe, el contenedor usa el puerto `10000`.

Configura al menos estas variables en **Environment** antes de probar el servicio:

```text
Database__Username
Database__Password
WhatsApp__AccessToken
WhatsApp__VerifyToken
WhatsApp__AppSecret
```

Render termina HTTPS en su proxy y reenvía el protocolo original mediante cabeceras `X-Forwarded-*`, que la API procesa antes de atender el webhook.
