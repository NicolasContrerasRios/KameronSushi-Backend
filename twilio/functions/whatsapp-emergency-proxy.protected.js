const twilio = require("twilio");

const DEFAULT_BACKEND_URL =
  "https://kameronsushi-backend.onrender.com/webhooks/twilio/whatsapp";
const DEFAULT_MESSAGE =
  "Kameron Sushi no puede recibir pedidos por WhatsApp en este momento. " +
  "Por favor, inténtalo nuevamente en unos minutos.";

exports.handler = async function handler(context, event, callback) {
  const backendUrl = (context.BACKEND_WEBHOOK_URL || DEFAULT_BACKEND_URL).trim();
  const authToken = context.AUTH_TOKEN;
  const timeoutMs = parseTimeout(context.BACKEND_TIMEOUT_MS);

  if (!authToken) {
    console.error("Emergency proxy is missing the Twilio AUTH_TOKEN credential.");
    return callback(null, buildEmergencyResponse(context));
  }

  const parameters = extractWebhookParameters(event);
  const formBody = encodeForm(parameters);
  const signature = twilio.getExpectedTwilioSignature(
    authToken,
    backendUrl,
    parameters,
  );
  const abortController = new AbortController();
  const timeout = setTimeout(() => abortController.abort(), timeoutMs);

  try {
    const backendResponse = await fetch(backendUrl, {
      method: "POST",
      headers: {
        "Content-Type": "application/x-www-form-urlencoded",
        "X-Twilio-Signature": signature,
      },
      body: formBody,
      signal: abortController.signal,
    });
    const responseBody = await backendResponse.text();

    if (!backendResponse.ok || !responseBody.includes("<Response")) {
      throw new Error(`Backend returned HTTP ${backendResponse.status}.`);
    }

    const response = new Twilio.Response();
    response.appendHeader("Content-Type", "application/xml; charset=utf-8");
    response.setBody(responseBody);
    return callback(null, response);
  } catch (error) {
    const reason = error && error.name === "AbortError" ? "timeout" : "request_failed";
    console.error(`WhatsApp backend unavailable (${reason}).`);
    return callback(null, buildEmergencyResponse(context));
  } finally {
    clearTimeout(timeout);
  }
};

function extractWebhookParameters(event) {
  const parameters = {};
  for (const [name, value] of Object.entries(event || {})) {
    if (name === "request" || value === undefined || value === null) continue;
    parameters[name] = Array.isArray(value)
      ? value.map((item) => String(item))
      : String(value);
  }
  return parameters;
}

function encodeForm(parameters) {
  const form = new URLSearchParams();
  for (const [name, value] of Object.entries(parameters)) {
    if (Array.isArray(value)) {
      for (const item of value) form.append(name, item);
    } else {
      form.append(name, value);
    }
  }
  return form.toString();
}

function parseTimeout(value) {
  const parsed = Number.parseInt(value || "12000", 10);
  return Number.isFinite(parsed) && parsed >= 1000 && parsed <= 14000
    ? parsed
    : 12000;
}

function buildEmergencyResponse(context) {
  const response = new Twilio.twiml.MessagingResponse();
  response.message((context.EMERGENCY_MESSAGE || DEFAULT_MESSAGE).trim());
  return response;
}
