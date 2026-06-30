using System.Text.Json;

namespace WinButler.Services.Definitions;

/// <summary>Shared JSON options so bundled and remote definitions parse identically.</summary>
internal static class DefinitionsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };
}
