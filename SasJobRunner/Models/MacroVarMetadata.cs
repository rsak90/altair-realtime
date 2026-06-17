using System.Text.Json.Serialization;

namespace SasJobRunner.Models;

/// <summary>
/// Metadata for macro variable persistence files.
/// Provides context about who owns the session and when it was last updated.
/// </summary>
internal sealed class MacroVarMetadata
{
    /// <summary>
    /// The user ID who owns this session.
    /// Used to reconstruct the session folder path from sessionId alone.
    /// </summary>
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the last update to the macro variables.
    /// Recorded in UTC for consistency across time zones.
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; }
}
