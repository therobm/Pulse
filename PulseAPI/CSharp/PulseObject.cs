
namespace PulseAPI.CSharp
{
	/// <summary>
	/// 1:1 mapping of PulseObject type
	/// useful for avoiding string comparisons and reflection
	/// </summary>
	public enum eDataType
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
		PodcastEpisodes,
		PodcastEpisode,
		CoverArt,
		SongData
	}

	public class PulseObject
	{
		public string Id;
		public eDataType Kind;
	}
}
