using Microsoft.AspNetCore.Http;

namespace Pulse.Protocols
{
	// Scaffold for the Pulse-native API surface, intended to become the primary
	// path that Thump (and future first-party clients) call instead of /rest/
	// Subsonic. Each handler returns 501 Not Implemented for now -- bodies land
	// once the shape of each endpoint is locked in.
	//
	// Design rules to follow as these are filled in:
	//  - JSON field names match the C# property/anonymous-member names exactly.
	//    No naming policy, no JsonPropertyName attributes.
	//  - On success, return the resource directly. No Subsonic-style
	//    `{ "subsonic-response": { ... } }` envelope.
	//  - On error, return a non-2xx with `{ error = "<message>" }`. Where it's
	//    helpful, add `errorCode` (e.g. "playlist_not_found") so clients can
	//    branch without parsing prose.
	//  - Auth: every endpoint takes `u=<username>` for now. (To be tightened to
	//    an API key / session later -- the scaffold deliberately leaves it
	//    minimal so we don't pre-commit to a scheme.)
	//  - Numeric query params resolve through ParseQueryInt (FL#307) so bad input
	//    falls back to a default instead of throwing.
	//  - Once the Pulse path is primary, the Subsonic /rest/ handlers should
	//    translate requests into these and translate responses back -- not the
	//    other way round.
	public partial class PulseAPI
	{
		// ─── System ─────────────────────────────────────────────────────────

		/// <summary>
		/// GET /pulse/ping?u=&lt;name&gt;
		/// Heartbeat + auth probe. Returns { ok = true, serverVersion, userName }.
		/// </summary>
		public IResult HandlePing(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// GET /pulse/me?u=&lt;name&gt;
		/// The current user record. Returns { name, displayName, isAdmin,
		/// scrobblingEnabled, ... }.
		/// </summary>
		public IResult HandleMe(HttpContext context)
		{
			return NotImplemented();
		}

		// ─── Browsing ───────────────────────────────────────────────────────

		/// <summary>
		/// GET /pulse/artists?u=&lt;name&gt;
		/// All artists, alphabetised. Returns
		/// [{ id, name, coverArt, albumCount, score }].
		/// </summary>
		public IResult HandleGetArtists(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// GET /pulse/artist?id=&lt;id&gt;&amp;u=&lt;name&gt;
		/// Single artist plus their albums. Returns
		/// { id, name, coverArt, albums:[{ id, name, year, coverArt }] }.
		/// </summary>
		public IResult HandleGetArtist(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// GET /pulse/album?id=&lt;id&gt;&amp;u=&lt;name&gt;
		/// Single album plus its tracks (in disc/track order). Returns
		/// { id, name, artistId, artistName, year, coverArt,
		///   tracks:[{ id, title, trackNumber, discNumber, duration, coverArt }] }.
		/// </summary>
		public IResult HandleGetAlbum(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// GET /pulse/albums?sort=name|newest|year|random|starred|highest
		///                  &amp;size=20&amp;offset=0&amp;u=&lt;name&gt;
		/// Paged albums by sort. Returns
		/// [{ id, name, artist, artistId, year, coverArt }].
		/// </summary>
		public IResult HandleGetAlbums(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// GET /pulse/genres
		/// Returns [{ name, songCount, albumCount }].
		/// </summary>
		public IResult HandleGetGenres(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// GET /pulse/genre?name=&lt;genre&gt;&amp;count=50&amp;offset=0&amp;u=&lt;name&gt;
		/// Songs in a genre, paged. Returns the same track shape as /pulse/album.
		/// </summary>
		public IResult HandleGetGenre(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// GET /pulse/track?id=&lt;id&gt;
		/// Single track. Useful for refreshing a single cached row.
		/// </summary>
		public IResult HandleGetTrack(HttpContext context)
		{
			return NotImplemented();
		}

		// ─── Search ─────────────────────────────────────────────────────────

		/// <summary>
		/// GET /pulse/search?q=&lt;query&gt;&amp;artistCount=20&amp;albumCount=20
		///                  &amp;songCount=20&amp;playlistCount=20&amp;u=&lt;name&gt;
		/// Unified search. Returns
		/// { artists:[...], albums:[...], songs:[...], playlists:[...] }.
		/// Subsonic search3 does not include playlists; this does.
		/// </summary>
		public IResult HandleSearch(HttpContext context)
		{
			return NotImplemented();
		}

		// ─── Playlists ──────────────────────────────────────────────────────

		/// <summary>
		/// GET /pulse/playlists?u=&lt;name&gt;
		/// All playlists visible to the user. Returns
		/// [{ id, name, comment, songCount, duration, coverArt, lastPlayed }].
		/// </summary>
		public IResult HandleGetPlaylists(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// GET /pulse/playlist?id=&lt;id&gt;&amp;u=&lt;name&gt;
		/// Single playlist with its tracks. Returns
		/// { id, name, comment, songCount, duration, coverArt,
		///   tracks:[{ id, title, artist, album, duration, coverArt }] }.
		/// </summary>
		public IResult HandleGetPlaylist(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// POST /pulse/createPlaylist  body: { name, comment, songIds:[...], u }
		/// Returns the created playlist (same shape as /pulse/playlist).
		/// </summary>
		public IResult HandleCreatePlaylist(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// POST /pulse/updatePlaylist
		///       body: { id, name?, comment?, tracks?:[songId, songId, ...], u }
		/// `tracks` (when present) is the *full* desired ordering -- the server
		/// reconciles its current state to it. Cleaner than Subsonic's
		/// remove-by-index + append-by-id dance.
		/// Returns the updated playlist.
		/// </summary>
		public IResult HandleUpdatePlaylist(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// POST /pulse/deletePlaylist?id=&lt;id&gt;&amp;u=&lt;name&gt;
		/// Returns { ok = true }.
		/// </summary>
		public IResult HandleDeletePlaylist(HttpContext context)
		{
			return NotImplemented();
		}

		// ─── Favourites &amp; ratings ───────────────────────────────────────────

		/// <summary>
		/// GET /pulse/starred?u=&lt;name&gt;
		/// Everything this user has starred. Returns
		/// { artists:[...], albums:[...], songs:[...] }.
		/// </summary>
		public IResult HandleGetStarred(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// POST /pulse/star
		///       body: { kind: "track"|"album"|"artist", id, starred: true|false, u }
		/// One endpoint for both star and unstar. Returns { ok = true }.
		/// </summary>
		public IResult HandleStar(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// POST /pulse/rate?id=&lt;trackId&gt;&amp;rating=0..5&amp;u=&lt;name&gt;
		/// Rating 0 clears the rating. Returns { ok = true }.
		/// </summary>
		public IResult HandleRate(HttpContext context)
		{
			return NotImplemented();
		}

		// ─── Playback ───────────────────────────────────────────────────────

		/// <summary>
		/// GET /pulse/stream?id=&lt;trackId&gt;&amp;u=&lt;name&gt;
		/// Streams the audio file with HTTP range support. Real body will mirror
		/// the Subsonic stream handler's range/MIME handling.
		/// </summary>
		public IResult HandleStream(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// GET /pulse/coverArt?id=&lt;id&gt;&amp;size=&lt;px&gt;
		/// Returns the image bytes for a cover-art id (track / album / artist /
		/// playlist composite). `size` (optional) hints downscaling. Falls back
		/// to the default cover when the id is unknown or empty.
		/// </summary>
		public IResult HandleGetCoverArt(HttpContext context)
		{
			return NotImplemented();
		}

		/// <summary>
		/// POST /pulse/scrobble
		///       body: { trackId, event: "start"|"complete"|"skip",
		///               elapsedSeconds, u }
		/// Replaces Subsonic's submission=true/false split with an explicit
		/// event tag, so the server doesn't have to infer skip vs complete from
		/// timing alone. Returns { ok = true }.
		/// </summary>
		public IResult HandleScrobble(HttpContext context)
		{
			return NotImplemented();
		}

		// ─── Home / discovery aggregate ─────────────────────────────────────

		/// <summary>
		/// GET /pulse/home?u=&lt;name&gt;
		/// One call to populate Thump's home view -- a pre-mixed bundle of
		/// shelves (recently played, popular artists, top playlists, recently
		/// added albums, ...). Returns { shelves: [{ kind, title, items: [...] }] }.
		/// Saves the client a fan-out of /pulse/recentlyPlayed +
		/// /pulse/popularArtists + /pulse/topPlaylists + /pulse/albums calls.
		/// </summary>
		public IResult HandleHome(HttpContext context)
		{
			return NotImplemented();
		}

		// ─── helpers ────────────────────────────────────────────────────────

		private static IResult NotImplemented()
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}
	}
}
