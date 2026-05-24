

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
			PulseAnalyticsInfo analytics = m_musicManager.GetAnalytics();

			int count = int.Parse(context.Request.Query["count"].FirstOrDefault() ?? "10");

			List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();


			List<ArtistInfo> sorted = new List<ArtistInfo>(allArtists);
			sorted.Sort(CompareArtistScoreDescending);

			List<object> artists = new List<object>();
			int limit = Math.Min(count, sorted.Count);
			for (int idx = 0; idx < limit; idx++)
			{
				ArtistInfo artist = sorted[idx];
				if (artist != null)
				{
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
						score = sorted[idx].WeightedScore,
						coverArt = coverArt
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
			all.Sort(ComparePlaylistSongCountDescending);

			List<object> playlists = new List<object>();
			int limit = Math.Min(count, all.Count);
			for (int idx = 0; idx < limit; idx++)
			{
				PlaylistInfo playlist = all[idx];
				playlists.Add(new
				{
					id = playlist.Id,
					name = playlist.Name,
					songCount = playlist.GetSongCount(),
					duration = playlist.DurationSeconds
				});
			}

			return Results.Json(new { playlists = playlists });
		}

		private static int CompareArtistScoreDescending(ArtistInfo left, ArtistInfo right)
		{
			return right.WeightedScore.CompareTo(left.WeightedScore);
		}

		private static int ComparePlaylistSongCountDescending(PlaylistInfo left, PlaylistInfo right)
		{
			return right.GetSongCount().CompareTo(left.GetSongCount());
		}
	}
}
