# Respuesta de emergencia de WhatsApp

Este proxy se ejecuta en Twilio Functions, fuera de Render. Reenvía cada webhook al backend y devuelve exactamente el TwiML generado por la aplicación. Si Render no responde, supera el tiempo de espera o devuelve un error, Twilio contesta al cliente con un aviso temporal.

## Crear la Function

1. En Twilio abre **Functions and Assets > Services** y crea un servicio, por ejemplo `kameron-sushi-emergency`.
2. Agrega una Function con ruta `/whatsapp` y visibilidad **Protected**.
3. Copia el contenido de `functions/whatsapp-emergency-proxy.protected.js`.
4. En **Dependencies**, confirma que exista el paquete `twilio`.
5. En **Environment Variables** agrega:

   - `BACKEND_WEBHOOK_URL=https://kameronsushi-backend.onrender.com/webhooks/twilio/whatsapp`
   - `BACKEND_TIMEOUT_MS=12000`
   - `EMERGENCY_MESSAGE=Kameron Sushi no puede recibir pedidos por WhatsApp en este momento. Por favor, inténtalo nuevamente en unos minutos.`

6. Activa **Add my Twilio credentials (ACCOUNT_SID) and (AUTH_TOKEN) to ENV**. La Function usa `AUTH_TOKEN` para volver a firmar la solicitud dirigida al backend.
7. Presiona **Deploy All** y copia la URL publicada de `/whatsapp`.
8. En el Sandbox de WhatsApp cambia **When a message comes in** por la URL de la Function, usando método `POST`.

No cambies `BACKEND_WEBHOOK_URL` por una URL con una barra adicional al final. La firma incluye la URL exacta.

## Prueba controlada

1. Con Render funcionando, envía `hola`: debe continuar el menú normal.
2. Cambia temporalmente `BACKEND_WEBHOOK_URL` a una ruta inexistente y despliega la Function.
3. Envía otro mensaje: debe recibirse el aviso de emergencia.
4. Restaura la URL correcta y vuelve a desplegar.

El proxy no registra números, nombres ni el contenido del mensaje. Solo escribe `timeout` o `request_failed` en caso de falla.
