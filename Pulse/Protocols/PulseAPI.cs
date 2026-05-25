

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

		// Bumps a playlist's LastPlayed to now. Called by the web client when the
		// user clicks Play / Shuffle on a playlist; lets the left-rail "Recent"
		// sort surface the playlists you actually listen to. Also bumps the
		// per-user timestamp so the home carousel can rank by what *this* user
		// listens to rather than aggregate activity.
		public IResult HandleMarkPlaylistPlayed(HttpContext context)
		{
			string playlistId = context.Request.Query["id"].FirstOrDefault();
			if (string.IsNullOrEmpty(playlistId))
			{
				return Results.Json(new { ok = false });
			}
			PlaylistInfo playlist = m_musicManager.GetPlaylist(playlistId);
			if (playlist == null)
			{
				return Results.Json(new { ok = false });
			}
			string user = context.Request.Query["u"].FirstOrDefault();
			DateTime now = DateTime.UtcNow;
			playlist.LastPlayed = now;
			if (!string.IsNullOrEmpty(user))
			{
				playlist.UserLastPlayed[user] = now;
			}
			playlist.m_bIsDirty = true;
			return Results.Json(new { ok = true });
		}

		// Returns every track for a given artist in (album-index, track-number) order.
		// Used by the artist detail Play / Shuffle buttons so the client can avoid
		// firing one getAlbum call per album.
		public IResult HandleArtistTracks(HttpContext context)
		{
			string artistId = context.Request.Query["id"].FirstOrDefault();
			if (string.IsNullOrEmpty(artistId))
			{
				return Results.Json(new { tracks = new List<object>() });
			}

			ArtistInfo artist = m_musicManager.GetArtist(artistId);
			if (artist == null)
			{
				return Results.Json(new { tracks = new List<object>() });
			}

			List<object> tracks = new List<object>();
			for (int albumIndex = 0; albumIndex < artist.Albums.Count; albumIndex++)
			{
				AlbumInfo album = artist.Albums[albumIndex];
				List<TrackInfo> ordered = new List<TrackInfo>(album.Tracks);
				ordered.Sort(CompareTrackByDiscThenNumber);
				for (int trackIndex = 0; trackIndex < ordered.Count; trackIndex++)
				{
					TrackInfo track = ordered[trackIndex];
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

			return Results.Json(new { tracks = tracks });
		}

		private static int CompareTrackByDiscThenNumber(TrackInfo left, TrackInfo right)
		{
			int discCompare = left.DiscNumber.CompareTo(right.DiscNumber);
			if (discCompare != 0)
			{
				return discCompare;
			}
			return left.TrackNumber.CompareTo(right.TrackNumber);
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
					coverArt = coverArt,
					lastPlayed = FormatLastPlayedForJson(artist.LastPlayed)
				});
			}

			return Results.Json(new { artists = artists });
		}

		public IResult HandleTopPlaylists(HttpContext context)
		{
			return RankAndEmitPlaylists(context, false);
		}

		// Same response shape as topPlaylists, sorted by per-user LastPlayed
		// descending (never-played falls to the back). Separate route from
		// topPlaylists so callers can pick the semantic they want without
		// reading a query param that contradicts the route name (#151).
		public IResult HandleRecentPlaylists(HttpContext context)
		{
			return RankAndEmitPlaylists(context, true);
		}

		private IResult RankAndEmitPlaylists(HttpContext context, bool sortByRecency)
		{
			int count = int.Parse(context.Request.Query["count"].FirstOrDefault() ?? "10");
			string user = context.Request.Query["u"].FirstOrDefault();

			List<PlaylistInfo> all = m_musicManager.GetAllPlaylists(user);

			// Playlists don't have their own score; the average of the scores
			// of the distinct artists whose tracks appear in the playlist is
			// the tiebreaker. Average (not sum) so a long playlist of mediocre
			// tracks doesn't outrank a tight playlist of favorites.
			List<PlaylistRankRow> ranked = new List<PlaylistRankRow>();
			for (int playlistIndex = 0; playlistIndex < all.Count; playlistIndex++)
			{
				PlaylistInfo playlist = all[playlistIndex];
				HashSet<string> seenArtistIds = new HashSet<string>();
				float total = 0f;
				int artistCount = 0;
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
						artistCount++;
					}
				}
				float average = 0f;
				if (artistCount > 0)
				{
					average = total / artistCount;
				}

				PlaylistRankRow row = new PlaylistRankRow();
				row.Playlist = playlist;
				row.Score = average;
				row.LastPlayed = playlist.GetLastPlayed(user);
				ranked.Add(row);
			}
			// Sort key follows the route the caller chose:
			//  - topPlaylists: score desc, lastPlayed tiebreaker
			//  - recentPlaylists: lastPlayed desc (never-played to the back),
			//    score tiebreaker so unplayed users still get something sensible.
			if (sortByRecency)
			{
				ranked.Sort(ComparePlaylistRankRow);
			}
			else
			{
				ranked.Sort(ComparePlaylistRankRowByScore);
			}

			List<object> playlists = new List<object>();
			int limit = Math.Min(count, ranked.Count);
			for (int idx = 0; idx < limit; idx++)
			{
				PlaylistInfo playlist = ranked[idx].Playlist;
				playlists.Add(new
				{
					id = playlist.Id,
					name = playlist.Name,
					songCount = playlist.GetSongCount(),
					duration = playlist.DurationSeconds,
					score = ranked[idx].Score,
					lastPlayed = FormatLastPlayedForJson(ranked[idx].LastPlayed),
					// Synthetic cover-art id (#143). Clients pass this to
					// getCoverArt to fetch a 4-tile composite assembled from
					// the playlist's first distinct album covers.
					coverArt = "pl-" + playlist.Id
				});
			}

			return Results.Json(new { playlists = playlists });
		}

		private class PlaylistRankRow
		{
			public PlaylistInfo Playlist;
			public float Score;
			public DateTime LastPlayed;
		}

		private static int ComparePlaylistRankRow(PlaylistRankRow left, PlaylistRankRow right)
		{
			int byLastPlayed = right.LastPlayed.CompareTo(left.LastPlayed);
			if (byLastPlayed != 0)
			{
				return byLastPlayed;
			}
			return right.Score.CompareTo(left.Score);
		}

		private static int ComparePlaylistRankRowByScore(PlaylistRankRow left, PlaylistRankRow right)
		{
			int byScore = right.Score.CompareTo(left.Score);
			if (byScore != 0)
			{
				return byScore;
			}
			return right.LastPlayed.CompareTo(left.LastPlayed);
		}

		// Round-trip ISO-8601 string for the JS side, empty for "never played"
		// so the JS sort can treat that as oldest without parsing junk.
		private static string FormatLastPlayedForJson(DateTime value)
		{
			if (value == default(DateTime))
			{
				return "";
			}
			return value.ToString("o");
		}

		private static int CompareArtistScoredDescending(KeyValuePair<ArtistInfo, float> left, KeyValuePair<ArtistInfo, float> right)
		{
			return right.Value.CompareTo(left.Value);
		}
	}
}
