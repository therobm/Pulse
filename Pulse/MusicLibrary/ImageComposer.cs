
using System.Collections.Generic;
using System.IO;

namespace Pulse.MusicLibrary
{
	public static class ImageComposer
	{
		public static byte[] ComposeTiledImage(List<byte[]> tiles, int size)
		{
			int tileCount = tiles.Count;
			using (System.Drawing.Bitmap canvas = new System.Drawing.Bitmap(size, size))
			{
				using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(canvas))
				{
					graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
					graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
					graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
					graphics.Clear(System.Drawing.Color.Black);

					if (tileCount == 1)
					{
						DrawTile(graphics, tiles[0], 0, 0, size, size);
					}
					else if (tileCount == 2)
					{
						int half = size / 2;
						DrawTile(graphics, tiles[0], 0, 0, half, size);
						DrawTile(graphics, tiles[1], half, 0, size - half, size);
					}
					else
					{
						int half = size / 2;
						DrawTile(graphics, tiles[0], 0, 0, half, half);
						DrawTile(graphics, tiles[1], half, 0, size - half, half);
						DrawTile(graphics, tiles[2], 0, half, half, size - half);
						if (tileCount >= 4)
						{
							DrawTile(graphics, tiles[3], half, half, size - half, size - half);
						}
					}
				}

				using (MemoryStream output = new MemoryStream())
				{
					System.Drawing.Imaging.ImageCodecInfo jpegCodec = GetJpegCodec();
					System.Drawing.Imaging.EncoderParameters encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
					encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
					canvas.Save(output, jpegCodec, encoderParams);
					return output.ToArray();
				}
			}
		}

		public static System.Drawing.Imaging.ImageCodecInfo GetJpegCodec()
		{
			System.Drawing.Imaging.ImageCodecInfo[] codecs = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders();
			for (int idx = 0; idx < codecs.Length; idx++)
			{
				if (codecs[idx].FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid)
				{
					return codecs[idx];
				}
			}
			return null;
		}

		public static void DrawTile(System.Drawing.Graphics graphics, byte[] imageBytes, int x, int y, int width, int height)
		{
			using (MemoryStream source = new MemoryStream(imageBytes))
			{
				using (System.Drawing.Image tile = System.Drawing.Image.FromStream(source))
				{
					graphics.DrawImage(tile, new System.Drawing.Rectangle(x, y, width, height));
				}
			}
		}
	}
}