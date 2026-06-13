
namespace PulseAPI.CSharp
{
	/// <summary>
	/// 1:1 mapping of PulseObject type
	/// useful for avoiding string comparisons and reflection
	/// </summary>
	public enum ePulseWireType
	{
		Track,
		Album,
		AlbumTracks,
		Playlist,
		PlaylistTracks,
		Artist,
		ArtistAlbums,
		ArtistTracks,
		Genre,
		GenreDetails,
		Podcast,
		PodcastDetails,
		PodcastEpisode,
		CoverArt,
		SongData,
		Audiobook,
		Chapter,
		AudiobookDetails,
		Stats,
		Version,
		Invalid,
	}

	public class PulseObject
	{
		public string Id;
		public ePulseWireType Kind;
	}
}
