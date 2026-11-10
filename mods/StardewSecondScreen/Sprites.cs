using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.ItemTypeDefinitions;

namespace StardewSecondScreen;

internal sealed class Sprites
{
	private readonly HashSet<string> _sent = new HashSet<string>();

	public void Reset()
	{
		_sent.Clear();
	}

	public bool AlreadySent(string id)
	{
		return _sent.Contains(id);
	}

	public void Remember(string id)
	{
		_sent.Add(id);
	}

	public byte[]? EncodeRegion(Texture2D? sheet, Rectangle source)
	{
		try
		{
			if (sheet == null || source.Width <= 0 || source.Height <= 0)
			{
				return null;
			}
			if (source.Right > sheet.Width || source.Bottom > sheet.Height)
			{
				return null;
			}
			Color[] array = new Color[source.Width * source.Height];
			sheet.GetData(0, source, array, 0, array.Length);
			using Texture2D texture2D = new Texture2D(sheet.GraphicsDevice, source.Width, source.Height);
			texture2D.SetData(array);
			using MemoryStream memoryStream = new MemoryStream();
			texture2D.SaveAsPng(memoryStream, source.Width, source.Height);
			return memoryStream.ToArray();
		}
		catch
		{
			return null;
		}
	}

	public byte[]? EncodeId(string qualifiedId)
	{
		try
		{
			if (string.IsNullOrEmpty(qualifiedId))
			{
				return null;
			}
			ParsedItemData data = ItemRegistry.GetData(qualifiedId);
			if (data == null)
			{
				return null;
			}
			byte[] array = Cut(data);
			if (array != null)
			{
				_sent.Add(qualifiedId);
			}
			return array;
		}
		catch
		{
			return null;
		}
	}

	private byte[]? Cut(ParsedItemData data)
	{
		Texture2D texture = data.GetTexture();
		Rectangle sourceRect = data.GetSourceRect(0, (int?)null);
		if (texture == null || sourceRect.Width <= 0 || sourceRect.Height <= 0)
		{
			return null;
		}
		if (sourceRect.Right > texture.Width || sourceRect.Bottom > texture.Height)
		{
			return null;
		}
		Color[] array = new Color[sourceRect.Width * sourceRect.Height];
		texture.GetData(0, sourceRect, array, 0, array.Length);
		using Texture2D texture2D = new Texture2D(texture.GraphicsDevice, sourceRect.Width, sourceRect.Height);
		texture2D.SetData(array);
		using MemoryStream memoryStream = new MemoryStream();
		texture2D.SaveAsPng(memoryStream, sourceRect.Width, sourceRect.Height);
		return memoryStream.ToArray();
	}

	public byte[]? Encode(Item item)
	{
		try
		{
			ParsedItemData dataOrErrorItem = ItemRegistry.GetDataOrErrorItem(item.QualifiedItemId);
			Texture2D texture = dataOrErrorItem.GetTexture();
			Rectangle sourceRect = dataOrErrorItem.GetSourceRect(0, (int?)null);
			if (texture == null || sourceRect.Width <= 0 || sourceRect.Height <= 0)
			{
				return null;
			}
			Color[] array = new Color[sourceRect.Width * sourceRect.Height];
			texture.GetData(0, sourceRect, array, 0, array.Length);
			using Texture2D texture2D = new Texture2D(texture.GraphicsDevice, sourceRect.Width, sourceRect.Height);
			texture2D.SetData(array);
			using MemoryStream memoryStream = new MemoryStream();
			texture2D.SaveAsPng(memoryStream, sourceRect.Width, sourceRect.Height);
			_sent.Add(item.QualifiedItemId);
			return memoryStream.ToArray();
		}
		catch
		{
			return null;
		}
	}
}
