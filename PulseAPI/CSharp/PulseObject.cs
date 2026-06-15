
using System;
using System.Collections.Generic;

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
	public class PulseMusicObject : PulseObject
	{
		public float Score;
		public DateTime LastPlayed;
		public static void SortByScore(List<PulseMusicObject> items)
		{
			items.Sort((a, b) => b.Score.CompareTo(a.Score));
		}
		public static void SortByLastPlayed(List<PulseMusicObject> items)
		{
			items.Sort((a, b) => b.LastPlayed.CompareTo(a.LastPlayed));
		}
	}
}
