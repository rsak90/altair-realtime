using System.Text.Json.Serialization;

namespace SasJobRunner.Models;

/// <summary>
/// Internal model for serializing macro variables to JSON with metadata.
/// This model represents the structure of the variables.json file stored in session folders.
/// </summary>
internal sealed class MacroVarFile
{
    /// <summary>
    /// Metadata about the macro variable file, including user and timestamp information.
    /// </summary>
    [JsonPropertyName("metadata")]
    public MacroVarMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Dictionary of macro variable names to their values.
    /// Both keys and values must be strings to match SAS macro variable semantics.
    /// </summary>
    [JsonPropertyName("variables")]
    public Dictionary<string, string> Variables { get; set; } = new();
}
