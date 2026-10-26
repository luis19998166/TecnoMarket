using Microsoft.EntityFrameworkCore;

namespace BlazorPub.Data;

public class TiendaDb(DbContextOptions<TiendaDb> options) : DbContext(options)
{
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<PedidoItem> PedidoItems => Set<PedidoItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Producto>().Property(p => p.Precio).HasPrecision(12, 2);
        b.Entity<Pedido>().Property(p => p.Total).HasPrecision(12, 2);
        b.Entity<PedidoItem>().Property(p => p.PrecioUnitario).HasPrecision(12, 2);
        b.Entity<Pedido>().Property(p => p.Estado).HasConversion<string>().HasMaxLength(20);
        b.Entity<Pedido>().Property(p => p.MetodoPago).HasConversion<string>().HasMaxLength(20);
        b.Entity<Pedido>().HasMany(p => p.Items).WithOne().HasForeignKey(i => i.PedidoId);
    }
}
