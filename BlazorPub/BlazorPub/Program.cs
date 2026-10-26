using System.Reflection;
using BlazorPub.Client.Pages;
using BlazorPub.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Health checks: el proxy solo envia trafico a instancias "sanas"
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAntiforgery();

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0";
var instancia = Environment.GetEnvironmentVariable("INSTANCIA") ?? Environment.MachineName;

app.MapHealthChecks("/healthz");
app.MapGet("/api/version", () => new { version, instancia });

// Endpoint de negocio simulado (calculo de un pedido)
app.MapGet("/api/pedidos/{id:int}", (int id, int? iter) =>
{
#if BUGGY
    throw new InvalidOperationException("Defecto introducido en la version con error");
#else
    double total = 0;
    int n = Math.Clamp(iter ?? 2000, 1, 2_000_000);
    for (int i = 1; i <= n; i++) total += Math.Sqrt(i * id) % 7;
    return Results.Ok(new { id, total = Math.Round(total, 2), version, instancia });
#endif
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BlazorPub.Client._Imports).Assembly);

app.Run();
