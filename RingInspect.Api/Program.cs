using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RingInspect.Api;
using RingInspect.Api.Detection;

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) &&
    !args.Any(a => a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase)))
{
    builder.WebHost.UseUrls("http://127.0.0.1:5080");
}

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 128L * 1024 * 1024);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();

app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (ex is System.Text.Json.JsonException or BadHttpRequestException)
    {
        // 请求体不是合法 JSON / 绑定失败：对外返回 400，不回显内部细节
        Console.Error.WriteLine("[请求格式错误] " + ex.GetType().Name + ": " + ex.Message);
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.Clear();
            ctx.Response.StatusCode = 400;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            await ctx.Response.WriteAsJsonAsync(new ApiError
            {
                ErrorCode = "INVALID_JSON",
                Error = "请求体不是合法的 JSON，或字段类型不匹配",
            });
        }
    }
    catch (Exception ex)
    {
        // 内部异常只写日志，对外返回通用错误，避免泄露路径等本机信息
        Console.Error.WriteLine("[服务内部错误] " + ex);
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.Clear();
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            await ctx.Response.WriteAsJsonAsync(new ApiError
            {
                ErrorCode = "INTERNAL_ERROR",
                Error = "服务内部错误，请联系管理员并提供请求时间",
            });
        }
    }
});

Console.WriteLine("RingInspect API 启动中…");
ApiEndpoints.Map(app);
app.Run();
