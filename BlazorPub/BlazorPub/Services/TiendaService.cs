using System.Globalization;
using BlazorPub.Data;
using Microsoft.EntityFrameworkCore;

namespace BlazorPub.Services;

public record VentaDia(DateTime Dia, decimal Monto, int Pedidos);
public record VentaGrupo(string Nombre, decimal Monto, int Unidades);
public record ResumenVentas(decimal Total, int Pedidos, decimal TicketPromedio, int Unidades, int PedidosPendientes,
    List<VentaDia> PorDia, List<VentaGrupo> PorCategoria, List<VentaGrupo> TopProductos, List<VentaGrupo> PorMetodoPago);

public class TiendaService(IDbContextFactory<TiendaDb> factory)
{
    public static readonly CultureInfo Cultura = new("es-BO");
    public static string Bs(decimal v) => "Bs " + v.ToString("N2", Cultura);
    /// <summary>Las fechas se guardan en UTC; Bolivia es UTC-4 todo el año.</summary>
    public static DateTime HoraBolivia(DateTime utc) => utc.AddHours(-4);

    public async Task<List<Producto>> ProductosAsync(string? buscar = null, string? categoria = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var q = db.Productos.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(categoria)) q = q.Where(p => p.Categoria == categoria);
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var t = buscar.Trim().ToLower();
            q = q.Where(p => p.Nombre.ToLower().Contains(t) || p.Descripcion.ToLower().Contains(t));
        }
        return await q.OrderByDescending(p => p.Destacado).ThenBy(p => p.Nombre).ToListAsync();
    }

    public async Task<List<string>> CategoriasAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Productos.Select(p => p.Categoria).Distinct().OrderBy(c => c).ToListAsync();
    }

    public async Task<Producto?> ProductoAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Productos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task GuardarProductoAsync(Producto p)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (p.Id == 0) db.Productos.Add(p); else db.Productos.Update(p);
        await db.SaveChangesAsync();
    }

    /// <summary>Registra el pedido validando stock dentro de una transacción.</summary>
    public async Task<(Pedido? pedido, string? error)> CrearPedidoAsync(DatosCompra datos, IReadOnlyList<ItemCarrito> items)
    {
        if (items.Count == 0) return (null, "El carrito está vacío.");
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var ids = items.Select(i => i.ProductoId).ToList();
        var productos = await db.Productos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        var pedido = new Pedido
        {
            Cliente = datos.Cliente.Trim(), Email = datos.Email.Trim(), Telefono = datos.Telefono.Trim(),
            Direccion = datos.Direccion.Trim(), MetodoPago = datos.MetodoPago
        };
        foreach (var it in items)
        {
            if (!productos.TryGetValue(it.ProductoId, out var p)) return (null, $"El producto «{it.Nombre}» ya no existe.");
            if (p.Stock < it.Cantidad) return (null, $"Solo quedan {p.Stock} unidades de «{p.Nombre}».");
            p.Stock -= it.Cantidad;
            // El precio se toma de la base de datos, nunca del navegador
            pedido.Items.Add(new PedidoItem { ProductoId = p.Id, NombreProducto = p.Nombre, Categoria = p.Categoria, PrecioUnitario = p.Precio, Cantidad = it.Cantidad });
        }
        pedido.Total = pedido.Items.Sum(i => i.Subtotal);
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (pedido, null);
    }

    public async Task<Pedido?> PedidoAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Pedidos.AsNoTracking().Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<List<Pedido>> PedidosAsync(EstadoPedido? estado = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var q = db.Pedidos.AsNoTracking().Include(p => p.Items).AsQueryable();
        if (estado is not null) q = q.Where(p => p.Estado == estado);
        return await q.OrderByDescending(p => p.Fecha).ToListAsync();
    }

    public async Task CambiarEstadoAsync(int id, EstadoPedido estado)
    {
        await using var db = await factory.CreateDbContextAsync();
        var p = await db.Pedidos.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (p is null || p.Estado == estado) return;
        // Al cancelar se devuelve el stock; al reactivar se descuenta de nuevo
        if (estado == EstadoPedido.Cancelado || p.Estado == EstadoPedido.Cancelado)
        {
            int signo = estado == EstadoPedido.Cancelado ? 1 : -1;
            var ids = p.Items.Select(i => i.ProductoId).ToList();
            var productos = await db.Productos.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            foreach (var it in p.Items)
                if (productos.TryGetValue(it.ProductoId, out var prod)) prod.Stock = Math.Max(0, prod.Stock + signo * it.Cantidad);
        }
        p.Estado = estado;
        await db.SaveChangesAsync();
    }

    /// <summary>Reporte de ventas (excluye pedidos cancelados). Se agrega en memoria porque SQLite no suma decimal.</summary>
    public async Task<ResumenVentas> ReporteAsync(int dias = 30)
    {
        await using var db = await factory.CreateDbContextAsync();
        var desde = HoraBolivia(DateTime.UtcNow).Date.AddDays(-(dias - 1));   // dia local
        var desdeUtc = desde.AddHours(4);
        var pedidos = (await db.Pedidos.AsNoTracking().Include(p => p.Items)
            .Where(p => p.Fecha >= desdeUtc).ToListAsync()).Where(p => p.Estado != EstadoPedido.Cancelado).ToList();
        var items = pedidos.SelectMany(p => p.Items).ToList();

        var porDia = Enumerable.Range(0, dias).Select(d => desde.AddDays(d)).Select(d =>
        {
            var del = pedidos.Where(p => HoraBolivia(p.Fecha).Date == d).ToList();
            return new VentaDia(d, del.Sum(p => p.Total), del.Count);
        }).ToList();
        var porCat = items.GroupBy(i => i.Categoria).Select(g => new VentaGrupo(g.Key, g.Sum(i => i.Subtotal), g.Sum(i => i.Cantidad)))
            .OrderByDescending(g => g.Monto).ToList();
        var top = items.GroupBy(i => i.NombreProducto).Select(g => new VentaGrupo(g.Key, g.Sum(i => i.Subtotal), g.Sum(i => i.Cantidad)))
            .OrderByDescending(g => g.Unidades).ThenByDescending(g => g.Monto).Take(5).ToList();
        var porPago = pedidos.GroupBy(p => p.MetodoPago).Select(g => new VentaGrupo(g.Key.ToString(), g.Sum(p => p.Total), g.Count()))
            .OrderByDescending(g => g.Monto).ToList();
        decimal total = pedidos.Sum(p => p.Total);
        return new ResumenVentas(total, pedidos.Count, pedidos.Count == 0 ? 0 : total / pedidos.Count, items.Sum(i => i.Cantidad),
            pedidos.Count(p => p.Estado == EstadoPedido.Pendiente), porDia, porCat, top, porPago);
    }
}
