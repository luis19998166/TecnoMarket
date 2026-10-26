using System.Reflection;
using System.Text;
using BlazorPub.Components;
using BlazorPub.Data;
using BlazorPub.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// Base de datos: SQLite en local; Azure SQL si la cadena de conexion apunta a un servidor SQL
var cadena = builder.Configuration.GetConnectionString("Tienda") ?? "Data Source=tienda.db";
var esSqlServer = cadena.Contains("Server=", StringComparison.OrdinalIgnoreCase);
builder.Services.AddDbContextFactory<TiendaDb>(o =>
{
    if (esSqlServer) o.UseSqlServer(cadena);
    else o.UseSqlite(cadena);
});
builder.Services.AddScoped<TiendaService>();
builder.Services.AddScoped<CarritoService>();

// Health checks: el proxy (o Azure App Service) solo envia trafico a instancias "sanas"
builder.Services.AddHealthChecks().AddDbContextCheck<TiendaDb>();

var app = builder.Build();

// Crea la base de datos y carga el catalogo inicial la primera vez
if (!esSqlServer && Path.GetDirectoryName(cadena.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase)) is { Length: > 0 } dir)
    Directory.CreateDirectory(dir);
using (var db = app.Services.GetRequiredService<IDbContextFactory<TiendaDb>>().CreateDbContext())
{
    db.Database.EnsureCreated();
    DatosIniciales.Cargar(db);
}

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

// Endpoint de carga sintetica usado por el laboratorio (lab/lab.mjs)
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

// API de la tienda: catalogo y reportes que puede consumir la propia web u otro sistema
app.MapGet("/api/tienda/productos", async (TiendaService t, string? buscar, string? categoria) =>
    await t.ProductosAsync(buscar, categoria));
app.MapGet("/api/tienda/reportes/ventas", async (TiendaService t, int? dias) =>
    await t.ReporteAsync(Math.Clamp(dias ?? 30, 1, 365)));
app.MapGet("/api/tienda/reportes/pedidos.csv", async (TiendaService t) =>
{
    var sb = new StringBuilder("Pedido;Fecha;Cliente;Email;Estado;MetodoPago;Producto;Categoria;Cantidad;PrecioUnitario;Subtotal\n");
    foreach (var p in await t.PedidosAsync())
        foreach (var i in p.Items)
            sb.AppendLine(string.Join(';', p.Id, p.Fecha.ToString("yyyy-MM-dd HH:mm"), p.Cliente, p.Email, p.Estado, p.MetodoPago,
                i.NombreProducto, i.Categoria, i.Cantidad, i.PrecioUnitario.ToString("0.00"), i.Subtotal.ToString("0.00")));
    var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();  // BOM para que Excel lea las tildes
    return Results.File(bytes, "text/csv", "pedidos.csv");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BlazorPub.Client._Imports).Assembly);

app.Run();
