namespace PulseAPI.CSharp
{
	public class PulsePodcastChannel : PulseObject
	{
		public string Title;
		public string Description;
		public string CoverArt;
		public string Url;
		public string Status;
		public int EpisodeCount;

		public PulsePodcastChannel()
		{
			Kind = eDataType.Podcast;
		}
	}
}
