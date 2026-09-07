using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PersonalAIAssistant.Memory.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AuthController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public record LoginRequest(string UserId, string? TenantId = "default", string? GeminiApiKey = null, string? Role = "User");
    public record LoginResponse(string Token, string UserId, string TenantId, DateTime ExpiresAtUtc);

    /// <summary>
    /// Simulates client AI application login. Generates a signed JWT session token with user claims
    /// and optional client-provided Gemini API key (BYOK).
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest("UserId is required.");

        var jwtSecret = _configuration["Jwt:SecretKey"] ?? "SuperSecretJwtAuthenticationSigningKey32BytesLongStringForHmacSha256!";
        var jwtIssuer = _configuration["Jwt:Issuer"] ?? "PersonalAIAssistant.Memory";
        var jwtAudience = _configuration["Jwt:Audience"] ?? "PersonalAIAssistant.Memory.Api";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expires = DateTime.UtcNow.AddDays(7);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.UserId),
            new(JwtRegisteredClaimNames.Name, request.UserId),
            new("tenant_id", request.TenantId ?? "default"),
            new(ClaimTypes.Role, request.Role ?? "User")
        };

        if (!string.IsNullOrWhiteSpace(request.GeminiApiKey))
        {
            claims.Add(new Claim("gemini_api_key", request.GeminiApiKey.Trim()));
        }

        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return Ok(new LoginResponse(tokenString, request.UserId, request.TenantId ?? "default", expires));
    }
}
