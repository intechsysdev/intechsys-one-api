# one-api

API de la plataforma. .NET 10, minimal APIs, EF Core sobre SQL Server y Microsoft Identity
(tablas `AspNetUsers` y compañía) extendido con el modelo multiempresa.

## Proyectos

| Proyecto | Responsabilidad |
|---|---|
| `One.Domain` | Entidades, enumeraciones e identidad (`ApplicationUser`, `ApplicationRole`). |
| `One.Application` | DTOs, contratos de servicio y tipos de resultado. Sin dependencias de infraestructura. |
| `One.Infrastructure` | `OneDbContext`, configuraciones EF, criptografía, servicios de aplicación y seed. |
| `One.Api` | Endpoints, autenticación JWT, autorización, OpenAPI y arranque. |

## Comandos

```bash
dotnet run --project src/One.Api                 # arranca en http://localhost:5180
dotnet build                                     # compila la solución

# Migraciones
dotnet ef migrations add <Nombre> --project src/One.Infrastructure --startup-project src/One.Api --output-dir Persistence/Migrations
dotnet ef database update  --project src/One.Infrastructure --startup-project src/One.Api
```

Las migraciones también se aplican solas en cada arranque (`DbSeeder.RunAsync`).

## Superficie HTTP

### Portal (JWT)

| Método | Ruta | Notas |
|---|---|---|
| `POST` | `/api/v1/auth/login` · `/refresh` · `/logout` | Anónimos salvo logout. Limitados por IP. |
| `GET`/`PUT` | `/api/v1/auth/me` | Perfil del usuario autenticado. |
| `POST` | `/api/v1/auth/change-password` | Cierra el resto de sesiones. |
| `GET` | `/api/v1/tenants` | Devuelve solo las empresas visibles para quien llama. |
| `POST`/`PUT`/`DELETE` | `/api/v1/tenants/{id}` | Alta y archivado requieren `PlatformAdmin`. |
| `*` | `/api/v1/tenants/{id}/members` | Requiere rol `Owner` o `Admin` en la empresa. |
| `*` | `/api/v1/tenants/{id}/apps` | Asignación de apps del catálogo. |
| `*` | `/api/v1/tenants/{id}/apps/{taId}/settings` | Variables por entorno. `…/bulk` guarda el formulario completo. |
| `GET` | `/api/v1/tenants/{id}/apps/{taId}/settings/{sid}/reveal` | Descifra un secreto. Queda auditado. |
| `*` | `/api/v1/tenants/{id}/apps/{taId}/credentials` | Emitir, rotar, revocar y eliminar. |
| `*` | `/api/v1/apps` | Catálogo y esquema de variables. Escritura: `PlatformAdmin`. |
| `*` | `/api/v1/users` | Gestión de cuentas. Lectura: `PlatformRead`. |
| `GET` | `/api/v1/dashboard` · `/audit-logs` | Métricas y traza. `PlatformRead`. |

### Integración (api key + secreto)

| Método | Ruta | Devuelve |
|---|---|---|
| `GET` | `/api/v1/integration/config` | Empresa, app, entorno y variables resueltas. |
| `GET` | `/api/v1/integration/verify` | Validez de la credencial, sin variables. |

Cabeceras `X-Api-Key` y `X-Api-Secret`; también se acepta `Authorization: Basic`.

## Roles

- `PlatformAdmin` — control total: empresas, catálogo, usuarios y credenciales.
- `PlatformSupport` — lectura transversal, **sin** acceso a valores secretos.
- Sin rol global, el usuario solo ve las empresas en las que es miembro, y solo
  `Owner`/`Admin` pueden escribir configuración o revelar secretos.

## Configuración

`appsettings.json` trae todo lo que no es sensible:

```jsonc
{
  "ConnectionStrings": { "Default": "Server=JEFO-PC;Database=OneCentral;Trusted_Connection=True;TrustServerCertificate=True" },
  "Jwt":      { "Issuer": "one-api", "Audience": "one-front", "SigningKey": "", "AccessTokenMinutes": 60, "RefreshTokenDays": 14 },
  "Security": { "EncryptionKey": "", "ApiKeyPepper": "" },
  "Seed":     { "Enabled": true, "AdminEmail": "…", "AdminPassword": "…", "IncludeDemoData": true },
  "Cors":     { "AllowedOrigins": ["http://localhost:5173"] }
}
```

Ponga `Seed:IncludeDemoData` en `false` para arrancar sin la empresa y la app de ejemplo.

### Material criptográfico

Las tres claves van **vacías en el repositorio a propósito**. El arranque falla de inmediato
si no se suministran, que es el comportamiento buscado: nunca debe arrancar con una clave por
defecto. En desarrollo se cargan con user-secrets:

```bash
dotnet user-secrets set "Jwt:SigningKey"           "$(openssl rand -base64 48)" --project src/One.Api
dotnet user-secrets set "Security:EncryptionKey"   "$(openssl rand -base64 32)" --project src/One.Api
dotnet user-secrets set "Security:ApiKeyPepper"    "$(openssl rand -base64 48)" --project src/One.Api
```

En despliegue, por variables de entorno (`__` separa los niveles) o por el gestor de secretos
de la plataforma:

```
Jwt__SigningKey=…   Security__EncryptionKey=…   Security__ApiKeyPepper=…
```

> Las claves **no son intercambiables entre entornos**. `EncryptionKey` descifra las variables
> secretas ya guardadas y `ApiKeyPepper` valida las api keys emitidas: cambiar cualquiera de
> las dos en una base con datos deja esos datos inservibles. Si necesita rotarlas, hay que
> re-cifrar las variables y reemitir las credenciales.
