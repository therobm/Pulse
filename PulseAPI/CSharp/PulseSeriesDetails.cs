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
}
