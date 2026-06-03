namespace PulseAPI.CSharp
{
	public class PulseTrack : PulseObject
	{
		public string Title;
		public string Artist;
		public string ArtistId;
		public string Album;
		public string AlbumId;
		public string CoverArt;
		public int Duration;
		public bool Starred;

		public PulseTrack()
		{
			Kind = eDataType.Track;
		}

		public string GetImageId()
		{
			if (!string.IsNullOrEmpty(CoverArt))
			{
				return CoverArt;
			}
			if (!string.IsNullOrEmpty(AlbumId))
			{
				return AlbumId;
			}
			return null;
		}
	}
}
