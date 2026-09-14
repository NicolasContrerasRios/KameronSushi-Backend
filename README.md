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

Esta base todavía no implementa casos de uso, autenticación ni conexión a una base de datos. Los directorios reservan su ubicación; se añadirán las dependencias necesarias al implementar cada funcionalidad.

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

El pago con tarjeta responde que todavía no está disponible hasta integrar Mercado Pago. La tarifa y cobertura de delivery todavía necesitan una regla comercial; antes de producción se debe configurar ese cálculo. Quitar líneas o cambiar cantidades y los avisos automáticos desde cocina también quedan para la siguiente etapa.

## Ejecutar

Desde la carpeta `backend`, con el SDK .NET 10.0.201 (o un parche posterior de la banda 10.0.2xx):

```powershell
dotnet restore KameronSushi.Backend.slnx
dotnet build KameronSushi.Backend.slnx --no-restore
dotnet run --project src/KameronSushi.Api --launch-profile http
```

- Salud del proceso: `http://localhost:5102/health`. Incluye una consulta real a PostgreSQL y devuelve estado no saludable si faltan credenciales o la base no responde.
- Documento OpenAPI en Development: `http://localhost:5102/openapi/v1.json`. No incluye interfaz Swagger UI.
- Peticiones de ejemplo: `src/KameronSushi.Api/KameronSushi.Api.http`.

Para desarrollo con HTTPS:

```powershell
dotnet dev-certs https --trust
dotnet run --project src/KameronSushi.Api --launch-profile https
```

La API utiliza `https://localhost:7227` y redirige HTTP a HTTPS cuando el puerto HTTPS está configurado. La gestión de errores utiliza Problem Details. Guarda credenciales en secretos de usuario o variables de entorno, nunca en los archivos versionados de configuración.
