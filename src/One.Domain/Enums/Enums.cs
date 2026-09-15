namespace One.Domain.Enums;

/// <summary>Estado del ciclo de vida de una empresa (tenant).</summary>
public enum TenantStatus
{
    Active = 1,
    Trial = 2,
    Suspended = 3,
    Archived = 4
}

/// <summary>Rol de un usuario dentro de una empresa concreta.</summary>
public enum TenantRole
{
    Owner = 1,
    Admin = 2,
    Member = 3,
    Viewer = 4
}

/// <summary>Entorno lógico al que pertenece una credencial o una variable.</summary>
public enum AppEnvironment
{
    Development = 1,
    Staging = 2,
    Production = 3
}

/// <summary>Tipo de dato de una variable de configuración.</summary>
public enum SettingDataType
{
    String = 1,
    Number = 2,
    Boolean = 3,
    Json = 4,
    Url = 5,
    Email = 6,
    Secret = 7
}

/// <summary>Estado de una credencial de integración.</summary>
public enum CredentialStatus
{
    Active = 1,
    Revoked = 2,
    Expired = 3
}

/// <summary>Estado de la suscripción de una empresa a una app.</summary>
public enum SubscriptionStatus
{
    Active = 1,
    Paused = 2,
    Expired = 3,
    Cancelled = 4
}
