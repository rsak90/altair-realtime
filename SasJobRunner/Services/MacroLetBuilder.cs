using System.Text.RegularExpressions;

namespace SasJobRunner.Services;

public static class MacroLetBuilder
{
    private static readonly Regex ValidNamePattern =
        new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Builds a SAS %let statement: <c>%let {name} = {value};</c>
    /// </summary>
    /// <param name="name">The SAS macro variable name. Must match [A-Za-z_][A-Za-z0-9_]*.</param>
    /// <param name="value">The value to assign.</param>
    /// <returns>A SAS %let statement string.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is not a valid SAS identifier.</exception>
    public static string Build(string name, string value)
    {
        if (!ValidNamePattern.IsMatch(name))
            throw new ArgumentException(
                $"'{name}' is not a valid SAS macro variable name. " +
                "Names must start with a letter or underscore and contain only letters, digits, or underscores.",
                nameof(name));

        return $"%let {name} = {value};";
    }
}
