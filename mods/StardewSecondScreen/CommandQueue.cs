using System;
using System.Collections.Concurrent;
using System.Text.Json;

namespace StardewSecondScreen;

internal sealed class CommandQueue
{
	private const int Limit = 64;

	private readonly ConcurrentQueue<Command> _pending = new ConcurrentQueue<Command>();

	public void Accept(string raw)
	{
		if (_pending.Count >= 64)
		{
			return;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(raw);
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind == JsonValueKind.Object)
			{
				string text = Field(rootElement, "type");
				if (!string.IsNullOrEmpty(text))
				{
					_pending.Enqueue(new Command(text, Number(rootElement, "slot") ?? Number(rootElement, "from") ?? Number(rootElement, "value") ?? (-1), Number(rootElement, "to") ?? (-1), Field(rootElement, "key") ?? ""));
				}
			}
		}
		catch (JsonException)
		{
		}
	}

	public void Drain(Action<Command> apply)
	{
		Command result;
		while (_pending.TryDequeue(out result))
		{
			try
			{
				apply(result);
			}
			catch
			{
			}
		}
	}

	private static string? Field(JsonElement root, string key)
	{
		if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}

	private static int? Number(JsonElement root, string key)
	{
		if (!root.TryGetProperty(key, out var value))
		{
			return null;
		}
		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var value2))
		{
			return value2;
		}
		if (value.ValueKind != JsonValueKind.String || !int.TryParse(value.GetString(), out value2))
		{
			return null;
		}
		return value2;
	}
}
