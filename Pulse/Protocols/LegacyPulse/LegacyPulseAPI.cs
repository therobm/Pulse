

using Microsoft.AspNetCore.Http;
using Pulse.MusicLibrary;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Pulse.Protocols.LegacyPulse
{
	public partial class LegacyPulseAPI
	{
		MusicManager m_musicManager;
		PulseService m_pulseService;
		private byte[] m_defaultCoverArt;
		private ConcurrentDictionary<string, byte[]> m_coverArtCache = new ConcurrentDictionary<string, byte[]>();
		private object m_recentLock = new object();



		public LegacyPulseAPI(PulseService pulse, MusicManager musicManager)
		{
			m_pulseService = pulse;
			m_musicManager = musicManager;
			m_defaultCoverArt = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Content", "Media", "pulseLogo.png"));
		}

		// Mixed-kind recents shelf (Flatline #223). Accepts a comma-separated
		// `types` query param -- any combination of "track", "artist", "album",
		// "playlist". When `types` is omitted the response stays tracks-only to
		// keep pre-#223 external callers working; new callers (e.g. Thump) pass
		// `types=track,artist,playlist` to opt into the mixed shelf. Each item
		// carries a `kind` discriminator and a `coverArt` id that always
		// resolves through getCoverArt. The response also still includes the
		// legacy `tracks` field (kind=track subset) for backward compatibility
		// until #228 retires it.
		public IResult HandleRecentlyPlayed(HttpContext context)
		{
			int count = QueryParameters.GetInt(context, "count", 10);
			string user = QueryParameters.GetString(context, "u");
			string typesParam = QueryParameters.GetString(context, "types");

			bool includeTracks;
			bool includeArtists;
			bool includeAlbums;
			bool includePlaylists;
			ParseTypesParam(typesParam, out includeTracks, out includeArtists, out includeAlbums, out includePlaylists);

			List<RecentCandidate> candidates = new List<RecentCandidate>();

			if (includeTracks)
			{
				PulseAnalyticsInfo analytics = m_musicManager.GetAnalytics();
				lock (m_recentLock)
				{
					for (int idx = 0; idx < analytics.RecentlyPlayed.Count; idx++)
					{
						TrackInfo track = m_musicManager.GetTrack(analytics.RecentlyPlayed[idx]);
						if (track == null) { continue; }
						RecentCandidate candidate = new RecentCandidate();
						candidate.Kind = "track";
						candidate.Track = track;
						// Tracks in analytics.RecentlyPlayed are FIFO-ordered; if a
						// track somehow has no LastPlayed, fall back to the position
						// so it still slots in roughly the right spot.
						if (track.LastPlayed != default(DateTime))
						{
							candidate.RankTime = track.LastPlayed;
						}
						else
						{
							candidate.RankTime = DateTime.UtcNow.AddSeconds(-idx);
						}
						candidates.Add(candidate);
					}
				}
			}

			if (includeArtists)
			{
				List<ArtistInfo> allArtists = m_musicManager.GetAllArtists();
				for (int idx = 0; idx < allArtists.Count; idx++)
				{
					ArtistInfo artist = allArtists[idx];
					if (artist.LastPlayed == default(DateTime)) { continue; }
					RecentCandidate candidate = new RecentCandidate();
					candidate.Kind = "artist";
					candidate.Artist = artist;
					candidate.RankTime = artist.LastPlayed;
					candidates.Add(candidate);
				}
			}

			if (includeAlbums)
			{
				List<AlbumInfo> allAlbums = m_musicManager.GetAllAlbums();
				for (int idx = 0; idx < allAlbums.Count; idx++)
				{
					AlbumInfo album = allAlbums[idx];
					DateTime albumLastPlayed = default(DateTime);
					for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
					{
						DateTime trackLastPlayed = album.Tracks[trackIndex].LastPlayed;
						if (trackLastPlayed > albumLastPlayed) { albumLastPlayed = trackLastPlayed; }
					}
					if (albumLastPlayed == default(DateTime)) { continue; }
					RecentCandidate candidate = new RecentCandidate();
					candidate.Kind = "album";
					candidate.Album = album;
					candidate.RankTime = albumLastPlayed;
					candidates.Add(candidate);
				}
			}

			if (includePlaylists)
			{
				List<PlaylistInfo> allPlaylists = m_musicManager.GetAllPlaylists(user);
				for (int idx = 0; idx < allPlaylists.Count; idx++)
				{
					PlaylistInfo playlist = allPlaylists[idx];
					DateTime playlistLastPlayed = playlist.GetLastPlayed(user);
					if (playlistLastPlayed == default(DateTime)) { continue; }
					RecentCandidate candidate = new RecentCandidate();
					candidate.Kind = "playlist";
					candidate.Playlist = playlist;
					candidate.RankTime = playlistLastPlayed;
					candidates.Add(candidate);
				}
			}

			candidates.Sort(CompareRecentCandidateDescending);

			List<object> items = new List<object>();
			List<object> legacyTracks = new List<object>();
			int emit = Math.Min(count, candidates.Count);
			for (int idx = 0; idx < emit; idx++)
			{
				object built = BuildRecentItem(candidates[idx]);
				items.Add(built);
				// Mirror the kind=track items into the legacy `tracks` field
				// without their `kind` / `lastPlayed` keys, matching the pre-#223
				// shape exactly. Removed by #228 once external callers migrate.
				if (string.Equals(candidates[idx].Kind, "track", StringComparison.Ordinal))
				{
					TrackInfo t = candidates[idx].Track;
					legacyTracks.Add(new
					{
						id = t.Id,
						title = t.Title,
						artist = t.Artist,
						artistId = t.ArtistId,
						album = t.Album,
						albumId = t.AlbumId,
						coverArt = t.CoverArtId,
						duration = t.DurationSeconds
					});
				}
			}

			return Results.Json(new { items = items, tracks = legacyTracks });
		}

		private static void ParseTypesParam(string raw, out bool track, out bool artist, out bool album, out bool playlist)
		{
			if (string.IsNullOrWhiteSpace(raw))
			{
				// Omitted -> tracks only, matching pre-#223 behavior. New callers
				// must opt into mixed shelves explicitly. Bug #228 tracks the
				// future flip to default=all once external clients have migrated.
				track = true; artist = false; album = false; playlist = false;
				return;
			}
			track = false; artist = false; album = false; playlist = false;
			string[] parts = raw.Split(',');
			for (int idx = 0; idx < parts.Length; idx++)
			{
				string part = parts[idx].Trim();
				if (string.Equals(part, "track", StringComparison.OrdinalIgnoreCase)) { track = true; }
				else if (string.Equals(part, "artist", StringComparison.OrdinalIgnoreCase)) { artist = true; }
				else if (string.Equals(part, "album", StringComparison.OrdinalIgnoreCase)) { album = true; }
				else if (string.Equals(part, "playlist", StringComparison.OrdinalIgnoreCase)) { playlist = true; }
			}
		}

		private static int CompareRecentCandidateDescending(RecentCandidate left, RecentCandidate right)
		{
			return right.RankTime.CompareTo(left.RankTime);
		}

		private object BuildRecentItem(RecentCandidate candidate)
		{
			if (string.Equals(candidate.Kind, "track", StringComparison.Ordinal))
			{
				TrackInfo track = candidate.Track;
				return new
				{
					kind = "track",
					id = track.Id,
					title = track.Title,
					artist = track.Artist,
					artistId = track.ArtistId,
					album = track.Album,
					albumId = track.AlbumId,
					coverArt = track.CoverArtId,
					duration = track.DurationSeconds,
					lastPlayed = FormatLastPlayedForJson(track.LastPlayed)
				};
			}
			if (string.Equals(candidate.Kind, "artist", StringComparison.Ordinal))
			{
				ArtistInfo artist = candidate.Artist;
				return new
				{
					kind = "artist",
					id = artist.Id,
					name = artist.Name,
					albumCount = artist.Albums.Count,
					coverArt = "ar-" + artist.Id,
					lastPlayed = FormatLastPlayedForJson(artist.LastPlayed)
				};
			}
			if (string.Equals(candidate.Kind, "album", StringComparison.Ordinal))
			{
				AlbumInfo album = candidate.Album;
				return new
				{
					kind = "album",
					id = album.Id,
					name = album.Name,
					artist = album.ArtistName,
					artistId = album.ArtistId,
					year = album.Year,
					coverArt = album.CoverArtId,
					lastPlayed = FormatLastPlayedForJson(candidate.RankTime)
				};
			}
			PlaylistInfo playlist = candidate.Playlist;
			return new
			{
				kind = "playlist",
				id = playlist.Id,
				name = playlist.Name,
				songCount = playlist.GetSongCount(),
				duration = playlist.DurationSeconds,
				coverArt = "pl-" + playlist.Id,
				lastPlayed = FormatLastPlayedForJson(candidate.RankTime)
			};
		}

		private class RecentCandidate
		{
			public string Kind = "";
			public DateTime RankTime;
			public TrackInfo Track;
			public ArtistInfo Artist;
			public AlbumInfo Album;
			public PlaylistInfo Playlist;
		}

		// Bumps a playlist's LastPlayed to now. Called by the web client when the
		// user clicks Play / Shuffle on a playlist; lets the left-rail "Recent"
		// sort surface the playlists you actually listen to. Also bumps the
		// per-user timestamp so the home carousel can rank by what *this* user
		// listens to rather than aggregate activity.
		public IResult HandleMarkPlaylistPlayed(HttpContext context)
		{
			string playlistId = QueryParameters.GetString(context, "id");
			if (string.IsNullOrEmpty(playlistId))
			{
				return Results.Json(new { ok = false });
			}
			PlaylistInfo playlist = m_musicManager.GetPlaylist(playlistId);
			if (playlist == null)
			{
				return Results.Json(new { ok = false });
			}
			string user = QueryParameters.GetString(context, "u");
			DateTime now = DateTime.UtcNow;
			playlist.LastPlayed = now;
			if (!string.IsNullOrEmpty(user))
			{
				playlist.UserLastPlayed[user] = now;
			}
			playlist.m_bIsDirty = true;
			return Results.Json(new { ok = true });
		}

		// Returns every user row from the v5 users table plus any orphan
		// user_names still referenced by per-user data (so the operator can
		// see and clean them up). Each row carries activity counters derived
		// from the in-memory stores.
		public IResult HandleListUsers(HttpContext context)
		{
			List<UserRecord> users = m_musicManager.GetAllUsers();
			List<object> response = new List<object>();
			for (int index = 0; index < users.Count; index++)
			{
				UserRecord user = users[index];
				string createdStr = "";
				if (user.Created != DateTime.MinValue)
				{
					createdStr = user.Created.ToString("o");
				}
				response.Add(new
				{
					name = user.Name,
					displayName = user.DisplayName,
					created = createdStr,
					isAdmin = user.IsAdmin,
					scoredTrackCount = user.ScoredTrackCount,
					starredCount = user.StarredCount,
					playlistLastPlayedCount = user.PlaylistLastPlayedCount
				});
			}
			return Results.Json(new { users = response });
		}

		public IResult HandleCreateUser(HttpContext context)
		{
			string name = QueryParameters.GetString(context, "name");
			string displayName = QueryParameters.GetString(context, "displayName") ?? "";
			bool isAdmin = QueryParameters.GetBool(context, "isAdmin");

			string error = m_musicManager.CreateUser(name, displayName, isAdmin);
			if (!string.IsNullOrEmpty(error))
			{
				return Results.Json(new { ok = false, error = error });
			}
			Log.Info(-1, "Settings: created user '" + name + "'");
			return Results.Json(new { ok = true });
		}

		public IResult HandleUpdateUser(HttpContext context)
		{
			string oldName = QueryParameters.GetString(context, "name");
			string newName = QueryParameters.GetString(context, "newName");
			string displayName = QueryParameters.GetString(context, "displayName") ?? "";
			bool isAdmin = QueryParameters.GetBool(context, "isAdmin");

			if (string.IsNullOrEmpty(newName))
			{
				newName = oldName;
			}

			string error = m_musicManager.UpdateUser(oldName, newName, displayName, isAdmin);
			if (!string.IsNullOrEmpty(error))
			{
				return Results.Json(new { ok = false, error = error });
			}
			Log.Info(-1, "Settings: updated user '" + oldName + "' (now '" + newName + "')");
			return Results.Json(new { ok = true });
		}

		// Deletes every per-user row for the given user_name across the database
		// and the in-memory caches. Bug #201 -- used by the settings page to clean
		// up duplicate-cased names that crept in (e.g. "shannon" vs "Shannon").
		public IResult HandleDeleteUser(HttpContext context)
		{
			string userName = QueryParameters.GetString(context, "user");
			if (string.IsNullOrEmpty(userName))
			{
				return Results.Json(new { ok = false, error = "Missing user" });
			}
			m_musicManager.DeleteUser(userName);
			Log.Info(-1, "Settings: deleted user '" + userName + "'");
			return Results.Json(new { ok = true });
		}

		// Returns every track for a given artist in (album-index, track-number) order.
		// Used by the artist detail Play / Shuffle buttons so the client can avoid
		// firing one getAlbum call per album.
		public IResult HandleArtistTracks(HttpContext context)
		{
			string artistId = QueryParameters.GetString(context, "id");
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
			int count = QueryParameters.GetInt(context, "count", 10);
			string user = QueryParameters.GetString(context, "u");

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
				artists.Add(new
				{
					id = artist.Id,
					name = artist.Name,
					albumCount = artist.Albums.Count,
					score = scored[idx].Value,
					coverArt = "ar-" + artist.Id,
					lastPlayed = FormatLastPlayedForJson(artist.LastPlayed)
				});
			}

			return Results.Json(new { artists = artists });
		}

		// Score-based ranking is on hold; for now this returns playlists ordered
		// by per-user last-played, same as HandleRecentPlaylists. The "top"
		// ranking will get a real definition later.
		public IResult GetTopPlaylists(HttpContext context)
		{
			int count = QueryParameters.GetInt(context, "count", 10);
			string user = QueryParameters.GetString(context, "u");
			string api = QueryParameters.GetString(context, "api");

			List<PlaylistInfo> all = m_musicManager.GetAllPlaylists(user);

			List<KeyValuePair<PlaylistInfo, DateTime>> ranked = new List<KeyValuePair<PlaylistInfo, DateTime>>();
			for (int index = 0; index < all.Count; index++)
			{
				ranked.Add(new KeyValuePair<PlaylistInfo, DateTime>(all[index], all[index].GetLastPlayed(user)));
			}
			ranked.Sort(ComparePlaylistByLastPlayedDescending);

			int limit = Math.Min(count, ranked.Count);

			// Legacy Thump (no `api` param) — pre-envelope wire shape. Delete
			// this branch once every fielded client is on the envelope-aware
			// build.
			if (string.IsNullOrEmpty(api))
			{
				List<object> legacyPlaylists = new List<object>();
				for (int index = 0; index < limit; index++)
				{
					PlaylistInfo playlist = ranked[index].Key;
					legacyPlaylists.Add(new
					{
						id = playlist.Id,
						name = playlist.Name,
						songCount = playlist.GetSongCount(),
						duration = playlist.DurationSeconds,
						score = 0f,
						lastPlayed = FormatLastPlayedForJson(ranked[index].Value),
						coverArt = "pl-" + playlist.Id
					});
				}
				return Results.Json(new { playlists = legacyPlaylists });
			}

			SearchResult result = new SearchResult();
			result.Playlists = new List<PlaylistInfo>();
			for (int index = 0; index < limit; index++)
			{
				result.Playlists.Add(ranked[index].Key);
			}
			return CreateResponse(result);
		}

		public IResult GetRecentPlaylists(HttpContext context)
		{
			int count = QueryParameters.GetInt(context, "count", 10);
			string user = QueryParameters.GetString(context, "u");
			string api = QueryParameters.GetString(context, "api");

			List<PlaylistInfo> all = m_musicManager.GetAllPlaylists(user);

			List<KeyValuePair<PlaylistInfo, DateTime>> ranked = new List<KeyValuePair<PlaylistInfo, DateTime>>();
			for (int index = 0; index < all.Count; index++)
			{
				ranked.Add(new KeyValuePair<PlaylistInfo, DateTime>(all[index], all[index].GetLastPlayed(user)));
			}
			ranked.Sort(ComparePlaylistByLastPlayedDescending);

			int limit = Math.Min(count, ranked.Count);

			// Legacy Thump (no `api` param) — pre-envelope wire shape. Delete
			// this branch once every fielded client is on the envelope-aware
			// build.
			if (string.IsNullOrEmpty(api))
			{
				List<object> legacyPlaylists = new List<object>();
				for (int index = 0; index < limit; index++)
				{
					PlaylistInfo playlist = ranked[index].Key;
					legacyPlaylists.Add(new
					{
						id = playlist.Id,
						name = playlist.Name,
						songCount = playlist.GetSongCount(),
						duration = playlist.DurationSeconds,
						score = 0f,
						lastPlayed = FormatLastPlayedForJson(ranked[index].Value),
						coverArt = "pl-" + playlist.Id
					});
				}
				return Results.Json(new { playlists = legacyPlaylists });
			}

			SearchResult result = new SearchResult();
			result.Playlists = new List<PlaylistInfo>();
			for (int index = 0; index < limit; index++)
			{
				result.Playlists.Add(ranked[index].Key);
			}
			return CreateResponse(result);
		}

		private static int ComparePlaylistByLastPlayedDescending(KeyValuePair<PlaylistInfo, DateTime> left, KeyValuePair<PlaylistInfo, DateTime> right)
		{
			return right.Value.CompareTo(left.Value);
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

		protected IResult CreateResponse()
		{
			PulseResponse response = new PulseResponse();
			return Results.Json(response);
		}

		protected IResult CreateResponse(PulseInfo content)
		{
			PulseResponse response = new PulseResponse();
			response.item = content;
			return Results.Json(response);
		}

		protected IResult CreateResponse(byte[] data)
		{
			PulseResponse response = new PulseResponse();
			response.data = data;
			return Results.Json(response);
		}
		protected IResult CreateResponse(Error error)
		{
			PulseResponse response = new PulseResponse();
			response.error = error;
			return Results.Json(response);
		}

	}
}
