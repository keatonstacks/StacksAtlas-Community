using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace StacksAtlas.API.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddStacksAtlasSwagger(this IServiceCollection services)
    {
        services.AddOpenApi(options => 
        {
            options.AddDocumentTransformer((document, context, cancellationToken) => 
            {
                var version = typeof(SwaggerExtensions).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
                document.Info.Title = "StacksAtlas Appliance API";
                document.Info.Version = $"v{version}";
                document.Info.Description = "Industrial-grade network discovery and security auditing engine.";
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Enter your Admin JWT token to authenticate requests."
                };

                document.Security ??= new List<OpenApiSecurityRequirement>();
                document.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
                });
                
                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static void UseStacksAtlasSwagger(this WebApplication app)
    {
        var version = typeof(SwaggerExtensions).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        app.MapOpenApi(); 
        app.UseSwaggerUI(options => 
        {
            options.SwaggerEndpoint("/openapi/v1.json", $"StacksAtlas API v{version}");
            options.RoutePrefix = "api-docs";
            options.HeadContent = GetCustomSwaggerCss();
        });
    }

    private static string GetCustomSwaggerCss()
    {
        return @"
        <style>
            body { background-color: #0F172A !important; margin: 0; padding: 0; }
            .swagger-ui { color: #E2E8F0; font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }
            .swagger-ui .topbar { background-color: #0F172A; border-bottom: 1px solid #1E293B; }
            .swagger-ui .topbar-wrapper img { filter: brightness(0) invert(1); opacity: 0.9; }
            .swagger-ui .info .title { color: #F1F5F9; }
            .swagger-ui .info p { color: #94A3B8; }
            .swagger-ui .info a { color: #3B82F6; }
            .swagger-ui .scheme-container { background-color: #0F172A; border-bottom: 1px solid #334155; margin-bottom: 20px; box-shadow: none; }
            .swagger-ui .opblock .opblock-summary-method { border-radius: 4px; }
            .swagger-ui th, .swagger-ui td { color: #CBD5E1; }
            .swagger-ui .parameter__name, .swagger-ui .parameter__type { color: #F1F5F9; }
            .swagger-ui .tab li { color: #94A3B8; }
            .swagger-ui .tab li.active { color: #F1F5F9; border-bottom: 2px solid #3B82F6; }
            .swagger-ui input[type=text], .swagger-ui input[type=password], .swagger-ui textarea, .swagger-ui select { background: #1E293B; border: 1px solid #334155; color: #F1F5F9; border-radius: 6px; padding: 8px; }
            .swagger-ui .btn { background: #3B82F6; color: white; border: none; box-shadow: 0 2px 4px rgba(59, 130, 246, 0.2); border-radius: 6px; font-weight: 600; }
            .swagger-ui .btn.authorize { color: #10B981; border-color: #10B981; background: rgba(16, 185, 129, 0.1); }
            .swagger-ui .btn.authorize svg { fill: #10B981; }
            .swagger-ui section.models h4 { color: #F1F5F9; border-bottom: 1px solid #334155; }
            .swagger-ui section.models .model-container { background: #1E293B; border-radius: 8px; border: 1px solid #334155; }
            .swagger-ui .model-title { color: #F1F5F9; }
            .swagger-ui .model { color: #CBD5E1; }
            .swagger-ui .prop-type { color: #3B82F6; }
            .swagger-ui .opblock.opblock-get { background: rgba(59, 130, 246, 0.05); border-color: rgba(59, 130, 246, 0.3); }
            .swagger-ui .opblock.opblock-get .opblock-summary-method { background: #3B82F6; }
            .swagger-ui .opblock.opblock-post { background: rgba(16, 185, 129, 0.05); border-color: rgba(16, 185, 129, 0.3); }
            .swagger-ui .opblock.opblock-post .opblock-summary-method { background: #10B981; }
            .swagger-ui .opblock.opblock-delete { background: rgba(239, 68, 68, 0.05); border-color: rgba(239, 68, 68, 0.3); }
            .swagger-ui .opblock.opblock-delete .opblock-summary-method { background: #EF4444; }
            .swagger-ui .opblock.opblock-patch { background: rgba(245, 158, 11, 0.05); border-color: rgba(245, 158, 11, 0.3); }
            .swagger-ui .opblock.opblock-patch .opblock-summary-method { background: #F59E0B; }
            .swagger-ui .opblock-summary-description { color: #CBD5E1; }
            .swagger-ui .opblock-summary-path { color: #F1F5F9; }
            .swagger-ui .opblock-summary-path__deprecated { color: #94A3B8; }
            .swagger-ui .opblock-body pre.microlight { background: #111827 !important; border-radius: 8px; padding: 12px; }
            .swagger-ui .responses-inner h4, .swagger-ui .responses-inner h5 { color: #F1F5F9; }
            .swagger-ui .response-col_status { color: #F1F5F9; }
            .swagger-ui table thead tr th, .swagger-ui table tbody tr td { border-bottom: 1px solid #334155; }
            .swagger-ui .markdown p, .swagger-ui .markdown pre, .swagger-ui .markdown ul, .swagger-ui .markdown ol { color: #CBD5E1; }
            .swagger-ui .authorization__btn { padding-right: 0; }
            .swagger-ui .authorization__btn svg { fill: #94A3B8; }
            .swagger-ui .json-schema-2020-12-accordion, .swagger-ui .json-schema-2020-12-expand-deep-button { background-color: transparent; color: #F1F5F9; }
            .swagger-ui .json-schema-2020-12__title { color: #F1F5F9; }
            .swagger-ui .json-schema-2020-12-accordion__icon svg { fill: #CBD5E1; }
        </style>";
    }
}
