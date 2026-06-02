using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Thump.Data;

namespace Thump.Pulse
{
	public class PulseAPI : MediaClient
	{
		public enum eServerType
		{
			Subsonic,
			Pulse
		}

		private SubsonicAPI m_subsonic;

		public PulseAPI(ThumpCache cache) : base(cache)
		{
			m_subsonic = new SubsonicAPI(cache);
		}

		public override void SetServerParams(string ip, string port, string username, string password, MediaClient.eAuthType authType, bool enableSSL)
		{
			m_subsonic.SetServerParams(ip, port, username, password, authType, enableSSL);
			base.SetServerParams(ip, port, username, password, authType, enableSSL);
		}

		//Pulse-native connection probe. Hits /pulse/ping?u=<user> directly so
		//PulseAPI has its own m_bIsOnline signal instead of borrowing
		//m_subsonic's. Endpoint returns a plain JSON object (no subsonic-response
		//envelope) - the body shape doesn't matter to us, only that the GET
		//succeeded.
		protected override bool Ping(out JsonElement response)
		{
			response = default;
			try
			{
				string url = m_baseUrl + "/pulse/ping?u=" + Uri.EscapeDataString(m_user);
				string json = HttpGet(url, false);
				if (string.IsNullOrEmpty(json))
				{
					m_bIsOnline = false;
					return false;
				}
				JsonDocument doc = JsonDocument.Parse(json);
				response = doc.RootElement;
				m_bIsOnline = true;
				return true;
			}
			catch (Exception ex)
			{
				//Don't log ping failures - this is online/offline polling and
				//noisy logs while disconnected aren't useful.
				Log.Exception(ex);
			}
			m_bIsOnline = false;
			return false;
		}

		public override void GetTrack(string trackId, Action<PulseTrack> onComplete)
		{
			m_subsonic.GetTrack(trackId, onComplete);
		}
		public override void GetArtists(Action<List<PulseArtist>> onComplete)
		{
			m_subsonic.GetArtists(onComplete);
		}

		public override void GetArtist(string artistId, Action<PulseArtist> onComplete)
		{
			m_subsonic.GetArtist(artistId, onComplete);
		}
		public override void GetArtistTracks(string artistId, Action<List<PulseTrack>> onComplete)
		{
			m_subsonic.GetArtistTracks(artistId, onComplete);
		}
		public override void GetPodcasts(Action<List<PulsePodcastChannel>> onComplete)
		{
			m_subsonic.GetPodcasts(onComplete);
		}

		public override void Search(string query, Action<PulseSearchData> onComplete)
		{
			m_subsonic.Search(query, onComplete);
		}

		public override void GetArtistAlbums(string artistId, Action<List<PulseAlbum>> onComplete)
		{
			m_subsonic.GetArtistAlbums(artistId, onComplete);
		}

		public override void GetAlbum(string albumId, Action<PulseAlbum> onComplete)
		{
			m_subsonic.GetAlbum(albumId, onComplete);
		}

		public override void GetAlbums(Action<List<PulseAlbum>> onComplete)
		{
			m_subsonic.GetAlbums(onComplete);
		}

		public override void CreatePlaylist(string name, Action<PulsePlaylist> onComplete)
		{
			m_subsonic.CreatePlaylist(name, onComplete);
		}

		public override void RenamePlaylist(string playlistId, string newName, Action<bool> onComplete)
		{
			m_subsonic.RenamePlaylist(playlistId, newName, onComplete);
		}

		public override void Star(string trackId, Action<bool> onComplete)
		{
			m_subsonic.Star(trackId, onComplete);
		}

		public override void Unstar(string trackId, Action<bool> onComplete)
		{
			m_subsonic.Unstar(trackId, onComplete);
		}

		public override void DeletePlaylist(string playlistId, Action<bool> onComplete)
		{
			m_subsonic.DeletePlaylist(playlistId, onComplete);
		}

		public override void AddTrackToPlaylist(string playlistId, string songId, Action<bool> onComplete)
		{
			m_subsonic.AddTrackToPlaylist(playlistId, songId, onComplete);
		}

		public override void RemoveTrackFromPlaylist(string playlistId, int songIndex, Action<bool> onComplete)
		{
			m_subsonic.RemoveTrackFromPlaylist(playlistId, songIndex, onComplete);
		}

		public override void ReorderPlaylist(string playlistId, int fromIndex, int toIndex, List<PulseTrack> newOrder, Action<bool> onComplete)
		{
			m_subsonic.ReorderPlaylist(playlistId, fromIndex, toIndex, newOrder, onComplete);
		}

		public override void MarkPlaylistPlayed(string playlistId, Action<bool> onComplete)
		{
			m_subsonic.MarkPlaylistPlayed(playlistId, onComplete);
		}

		public override void GetPlaylists(Action<List<PulsePlaylist>> onComplete)
		{
			m_subsonic.GetPlaylists(onComplete);
		}

		public override void GetPlaylist(string playlistId, Action<PulsePlaylist> onComplete)
		{
			m_subsonic.GetPlaylist(playlistId, onComplete);
		}

		public override void GetCoverArt(string coverArtId, Action<byte[]> onComplete)
		{
			m_subsonic.GetCoverArt(coverArtId, onComplete);
		}

		public override void GetTrackAudio(string trackId, Action<byte[]> onComplete)
		{
			m_subsonic.GetTrackAudio(trackId, onComplete);
		}

		public override string GetTrackAudioURL(string trackId)
		{
			return m_subsonic.GetTrackAudioURL(trackId);
		}

		public override void GetRecentlyPlayed(Action<List<PulseObject>> onComplete)
		{
			m_subsonic.GetRecentlyPlayed(onComplete);
		}

		public override void GetPopularArtists(Action<List<PulseArtist>> onComplete)
		{
			m_subsonic.GetPopularArtists(onComplete);
		}

		public override void GetTopPlaylists(Action<List<PulsePlaylist>> onComplete)
		{
			m_subsonic.GetTopPlaylists(onComplete);
		}

		public override void GetRecentPlaylists(Action<List<PulsePlaylist>> onComplete)
		{
			m_subsonic.GetRecentPlaylists(onComplete);
		}

		public override void GetRecentlyAdded(Action<List<PulseObject>> onComplete)
		{
			m_subsonic.GetRecentlyAdded(onComplete);
		}

		public override void GetGenres(Action<List<PulseGenre>> onComplete)
		{
			m_subsonic.GetGenres(onComplete);
		}

		public override void GetTopItems(Action<List<PulseObject>> onComplete)
		{
			m_subsonic.GetTopItems(onComplete);
		}

		public override void GetTracksForGenre(string genre, Action<List<PulseTrack>> onComplete)
		{
			m_subsonic.GetTracksForGenre(genre, onComplete);
		}

		public override void GetFavorites(Action<List<PulseTrack>> onComplete)
		{
			m_subsonic.GetFavorites(onComplete);
		}

		
		public override void ReportTrackAnalytics(string trackId)
		{
			if (string.IsNullOrEmpty(trackId))
			{
				return;
			}
			if (!IsOnline())
			{
				return;
			}
			Task.Run(() =>
			{
				try
				{
					string url = m_baseUrl + "/pulse/reportTrackAnalytics?id=" + Uri.EscapeDataString(trackId) + "&u=" + Uri.EscapeDataString(m_user);
					HttpGet(url, false);
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
			});
		}
	}
}
