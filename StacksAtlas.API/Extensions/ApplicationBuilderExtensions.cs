using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using StacksAtlas.Core.Helpers;
using StacksAtlas.Core.Services.Security;
using StacksAtlas.Core.State;
using StacksAtlas.Core.Services.Auth;
using Microsoft.Extensions.FileProviders;

namespace StacksAtlas.API.Extensions;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseStacksAtlasStartupGate(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var sysState = context.RequestServices.GetService<SystemStateProvider>();
            if (sysState != null && !sysState.IsDatabaseReady)
            {
                var path = context.Request.Path.Value ?? "";

                if (ApplianceBrowserRoutingPolicy.IsBootstrapApiPath(path))
                {
                    await next();
                    return;
                }

                if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new { message = "System initializing. Please retry shortly." });
                    return;
                }

                bool isPageRequest = path == "/" || path == "/index.html" || (!path.StartsWith("/api") && !path.Contains("."));

                if (isPageRequest)
                {
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(@"
                        <!DOCTYPE html>
                        <html>
                        <head>
                            <title>Initializing StacksAtlas...</title>
                            <meta http-equiv='refresh' content='1'>
                            <style>
                                body { background: #0F172A; color: #E2E8F0; font-family: 'Segoe UI', sans-serif; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }
                                .loader { border: 3px solid rgba(255,255,255,0.1); border-top: 3px solid #3B82F6; border-radius: 50%; width: 24px; height: 24px; animation: spin 0.8s linear infinite; margin-bottom: 20px; }
                                @keyframes spin { 0% { transform: rotate(0deg); } 100% { transform: rotate(360deg); } }
                                .content { text-align: center; display: flex; flex-direction: column; align-items: center; }
                                h2 { font-weight: 600; font-size: 1.1rem; margin: 0 0 8px 0; letter-spacing: 0.5px; }
                                p { font-size: 0.85rem; color: #94A3B8; margin: 0; }
                            </style>
                        </head>
                        <body>
                            <div class='content'>
                                <div class='loader'></div>
                                <h2>SYSTEM STARTING...</h2>
                                <p>Securing Database & Initializing Services</p>
                            </div>
                        </body>
                        </html>");
                    return;
                }
            }
            await next();
        });
    }

    public static IApplicationBuilder UsePortableLocalOnly(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (PortableMode.IsEnabled && !PortableMode.AllowLanBind && !ApplianceBrowserRoutingPolicy.IsLoopback(context.Connection.RemoteIpAddress))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { message = "StacksAtlas Portable accepts local connections only." });
                return;
            }

            await next();
        });
    }

    /// <summary>
    /// HTTP-first browser routing on loopback only. API and federation traffic are never redirected.
    /// </summary>
    public static IApplicationBuilder UseApplianceSchemeRouting(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "/";

            if (ApplianceBrowserRoutingPolicy.ShouldBypassSchemeRouting(path, context.Connection.RemoteIpAddress))
            {
                await next();
                return;
            }

            var settings = ApplianceDashboardUrls.LoadSettingsOrDefault(PlatformPaths.BaseDataDir);
            var caPath = ApplianceTlsTrust.GetCaPfxPath(PlatformPaths.BaseDataDir);
            var preferHttps = ApplianceDashboardUrls.WantsHttps(settings)
                && ApplianceDashboardUrls.CanUseHttps(caPath);

            var query = context.Request.QueryString.Value ?? "";

            if (context.Request.IsHttps)
            {
                if (!preferHttps)
                {
                    context.Response.Redirect($"http://127.0.0.1:{settings.HttpPort}{path}{query}");
                    return;
                }
            }
            else if (preferHttps)
            {
                context.Response.Redirect($"https://localhost:{settings.HttpsPort}{path}{query}");
                return;
            }

            await next();
        });
    }

    public static IApplicationBuilder UsePortableHttpBootstrap(this IApplicationBuilder app) =>
        UseApplianceSchemeRouting(app);

    public static IApplicationBuilder UseStacksAtlasSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;

            // Same-origin SPA model: reflect Origin only when it matches this appliance (no wildcard *).
            var origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin)
                && Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
                && string.Equals(originUri.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
                && originUri.Port == context.Request.Host.Port)
            {
                headers.AccessControlAllowOrigin = origin;
                headers.Vary = "Origin";
            }

            headers.AccessControlAllowHeaders = "Content-Type, Authorization";
            headers.AccessControlAllowMethods = "GET,POST,PATCH,PUT,DELETE,OPTIONS";
            headers.AccessControlExposeHeaders = "Content-Disposition";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "SAMEORIGIN";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            if (string.Equals(context.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return;
            }

            await next();
        });
    }

    public static IApplicationBuilder UseStacksAtlasSetupRedirect(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/")
            {
                var authService = context.RequestServices.GetService<IAuthService>();
                if (authService != null && !authService.AnyUsers())
                {
                    context.Response.Redirect("/onboarding");
                    return;
                }
            }
            await next();
        });
    }

    public static void UseStacksAtlasStaticFiles(this WebApplication app, string? webRootToUse)
    {
        if (!string.IsNullOrEmpty(webRootToUse))
        {
            var fileProvider = new PhysicalFileProvider(webRootToUse);
            var options = new DefaultFilesOptions { FileProvider = fileProvider };
            app.UseDefaultFiles(options);
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = fileProvider,
                OnPrepareResponse = ctx => ApplySpaCacheHeaders(ctx.Context),
            });
            app.MapFallbackToFile("index.html", new StaticFileOptions
            {
                FileProvider = fileProvider,
                OnPrepareResponse = ctx => ApplySpaCacheHeaders(ctx.Context),
            });
        }
        else
        {
            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = ctx => ApplySpaCacheHeaders(ctx.Context) });
            app.MapFallbackToFile("index.html", new StaticFileOptions { OnPrepareResponse = ctx => ApplySpaCacheHeaders(ctx.Context) });
        }
    }

    private static void ApplySpaCacheHeaders(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Equals("/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
        }
        else if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    }
}
