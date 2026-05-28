using System.Collections.Generic;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using Com.Google.Common.Util.Concurrent;
using Thump.Data;
using Thump.Pulse;

namespace Thump.Playback
{
	public class ThumpLibraryCallback : Java.Lang.Object, MediaLibraryService.MediaLibrarySession.ICallback
	{
		private const string s_rootId = "root";
		private const string s_albumsId = "albums";
		private const string s_playlistsId = "playlists";
		private const string s_artistsId = "artists";
		private const string s_genresId = "genres";

		private static ThumpData s_serviceData;

		private ThumpData GetData()
		{
			if (MainView.Self != null)
			{
				return MainView.Data;
			}
			if (s_serviceData == null)
			{
				string cacheRoot = Microsoft.Maui.Storage.FileSystem.CacheDirectory;
				PulseClient client = new PulseClient(MainView.ServerUrl, MainView.ServerUser);
				ThumpCache cache = new ThumpCache(System.IO.Path.Combine(cacheRoot, "thump.db"), System.IO.Path.Combine(cacheRoot, "blobs"));
				s_serviceData = new ThumpData(client, cache);
			}
			return s_serviceData;
		}

		public IListenableFuture OnGetLibraryRoot(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, MediaLibraryService.LibraryParams libraryParams)
		{
			MediaItem root = BuildBrowsableItem(s_rootId, "Thump");
			return Futures.ImmediateFuture(LibraryResult.OfItem(root, libraryParams));
		}

		public IListenableFuture OnGetItem(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, string mediaId)
		{
			MediaItem item = BuildItemForId(mediaId);
			return Futures.ImmediateFuture(LibraryResult.OfItem(item, null));
		}

		public IListenableFuture OnGetChildren(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, string parentId, int page, int pageSize, MediaLibraryService.LibraryParams libraryParams)
		{
			if (parentId == s_rootId)
			{
				List<MediaItem> categories = new List<MediaItem>();
				categories.Add(BuildBrowsableItem(s_albumsId, "Albums"));
				categories.Add(BuildBrowsableItem(s_playlistsId, "Playlists"));
				categories.Add(BuildBrowsableItem(s_artistsId, "Artists"));
				categories.Add(BuildBrowsableItem(s_genresId, "Genres"));
				return Futures.ImmediateFuture(LibraryResult.OfItemList(categories, libraryParams));
			}

			SettableFuture future = SettableFuture.Create();
			LoadChildren(parentId, libraryParams, future);
			return future;
		}

		private void LoadChildren(string parentId, MediaLibraryService.LibraryParams libraryParams, SettableFuture future)
		{
			if (parentId == s_albumsId)
			{
				GetData().GetAlbums((albums) =>
				{
					List<MediaItem> items = new List<MediaItem>();
					if (albums != null)
					{
						for (int idx = 0; idx < albums.Count; idx++)
						{
							PulseAlbum album = albums[idx];
							items.Add(BuildBrowsableItem("album/" + album.Id, album.Name));
						}
					}
					future.Set(LibraryResult.OfItemList(items, libraryParams));
				});
				return;
			}
			if (parentId == s_playlistsId)
			{
				GetData().GetPlaylists((playlists) =>
				{
					List<MediaItem> items = new List<MediaItem>();
					if (playlists != null)
					{
						for (int idx = 0; idx < playlists.Count; idx++)
						{
							PulsePlaylist playlist = playlists[idx];
							items.Add(BuildBrowsableItem("playlist/" + playlist.Id, playlist.Name));
						}
					}
					future.Set(LibraryResult.OfItemList(items, libraryParams));
				});
				return;
			}
			if (parentId == s_artistsId)
			{
				GetData().GetArtists((artists) =>
				{
					List<MediaItem> items = new List<MediaItem>();
					if (artists != null)
					{
						for (int idx = 0; idx < artists.Count; idx++)
						{
							PulseArtist artist = artists[idx];
							items.Add(BuildBrowsableItem("artist/" + artist.Id, artist.Name));
						}
					}
					future.Set(LibraryResult.OfItemList(items, libraryParams));
				});
				return;
			}
			if (parentId == s_genresId)
			{
				GetData().GetGenres((genres) =>
				{
					List<MediaItem> items = new List<MediaItem>();
					if (genres != null)
					{
						for (int idx = 0; idx < genres.Count; idx++)
						{
							PulseGenre genre = genres[idx];
							items.Add(BuildBrowsableItem("genre/" + genre.Name, genre.Name));
						}
					}
					future.Set(LibraryResult.OfItemList(items, libraryParams));
				});
				return;
			}

			string prefix = ParsePrefix(parentId);
			string value = ParseValue(parentId);
			if (prefix == "album")
			{
				GetData().GetAlbum(value, (album) =>
				{
					future.Set(LibraryResult.OfItemList(BuildTrackItems(album.Songs), libraryParams));
				});
				return;
			}
			if (prefix == "playlist")
			{
				GetData().GetPlaylist(value, (playlist) =>
				{
					future.Set(LibraryResult.OfItemList(BuildTrackItems(playlist.Songs), libraryParams));
				});
				return;
			}
			if (prefix == "artist")
			{
				PulseArtist artist = new PulseArtist();
				artist.Id = value;
				GetData().GetAlbumsForArtist(artist, (albums) =>
				{
					List<MediaItem> items = new List<MediaItem>();
					if (albums != null)
					{
						for (int idx = 0; idx < albums.Count; idx++)
						{
							PulseAlbum album = albums[idx];
							items.Add(BuildBrowsableItem("album/" + album.Id, album.Name));
						}
					}
					future.Set(LibraryResult.OfItemList(items, libraryParams));
				});
				return;
			}
			if (prefix == "genre")
			{
				PulseGenre genre = new PulseGenre();
				genre.Name = value;
				GetData().GetTracksForGenre(genre, (tracks) =>
				{
					future.Set(LibraryResult.OfItemList(BuildTrackItems(tracks), libraryParams));
				});
				return;
			}

			future.Set(LibraryResult.OfItemList(new List<MediaItem>(), libraryParams));
		}

		public IListenableFuture OnAddMediaItems(MediaSession session, MediaSession.ControllerInfo controller, IList<MediaItem> mediaItems)
		{
			SettableFuture future = SettableFuture.Create();
			Java.Util.ArrayList resolved = new Java.Util.ArrayList();
			ResolveItems(mediaItems, 0, resolved, future);
			return future;
		}

		private void ResolveItems(IList<MediaItem> items, int index, Java.Util.ArrayList resolved, SettableFuture future)
		{
			if (index >= items.Count)
			{
				future.Set(resolved);
				return;
			}
			MediaItem item = items[index];
			string trackId = StripTrackPrefix(item.MediaId);
			if (string.IsNullOrEmpty(trackId))
			{
				resolved.Add(item);
				ResolveItems(items, index + 1, resolved, future);
				return;
			}
			PulseTrack track = new PulseTrack();
			track.Id = trackId;
			GetData().GetTrackAudioFile(track, (localPath) =>
			{
				MediaItem resolvedItem = item;
				if (!string.IsNullOrEmpty(localPath))
				{
					Android.Net.Uri uri = Android.Net.Uri.FromFile(new Java.IO.File(localPath));
					resolvedItem = item.BuildUpon().SetUri(uri).Build();
				}
				resolved.Add(resolvedItem);
				ResolveItems(items, index + 1, resolved, future);
			});
		}

		private static List<MediaItem> BuildTrackItems(List<PulseTrack> tracks)
		{
			List<MediaItem> items = new List<MediaItem>();
			if (tracks == null)
			{
				return items;
			}
			for (int idx = 0; idx < tracks.Count; idx++)
			{
				PulseTrack track = tracks[idx];
				items.Add(BuildPlayableItem("track/" + track.Id, track.Title, track.Artist));
			}
			return items;
		}

		private static MediaItem BuildItemForId(string mediaId)
		{
			string trackId = StripTrackPrefix(mediaId);
			if (!string.IsNullOrEmpty(trackId))
			{
				return BuildPlayableItem(mediaId, mediaId, "");
			}
			return BuildBrowsableItem(mediaId, mediaId);
		}

		private static MediaItem BuildBrowsableItem(string mediaId, string title)
		{
			MediaMetadata.Builder metadata = new MediaMetadata.Builder();
			metadata.SetTitle(title);
			metadata.SetIsBrowsable(Java.Lang.Boolean.True);
			metadata.SetIsPlayable(Java.Lang.Boolean.False);

			MediaItem.Builder builder = new MediaItem.Builder();
			builder.SetMediaId(mediaId);
			builder.SetMediaMetadata(metadata.Build());
			return builder.Build();
		}

		private static MediaItem BuildPlayableItem(string mediaId, string title, string subtitle)
		{
			MediaMetadata.Builder metadata = new MediaMetadata.Builder();
			metadata.SetTitle(title);
			metadata.SetArtist(subtitle);
			metadata.SetIsBrowsable(Java.Lang.Boolean.False);
			metadata.SetIsPlayable(Java.Lang.Boolean.True);

			MediaItem.Builder builder = new MediaItem.Builder();
			builder.SetMediaId(mediaId);
			builder.SetMediaMetadata(metadata.Build());
			return builder.Build();
		}

		private static string ParsePrefix(string mediaId)
		{
			int slash = mediaId.IndexOf('/');
			if (slash < 0)
			{
				return mediaId;
			}
			return mediaId.Substring(0, slash);
		}

		private static string ParseValue(string mediaId)
		{
			int slash = mediaId.IndexOf('/');
			if (slash < 0)
			{
				return "";
			}
			return mediaId.Substring(slash + 1);
		}

		private static string StripTrackPrefix(string mediaId)
		{
			if (string.IsNullOrEmpty(mediaId))
			{
				return null;
			}
			if (!mediaId.StartsWith("track/"))
			{
				return null;
			}
			return mediaId.Substring("track/".Length);
		}
	}
}
