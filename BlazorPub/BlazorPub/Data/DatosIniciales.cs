namespace BlazorPub.Data;

/// <summary>Catálogo inicial y pedidos de demostración para que los reportes tengan datos.</summary>
public static class DatosIniciales
{
    public static void Cargar(TiendaDb db)
    {
        if (db.Productos.Any()) return;

        var productos = new List<Producto>
        {
            new() { Nombre = "Laptop Gamer Nitro 15", Categoria = "Laptops", Precio = 8999m, Stock = 6, Icono = "💻", Destacado = true, Descripcion = "Intel Core i7, 16 GB RAM, SSD 512 GB y RTX 4060. Pantalla de 15,6\" a 144 Hz." },
            new() { Nombre = "Laptop Ultradelgada Air 14", Categoria = "Laptops", Precio = 6499m, Stock = 9, Icono = "💻", Descripcion = "Ryzen 7, 16 GB RAM, SSD 1 TB, 1,2 kg y 14 horas de batería." },
            new() { Nombre = "Laptop Estudiante 15", Categoria = "Laptops", Precio = 3899m, Stock = 14, Icono = "💻", Descripcion = "Core i5, 8 GB RAM y SSD 256 GB. Ideal para clases y ofimática." },
            new() { Nombre = "Smartphone Galaxy A55", Categoria = "Celulares", Precio = 3299m, Stock = 20, Icono = "📱", Destacado = true, Descripcion = "Pantalla AMOLED de 6,6\", 128 GB, cámara de 50 MP y resistencia al agua IP67." },
            new() { Nombre = "Smartphone Redmi Note 13", Categoria = "Celulares", Precio = 1899m, Stock = 25, Icono = "📱", Descripcion = "256 GB, carga rápida de 33 W y batería de 5000 mAh." },
            new() { Nombre = "iPhone 15 128 GB", Categoria = "Celulares", Precio = 7999m, Stock = 5, Icono = "📱", Descripcion = "Chip A16 Bionic, Dynamic Island y USB-C." },
            new() { Nombre = "Audífonos Bluetooth ANC", Categoria = "Audio", Precio = 899m, Stock = 30, Icono = "🎧", Destacado = true, Descripcion = "Cancelación activa de ruido y 30 horas de autonomía." },
            new() { Nombre = "Parlante Portátil 20 W", Categoria = "Audio", Precio = 549m, Stock = 18, Icono = "🔊", Descripcion = "Resistente al agua, con 12 horas de música y luces LED." },
            new() { Nombre = "Auriculares In-Ear TWS", Categoria = "Audio", Precio = 299m, Stock = 40, Icono = "🎧", Descripcion = "Estuche de carga y conexión instantánea." },
            new() { Nombre = "Consola PlayStation 5 Slim", Categoria = "Gaming", Precio = 5799m, Stock = 4, Icono = "🎮", Destacado = true, Descripcion = "Edición digital, 1 TB y control DualSense." },
            new() { Nombre = "Control Inalámbrico Pro", Categoria = "Gaming", Precio = 459m, Stock = 22, Icono = "🕹️", Descripcion = "Compatible con PC y consolas, batería recargable." },
            new() { Nombre = "Monitor Gamer 27\" 165 Hz", Categoria = "Gaming", Precio = 2199m, Stock = 8, Icono = "🖥️", Descripcion = "Panel IPS QHD con 1 ms de respuesta y FreeSync." },
            new() { Nombre = "Teclado Mecánico RGB", Categoria = "Accesorios", Precio = 389m, Stock = 35, Icono = "⌨️", Descripcion = "Switches rojos, retroiluminación RGB y formato TKL." },
            new() { Nombre = "Mouse Inalámbrico Ergonómico", Categoria = "Accesorios", Precio = 179m, Stock = 50, Icono = "🖱️", Descripcion = "Sensor de 4000 DPI, silencioso y con receptor USB." },
            new() { Nombre = "Disco SSD Externo 1 TB", Categoria = "Accesorios", Precio = 699m, Stock = 3, Icono = "💾", Descripcion = "USB-C de 1050 MB/s, compacto y resistente." },
            new() { Nombre = "Cargador GaN 65 W", Categoria = "Accesorios", Precio = 259m, Stock = 45, Icono = "🔌", Descripcion = "Tres puertos para laptop, celular y tablet al mismo tiempo." },
        };
        db.Productos.AddRange(productos);
        db.SaveChanges();

        // 60 pedidos de demostración en los últimos 30 días (semilla fija = siempre los mismos datos)
        var rnd = new Random(2026);
        var clientes = new[] { "María Rojas", "Juan Pérez", "Carla Gutiérrez", "Diego Vaca", "Lucía Suárez", "Andrés Justiniano", "Paola Méndez", "Jorge Áñez", "Valeria Paz", "Mateo Saucedo" };
        var estados = new[] { EstadoPedido.Entregado, EstadoPedido.Entregado, EstadoPedido.Enviado, EstadoPedido.Pagado, EstadoPedido.Pendiente, EstadoPedido.Cancelado };
        for (int i = 0; i < 60; i++)
        {
            var c = clientes[rnd.Next(clientes.Length)];
            var p = new Pedido
            {
                Fecha = DateTime.UtcNow.Date.AddDays(-rnd.Next(0, 30)).AddHours(rnd.Next(9, 22)).AddMinutes(rnd.Next(60)),
                Cliente = c, Email = c.Split(' ')[0].ToLowerInvariant() + "@correo.bo", Telefono = "7" + rnd.Next(1000000, 9999999),
                Direccion = "Santa Cruz de la Sierra", MetodoPago = (MetodoPago)rnd.Next(3), Estado = estados[rnd.Next(estados.Length)]
            };
            foreach (var prod in productos.OrderBy(_ => rnd.Next()).Take(rnd.Next(1, 4)))
                p.Items.Add(new PedidoItem { ProductoId = prod.Id, NombreProducto = prod.Nombre, Categoria = prod.Categoria, PrecioUnitario = prod.Precio, Cantidad = rnd.Next(1, 3) });
            p.Total = p.Items.Sum(x => x.Subtotal);
            db.Pedidos.Add(p);
        }
        db.SaveChanges();
    }
}
