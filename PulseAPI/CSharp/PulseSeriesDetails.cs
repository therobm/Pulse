using System.Collections.Generic;

namespace PulseAPI.CSharp
{
	/// <summary>
	/// Wire-type carrying a PulseAudiobook and its ordered chapters.
	/// </summary>
	public class PulseAudiobookDetails : PulseObject
	{
		public PulseAudiobook Book;
		public List<PulseChapter> Chapters;

		public PulseAudiobookDetails()
		{
			Chapters = new List<PulseChapter>();
			Kind = eDataType.AudiobookDetails;
		}
	}

	/// <summary> Podcast series plus the episodes the client should show. </summary>
	public class PulsePodcastDetails : PulseObject
	{
		public PulsePodcast Series;
		public List<PulseEpisode> Episodes;

		public PulsePodcastDetails()
		{
			Episodes = new List<PulseEpisode>();
			Kind = eDataType.PodcastEpisodes;
		}
	}
}
