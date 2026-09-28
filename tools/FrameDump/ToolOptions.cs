using System.Globalization;

namespace FrameDump;

/// <summary>Very small command line parser: positional arguments plus --key value and --flag options.</summary>
internal sealed class ToolOptions
{
	private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<string> _positional = [];

	internal IReadOnlyList<string> Positional => _positional;

	internal static ToolOptions Parse(string[] arguments)
	{
		var options = new ToolOptions();
		for (var index = 0; index < arguments.Length; index++)
		{
			var argument = arguments[index];
			if (!argument.StartsWith("--", StringComparison.Ordinal))
			{
				options._positional.Add(argument);
				continue;
			}

			var key = argument[2..];
			if (index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
			{
				options._values[key] = arguments[index + 1];
				index++;
			}
			else options._flags.Add(key);
		}

		return options;
	}

	internal bool HasFlag(string key) => _flags.Contains(key);

	internal bool HasValue(string key) => _values.ContainsKey(key);

	internal string GetString(string key, string defaultValue) => _values.TryGetValue(key, out var value) ? value : defaultValue;

	internal int GetInt32(string key, int defaultValue) => _values.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : defaultValue;

	internal double GetDouble(string key, double defaultValue) => _values.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : defaultValue;
}
