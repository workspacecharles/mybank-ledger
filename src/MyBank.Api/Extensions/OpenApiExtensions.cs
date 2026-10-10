using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace MyBank.Api.Extensions;

internal static class OpenApiExtensions
{
    private const string BearerScheme = "Bearer";

    /// <summary>Contrato OpenAPI com o esquema Bearer, para as interfaces pedirem o token nas rotas protegidas.</summary>
    public static void AddMyBankOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Token JWT. Localmente, gere um em POST /dev/token.",
                };
                return Task.CompletedTask;
            });

            // Cadeado só nas rotas que exigem autorização; /dev/token e /health continuam públicas.
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(BearerScheme, context.Document)] = [],
                    });
                }

                return Task.CompletedTask;
            });
        });
    }

    /// <summary>Publica o contrato e duas interfaces para explorá-lo: Scalar (/scalar) e Swagger UI (/swagger).</summary>
    public static void MapMyBankApiDocs(this WebApplication app)
    {
        app.MapOpenApi();
        app.MapScalarApiReference(scalar => scalar.WithTitle("MyBank API").AddPreferredSecuritySchemes(BearerScheme));
        app.UseSwaggerUI(swagger =>
        {
            swagger.SwaggerEndpoint("/openapi/v1.json", "MyBank API v1");
            swagger.DocumentTitle = "MyBank API";
        });
    }
}
