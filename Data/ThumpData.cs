using System;
using System.Collections.Generic;
using Microsoft.Maui.ApplicationModel;
using Thump.Pulse;


namespace Thump.Data
{
	public enum eDataType
	{
		Track,
		Album,
		Playlist,
		Artist,
		CoverArt,
		SongData,
		Genre,
		Podcast,
		PodcastEpisode
	}



	public enum eRoutes
	{
		GetTrack,
		GetArtists,
		GetAlbum,
		GetAlbums,
		GetAlbumsForArtist,
		GetPlaylists,
		GetPlaylist,
		GetGenres,
		GetTracksForGenre,
		GetCoverArt,
		GetRecentlyPlayed,
		GetTopPlaylists,
		GetPopularArtists,
		GetRecentlyAdded,
		GetFavorites,
	}

	public class ThumpDataOb
	{
		public eDataType Kind { get; set; }
	}
	public class CoverArt : ThumpDataOb
	{
		public byte[] m_data;
	}
	public class SongData : ThumpDataOb
	{
		public byte[] m_data;
	}


	public class ThumpData
	{
		public IMediaClient Pulse {get {return m_pulseClient; } }
		public ThumpCache Cache {get { return m_cache; } }
		private IMediaClient m_pulseClient;
		private ThumpCache m_cache;

		private Dictionary<eRoutes, DataRoute> m_dataRoutes;


		public ThumpData(IMediaClient pulseClient, ThumpCache cache)
		{
			m_pulseClient = pulseClient;
			m_cache = cache;

			m_dataRoutes = new Dictionary<eRoutes, DataRoute>()
			{
				{ eRoutes.GetTrack,					new DataRoute<PulseTrack>(this,				eRouteCachingMethod.NetworkAuthorative, m_pulseClient.GetTrack,					m_cache.GetTrack,				m_cache.UpdateTrack,                IsValidObject) },
				{ eRoutes.GetArtists,				new DataRoute<List<PulseArtist>>(this,		eRouteCachingMethod.NetworkAuthorative,	m_pulseClient.GetArtists,				m_cache.GetAllArtists,			m_cache.UpdateAllArtists,			IsValidList<PulseArtist>) },
				{ eRoutes.GetAlbum,					new DataRouteID<PulseAlbum>(this,			eRouteCachingMethod.NetworkAuthorative,	m_pulseClient.GetAlbum,					m_cache.GetAlbum,				m_cache.UpdateAlbum,				IsValidObject) },
				{ eRoutes.GetAlbums,				new DataRoute<List<PulseAlbum>>(this,		eRouteCachingMethod.NetworkAuthorative,	m_pulseClient.GetAlbums,				m_cache.GetAlbums,				m_cache.UpdateAlbums,				IsValidList<PulseAlbum>) },
				{ eRoutes.GetAlbumsForArtist,		new DataRouteID<List<PulseAlbum>>(this,		eRouteCachingMethod.NetworkAuthorative,	m_pulseClient.GetArtistAlbums,			m_cache.GetAlbumsForArtist,		m_cache.UpdateAlbumsForArtist,		IsValidList<PulseAlbum>) },
				{ eRoutes.GetPlaylists,				new DataRoute<List<PulsePlaylist>>(this,	eRouteCachingMethod.NetworkAuthorative,	m_pulseClient.GetPlaylists,				m_cache.GetAllPlaylists,		m_cache.UpdateAllPlaylists,			IsValidList<PulsePlaylist>) },
				{ eRoutes.GetPlaylist,				new DataRouteID<PulsePlaylist>(this,		eRouteCachingMethod.NetworkAuthorative,	m_pulseClient.GetPlaylist,				m_cache.GetPlaylist,			m_cache.UpdatePlaylist,				IsValidObject) },
				{ eRoutes.GetGenres,				new DataRoute<List<PulseGenre>>(this,		eRouteCachingMethod.NetworkAuthorative,	m_pulseClient.GetGenres,				m_cache.GetGenres,				m_cache.UpdateGenres,				IsValidList<PulseGenre>) },
				{ eRoutes.GetTracksForGenre,		new DataRouteID<List<PulseTrack>>(this,		eRouteCachingMethod.NetworkAuthorative,	m_pulseClient.GetTracksForGenre,		m_cache.GetTracksForGenre,		m_cache.UpdateTracksForGenre,		IsValidList<PulseTrack>) },
				{ eRoutes.GetCoverArt,				new DataRouteID<byte[]>(this,				eRouteCachingMethod.LocalFirst,			m_pulseClient.GetCoverArt,				m_cache.GetCoverArt,			m_cache.UpdateCoverArt,				IsValidBinary) },
				{ eRoutes.GetRecentlyPlayed,		new DataRoute<List<PulseObject>>(this,		eRouteCachingMethod.NetworkAuthorative,	m_pulseClient.GetRecentlyPlayed,		m_cache.GetRecentlyPlayed,		m_cache.UpdateRecentlyPlayed,		IsValidList<PulseObject>) },
				{ eRoutes.GetTopPlaylists,			new DataRoute<List<PulsePlaylist>>(this,	eRouteCachingMethod.NetworkAuthorative,	m_pulseClient.GetTopPlaylists,			m_cache.GetTopPlaylists,		m_cache.UpdateTopPlaylists,			IsValidList<PulsePlaylist>) },
				{ eRoutes.GetPopularArtists,		new DataRoute<List<PulseArtist>>(this,		eRouteCachingMethod.NetworkAuthorative,	m_pulseClient.GetPopularArtists,		m_cache.GetPopularArtists,		m_cache.UpdatePopularArtists,		IsValidList<PulseArtist>) },
				{ eRoutes.GetRecentlyAdded,			new DataRoute<List<PulseObject>>(this,		eRouteCachingMethod.NetworkAuthorative,	m_pulseClient.GetRecentlyAdded,			m_cache.GetRecentlyAdded,		m_cache.UpdateRecentlyAdded,		IsValidList<PulseObject>) },
				{ eRoutes.GetFavorites,				new DataRoute<List<PulseTrack>>(this,		eRouteCachingMethod.NetworkAuthorative,	m_pulseClient.GetFavorites,				m_cache.GetFavorites,			m_cache.UpdateFavorites,			IsValidList<PulseTrack>) },
			};

		}

		public bool IsOnline()
		{
			return m_pulseClient.IsOnline();
		}
		private void GetData<T>(DataRoute<T> dataRoute, Action<T> callback) where T : class
		{
			dataRoute.GetData(callback);
		}

		private void GetData<T>(DataRouteID<T> dataRoute, string id, Action<T> callback) where T : class
		{
			dataRoute.GetData(id, callback);
		}

		private static bool IsValidList<T>(List<T> list)
		{
			if (list == null)
			{
				return false;
			}
			return list.Count > 0;
		}

		private static bool IsValidObject(PulseObject pulseObject)
		{
			if (pulseObject == null)
			{
				return false;
			}
			return !string.IsNullOrEmpty(pulseObject.Id);
		}

		private static bool IsValidBinary(byte[] data)
		{
			return data != null && data.Length > 0;
		}

		public DataRoute<T> GetDataRoute<T>(eRoutes route) where T : class
		{
			if (m_dataRoutes.TryGetValue(route, out DataRoute dataRoute))
				return dataRoute as DataRoute<T>;
			return null;
		}

		public DataRouteID<T> GetDataRouteID<T>(eRoutes route) where T : class
		{
			if (m_dataRoutes.TryGetValue(route, out DataRoute dataRoute))
				return dataRoute as DataRouteID<T>;
			return null;
		}

		public void GetArtists(Action<List<PulseArtist>> callback)
		{
			if (callback == null)
			{
				return;
			}
			DataRoute<List<PulseArtist>> dataRoute = GetDataRoute<List<PulseArtist>>(eRoutes.GetArtists);
			if (dataRoute != null)
			{
				GetData(dataRoute, callback);
			}
			else
			{
				callback(new List<PulseArtist>());
			}
		}

		public void GetArtist(string artistID, Action<PulseArtist> callback)
		{
			if (callback == null)
			{
				return;
			}
			DataRouteID<PulseArtist> dataRoute = GetDataRouteID<PulseArtist>(eRoutes.GetAlbum);
			if (dataRoute != null)
			{
				GetData(dataRoute, artistID, callback);
			}
			else
			{
				callback(null);
			}
		}


		public void GetAlbumsForArtist(PulseArtist artist, Action<List<PulseAlbum>> callback)
		{
		}
		public void GetAlbumsForArtist(string artistID, Action<List<PulseAlbum>> callback)
		{ 
			if (callback == null)
			{
				return;
			}
			DataRouteID<List<PulseAlbum>> dataRoute = GetDataRouteID<List<PulseAlbum>>(eRoutes.GetAlbumsForArtist);
			if (dataRoute != null)
			{
				GetData(dataRoute, artistID, callback);
			}
			else
			{
				callback(new List<PulseAlbum>());
			}
		}


		public void GetTracksForArtist(PulseArtist artist, Action<List<PulseTrack>> callback)
		{
			GetTracksForArtist(artist.Id, callback);
		}
		public void GetTracksForArtist(string artistID, Action<List<PulseTrack>> callback)
		{ 
			if (callback == null)
			{
				return;
			}

			GetAlbumsForArtist(artistID, (albums) =>
			{
				List<PulseTrack> tracks = new List<PulseTrack>();
				for(int i = 0; i < albums.Count; i++)
				{
					tracks.AddRange(albums[i].Tracks);
				}
				callback(tracks);
			});
		}
		public void GetAlbum(string albumId, Action<PulseAlbum> callback)
		{
			if (callback == null)
			{
				return;
			}
			DataRouteID<PulseAlbum> dataRoute = GetDataRouteID<PulseAlbum>(eRoutes.GetAlbum);
			if (dataRoute != null)
			{
				GetData(dataRoute, albumId, callback);
			}
			else
			{
				callback(null);
			}
		}

		public void GetTracksForAlbum(PulseAlbum album, Action<List<PulseTrack>> callback)
		{
			GetTracksForAlbum(album.Id, callback);
		}
		public void GetTracksForAlbum(string albumId, Action<List<PulseTrack>> callback)
		{ 
			if (callback == null)
			{
				return;
			}
			GetAlbum(albumId, (fullAlbum) =>
			{
				if (fullAlbum == null)
				{
					callback(new List<PulseTrack>());
					return;
				}
				callback(fullAlbum.Tracks);
			});
		}

		public void GetPlaylists(Action<List<PulsePlaylist>> callback)
		{
			if (callback == null)
			{
				return;
			}
			DataRoute<List<PulsePlaylist>> dataRoute = GetDataRoute<List<PulsePlaylist>>(eRoutes.GetPlaylists);
			if (dataRoute != null)
			{
				GetData(dataRoute, callback);
			}
			else
			{
				callback(new List<PulsePlaylist>());
			}
		}

		public void GetPlaylist(string playlistId, Action<PulsePlaylist> callback)
		{
			if (callback == null)
			{
				return;
			}
			DataRouteID<PulsePlaylist> dataRoute = GetDataRouteID<PulsePlaylist>(eRoutes.GetPlaylist);
			if (dataRoute != null)
			{
				GetData(dataRoute, playlistId, callback);
			}
			else
			{
				callback(null);
			}
		}
		public void GetTracksForPlaylist(PulsePlaylist Playlist, Action<List<PulseTrack>> callback)
		{
			GetTracksForPlaylist(Playlist.Id, callback);
		}
		public void GetTracksForPlaylist(string PlaylistId, Action<List<PulseTrack>> callback)
		{
			if (callback == null)
			{
				return;
			}
			GetPlaylist(PlaylistId, (fullPlaylist) =>
			{
				if (fullPlaylist == null)
				{
					callback(new List<PulseTrack>());
					return;
				}
				callback(fullPlaylist.Tracks);
			});
		}

		public void GetAlbums(Action<List<PulseAlbum>> callback)
		{
			if (callback == null)
			{
				return;
			}
			DataRoute<List<PulseAlbum>> dataRoute = GetDataRoute<List<PulseAlbum>>(eRoutes.GetAlbums);
			if (dataRoute != null)
			{
				GetData(dataRoute, callback);
			}
			else
			{
				callback(null);
			}
		}

		public void GetGenres(Action<List<PulseGenre>> callback)
		{
			if (callback == null)
			{
				return;
			}
			DataRoute<List<PulseGenre>> dataRoute = GetDataRoute<List<PulseGenre>>(eRoutes.GetGenres);
			if (dataRoute != null)
			{
				GetData(dataRoute, callback);
			}
			else
			{
				callback(null);
			}
		}

		public void GetTracksForGenre(PulseGenre genre, Action<List<PulseTrack>> callback)
		{
			GetTracksForGenre(genre.Name, callback);
		}
		public void GetTracksForGenre(string genreName, Action<List<PulseTrack>> callback)
		{ 
			if (callback == null)
			{
				return;
			}
			DataRouteID<List<PulseTrack>> dataRoute = GetDataRouteID<List<PulseTrack>>(eRoutes.GetTracksForGenre);
			if (dataRoute != null)
			{
				GetData(dataRoute, genreName, callback);
			}
			else
			{
				callback(null);
			}
		}
		public void GetTrack(string trackId, Action<PulseTrack> callback)
		{
			if (callback == null)
			{
				return;
			}
			DataRouteID<PulseTrack> dataRoute = GetDataRouteID<PulseTrack>(eRoutes.GetTracksForGenre);
			if (dataRoute != null)
			{
				GetData(dataRoute, trackId, callback);
			}
			else
			{
				callback(null);
			}
		}
		public byte[] GetTrackAudioData(PulseTrack track)
		{
			if (track == null)
				return null;
			return GetTrackAudioData(track.Id);
		}
		public byte[] GetTrackAudioData(string trackId)
		{ 
			if (string.IsNullOrEmpty(trackId))
			{
				return null;
			}
			string blobKey = "track:" + trackId;

			//try our local cache first
			byte[] trackData = null;
			m_cache.ExecuteSync(() => 
			{ 
				trackData = m_cache.ReadBlob(blobKey);
			});

			if (trackData != null && trackData.Length > 0)
			{
				return trackData;
			}

			//we only stream from disk, whoever wanted this should have cached ahead
			return null;


			/*
			//We're offline, no point in trying to pull new data
			if (!IsOnline())
				return	null;
			
			ManualResetEventSlim wait = new ManualResetEventSlim(false);
			m_pulseClient.GetTrackAudio(trackId, (data) =>
			{
				trackData = data;
				wait.Set();
			});
			wait.Wait();

			if (trackData == null || trackData.Length == 0)
			{
				//track is busted and/or missing
				return null;
			}

			//Save this file to our cache
			m_cache.ExecuteSync(() => 
			{ 
				m_cache.WriteBlob(blobKey, trackData, "audio"); 
			});
			return trackData;*/
		}

		/// <summary>
		/// Checks if a track has been locally cached
		/// </summary>
		/// <param name="track"></param>
		/// <returns></returns>
		public bool IsTrackCached(PulseTrack track)
		{
			return IsTrackCached(track.Id);
		}
		public bool IsTrackCached(string trackId)
		{
			if ( string.IsNullOrEmpty(trackId))
			{
				return false;
			}
			string blobKey = "track:" + trackId;
			bool cached = false;
			m_cache.ExecuteSync(() =>
			{
				cached = !string.IsNullOrEmpty(m_cache.GetBlobFilePath(blobKey));
			});

			return cached;
		}


		/// <summary>
		/// Requests a track be cached to disk
		/// </summary>
		/// <param name="track"></param>
		/// <param name="onComplete"></param>
		public void CacheTrack(PulseTrack track, Action<bool> onComplete)
		{
			CacheTrack(track.Id, onComplete);
		}
		public void CacheTrack(string trackId, Action<bool> onComplete)
		{ 
			if (IsTrackCached(trackId))
			{
				if (onComplete != null)
					onComplete(true);
				return;
			}

			string blobKey = "track:" + trackId;
			m_pulseClient.GetTrackAudio(trackId, (data) =>
			{
				if (data == null || data.Length == 0)
				{
					MainThread.BeginInvokeOnMainThread(() => 
					{
						if (onComplete != null)
							onComplete(false);
					});
					return;
				}
				m_cache.ExecuteSync(() =>
				{ 
					m_cache.WriteBlob(blobKey, data, "audio");
				});

				MainThread.BeginInvokeOnMainThread(() => 
				{
					if (onComplete != null)
						onComplete(true);
				});
			});
		}

		public void Search(string query, Action<PulseSearchData> callback)
		{
			if (callback == null)
			{
				return;
			}
			m_pulseClient.Search(query, callback);
		}

		public void GetPodcasts(Action<List<PulsePodcastChannel>> callback)
		{
			if (callback == null)
			{
				return;
			}
			m_pulseClient.GetPodcasts(callback);
		}

		public void StarTrack(string trackId, Action<bool> callback)
		{
			if (callback == null)
			{
				return;
			}
			m_pulseClient.Star(trackId, callback);
		}

		public void UnstarTrack(string trackId, Action<bool> callback)
		{
			if (callback == null)
			{
				return;
			}
			m_pulseClient.Unstar(trackId, callback);
		}

		public void GetCoverArt(string coverArtId, Action<byte[]> callback)
		{
			if (callback == null)
			{
				return;
			}
			DataRouteID<byte[]> dataRoute = GetDataRouteID<byte[]>(eRoutes.GetCoverArt);
			if (dataRoute != null)
			{
				GetData(dataRoute, coverArtId, callback);
			}
			else
			{
				callback(null);
			}
		}

		public void GetRecentlyPlayed(Action<List<PulseObject>> callback)
		{
			if (callback == null)
			{
				return;
			}
			DataRoute<List<PulseObject>> dataRoute = GetDataRoute<List<PulseObject>>(eRoutes.GetRecentlyPlayed);
			if (dataRoute != null)
			{
				GetData(dataRoute, callback);
			}
			else
			{
				callback(null);
			}
		}

		public void GetTopPlaylists(Action<List<PulsePlaylist>> callback)
		{
			if (callback == null)
			{
				return;
			}
			DataRoute<List<PulsePlaylist>> dataRoute = GetDataRoute<List<PulsePlaylist>>(eRoutes.GetTopPlaylists);
			if (dataRoute != null)
			{
				GetData(dataRoute, callback);
			}
			else
			{
				callback(null);
			}
		}

		public void GetPopularArtists(Action<List<PulseArtist>> callback)
		{
			if (callback == null)
			{
				return;
			}
			DataRoute<List<PulseArtist>> dataRoute = GetDataRoute<List<PulseArtist>>(eRoutes.GetPopularArtists);
			if (dataRoute != null)
			{
				GetData(dataRoute, callback);
			}
			else
			{
				callback(null);
			}
		}

		public void GetRecentlyAdded(Action<List<PulseObject>> callback)
		{
			if (callback == null)
			{
				return;
			}
			DataRoute<List<PulseObject>> dataRoute = GetDataRoute<List<PulseObject>>(eRoutes.GetRecentlyAdded);
			if (dataRoute != null)
			{
				GetData(dataRoute, callback);
			}
			else
			{
				Log.Error("Error no data path for: " + eRoutes.GetRecentlyAdded.ToString());
				callback(null);
			}
		}

		public void GetTopItems(Action<List<PulseObject>> callback)
		{
			if (callback == null)
			{
				return;
			}
			m_pulseClient.GetTopItems(callback);
		}

		public void GetFavories(Action<List<PulseObject>> callback)
		{
			if (callback == null)
			{
				return;
			}
			DataRoute<List<PulseObject>> dataRoute = GetDataRoute<List<PulseObject>>(eRoutes.GetFavorites);
			if (dataRoute != null)
			{
				GetData(dataRoute, callback);
			}
			else
			{
				callback(null);
			}
		}
	}
}