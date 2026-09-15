namespace One.Application.Common;

/// <summary>Página de resultados con los metadatos que necesita el portal para paginar.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}

/// <summary>Parámetros de consulta comunes a todos los listados.</summary>
public sealed class PageRequest
{
    private const int MaxPageSize = 200;

    private int _page = 1;
    private int _pageSize = 20;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => 20,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    /// <summary>Texto libre de búsqueda.</summary>
    public string? Search { get; set; }

    public string? SortBy { get; set; }

    public bool Descending { get; set; }

    public int Skip => (Page - 1) * PageSize;
}
