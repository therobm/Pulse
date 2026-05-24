

using Microsoft.AspNetCore.Http;
using Pulse.MusicLibrary;
using Pulse.SubsonicService;
using System.Collections.Concurrent;

namespace Pulse.Protocols
{
	public class PulseAPI
	{
		MusicManager m_musicManager;
		PulseService m_pulseService;
		private byte[] m_defaultCoverArt;
		private ConcurrentDictionary<string, byte[]> m_coverArtCache = new ConcurrentDictionary<string, byte[]>();
		private object m_recentLock = new object();
		private int m_maxRecent = 50;

		public PulseAPI(PulseService pulse, MusicManager musicManager)
		{
			m_pulseService = pulse;
			m_musicManager = musicManager;
			m_defaultCoverArt = File.ReadAllBytes("./Content/Media/pulseLogo.png");
		}

	

		public void OnTrackPlayedA(string user, string trackId)
		{
			TrackInfo track = m_musicManager.GetTrack(trackId);
			if (track == null) 
			{ 
				return; 
			}

			track.Score.PlayCount = track.Score.PlayCount + 1;
			track.LastPlayed = DateTime.UtcNow;

			if (user != null)
			{
				if (!track.UserScore.ContainsKey(user))
				{
					track.UserScore[user] = new ScoreData();
				}
				track.UserScore[user].PlayCount = track.UserScore[user].PlayCount + 1;
			}

			int artistCount = 0;
			PulseAnalyticsInfo analytics = m_musicManager.GetAnalytics();

			analytics.ArtistPlayCounts.TryGetValue(track.ArtistId, out artistCount);
			analytics.ArtistPlayCounts[track.ArtistId] = artistCount + 1;

			lock (m_recentLock)
			{
				analytics.RecentlyPlayed.Remove(trackId);
				analytics.RecentlyPlayed.Insert(0, trackId);
				if (analytics.RecentlyPlayed.Count > m_maxRecent)
				{
					analytics.RecentlyPlayed.RemoveAt(analytics.RecentlyPlayed.Count - 1);
				}
			}
			analytics.m_bIsDirty = true;
		}

		public IResult HandleRecentlyPlayed(HttpContext context)
		{
			PulseAnalyticsInfo analytics = m_musicManager.GetAnalytics();
			int count = int.Parse(context.Request.Query["count"].FirstOrDefault() ?? "10");
			string user = context.Request.Query["u"].FirstOrDefault();

			List<object> tracks = new List<object>();
			lock (m_recentLock)
			{
				int limit = Math.Min(count, analytics.RecentlyPlayed.Count);
				for (int idx = 0; idx < limit; idx++)
				{
					TrackInfo track = m_musicManager.GetTrack(analytics.RecentlyPlayed[idx]);
					if (track != null)
					{
						tracks.Add(new
						{
							id = track.Id,
							title = track.Title,
							artist = track.Artist,
							artistId = track.ArtistId,
							album = track.Album,
							albumId = track.AlbumId,
							coverArt = track.CoverArtId,
							duration = track.DurationSeconds
						});
					}
				}
			}

			return Results.Json(new { tracks = tracks });
		}

		public IResult HandlePopularArtists(HttpContext context)
		{
			PulseAnalyticsInfo analytics = m_musicManager.GetAnalytics();

			int count = int.Parse(context.Request.Query["count"].FirstOrDefault() ?? "10");

			List<KeyValuePair<string, int>> sorted = new List<KeyValuePair<string, int>>(analytics.ArtistPlayCounts);
			sorted.Sort((left, right) => right.Value.CompareTo(left.Value));

			List<object> artists = new List<object>();
			int limit = Math.Min(count, sorted.Count);
			for (int idx = 0; idx < limit; idx++)
			{
				ArtistInfo artist = m_musicManager.GetArtist(sorted[idx].Key);
				if (artist != null)
				{
					artists.Add(new
					{
						id = artist.Id,
						name = artist.Name,
						albumCount = artist.Albums.Count,
						playCount = sorted[idx].Value,
						coverArt = artist.Albums.Count > 0 ? artist.Albums[0].CoverArtId : null
					});
				}
			}

			return Results.Json(new { artists = artists });
		}

		public IResult HandleTopPlaylists(HttpContext context)
		{
			int count = int.Parse(context.Request.Query["count"].FirstOrDefault() ?? "10");
			string user = context.Request.Query["u"].FirstOrDefault();

			List<PlaylistInfo> all = m_musicManager.GetAllPlaylists(user);
			all.Sort((left, right) => right.SongCount.CompareTo(left.SongCount));

			List<object> playlists = new List<object>();
			int limit = Math.Min(count, all.Count);
			for (int idx = 0; idx < limit; idx++)
			{
				PlaylistInfo playlist = all[idx];
				playlists.Add(new
				{
					id = playlist.Id,
					name = playlist.Name,
					songCount = playlist.SongCount,
					duration = playlist.DurationSeconds
				});
			}

			return Results.Json(new { playlists = playlists });
		}
	}
}
