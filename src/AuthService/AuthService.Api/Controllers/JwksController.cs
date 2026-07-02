using AuthService.Business.DTOs;
using AuthService.Business.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.Api.Controllers;

/// <summary>
/// Publishes the RSA public key as a JWKS document so other services can verify
/// token signatures locally (architecture §6.1). Public + unauthenticated, and
/// deliberately NOT wrapped in the standard envelope — it must be a spec-compliant
/// raw JWKS for interop.
/// </summary>
[ApiController]
[AllowAnonymous]
public class JwksController(JwtKeyService keys) : ControllerBase
{
    // The design doc references both /.well-known/jwks and jwks.json; expose both.
    [HttpGet("auth/v1/.well-known/jwks")]
    [HttpGet("auth/v1/.well-known/jwks.json")]
    public ActionResult<JwksDto> Get() => keys.GetJwks();
}
