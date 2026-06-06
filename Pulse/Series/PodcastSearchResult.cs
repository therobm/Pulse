namespace Pulse.Series
{
	/// <summary>
	/// One hit from the podcast discovery service (see PulseConfig.PodcastSearchUrl).
	/// This is NOT a catalogued series - it is a remote candidate the user can add
	/// by its FeedUrl. ArtworkUrl points straight at the provider's image CDN;
	/// clients load it directly rather than through the server coverArt endpoint.
	/// A public-field data bag, matching SeriesInfo's style.
	/// </summary>
	public class PodcastSearchResult
	{
		public string Title = "";
		public string Author = "";
		public string Description = "";
		public string FeedUrl = "";
		public string ArtworkUrl = "";
	}
}
