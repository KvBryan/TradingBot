using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace WebhookListener.Features.Webhooks;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class TradeHub : Hub
{
    // Hub para transmitir actualizaciones de trades en tiempo real.
    // El frontend se suscribe a este hub y escucha el evento "TradeUpdated".
}
