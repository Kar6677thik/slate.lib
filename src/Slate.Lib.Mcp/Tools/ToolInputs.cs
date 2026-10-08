namespace Slate.Lib.Mcp.Tools;

internal static class ToolInputs
{
    public static string Text(string? value, string name, int maximum, bool allowEmpty = false)
    {
        value ??= "";
        if (!allowEmpty && string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.");
        if (value.Length > maximum) throw new ArgumentException($"{name} exceeds {maximum} characters.");
        return value.Trim();
    }

    public static int Range(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum) throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
        return value;
    }
}
