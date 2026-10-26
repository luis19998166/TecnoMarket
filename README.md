# Experimento: publicación de un aplicativo Blazor (tradicional vs. blue-green)

Laboratorio reproducible del informe IMRD "Publicación de aplicativos web propietarios en Blazor".

## Contenido
- `BlazorPub/` aplicación Blazor Web App (.NET 9) con `/healthz`, `/api/version` y `/api/pedidos/{id}`
  - `publicaciones/v1`, `v2`, `v3bug` versiones publicadas (v3 tiene un defecto deliberado)
  - `Dockerfile` y `despliegue/` (script blue-green y Nginx) para producción
- `.github/workflows/deploy.yml` pipeline CI/CD (compila, prueba, construye la imagen y despliega)
- `lab/proxy.mjs` proxy inverso con conmutación blue-green en caliente
- `lab/lab.mjs` orquestador de los escenarios E1 (actualización), E2 (reversión) y E3 (escalado/autoescalado)
- `lab/graficos.py` genera las figuras de `graficos/` a partir de `resultados/*.json`

## Cómo reproducir (Windows, .NET 9 SDK y Node.js)
```
cd BlazorPub
dotnet publish BlazorPub/BlazorPub.csproj -c Release -o publicaciones/v1 -p:InformationalVersion=1.0.0
dotnet publish BlazorPub/BlazorPub.csproj -c Release -o publicaciones/v2 -p:InformationalVersion=2.0.0
dotnet publish BlazorPub/BlazorPub.csproj -c Release -o publicaciones/v3bug -p:InformationalVersion=3.0.0-bug "-p:DefineConstants=BUGGY"
cd ../lab
node lab.mjs todo
py graficos.py
```
