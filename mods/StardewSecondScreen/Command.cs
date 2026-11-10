namespace StardewSecondScreen;

internal readonly struct Command(string type, int a, int b, string key = "")
{
	public readonly string Type = type;

	public readonly int A = a;

	public readonly int B = b;

	public readonly string Key = key;
}
