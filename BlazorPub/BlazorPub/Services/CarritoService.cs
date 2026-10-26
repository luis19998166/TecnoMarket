using BlazorPub.Data;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace BlazorPub.Services;

public class ItemCarrito
{
    public int ProductoId { get; set; }
    public string Nombre { get; set; } = "";
    public string Icono { get; set; } = "";
    public decimal Precio { get; set; }
    public int Cantidad { get; set; }
    public decimal Subtotal => Precio * Cantidad;
}

/// <summary>
/// Carrito por usuario (un circuito de Blazor Server). Se persiste cifrado en localStorage
/// para sobrevivir a recargas de página y a un cambio de instancia durante un despliegue.
/// </summary>
public class CarritoService(ProtectedLocalStorage storage)
{
    private const string Clave = "carrito";
    private readonly List<ItemCarrito> items = new();
    private bool cargado;

    public IReadOnlyList<ItemCarrito> Items => items;
    public int Unidades => items.Sum(i => i.Cantidad);
    public decimal Total => items.Sum(i => i.Subtotal);
    public event Action? Cambio;

    /// <summary>Solo puede llamarse después del primer render (requiere JS interop).</summary>
    public async Task CargarAsync()
    {
        if (cargado) return;
        cargado = true;
        try
        {
            var r = await storage.GetAsync<List<ItemCarrito>>(Clave);
            if (r.Success && r.Value is not null) { items.Clear(); items.AddRange(r.Value); Cambio?.Invoke(); }
        }
        catch { /* almacenamiento corrupto o de otra clave: se empieza vacío */ }
    }

    public async Task AgregarAsync(Producto p, int cantidad = 1)
    {
        var it = items.FirstOrDefault(i => i.ProductoId == p.Id);
        if (it is null) items.Add(new ItemCarrito { ProductoId = p.Id, Nombre = p.Nombre, Icono = p.Icono, Precio = p.Precio, Cantidad = Math.Min(cantidad, p.Stock) });
        else it.Cantidad = Math.Min(it.Cantidad + cantidad, p.Stock);
        await GuardarAsync();
    }

    public async Task CambiarCantidadAsync(int productoId, int cantidad)
    {
        var it = items.FirstOrDefault(i => i.ProductoId == productoId);
        if (it is null) return;
        if (cantidad <= 0) items.Remove(it); else it.Cantidad = cantidad;
        await GuardarAsync();
    }

    public async Task VaciarAsync() { items.Clear(); await GuardarAsync(); }

    private async Task GuardarAsync()
    {
        Cambio?.Invoke();
        try { await storage.SetAsync(Clave, items); } catch { /* sin conexión con el navegador */ }
    }
}
