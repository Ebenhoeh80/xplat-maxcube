namespace MaxCube.Application.Models;

/// <summary>Strukturierte Warnung: der Code wird in der UI in sechs Sprachen gerendert.</summary>
public sealed record ServiceWarning(string Code, string? Parameter = null);
