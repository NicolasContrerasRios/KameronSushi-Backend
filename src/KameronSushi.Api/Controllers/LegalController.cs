using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KameronSushi.Api.Controllers;

[ApiController,AllowAnonymous,Route("legal")]
public sealed class LegalController(IConfiguration configuration):ControllerBase
{
    [HttpGet("privacy")]
    public ContentResult Privacy()=>Html("Política de privacidad",PrivacyBody());

    [HttpGet("account-deletion")]
    public ContentResult AccountDeletion()=>Html("Eliminación de cuenta","""
        <h1>Eliminación de cuenta de Kameron Sushi</h1>
        <p>Puedes eliminar tu cuenta directamente desde la aplicación:</p>
        <ol><li>Abre <strong>Mi cuenta</strong>.</li><li>Presiona <strong>Eliminar mi cuenta</strong>.</li><li>Confirma la eliminación.</li></ol>
        <p>Se eliminarán tu cuenta, credenciales, puntos, dispositivos de notificación y datos de contacto. Los pedidos que deban conservarse como registro comercial permanecerán anonimizados y sin asociación a tu cuenta.</p>
        <p>Si no puedes acceder a la aplicación, solicita la eliminación escribiendo al correo de contacto indicado en esta página.</p>
        """);

    private string PrivacyBody()=>$"""
        <h1>Política de privacidad de Kameron Sushi</h1>
        <p><strong>Última actualización:</strong> 2 de noviembre de 2026.</p>
        <p>Kameron Sushi utiliza los datos necesarios para crear cuentas, gestionar pedidos, entregar compras, acumular puntos y enviar notificaciones relacionadas con el servicio.</p>
        <h2>Datos tratados</h2>
        <ul><li>Nombre, teléfono y correo electrónico opcional.</li><li>Dirección y ubicación autorizada para completar un delivery.</li><li>Historial, contenido y estado de pedidos.</li><li>Saldo y movimientos de puntos.</li><li>Identificadores técnicos del dispositivo para inicio de sesión y notificaciones.</li></ul>
        <h2>Finalidad</h2><p>Los datos se usan para identificar al cliente, preparar y entregar pedidos, mantener la sesión, prevenir abuso, administrar beneficios y comunicar cambios relevantes del pedido o del local.</p>
        <h2>Proveedores</h2><p>El servicio utiliza infraestructura de Render y PostgreSQL, Google Sign-In, Firebase Cloud Messaging y Cloudinary. Estos proveedores pueden procesar datos técnicos indispensables para prestar sus servicios.</p>
        <h2>Conservación y eliminación</h2><p>La cuenta puede eliminarse desde la aplicación. Los registros comerciales que deban conservarse quedarán anonimizados y sin vínculo con la cuenta eliminada.</p>
        <h2>Seguridad</h2><p>La comunicación se realiza mediante HTTPS. Los PIN no se guardan en texto legible y las sesiones son revocables.</p>
        <h2>Contacto</h2><p>Para consultas de privacidad escribe a <strong>{WebUtility.HtmlEncode(ContactEmail())}</strong>.</p>
        """;

    private string ContactEmail()=>configuration["Legal:ContactEmail"]??"REEMPLAZAR_CORREO_DE_CONTACTO";
    private ContentResult Html(string title,string body)=>Content($$"""
        <!doctype html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{{WebUtility.HtmlEncode(title)}} · Kameron Sushi</title><style>body{font-family:Arial,sans-serif;max-width:850px;margin:40px auto;padding:0 22px;color:#201d1b;line-height:1.6}h1,h2{color:#b83232}a{color:#b83232}</style></head><body>{{body}}<hr><p>Contacto: {{WebUtility.HtmlEncode(ContactEmail())}}</p></body></html>
        ""","text/html; charset=utf-8");
}
