using System.ComponentModel.DataAnnotations;

namespace BlazorPub.Data;

public class Producto
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string Nombre { get; set; } = "";
    [StringLength(400)] public string Descripcion { get; set; } = "";
    [Required, StringLength(40)] public string Categoria { get; set; } = "";
    [Range(0.01, 1_000_000)] public decimal Precio { get; set; }
    [Range(0, 100_000)] public int Stock { get; set; }
    [StringLength(8)] public string Icono { get; set; } = "📦";
    public bool Destacado { get; set; }
}

public enum EstadoPedido { Pendiente, Pagado, Enviado, Entregado, Cancelado }
public enum MetodoPago { QR, Transferencia, ContraEntrega }

public class Pedido
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    [Required, StringLength(80)] public string Cliente { get; set; } = "";
    [Required, EmailAddress, StringLength(120)] public string Email { get; set; } = "";
    [Required, StringLength(20)] public string Telefono { get; set; } = "";
    [Required, StringLength(200)] public string Direccion { get; set; } = "";
    public MetodoPago MetodoPago { get; set; } = MetodoPago.QR;
    public EstadoPedido Estado { get; set; } = EstadoPedido.Pendiente;
    public decimal Total { get; set; }
    public List<PedidoItem> Items { get; set; } = new();
}

public class PedidoItem
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int ProductoId { get; set; }
    public string NombreProducto { get; set; } = "";
    public string Categoria { get; set; } = "";
    public decimal PrecioUnitario { get; set; }
    public int Cantidad { get; set; }
    public decimal Subtotal => PrecioUnitario * Cantidad;
}

/// <summary>Datos del formulario de compra (validados con DataAnnotations).</summary>
public class DatosCompra
{
    [Required(ErrorMessage = "Ingresa tu nombre"), StringLength(80)] public string Cliente { get; set; } = "";
    [Required(ErrorMessage = "Ingresa tu correo"), EmailAddress(ErrorMessage = "Correo no válido")] public string Email { get; set; } = "";
    [Required(ErrorMessage = "Ingresa tu teléfono"), RegularExpression(@"^[0-9 +]{7,15}$", ErrorMessage = "Teléfono no válido")] public string Telefono { get; set; } = "";
    [Required(ErrorMessage = "Ingresa la dirección de entrega"), StringLength(200)] public string Direccion { get; set; } = "";
    public MetodoPago MetodoPago { get; set; } = MetodoPago.QR;
}
