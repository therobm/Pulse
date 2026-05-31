using System;
using System.Collections.Generic;
using System.Text;

namespace Thump.Playback
{
	public enum eAADirectory
	{
		Root,
		Home,
		Library,
		Podcasts,
		Albums,
		Playlists,
		Artists,
		Genres,
		RecentlyPlayed,
		RecentlyAdded,
		TopPlaylists,
		PopularArtists,
	}
	public enum eAAObject
	{
		Album,
		Artist,
		Playlist,
		Genre
	}

	public static class AAudoNavigation
	{
		private static Dictionary<eAADirectory, string> m_directories = new Dictionary<eAADirectory, string>()
		{
			{ eAADirectory.Root, "root" },
			{ eAADirectory.Home, "home" },
			{ eAADirectory.Library, "library" },
			{ eAADirectory.Podcasts, "podcasts" },
			{ eAADirectory.Albums, "albums" },
			{ eAADirectory.Playlists, "playlists" },
			{ eAADirectory.Artists, "artists" },
			{ eAADirectory.Genres, "genres" },
			{ eAADirectory.RecentlyPlayed, "home_recent" },
			{ eAADirectory.RecentlyAdded, "home_added" },
			{ eAADirectory.TopPlaylists, "home_top" },
			{ eAADirectory.PopularArtists, "home_popular" },
		};

		public static string GetId(eAADirectory directory)
		{
			string id;
			if (m_directories.TryGetValue(directory, out id))
			{
				return id;
			}
			return null;
		}

		public static bool TryGetDirectory(string id, out eAADirectory directory)
		{
			foreach (KeyValuePair<eAADirectory, string> pair in m_directories)
			{
				if (pair.Value == id)
				{
					directory = pair.Key;
					return true;
				}
			}
			directory = eAADirectory.Root;
			return false;
		}



	}
}
