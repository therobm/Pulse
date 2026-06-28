namespace PulseIngestion.Metadata
{
	public class TagLibReader : MetadataReader
	{
		public override TrackTags Read(string filePath)
		{
			TrackTags tags = new TrackTags();

			TagLib.File file = TagLib.File.Create(filePath);
			TagLib.Tag tag = file.Tag;

			tags.Artist = FirstNonEmpty(tag.AlbumArtists);
			if (string.IsNullOrEmpty(tags.Artist))
			{
				tags.Artist = FirstNonEmpty(tag.Performers);
			}
			tags.Album = tag.Album;
			tags.Title = tag.Title;
			tags.TrackNumber = tag.Track;

			file.Dispose();
			return tags;
		}

		private static string FirstNonEmpty(string[] values)
		{
			if (values == null)
			{
				return "";
			}
			for (int i = 0; i < values.Length; i++)
			{
				if (!string.IsNullOrEmpty(values[i]))
				{
					return values[i];
				}
			}
			return "";
		}
	}
}
