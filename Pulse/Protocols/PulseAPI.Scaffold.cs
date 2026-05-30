using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;

namespace Pulse.Protocols
{
	public partial class PulseAPI
	{
		public IResult HandlePing(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandlePing(u);
		}

		public IResult HandlePing(string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleMe(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleMe(u);
		}

		public IResult HandleMe(string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleGetArtists(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleGetArtists(u);
		}

		public IResult HandleGetArtists(string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleGetArtist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleGetArtist(id, u);
		}

		public IResult HandleGetArtist(string id, string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleGetAlbum(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleGetAlbum(id, u);
		}

		public IResult HandleGetAlbum(string id, string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleGetAlbums(HttpContext context)
		{
			string sort = context.Request.Query["sort"].FirstOrDefault();
			int size = QueryParameters.GetInt(context, "size", 20);
			int offset = QueryParameters.GetInt(context, "offset", 0);
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleGetAlbums(sort, size, offset, u);
		}

		public IResult HandleGetAlbums(string sort, int size, int offset, string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleGetGenres(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleGetGenres(u);
		}

		public IResult HandleGetGenres(string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleGetGenre(HttpContext context)
		{
			string name = context.Request.Query["name"].FirstOrDefault();
			int count = QueryParameters.GetInt(context, "count", 50);
			int offset = QueryParameters.GetInt(context, "offset", 0);
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleGetGenre(name, count, offset, u);
		}

		public IResult HandleGetGenre(string name, int count, int offset, string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleGetTrack(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleGetTrack(id, u);
		}

		public IResult HandleGetTrack(string id, string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleSearch(HttpContext context)
		{
			string q = context.Request.Query["q"].FirstOrDefault();
			int artistCount = QueryParameters.GetInt(context, "artistCount", 20);
			int albumCount = QueryParameters.GetInt(context, "albumCount", 20);
			int songCount = QueryParameters.GetInt(context, "songCount", 20);
			int playlistCount = QueryParameters.GetInt(context, "playlistCount", 20);
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleSearch(q, artistCount, albumCount, songCount, playlistCount, u);
		}

		public IResult HandleSearch(string q, int artistCount, int albumCount, int songCount, int playlistCount, string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleGetPlaylists(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleGetPlaylists(u);
		}

		public IResult HandleGetPlaylists(string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleGetPlaylist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleGetPlaylist(id, u);
		}

		public IResult HandleGetPlaylist(string id, string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleCreatePlaylist(HttpContext context)
		{
			string name = context.Request.Query["name"].FirstOrDefault();
			string comment = context.Request.Query["comment"].FirstOrDefault();
			List<string> songIds = context.Request.Query["songIds"].ToList();
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleCreatePlaylist(name, comment, songIds, u);
		}

		public IResult HandleCreatePlaylist(string name, string comment, List<string> songIds, string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleUpdatePlaylist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string name = context.Request.Query["name"].FirstOrDefault();
			string comment = context.Request.Query["comment"].FirstOrDefault();
			List<string> tracks = context.Request.Query["tracks"].ToList();
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleUpdatePlaylist(id, name, comment, tracks, u);
		}

		public IResult HandleUpdatePlaylist(string id, string name, string comment, List<string> tracks, string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleDeletePlaylist(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleDeletePlaylist(id, u);
		}

		public IResult HandleDeletePlaylist(string id, string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleGetStarred(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleGetStarred(u);
		}

		public IResult HandleGetStarred(string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleStar(HttpContext context)
		{
			string kind = context.Request.Query["kind"].FirstOrDefault();
			string id = context.Request.Query["id"].FirstOrDefault();
			bool starred = QueryParameters.GetBool(context, "starred", true);
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleStar(kind, id, starred, u);
		}

		public IResult HandleStar(string kind, string id, bool starred, string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleRate(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			int rating = QueryParameters.GetInt(context, "rating", 0);
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleRate(id, rating, u);
		}

		public IResult HandleRate(string id, int rating, string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleStream(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleStream(id, u);
		}

		public IResult HandleStream(string id, string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleGetCoverArt(HttpContext context)
		{
			string id = context.Request.Query["id"].FirstOrDefault();
			int size = QueryParameters.GetInt(context, "size", 0);
			return HandleGetCoverArt(id, size);
		}

		public IResult HandleGetCoverArt(string id, int size)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleScrobble(HttpContext context)
		{
			string trackId = context.Request.Query["trackId"].FirstOrDefault();
			string phase = context.Request.Query["event"].FirstOrDefault();
			int elapsedSeconds = QueryParameters.GetInt(context, "elapsedSeconds", 0);
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleScrobble(trackId, phase, elapsedSeconds, u);
		}

		public IResult HandleScrobble(string trackId, string phase, int elapsedSeconds, string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

		public IResult HandleHome(HttpContext context)
		{
			string u = context.Request.Query["u"].FirstOrDefault();
			return HandleHome(u);
		}

		public IResult HandleHome(string u)
		{
			return Results.Json(new { error = "not implemented" }, statusCode: 501);
		}

	}
}
