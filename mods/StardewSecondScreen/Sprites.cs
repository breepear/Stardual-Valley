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

	/// <summary>
	/// Stardual: one of the game's menu-tab pictures without its tab frame. Pixels that are the
	/// same in every tab of the row are the frame, so they're cleared; the rest is cropped.
	/// </summary>
	public byte[]? EncodeUnframed(Texture2D? sheet, Rectangle source, IReadOnlyList<Rectangle> row)
	{
		try
		{
			if (sheet == null || row.Count < 2) return null;
			int w = source.Width, h = source.Height;
			Color[] mine = new Color[w * h];
			sheet.GetData(0, source, mine, 0, mine.Length);
			var others = new List<Color[]>();
			foreach (Rectangle r in row)
			{
				if (r == source || r.Width != w || r.Height != h) continue;
				Color[] c = new Color[w * h];
				sheet.GetData(0, r, c, 0, c.Length);
				others.Add(c);
			}
			for (int i = 0; i < mine.Length; i++)
			{
				if (mine[i].A == 0 || others.TrueForAll(c => c[i] == mine[i])) mine[i] = Color.Transparent;
			}
			return EncodeCropped(sheet.GraphicsDevice, mine, w, h);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>A PNG of the pixels, cropped to the edges of what's visible.</summary>
	public byte[]? EncodeCropped(GraphicsDevice device, Color[] pixels, int w, int h)
	{
		try
		{
			int minX = w, minY = h, maxX = -1, maxY = -1;
			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					if (pixels[y * w + x].A <= 8) continue;
					if (x < minX) minX = x; if (y < minY) minY = y;
					if (x > maxX) maxX = x; if (y > maxY) maxY = y;
				}
			}
			if (maxX < 0) return null;
			int cw = maxX - minX + 1, ch = maxY - minY + 1;
			Color[] crop = new Color[cw * ch];
			for (int y = 0; y < ch; y++)
				for (int x = 0; x < cw; x++)
					crop[y * cw + x] = pixels[(y + minY) * w + x + minX];
			using Texture2D texture2D = new Texture2D(device, cw, ch);
			texture2D.SetData(crop);
			using MemoryStream memoryStream = new MemoryStream();
			texture2D.SaveAsPng(memoryStream, cw, ch);
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
