using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StardewSecondScreen;

internal static class Json
{
	public static string Escape(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length + 8);
		foreach (char c in value)
		{
			switch (c)
			{
			case '"':
				stringBuilder.Append("\\\"");
				continue;
			case '\\':
				stringBuilder.Append("\\\\");
				continue;
			case '\n':
				stringBuilder.Append("\\n");
				continue;
			case '\r':
				stringBuilder.Append("\\r");
				continue;
			case '\t':
				stringBuilder.Append("\\t");
				continue;
			}
			if (c < ' ')
			{
				StringBuilder stringBuilder2 = stringBuilder.Append("\\u");
				int num = c;
				stringBuilder2.Append(num.ToString("x4"));
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	public static string Str(string key, string? value)
	{
		return $"\"{key}\":\"{Escape(value)}\"";
	}

	public static string Num(string key, int value)
	{
		return "\"" + key + "\":" + value.ToString(CultureInfo.InvariantCulture);
	}

	public static string Num(string key, long value)
	{
		return "\"" + key + "\":" + value.ToString(CultureInfo.InvariantCulture);
	}

	public static string Flag(string key, bool value)
	{
		return $"\"{key}\":\"{(value ? "1" : "0")}\"";
	}

	public static string Object(params string[] fields)
	{
		return "{" + string.Join(",", fields) + "}";
	}

	public static string Array(string key, IEnumerable<string> objects)
	{
		return "\"" + key + "\":[" + string.Join(",", objects) + "]";
	}

	public static string Message(string type, params string[] fields)
	{
		List<string> list = new List<string> { Str("type", type) };
		list.AddRange(fields);
		return "{" + string.Join(",", list) + "}";
	}
}
