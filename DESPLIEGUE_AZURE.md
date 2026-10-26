# Desplegar TecnoMarket en Azure (blue-green con slots)

En Azure App Service, el **slot de staging** es el entorno "verde" y **producción** es el "azul".
GitHub Actions publica en staging. Azure lo calienta, exige que `/healthz` responda 200 y recién entonces
hace el **swap** (auto swap). Es la misma estrategia que se midió en el laboratorio de la investigación.

```
push a main ─► Actions: build ─► deploy a STAGING ─► Azure: warm-up + /healthz ─► SWAP ─► PRODUCCIÓN ─► Actions verifica la versión
                                                         └─ si /healthz falla, se cancela el swap y producción no se toca
```

> Se usa **Windows** porque el *auto swap* de Azure no está disponible en Linux, y un **perfil de publicación**
> porque las cuentas universitarias suelen no permitir crear un *service principal*.

## 0. Activar la suscripción (Azure for Students)
Portal → tarjeta **Microsoft Azure for Students** → **Inicio** → verifica con el correo de la universidad.
Te da US$ 100 de crédito sin tarjeta. Si al crear recursos una región da error de "política", elige otra
(East US 2, Central US o Brazil South suelen estar permitidas).

**Costos:** el plan **Standard S1** (necesario para slots y autoescalado) cuesta alrededor de US$ 70 al mes,
cobrado por hora. **Borra el grupo de recursos después de la defensa** (paso 9).

## 1. Grupo de recursos
Buscar **Grupos de recursos** → **Crear** → nombre `rg-tecnomarket` → región → Revisar y crear.

## 2. Base de datos Azure SQL (la comparten ambos slots)
1. Buscar **Bases de datos SQL** → **Crear**. Si aparece **"Aplicar oferta gratuita"**, actívala.
2. Grupo `rg-tecnomarket`, nombre de base `tecnomarketdb`, servidor **Crear nuevo**:
   nombre único (p. ej. `sql-tecnomarket-upds`), **Autenticación de SQL**, usuario `adminsql` y una contraseña segura (anótala).
3. Pestaña **Redes**: conectividad **Punto de conexión público** y
   **"Permitir que los servicios y recursos de Azure accedan a este servidor" = Sí**.
4. Revisar y crear. Al terminar: base de datos → **Cadenas de conexión** → copia la de **ADO.NET**
   y reemplaza `{your_password}` por tu contraseña.

> No uses SQLite en Azure con slots: cada slot tiene su propio disco y el swap también cambiaría la base de datos.

## 3. Web App
Buscar **App Services** → **Crear** → **Aplicación web**:

| Campo | Valor |
|---|---|
| Grupo de recursos | `rg-tecnomarket` |
| Nombre | `tecnomarket-upds` (único; será `https://tecnomarket-upds.azurewebsites.net`) |
| Publicar | Código |
| Pila del entorno de ejecución | **.NET 9 (STS)** |
| Sistema operativo | **Windows** |
| Plan de precios | Crear nuevo → **Standard S1** |

En la pestaña **Implementación**, deja la implementación continua **deshabilitada** (el workflow ya existe en el repositorio).
Si hay pestaña de **autenticación básica**, **habilítala** (se necesita para el perfil de publicación).

## 4. Configuración de producción
En la Web App → **Configuración** (o **Ajustes → Configuración**):

1. **Configuración general**
   - **Credenciales de publicación de autenticación básica de SCM: Activado**
   - **Web sockets: Activado** (Blazor Server usa SignalR)
   - **Afinidad de ARR: Activado** (cada usuario vuelve a la instancia donde vive su sesión)
   - **Siempre activo: Activado** → Guardar.
2. **Variables de entorno → Cadenas de conexión** → **Agregar**: nombre `Tienda`, valor = cadena del paso 2,
   tipo **SQLAzure**. **No** marques "Configuración de ranura de implementación". Aplicar.
3. **Variables de entorno → Configuración de la aplicación** → agrega estas dos (warm-up del swap):
   - `WEBSITE_SWAP_WARMUP_PING_PATH` = `/healthz`
   - `WEBSITE_SWAP_WARMUP_PING_STATUSES` = `200`
4. **Supervisión → Comprobación de estado** → Habilitar, ruta `/healthz` → Guardar.

## 5. Slot de staging con auto swap
1. **Implementación → Ranuras de implementación** → **Agregar ranura** → nombre `staging`,
   **Clonar configuración de**: `tecnomarket-upds` → Agregar.
2. Abre la ranura **staging** → **Configuración → Configuración general** →
   **Intercambio automático habilitado: Activado**, ranura de destino **production** → Guardar.
   Comprueba que también tenga Web sockets, Afinidad de ARR y autenticación básica de SCM activados.
3. En la ranura staging → **Información general** → **Descargar perfil de publicación** (se descarga un archivo `.PublishSettings`).

## 6. Autoescalado
En la Web App → **Escalar horizontalmente (plan de App Service)** → **Escalado automático personalizado**:
- Mínimo 1, máximo 3, predeterminado 1.
- Regla: **Porcentaje de CPU > 70 %** (promedio 5 min) → aumentar en 1.
- Regla: **Porcentaje de CPU < 30 %** (promedio 10 min) → disminuir en 1.

## 7. Conectar GitHub
Repositorio en GitHub → **Settings → Secrets and variables → Actions**:
- Pestaña **Secrets** → **New repository secret**: `AZURE_PUBLISH_PROFILE_STAGING` = todo el contenido del
  archivo `.PublishSettings` (ábrelo con el Bloc de notas y copia todo). **No subas ese archivo al repositorio.**
- Pestaña **Variables** → **New repository variable**: `AZURE_WEBAPP_NAME` = `tecnomarket-upds`.

Luego **Actions → azure-blue-green → Run workflow** (o haz cualquier push a `main`).
El job *deploy* termina en verde cuando producción ya responde con la versión nueva en `/api/version`.

## 8. Demostrar el blue-green en la defensa
1. Abre `https://tecnomarket-upds.azurewebsites.net` y deja productos en el carrito.
2. Cambia algo visible (por ejemplo, el título del hero en `Home.razor`) y haz push.
3. Durante el despliegue la tienda sigue respondiendo. Tras el swap aparece el cambio
   y `/api/version` muestra `1.0.<n.º de ejecución>`.
4. **Reversión:** Ranuras de implementación → **Intercambiar** (la versión anterior quedó en staging).

## 9. Limpieza (para no gastar el crédito)
Portal → `rg-tecnomarket` → **Eliminar grupo de recursos**. Se borra todo: web app, slot, plan y base de datos.
