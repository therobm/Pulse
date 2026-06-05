using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace Pulse.Series
{
	/// <summary>
	/// Channel-level metadata extracted from an RSS feed. Plain public-field
	/// data bag; strings default to "" so an absent element doesn't propagate
	/// as null.
	/// </summary>
	public class ParsedChannel
	{
		public string Title = "";
		public string Author = "";
		public string Description = "";
		public string ArtworkUrl = "";
	}

	/// <summary>
	/// One parsed RSS item. EnclosureUrl being "" means the item had no
	/// usable enclosure and was skipped by the parser before reaching the
	/// caller. PubDateRaw preserves the original wire string for diagnostics;
	/// PublishedDateIso is the normalized ISO-8601 UTC form, or "" when the
	/// raw value could not be parsed.
	/// </summary>
	public class ParsedItem
	{
		public string Guid = "";
		public string Title = "";
		public string Description = "";
		public string EnclosureUrl = "";
		public string MediaType = "";
		public long EnclosureLengthBytes = 0;
		public int DurationSeconds = 0;
		public string PublishedDateIso = "";
		public string PubDateRaw = "";
	}

	/// <summary>
	/// Result of parsing one feed. SkippedItemCount tallies items dropped
	/// because they had no usable enclosure URL or were malformed; the kept
	/// items are in Items.
	/// </summary>
	public class ParsedFeed
	{
		public ParsedChannel Channel;
		public List<ParsedItem> Items;
		public int SkippedItemCount = 0;

		public ParsedFeed()
		{
			Channel = new ParsedChannel();
			Items = new List<ParsedItem>();
		}
	}

	/// <summary>
	/// Streaming RSS 2.0 parser. Stays on a forward-only XmlReader so a
	/// 10 MB feed with thousands of items never materialises as a DOM.
	/// Deliberately scope-free of the database, HTTP, and config layers --
	/// give it a Stream of XML and it returns a ParsedFeed. Leniency:
	/// missing fields fall back to "" / 0; an item with no enclosure URL is
	/// skipped; one malformed item does not abort the rest of the feed.
	/// </summary>
	public class RssFeedParser
	{
		private const string c_itunesNamespace = "http://www.itunes.com/dtds/podcast-1.0.dtd";

		public ParsedFeed Parse(Stream feedXml)
		{
			ParsedFeed result = new ParsedFeed();
			if (feedXml == null)
			{
				return result;
			}

			XmlReaderSettings settings = new XmlReaderSettings();
			settings.IgnoreWhitespace = true;
			settings.IgnoreComments = true;
			settings.IgnoreProcessingInstructions = true;
			settings.DtdProcessing = DtdProcessing.Ignore;
			settings.CloseInput = false;

			XmlReader reader = XmlReader.Create(feedXml, settings);
			try
			{
				ParseChannel(reader, result);
			}
			catch (XmlException)
			{
				// Top-level XML corruption: return whatever was harvested
				// before the reader gave up. Callers see a (possibly empty)
				// ParsedFeed rather than an exception.
			}
			finally
			{
				reader.Close();
			}
			return result;
		}

		private void ParseChannel(XmlReader reader, ParsedFeed result)
		{
			bool foundChannel = false;
			while (reader.Read())
			{
				if (reader.NodeType == XmlNodeType.Element)
				{
					string scanLocalName = reader.LocalName;
					string scanNamespace = reader.NamespaceURI;
					if (scanLocalName == "channel" && scanNamespace == "")
					{
						foundChannel = true;
						break;
					}
				}
			}
			if (!foundChannel)
			{
				return;
			}

			int channelDepth = reader.Depth;
			while (reader.Read())
			{
				XmlNodeType nodeType = reader.NodeType;
				if (nodeType == XmlNodeType.EndElement && reader.Depth == channelDepth)
				{
					return;
				}
				if (nodeType != XmlNodeType.Element)
				{
					continue;
				}

				string localName = reader.LocalName;
				string namespaceUri = reader.NamespaceURI;

				if (localName == "item" && namespaceUri == "")
				{
					try
					{
						ParsedItem item = new ParsedItem();
						ReadItem(reader, item);
						if (string.IsNullOrEmpty(item.EnclosureUrl))
						{
							result.SkippedItemCount = result.SkippedItemCount + 1;
						}
						else
						{
							result.Items.Add(item);
						}
					}
					catch (XmlException)
					{
						result.SkippedItemCount = result.SkippedItemCount + 1;
					}
					continue;
				}

				if (localName == "title" && namespaceUri == "")
				{
					string titleText = ReadElementTextAndConsume(reader);
					if (result.Channel.Title == "")
					{
						result.Channel.Title = titleText;
					}
					continue;
				}

				if (localName == "description" && namespaceUri == "")
				{
					string descriptionText = ReadElementTextAndConsume(reader);
					if (result.Channel.Description == "")
					{
						result.Channel.Description = descriptionText;
					}
					continue;
				}

				if (localName == "author" && namespaceUri == c_itunesNamespace)
				{
					string authorText = ReadElementTextAndConsume(reader);
					if (result.Channel.Author == "")
					{
						result.Channel.Author = authorText;
					}
					continue;
				}

				if (localName == "summary" && namespaceUri == c_itunesNamespace)
				{
					string summaryText = ReadElementTextAndConsume(reader);
					if (result.Channel.Description == "")
					{
						result.Channel.Description = summaryText;
					}
					continue;
				}

				if (localName == "image" && namespaceUri == c_itunesNamespace)
				{
					string href = reader.GetAttribute("href");
					if (href == null)
					{
						href = "";
					}
					if (result.Channel.ArtworkUrl == "")
					{
						result.Channel.ArtworkUrl = href;
					}
					bool isEmptyItunesImage = reader.IsEmptyElement;
					if (!isEmptyItunesImage)
					{
						ConsumeElementSubtree(reader);
					}
					continue;
				}

				if (localName == "image" && namespaceUri == "")
				{
					string nestedUrl = ReadImageUrlFromChannelImage(reader);
					if (result.Channel.ArtworkUrl == "")
					{
						result.Channel.ArtworkUrl = nestedUrl;
					}
					continue;
				}

				bool isEmpty = reader.IsEmptyElement;
				if (!isEmpty)
				{
					ConsumeElementSubtree(reader);
				}
			}
		}

		private void ReadItem(XmlReader reader, ParsedItem item)
		{
			int itemDepth = reader.Depth;
			if (reader.IsEmptyElement)
			{
				return;
			}
			while (reader.Read())
			{
				XmlNodeType nodeType = reader.NodeType;
				if (nodeType == XmlNodeType.EndElement && reader.Depth == itemDepth)
				{
					return;
				}
				if (nodeType != XmlNodeType.Element)
				{
					continue;
				}

				string localName = reader.LocalName;
				string namespaceUri = reader.NamespaceURI;

				if (localName == "enclosure" && namespaceUri == "")
				{
					ReadEnclosure(reader, item);
					bool isEmptyEnclosure = reader.IsEmptyElement;
					if (!isEmptyEnclosure)
					{
						ConsumeElementSubtree(reader);
					}
					continue;
				}

				if (localName == "guid" && namespaceUri == "")
				{
					string guidText = ReadElementTextAndConsume(reader);
					if (item.Guid == "")
					{
						item.Guid = guidText;
					}
					continue;
				}

				if (localName == "title" && namespaceUri == "")
				{
					string titleText = ReadElementTextAndConsume(reader);
					if (item.Title == "")
					{
						item.Title = titleText;
					}
					continue;
				}

				if (localName == "description" && namespaceUri == "")
				{
					string descriptionText = ReadElementTextAndConsume(reader);
					if (item.Description == "")
					{
						item.Description = descriptionText;
					}
					continue;
				}

				if (localName == "summary" && namespaceUri == c_itunesNamespace)
				{
					string summaryText = ReadElementTextAndConsume(reader);
					if (item.Description == "")
					{
						item.Description = summaryText;
					}
					continue;
				}

				if (localName == "duration" && namespaceUri == c_itunesNamespace)
				{
					string durationText = ReadElementTextAndConsume(reader);
					item.DurationSeconds = ParseDuration(durationText);
					continue;
				}

				if (localName == "pubDate" && namespaceUri == "")
				{
					string pubDateText = ReadElementTextAndConsume(reader);
					item.PubDateRaw = pubDateText;
					item.PublishedDateIso = NormalizePubDate(pubDateText);
					continue;
				}

				bool isEmpty = reader.IsEmptyElement;
				if (!isEmpty)
				{
					ConsumeElementSubtree(reader);
				}
			}
		}

		private void ReadEnclosure(XmlReader reader, ParsedItem item)
		{
			string url = reader.GetAttribute("url");
			if (url == null)
			{
				url = "";
			}
			string type = reader.GetAttribute("type");
			if (type == null)
			{
				type = "";
			}
			string length = reader.GetAttribute("length");
			long lengthBytes = 0;
			if (!string.IsNullOrEmpty(length))
			{
				bool lengthOk = long.TryParse(length, NumberStyles.Integer, CultureInfo.InvariantCulture, out lengthBytes);
				if (!lengthOk)
				{
					lengthBytes = 0;
				}
			}
			if (item.EnclosureUrl == "")
			{
				item.EnclosureUrl = url;
				item.MediaType = type;
				item.EnclosureLengthBytes = lengthBytes;
			}
		}

		private string ReadImageUrlFromChannelImage(XmlReader reader)
		{
			string url = "";
			if (reader.IsEmptyElement)
			{
				return url;
			}
			int startDepth = reader.Depth;
			while (reader.Read())
			{
				XmlNodeType nodeType = reader.NodeType;
				if (nodeType == XmlNodeType.EndElement && reader.Depth == startDepth)
				{
					return url;
				}
				if (nodeType != XmlNodeType.Element)
				{
					continue;
				}
				if (reader.LocalName == "url" && reader.NamespaceURI == "")
				{
					string urlText = ReadElementTextAndConsume(reader);
					if (url == "")
					{
						url = urlText;
					}
					continue;
				}
				bool isEmpty = reader.IsEmptyElement;
				if (!isEmpty)
				{
					ConsumeElementSubtree(reader);
				}
			}
			return url;
		}

		/// <summary>
		/// Reads the textual content (including any CDATA sections) of the
		/// element the reader is currently positioned on, then leaves the
		/// reader on the matching EndElement so the outer loop's next
		/// Read() naturally advances to the next sibling.
		/// </summary>
		private string ReadElementTextAndConsume(XmlReader reader)
		{
			if (reader.IsEmptyElement)
			{
				return "";
			}
			StringBuilder builder = new StringBuilder();
			int startDepth = reader.Depth;
			while (reader.Read())
			{
				XmlNodeType nodeType = reader.NodeType;
				if (nodeType == XmlNodeType.EndElement && reader.Depth == startDepth)
				{
					return builder.ToString();
				}
				if (nodeType == XmlNodeType.Text || nodeType == XmlNodeType.CDATA || nodeType == XmlNodeType.SignificantWhitespace)
				{
					builder.Append(reader.Value);
				}
			}
			return builder.ToString();
		}

		/// <summary>
		/// Consumes everything up to and including the matching EndElement
		/// for the element the reader is currently on. Leaves the reader on
		/// that EndElement, mirroring ReadElementTextAndConsume.
		/// </summary>
		private void ConsumeElementSubtree(XmlReader reader)
		{
			if (reader.IsEmptyElement)
			{
				return;
			}
			int startDepth = reader.Depth;
			while (reader.Read())
			{
				if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == startDepth)
				{
					return;
				}
			}
		}

		/// <summary>
		/// Parses an itunes:duration value. The wire format is one of:
		/// plain integer seconds ("2742"), MM:SS ("45:42"), or HH:MM:SS
		/// ("01:45:42"). Returns 0 on any malformed input.
		/// </summary>
		private int ParseDuration(string duration)
		{
			if (string.IsNullOrEmpty(duration))
			{
				return 0;
			}
			string trimmed = duration.Trim();
			if (trimmed.Length == 0)
			{
				return 0;
			}
			if (trimmed.IndexOf(':') >= 0)
			{
				string[] parts = trimmed.Split(':');
				int partCount = parts.Length;
				int hours = 0;
				int minutes = 0;
				int seconds = 0;
				if (partCount == 3)
				{
					bool hoursOk = int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hours);
					bool minutesOk = int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes);
					bool secondsOk = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds);
					if (!hoursOk || !minutesOk || !secondsOk)
					{
						return 0;
					}
					return hours * 3600 + minutes * 60 + seconds;
				}
				if (partCount == 2)
				{
					bool minutesOk = int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes);
					bool secondsOk = int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds);
					if (!minutesOk || !secondsOk)
					{
						return 0;
					}
					return minutes * 60 + seconds;
				}
				return 0;
			}
			int totalSeconds = 0;
			bool plainOk = int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out totalSeconds);
			if (!plainOk)
			{
				return 0;
			}
			return totalSeconds;
		}

		/// <summary>
		/// Normalises an RFC822 pubDate to ISO-8601 UTC ("yyyy-MM-ddTHH:mm:ssZ").
		/// Returns "" when the input cannot be parsed; callers fall back to
		/// PubDateRaw for diagnostics.
		/// </summary>
		private string NormalizePubDate(string pubDate)
		{
			if (string.IsNullOrEmpty(pubDate))
			{
				return "";
			}
			DateTimeOffset parsed;
			bool ok = DateTimeOffset.TryParse(pubDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed);
			if (!ok)
			{
				return "";
			}
			DateTimeOffset utc = parsed.ToUniversalTime();
			return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
		}
	}
}
