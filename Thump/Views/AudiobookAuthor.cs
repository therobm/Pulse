namespace Thump.Views
{
	// Client-side grouping of audiobooks by their Author string. Not a server
	// catalogue entity - just a derived view over the books already fetched, so
	// the Library can browse authors -> their books without a PulseAuthor type.
	public class AudiobookAuthor
	{
		public string Name = "";
		public string CoverArt = "";
		public int BookCount = 0;
	}
}
