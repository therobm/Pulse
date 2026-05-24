using Pulse.MusicLibrary;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Pulse.Data
{
	public interface IPulseDatabase
	{
		int TrackCount { get; }
		int AlbumCount { get; }
		int ArtistCount { get; }
		PulseAnalyticsInfo Analytics { get; }

		TrackInfo GetTrack(string id);
		AlbumInfo GetAlbum(string id);
		ArtistInfo GetArtist(string id);
		List<TrackInfo> GetAllTracks();
		List<AlbumInfo> GetAllAlbums();
		List<ArtistInfo> GetAllArtists();
		bool TrackExists(string trackId);

		void SetRating(string trackId, int rating);
		void UpdateStar(string userName, string trackId, string albumId, string artistId, bool starred);
		ArtistInfo GetOrCreateArtist(string id, string name);
		AlbumInfo GetOrCreateAlbum(string id, string name, string artistId, string artistName, int year, string genre);
		void AddTrack(TrackInfo track, string albumId);
		bool RemoveTrack(string trackId);


		PlaylistInfo GetPlaylist(string id);
		List<PlaylistInfo> GetAllPlaylists(string userName);
		List<TrackInfo> GetPlaylistTracks(string playlistId);
		void DeletePlaylist(string playlistId);
		void CreateOrUpdate(PlaylistInfo playlist);
		void CreateOrUpdate(ArtistInfo artist);


		void Save();
	}

	public abstract class PulseDatabaseBase : IPulseDatabase
	{
		public int TrackCount { get { return m_tracks.Count; } }
		public int AlbumCount { get { return m_albums.Count; } }
		public int ArtistCount { get { return m_artists.Count; } }
		public PulseAnalyticsInfo Analytics { get { return m_analytics; } }

		protected ConcurrentDictionary<string, TrackInfo> m_tracks = new ConcurrentDictionary<string, TrackInfo>();
		protected ConcurrentDictionary<string, AlbumInfo> m_albums = new ConcurrentDictionary<string, AlbumInfo>();
		protected ConcurrentDictionary<string, ArtistInfo> m_artists = new ConcurrentDictionary<string, ArtistInfo>();
		protected ConcurrentDictionary<string, PlaylistInfo> m_playlists = new ConcurrentDictionary<string, PlaylistInfo>();
		protected ConcurrentDictionary<string, PlaylistInfo> m_autoPlaylists = new ConcurrentDictionary<string, PlaylistInfo>();
		protected PulseAnalyticsInfo m_analytics = new PulseAnalyticsInfo();

		protected object m_saveLock = new object();
		
	
		public TrackInfo GetTrack(string id)
		{
			TrackInfo track;
			m_tracks.TryGetValue(id, out track);
			return track;
		}

		public AlbumInfo GetAlbum(string id)
		{
			AlbumInfo album;
			m_albums.TryGetValue(id, out album);
			return album;
		}

		public ArtistInfo GetArtist(string id)
		{
			ArtistInfo artist;
			m_artists.TryGetValue(id, out artist);
			return artist;
		}

		public List<TrackInfo> GetAllTracks()
		{
			return new List<TrackInfo>(m_tracks.Values);
		}

		public List<AlbumInfo> GetAllAlbums()
		{
			return new List<AlbumInfo>(m_albums.Values);
		}

		public List<ArtistInfo> GetAllArtists()
		{
			List<ArtistInfo> list = new List<ArtistInfo>(m_artists.Values);
			list.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
			return list;
		}

		public bool TrackExists(string trackId)
		{
			return m_tracks.ContainsKey(trackId);
		}

		public void SetRating(string trackId, int rating)
		{
			TrackInfo track;
			if (m_tracks.TryGetValue(trackId, out track))
			{
				track.Rating = rating;
				track.m_bIsDirty = true;
			}
		}

		public void UpdateStar(string userName, string trackId, string albumId, string artistId, bool starred)
		{
			if (!string.IsNullOrEmpty(trackId))
			{
				TrackInfo track;
				if (m_tracks.TryGetValue(trackId, out track))
				{
					track.Starred[userName] = starred;
					track.m_bIsDirty = true;
				}
			}

			if (!string.IsNullOrEmpty(albumId))
			{
				AlbumInfo album;
				if (m_albums.TryGetValue(albumId, out album))
				{
					album.Starred[userName] = starred;
					album.m_bIsDirty = true;
				}
			}

			if (!string.IsNullOrEmpty(artistId))
			{
				ArtistInfo artist;
				if (m_artists.TryGetValue(artistId, out artist))
				{
					artist.Starred[userName] = starred;
					artist.m_bIsDirty = true;
				}
			}
		}

		public PlaylistInfo GetPlaylist(string id)
		{
			PlaylistInfo playlist;
			if (m_playlists.TryGetValue(id, out playlist))
			{
				return playlist;
			}
			m_autoPlaylists.TryGetValue(id, out playlist);
			return playlist;
		}

		public List<PlaylistInfo> GetAllPlaylists(string userName)
		{
			RebuildSmartPlaylists(userName);
			List<PlaylistInfo> list = new List<PlaylistInfo>(m_playlists.Values);
			list.AddRange(m_autoPlaylists.Values);
			return list;
		}

		public List<TrackInfo> GetPlaylistTracks(string playlistId)
		{
			PlaylistInfo playlist;
			if (!m_playlists.TryGetValue(playlistId, out playlist))
			{
				if (!m_autoPlaylists.TryGetValue(playlistId, out playlist))
				{
					return new List<TrackInfo>();
				}
			}

			List<TrackInfo> tracks = new List<TrackInfo>();
			for (int index = 0; index < playlist.TrackIds.Count; index++)
			{
				TrackInfo track;
				if (m_tracks.TryGetValue(playlist.TrackIds[index], out track))
				{
					tracks.Add(track);
				}
			}
			return tracks;
		}
		public virtual void DeletePlaylist(string playlistId)
		{
			m_playlists.TryRemove(playlistId, out _);
		}
		public void CreateOrUpdate(PlaylistInfo playlist)
		{
			m_playlists[playlist.Id] = playlist;
			m_playlists[playlist.Id].m_bIsDirty = true;
		}

		public void CreateOrUpdate(ArtistInfo artistInfo)
		{
			ArtistInfo artist;
			if (!m_artists.TryGetValue(artistInfo.Id, out artist))
			{
				artist = new ArtistInfo();
				m_artists[artistInfo.Id] = artist;
			}
			artist.Id = artistInfo.Id;
			artist.Name = artistInfo.Name;
			artist.m_bIsDirty = true;
		}

		public ArtistInfo GetOrCreateArtist(string id, string name)
		{
			ArtistInfo artist;
			if (m_artists.TryGetValue(id, out artist))
			{
				return artist;
			}

			artist = new ArtistInfo();
			artist.Id = id;
			artist.Name = name;
			artist.m_bIsDirty = true;
			m_artists[id] = artist;
			return artist;
		}

		public AlbumInfo GetOrCreateAlbum(string id, string name, string artistId, string artistName, int year, string genre)
		{
			AlbumInfo album;
			if (m_albums.TryGetValue(id, out album))
			{
				return album;
			}

			ArtistInfo artist = GetArtist(artistId);

			album = new AlbumInfo();
			album.Id = id;
			album.Name = name;
			album.ArtistId = artistId;
			album.ArtistName = artistName;
			album.Year = year;
			album.Genre = genre;
			album.CoverArtId = id;
			m_albums[id] = album;

			if (artist != null)
			{
				artist.Albums.Add(album);
				artist.m_bIsDirty = true;
			}

			return album;
		}

		public void AddTrack(TrackInfo track, string albumId)
		{
			m_tracks[track.Id] = track;

			AlbumInfo album;
			if (m_albums.TryGetValue(albumId, out album))
			{
				album.Tracks.Add(track);
				album.m_bIsDirty = true;
			}
		}

		public virtual bool RemoveTrack(string trackId)
		{
			TrackInfo track;
			if (!m_tracks.TryRemove(trackId, out track))
				return false;

			AlbumInfo album;
			if (m_albums.TryGetValue(track.AlbumId, out album))
			{
				album.Tracks.RemoveAll(t => t.Id == trackId);

				if (album.Tracks.Count == 0)
				{
					m_albums.TryRemove(track.AlbumId, out _);

					ArtistInfo artist;
					if (m_artists.TryGetValue(track.ArtistId, out artist))
					{
						artist.Albums.RemoveAll(a => a.Id == track.AlbumId);

						if (artist.Albums.Count == 0)
						{
							m_artists.TryRemove(track.ArtistId, out _);
						}
					}
				}
			}
			return true;
		}

		public abstract void Save();

		private void RebuildSmartPlaylists(string userName)
		{
			RebuildSmartPlaylist("Shared", null);
			if (!string.IsNullOrEmpty(userName)) 
				RebuildSmartPlaylist(userName, userName);
		}

		private void RebuildSmartPlaylist(string playlistName, string userName)
		{
			string playlistId = PulseUtility.GenerateID("smart/" + playlistName);

			List<TrackInfo> scoredTracks = new List<TrackInfo>();
			List<ArtistInfo> scoredArtists = new List<ArtistInfo>();
			List<TrackInfo> unplayedTracks = new List<TrackInfo>();

			foreach (ArtistInfo artistInfo in m_artists.Values)
			{
				if (artistInfo.WeightedScore > 0)
					scoredArtists.Add(artistInfo);
			}

			SmartPlaylist.CategorizeTracks(m_tracks.Values, userName, scoredTracks, unplayedTracks);

			Random rng = new Random();
			PlaylistInfo playlist = SmartPlaylist.BuildSmartPlaylist(playlistId, "Top Rated (" + userName + ")", scoredTracks, scoredArtists, unplayedTracks, userName, rng);
			m_autoPlaylists[playlistId] = playlist;
		}
	}


}
