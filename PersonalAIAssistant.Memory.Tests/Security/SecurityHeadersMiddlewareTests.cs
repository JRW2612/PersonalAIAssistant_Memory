using FluentAssertions;
using Microsoft.AspNetCore.Http;
using PersonalAIAssistant.Memory.Api.Middleware;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class SecurityHeadersMiddlewareTests
    {
        [Theory]
        [InlineData("/api/v1/memories")]
        [InlineData("/health")]
        [InlineData("/api/v1/connections")]
        public async Task InvokeAsync_StandardApiRoute_SetsStrictCspAndEnterpriseHeaders(string path)
        {
            // Arrange
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            var middleware = new SecurityHeadersMiddleware(innerContext => Task.CompletedTask);

            // Act
            await middleware.InvokeAsync(context);
            await context.Response.StartAsync();

            // Assert
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
            headers["X-Frame-Options"].ToString().Should().Be("DENY");
            headers["X-XSS-Protection"].ToString().Should().Be("1; mode=block");
            headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
            headers["Permissions-Policy"].ToString().Should().Contain("camera=()");

            headers["Content-Security-Policy"].ToString().Should().Be("default-src 'self'");
        }

        [Theory]
        [InlineData("/swagger")]
        [InlineData("/swagger/index.html")]
        [InlineData("/swagger/v1/swagger.json")]
        [InlineData("/connections")]
        public async Task InvokeAsync_SwaggerAndConnectionsRoutes_PermitsInlineScriptsAndStyles(string path)
        {
            // Arrange
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            var middleware = new SecurityHeadersMiddleware(innerContext => Task.CompletedTask);

            // Act
            await middleware.InvokeAsync(context);
            await context.Response.StartAsync();

            // Assert
            var csp = context.Response.Headers["Content-Security-Policy"].ToString();
            csp.Should().Contain("script-src 'self' 'unsafe-inline'");
            csp.Should().Contain("style-src 'self' 'unsafe-inline'");
            csp.Should().Contain("img-src 'self' data:");
            csp.Should().Contain("font-src 'self' data:");
        }
    }
}
