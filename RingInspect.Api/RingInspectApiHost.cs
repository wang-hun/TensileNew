using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RingInspect.Api;

public static class RingInspectApiHost
{
    public static WebApplication Create(string[] args, string? urls = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        if (!string.IsNullOrWhiteSpace(urls))
        {
            builder.WebHost.UseUrls(urls);
        }
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) &&
                 !args.Any(argument => argument.StartsWith("--urls", StringComparison.OrdinalIgnoreCase)))
        {
            builder.WebHost.UseUrls("http://127.0.0.1:5080");
        }

        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 128L * 1024 * 1024);
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        });
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        WebApplication app = builder.Build();
        app.UseCors();
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception exception)
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsJsonAsync(new RingInspect.Api.Detection.ApiError
                    {
                        ErrorCode = "INTERNAL_ERROR",
                        Error = exception.GetType().Name + ": " + exception.Message,
                    });
                }
            }
        });

        ApiEndpoints.Map(app);
        return app;
    }
}