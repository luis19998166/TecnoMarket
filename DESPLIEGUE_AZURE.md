# Desplegar TecnoMarket en Azure (blue-green con slots)

En Azure App Service, el **slot de staging** es el entorno "verde" y **producción** es el "azul".
GitHub Actions publica en staging, lo verifica (health check + prueba de humo) y hace **swap**.
Es la misma estrategia que se midió en el laboratorio de la investigación, pero en la nube.

```
GitHub (push a main) ─► Actions: build ─► deploy a STAGING ─► /healthz + prueba de humo ─► SWAP ─► PRODUCCIÓN
                                                                   └─ si falla, producción no se toca
```

## 0. Requisitos y costos

- Cuenta **Azure for Students** (US$ 100 de crédito, sin tarjeta): https://azure.microsoft.com/free/students
- Los slots y el autoescalado requieren el plan **Standard S1** o superior. Cuesta alrededor de
  US$ 70 al mes, pero se cobra por hora. **Borra el grupo de recursos después de la defensa** (paso 9).
- Alternativa gratuita: plan **F1**. Muestra la tienda, pero no tiene slots ni autoescalado (no hay blue-green).

## 1. Grupo de recursos

Portal de Azure → **Grupos de recursos** → Crear → nombre `rg-tecnomarket`, región `East US 2` (o `Brazil South`).

## 2. Base de datos Azure SQL (compartida por ambos slots)

> Usa Azure SQL y no SQLite: cada slot tiene su propio disco. Con SQLite, el swap cambiaría también la base de datos.

1. **Azure SQL** → Crear **SQL Database**. Si aparece la opción **"Apply free offer"**, actívala.
2. Servidor nuevo: autenticación **SQL**, usuario y contraseña propios (guárdalos).
3. En **Redes**: activa **"Allow Azure services and resources to access this server"**.
4. Una vez creada → **Cadenas de conexión** → copia la de **ADO.NET** y reemplaza `{your_password}`.

Las tablas y el catálogo inicial se crean solos la primera vez que arranca la app.

## 3. Web App

**App Services** → Crear **Web App**:

| Campo | Valor |
|---|---|
| Nombre | `tecnomarket-upds` (debe ser único: será `https://tecnomarket-upds.azurewebsites.net`) |
| Publicar | Código |
| Pila del runtime | **.NET 9 (STS)** |
| Sistema operativo | Linux |
| Plan | Nuevo, **Standard S1** |

## 4. Configuración obligatoria para Blazor Server

En la Web App:

1. **Configuración → Configuración general**
   - **Web sockets: Activado** (Blazor Server usa SignalR).
   - **Afinidad de sesión (ARR affinity): Activado** (cada usuario vuelve a la misma instancia, donde vive su circuito).
2. **Variables de entorno → Cadenas de conexión** → Agregar:
   nombre `Tienda`, tipo `SQLAzure`, valor = la cadena del paso 2. **No** marques "Configuración de slot".
3. **Supervisión → Comprobación de estado (Health check)** → Habilitar, ruta `/healthz`.
   Azure deja de enviar tráfico a una instancia que no responda.

## 5. Slot de staging

**Implementación → Ranuras de implementación** → Agregar ranura `staging`, y elige **clonar la configuración** de producción.
Su URL será `https://tecnomarket-upds-staging.azurewebsites.net`.

## 6. Autoescalado

**Escalar horizontalmente (plan de App Service)** → **Escalado automático personalizado**:
- Mínimo 1, máximo 3 instancias.
- Regla: **CPU > 70 %** durante 5 min → **+1 instancia**.
- Regla: **CPU < 30 %** durante 10 min → **−1 instancia**.

## 7. Conectar GitHub Actions

1. Abre **Cloud Shell** (ícono `>_` arriba en el portal, elige Bash) y ejecuta:
   ```bash
   az ad sp create-for-rbac --name gh-tecnomarket --role contributor \
     --scopes /subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-tecnomarket --json-auth
   ```
   Copia todo el JSON que imprime.
2. En GitHub → tu repositorio → **Settings → Secrets and variables → Actions**:
   - Pestaña **Secrets** → `AZURE_CREDENTIALS` = el JSON anterior.
   - Pestaña **Variables** → `AZURE_WEBAPP_NAME` = `tecnomarket-upds` y `AZURE_RESOURCE_GROUP` = `rg-tecnomarket`.
3. Haz un push a `main` (o **Actions → azure-blue-green → Run workflow**).

Mientras esas variables no existan, el workflow solo compila; no intenta desplegar.

> Si tu cuenta de estudiante no permite crear el *service principal*, puedes publicar a mano:
> en Visual Studio, clic derecho en `BlazorPub` → **Publicar** → Azure → App Service → elige el slot `staging`,
> y luego en el portal → Ranuras de implementación → **Intercambiar**.

## 8. Demostrar el blue-green en la defensa

1. Abre la tienda en producción y deja un producto en el carrito.
2. Cambia algo visible (por ejemplo, el título del hero en `Home.razor`) y haz push.
3. Mientras corre el workflow, la tienda sigue funcionando. Al terminar el swap aparece el cambio
   y `/api/version` muestra el nuevo número (`1.0.<n.º de ejecución>`).
4. **Reversión**: Ranuras de implementación → **Intercambiar** de nuevo (la versión anterior quedó en staging).

## 9. Limpieza (importante para no gastar el crédito)

Portal → `rg-tecnomarket` → **Eliminar grupo de recursos**. Se borra todo: web app, slot, plan y base de datos.
