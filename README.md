# TecnoMarket: tienda Blazor + laboratorio de publicación blue-green

Proyecto del informe IMRD "Publicación de aplicativos web propietarios en Blazor" (Programación Web II, UPDS).

## La aplicación: TecnoMarket

Tienda en línea de tecnología hecha con **Blazor Web App (.NET 9, Interactive Server)** y **EF Core**.

| Ruta | Qué hace |
|---|---|
| `/` | Catálogo con búsqueda y filtro por categoría |
| `/producto/{id}` | Detalle del producto y selector de cantidad |
| `/carrito` | Carrito (se guarda cifrado en el navegador y sobrevive a recargas) |
| `/checkout` | Datos de entrega validados, método de pago y confirmación (valida stock en una transacción) |
| `/admin` | Reportes: ventas, ticket promedio, ventas por día y por categoría, productos más vendidos |
| `/admin/pedidos` | Gestión de pedidos por estado (al cancelar se devuelve el stock) |
| `/admin/productos` | Alta y edición de productos, precio y stock |
| `/api/tienda/productos` | API JSON del catálogo |
| `/api/tienda/reportes/ventas?dias=30` | Reporte de ventas en JSON |
| `/api/tienda/reportes/pedidos.csv` | Exportación de pedidos para Excel |
| `/healthz`, `/api/version` | Health check (incluye la base de datos) y versión desplegada |

Base de datos: **SQLite** en local (`tienda.db`, se crea sola con 16 productos y 60 pedidos de demostración)
y **Azure SQL** en la nube (basta con cambiar la cadena de conexión `Tienda`).

> El panel `/admin` no tiene inicio de sesión porque es un proyecto académico. En un negocio real debe protegerse.

### Ejecutar en local
```
cd BlazorPub/BlazorPub
dotnet run
```
Abrir http://localhost:5083

### Demo blue-green en local (sin Azure)
```
cd lab
node demo.mjs
```
Publica la tienda como v1 (blue) y v2 (green), las levanta detrás del proxy y abre http://localhost:5200.
Teclas: `g` tráfico a green, `b` volver a blue, `s` estado, `q` salir. El pie de página muestra la versión y el servidor.
Después de cambiar el código, usa `node demo.mjs --rebuild`.

### Desplegar en Azure
Ver **[DESPLIEGUE_AZURE.md](DESPLIEGUE_AZURE.md)**: App Service con slot de staging, health check, autoescalado
y el workflow `.github/workflows/azure-blue-green.yml` (staging → prueba de humo → swap).

## El laboratorio de la investigación

- `lab/proxy.mjs` proxy inverso con conmutación blue-green en caliente
- `lab/lab.mjs` orquestador de los escenarios E1 (actualización), E2 (reversión) y E3 (escalado/autoescalado)
- `lab/graficos.py` genera las figuras de `graficos/` a partir de `resultados/*.json`
- `BlazorPub/Dockerfile` y `BlazorPub/despliegue/` alternativa con Docker + Nginx en un servidor Linux propio

Las mediciones del informe se hicieron sobre los endpoints `/healthz` y `/api/pedidos/{id}`, que se conservan intactos.

### Reproducir las mediciones (Windows, .NET 9 SDK y Node.js)
```
cd BlazorPub
dotnet publish BlazorPub/BlazorPub.csproj -c Release -o publicaciones/v1 -p:InformationalVersion=1.0.0
dotnet publish BlazorPub/BlazorPub.csproj -c Release -o publicaciones/v2 -p:InformationalVersion=2.0.0
dotnet publish BlazorPub/BlazorPub.csproj -c Release -o publicaciones/v3bug -p:InformationalVersion=3.0.0-bug "-p:DefineConstants=BUGGY"
cd ../lab
node lab.mjs todo
py graficos.py
```
