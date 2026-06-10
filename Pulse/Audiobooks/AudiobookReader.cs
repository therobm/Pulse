using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Pulse.Audiobooks
{
	/// <summary>
	/// Reads audio file tags, embedded chapter markers, and cover art.
	/// Knows about TagLib, ID3, and MP4 box structure so the scanner
	/// doesn't have to.
	/// </summary>
	public class AudiobookReader
	{
		public class ChapterMarker
		{
			public int StartMs = 0;
			public int EndMs = 0;
			public string Title = "";
		}

		public class AudiobookTags
		{
			public string Title = "";
			public string Album = "";
			public string Author = "";
			public uint Track = 0;
			public int DurationSeconds = 0;
		}

		private class BoxLocation
		{
			public long PayloadStart = 0;
			public long End = 0;
		}

		private string m_artCacheRoot;

		public AudiobookReader(string artCacheRoot)
		{
			m_artCacheRoot = artCacheRoot;
		}

		public AudiobookTags ReadFileTags(string path)
		{
			AudiobookTags result = new AudiobookTags();
			try
			{
				TagLib.File tagFile = TagLib.File.Create(path);
				try
				{
					if (tagFile.Tag != null)
					{
						result.Title = SafeString(tagFile.Tag.Title);
						result.Album = SafeString(tagFile.Tag.Album);
						string author = SafeString(tagFile.Tag.FirstAlbumArtist);
						if (string.IsNullOrEmpty(author))
						{
							author = SafeString(tagFile.Tag.FirstPerformer);
						}
						result.Author = author;
						result.Track = tagFile.Tag.Track;
					}
					if (tagFile.Properties != null)
					{
						result.DurationSeconds = (int)tagFile.Properties.Duration.TotalSeconds;
					}
				}
				finally
				{
					tagFile.Dispose();
				}
			}
			catch (Exception ex)
			{
				Log.Warning("Audiobook tag read failed for " + path + ": " + ex.Message);
			}
			return result;
		}

		public List<ChapterMarker> ExtractChapters(string path, int fileDurationSeconds)
		{
			List<ChapterMarker> markers = new List<ChapterMarker>();
			try
			{
				markers = ExtractId3Chapters(path);
				if (markers.Count == 0)
				{
					markers = ExtractNeroChapters(path);
				}
			}
			catch (Exception ex)
			{
				Log.Warning("Audiobook chapter extract failed for " + path + ": " + ex.Message);
				markers = new List<ChapterMarker>();
			}
			if (markers.Count > 0)
			{
				NormalizeChapterEnds(markers, fileDurationSeconds * 1000);
			}
			return markers;
		}

		public string ExtractEmbeddedCover(string path, string bookId)
		{
			try
			{
				TagLib.File tagFile = TagLib.File.Create(path);
				try
				{
					if (tagFile.Tag == null || tagFile.Tag.Pictures == null || tagFile.Tag.Pictures.Length == 0)
					{
						return "";
					}
					TagLib.IPicture picture = tagFile.Tag.Pictures[0];
					if (picture.Data == null || picture.Data.Data == null || picture.Data.Data.Length == 0)
					{
						return "";
					}

					string extension = ".jpg";
					if (picture.MimeType != null && picture.MimeType.ToLowerInvariant().Contains("png"))
					{
						extension = ".png";
					}
					string dir = Path.Combine(m_artCacheRoot, bookId);
					if (!Directory.Exists(dir))
					{
						Directory.CreateDirectory(dir);
					}
					string outPath = Path.Combine(dir, "cover" + extension);
					File.WriteAllBytes(outPath, picture.Data.Data);
					return outPath;
				}
				finally
				{
					tagFile.Dispose();
				}
			}
			catch (Exception ex)
			{
				Log.Warning("Audiobook cover extract failed for " + path + ": " + ex.Message);
				return "";
			}
		}

		private List<ChapterMarker> ExtractId3Chapters(string path)
		{
			List<ChapterMarker> markers = new List<ChapterMarker>();
			TagLib.File tagFile = TagLib.File.Create(path);
			try
			{
				TagLib.Id3v2.Tag id3 = tagFile.GetTag(TagLib.TagTypes.Id3v2, false) as TagLib.Id3v2.Tag;
				if (id3 == null)
				{
					return markers;
				}
				foreach (TagLib.Id3v2.Frame frame in id3.GetFrames())
				{
					TagLib.Id3v2.ChapterFrame chapter = frame as TagLib.Id3v2.ChapterFrame;
					if (chapter == null)
					{
						continue;
					}
					ChapterMarker marker = new ChapterMarker();
					marker.StartMs = (int)chapter.StartMilliseconds;
					marker.EndMs = (int)chapter.EndMilliseconds;
					marker.Title = ReadId3ChapterTitle(chapter);
					markers.Add(marker);
				}
			}
			finally
			{
				tagFile.Dispose();
			}
			return markers;
		}

		private string ReadId3ChapterTitle(TagLib.Id3v2.ChapterFrame chapter)
		{
			foreach (TagLib.Id3v2.Frame sub in chapter.SubFrames)
			{
				TagLib.Id3v2.TextInformationFrame text = sub as TagLib.Id3v2.TextInformationFrame;
				if (text != null && text.Text != null && text.Text.Length > 0)
				{
					return text.Text[0];
				}
			}
			return "";
		}

		// Nero chpl atom (moov.udta.chpl): the most common chapter store in m4b
		// audiobooks. Layout: FullBox header (4) + reserved (variable) + count (1),
		// then per chapter a u64 start in 100ns units + u8 title length + UTF-8 title.
		private List<ChapterMarker> ExtractNeroChapters(string path)
		{
			List<ChapterMarker> markers = new List<ChapterMarker>();
			FileStream stream = File.OpenRead(path);
			try
			{
				long fileLength = stream.Length;
				BoxLocation moov = FindBox(stream, 0, fileLength, "moov");
				if (moov == null)
				{
					return markers;
				}
				BoxLocation udta = FindBox(stream, moov.PayloadStart, moov.End, "udta");
				if (udta == null)
				{
					return markers;
				}
				BoxLocation chpl = FindBox(stream, udta.PayloadStart, udta.End, "chpl");
				if (chpl == null)
				{
					return markers;
				}

				long payloadLength = chpl.End - chpl.PayloadStart;
				if (payloadLength < 6 || payloadLength > 1000000)
				{
					return markers;
				}
				byte[] payload = new byte[payloadLength];
				stream.Seek(chpl.PayloadStart, SeekOrigin.Begin);
				if (!ReadFully(stream, payload, 0, payload.Length))
				{
					return markers;
				}

				// The reserved bytes before the 1-byte chapter count vary by writer:
				// 1 byte (mp4v2/GPAC -> entries at 6), 4 bytes (ffmpeg, which made
				// most .m4b files -> entries at 9), or none (-> entries at 5). Try
				// each and keep the first that parses cleanly, preferring the layout
				// that consumes the payload most fully.
				int[] candidateStarts = new int[] { 9, 6, 5 };
				List<ChapterMarker> best = null;
				int bestLeftover = int.MaxValue;
				for (int i = 0; i < candidateStarts.Length; i++)
				{
					int leftover;
					List<ChapterMarker> parsed = ParseNeroPayload(payload, candidateStarts[i], out leftover);
					if (parsed != null && leftover < bestLeftover)
					{
						best = parsed;
						bestLeftover = leftover;
					}
				}
				if (best != null)
				{
					markers = best;
				}
			}
			finally
			{
				stream.Close();
			}
			return markers;
		}

		private List<ChapterMarker> ParseNeroPayload(byte[] payload, int entriesStart, out int leftover)
		{
			leftover = int.MaxValue;
			if (entriesStart < 1 || entriesStart > payload.Length)
			{
				return null;
			}
			int count = payload[entriesStart - 1];
			if (count <= 0)
			{
				return null;
			}

			List<ChapterMarker> markers = new List<ChapterMarker>();
			int position = entriesStart;
			for (int i = 0; i < count; i++)
			{
				if (position + 9 > payload.Length)
				{
					return null;
				}
				long start100ns = ReadUInt64BE(payload, position);
				position = position + 8;
				int titleLength = payload[position];
				position = position + 1;
				if (position + titleLength > payload.Length)
				{
					return null;
				}
				string title = Encoding.UTF8.GetString(payload, position, titleLength);
				position = position + titleLength;

				long startMs = start100ns / 10000L;
				if (startMs < 0 || startMs > 360000000L)
				{
					return null;
				}
				ChapterMarker marker = new ChapterMarker();
				marker.StartMs = (int)startMs;
				marker.EndMs = 0;
				marker.Title = title;
				markers.Add(marker);
			}

			for (int i = 1; i < markers.Count; i++)
			{
				if (markers[i].StartMs < markers[i - 1].StartMs)
				{
					return null;
				}
			}
			leftover = payload.Length - position;
			return markers;
		}

		private void NormalizeChapterEnds(List<ChapterMarker> markers, int fileDurationMs)
		{
			markers.Sort(CompareMarkerByStart);
			for (int i = 0; i < markers.Count; i++)
			{
				if (markers[i].EndMs > markers[i].StartMs)
				{
					continue;
				}
				if (i + 1 < markers.Count)
				{
					markers[i].EndMs = markers[i + 1].StartMs;
				}
				else
				{
					markers[i].EndMs = fileDurationMs;
				}
			}
		}

		private int CompareMarkerByStart(ChapterMarker left, ChapterMarker right)
		{
			return left.StartMs.CompareTo(right.StartMs);
		}

		private BoxLocation FindBox(FileStream stream, long start, long end, string type)
		{
			long position = start;
			while (position + 8 <= end)
			{
				stream.Seek(position, SeekOrigin.Begin);
				byte[] header = new byte[8];
				if (!ReadFully(stream, header, 0, 8))
				{
					break;
				}
				long size = ReadUInt32BE(header, 0);
				string boxType = Encoding.ASCII.GetString(header, 4, 4);
				long payloadStart = position + 8;
				long boxEnd;
				if (size == 1)
				{
					byte[] extended = new byte[8];
					if (!ReadFully(stream, extended, 0, 8))
					{
						break;
					}
					long bigSize = ReadUInt64BE(extended, 0);
					payloadStart = position + 16;
					boxEnd = position + bigSize;
				}
				else if (size == 0)
				{
					boxEnd = end;
				}
				else
				{
					boxEnd = position + size;
				}
				if (boxEnd <= position || boxEnd > end)
				{
					break;
				}
				if (boxType == type)
				{
					BoxLocation location = new BoxLocation();
					location.PayloadStart = payloadStart;
					location.End = boxEnd;
					return location;
				}
				position = boxEnd;
			}
			return null;
		}

		private bool ReadFully(FileStream stream, byte[] buffer, int offset, int count)
		{
			int read = 0;
			while (read < count)
			{
				int got = stream.Read(buffer, offset + read, count - read);
				if (got <= 0)
				{
					return false;
				}
				read = read + got;
			}
			return true;
		}

		private long ReadUInt32BE(byte[] data, int offset)
		{
			long value = 0;
			for (int i = 0; i < 4; i++)
			{
				value = (value << 8) | (long)data[offset + i];
			}
			return value;
		}

		private long ReadUInt64BE(byte[] data, int offset)
		{
			long value = 0;
			for (int i = 0; i < 8; i++)
			{
				value = (value << 8) | (long)data[offset + i];
			}
			return value;
		}

		private string SafeString(string value)
		{
			if (value == null)
			{
				return "";
			}
			return value;
		}
	}
}
