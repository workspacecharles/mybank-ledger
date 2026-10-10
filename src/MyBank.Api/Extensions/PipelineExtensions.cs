using Microsoft.EntityFrameworkCore;
using MyBank.Core;

namespace MyBank.Api.Extensions;

internal static class PipelineExtensions
{
    /// <summary>Middlewares na ordem certa: erros, HTTPS, autenticação e autorização.</summary>
    public static void UseMyBankPipeline(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            // Localmente o schema é criado/atualizado na subida. Em produção, migrations rodam no pipeline de deploy.
            using var scope = app.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<MyBankDbContext>().Database.Migrate();
            app.MapOpenApi();
        }
        else
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();
    }
}
