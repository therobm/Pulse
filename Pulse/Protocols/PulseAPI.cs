

using Microsoft.AspNetCore.Http;
using Pulse.MusicLibrary;
using Pulse.SubsonicService;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pulse.Protocols
{
	public class PulseAPI
	{
		MusicManager m_musicManager;
		PulseService m_pulseService;
		private byte[] m_defaultCoverArt;
		private ConcurrentDictionary<string, byte[]> m_coverArtCache = new ConcurrentDictionary<string, byte[]>();
		private object m_recentLock = new object();

		public PulseAPI(PulseService pulse, MusicManager musicManager)
		{
			m_pulseService = pulse;
			m_musicManager = musicManager;
			m_defaultCoverArt = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Content", "Media", "pulseLogo.png"));
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
			int count = int.Parse(context.Request.Query["count"].FirstOrDefault() ?? "10");
			string user = context.Request.Query["u"].FirstOrDefault();

			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();

			List<KeyValuePair<ArtistInfo, float>> scored = new List<KeyValuePair<ArtistInfo, float>>();
			for (int idx = 0; idx < allArtists.Count; idx++)
			{
				scored.Add(new KeyValuePair<ArtistInfo, float>(allArtists[idx], allArtists[idx].GetScore(user)));
			}
			scored.Sort(CompareArtistScoredDescending);

			List<object> artists = new List<object>();
			int limit = Math.Min(count, scored.Count);
			for (int idx = 0; idx < limit; idx++)
			{
				ArtistInfo artist = scored[idx].Key;
				if (scored[idx].Value <= 0f)
				{
					break;
				}
				string coverArt = null;
				if (artist.Albums.Count > 0)
				{
					coverArt = artist.Albums[0].CoverArtId;
				}
				artists.Add(new
				{
					id = artist.Id,
					name = artist.Name,
					albumCount = artist.Albums.Count,
					score = scored[idx].Value,
					coverArt = coverArt
				});
			}

			return Results.Json(new { artists = artists });
		}

		public IResult HandleTopPlaylists(HttpContext context)
		{
			int count = int.Parse(context.Request.Query["count"].FirstOrDefault() ?? "10");
			string user = context.Request.Query["u"].FirstOrDefault();

			List<PlaylistInfo> all = m_musicManager.GetAllPlaylists(user);

			// Playlists don't have their own score; rank by the sum of the scores of
			// the distinct artists whose tracks appear in the playlist.
			List<KeyValuePair<PlaylistInfo, float>> scored = new List<KeyValuePair<PlaylistInfo, float>>();
			for (int playlistIndex = 0; playlistIndex < all.Count; playlistIndex++)
			{
				PlaylistInfo playlist = all[playlistIndex];
				HashSet<string> seenArtistIds = new HashSet<string>();
				float total = 0f;
				for (int trackIndex = 0; trackIndex < playlist.TrackIds.Count; trackIndex++)
				{
					TrackInfo track = m_musicManager.GetTrack(playlist.TrackIds[trackIndex]);
					if (track == null || string.IsNullOrEmpty(track.ArtistId))
					{
						continue;
					}
					if (!seenArtistIds.Add(track.ArtistId))
					{
						continue;
					}
					ArtistInfo artist = m_musicManager.GetArtist(track.ArtistId);
					if (artist != null)
					{
						total += artist.GetScore(user);
					}
				}
				scored.Add(new KeyValuePair<PlaylistInfo, float>(playlist, total));
			}
			scored.Sort(ComparePlaylistScoredDescending);

			List<object> playlists = new List<object>();
			int limit = Math.Min(count, scored.Count);
			for (int idx = 0; idx < limit; idx++)
			{
				PlaylistInfo playlist = scored[idx].Key;
				playlists.Add(new
				{
					id = playlist.Id,
					name = playlist.Name,
					songCount = playlist.GetSongCount(),
					duration = playlist.DurationSeconds,
					score = scored[idx].Value
				});
			}

			return Results.Json(new { playlists = playlists });
		}

		private static int CompareArtistScoredDescending(KeyValuePair<ArtistInfo, float> left, KeyValuePair<ArtistInfo, float> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		private static int ComparePlaylistScoredDescending(KeyValuePair<PlaylistInfo, float> left, KeyValuePair<PlaylistInfo, float> right)
		{
			return right.Value.CompareTo(left.Value);
		}
	}
}
