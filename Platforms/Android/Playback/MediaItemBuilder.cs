using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using System.Collections.Generic;
using Thump.Data;
using Thump.Pulse;

namespace Thump.Playback.AndroidOS
{
	public enum eAADirectory
	{
		Root,
		Home,
		Library,
		Podcasts,
		Albums,
		Playlists,
		Artists,
		Genres,
		RecentlyPlayed,
		RecentlyAdded,
		TopPlaylists,
		PopularArtists,
	}
	public enum eAAObject
	{
		Album,
		Artist,
		Playlist,
		Genre
	}

	public class MediaItemBuilder
	{
		private static Dictionary<eAADirectory, string> m_directories = new Dictionary<eAADirectory, string>()
		{
			{ eAADirectory.Root, "root" },
			{ eAADirectory.Home, "home" },
			{ eAADirectory.Library, "library" },
			{ eAADirectory.Podcasts, "podcasts" },
			{ eAADirectory.Albums, "albums" },
			{ eAADirectory.Playlists, "playlists" },
			{ eAADirectory.Artists, "artists" },
			{ eAADirectory.Genres, "genres" },
			{ eAADirectory.RecentlyPlayed, "home_recent" },
			{ eAADirectory.RecentlyAdded, "home_added" },
			{ eAADirectory.TopPlaylists, "home_top" },
			{ eAADirectory.PopularArtists, "home_popular" },
		};
		private static Dictionary<eAAObject, string> m_objects = new Dictionary<eAAObject, string>()
		{
			{ eAAObject.Album, "album" },
			{ eAAObject.Artist, "artist" },
			{ eAAObject.Playlist, "playlist" },
			{ eAAObject.Genre, "genre" },
		};

		public static MediaLibraryService.LibraryParams BuildContentStyleParams()
		{
			Android.OS.Bundle extras = new Android.OS.Bundle();
			extras.PutInt(MediaConstants.ExtrasKeyContentStyleBrowsable, MediaConstants.ExtrasValueContentStyleGridItem);
			extras.PutInt(MediaConstants.ExtrasKeyContentStylePlayable, MediaConstants.ExtrasValueContentStyleListItem);
			MediaLibraryService.LibraryParams.Builder builder = new MediaLibraryService.LibraryParams.Builder();
			builder.SetExtras(extras);
			return builder.Build();
		}

		public static string GetId(eAADirectory directory)
		{
			string id;
			if (m_directories.TryGetValue(directory, out id))
			{
				return id;
			}
			return null;
		}

		public static bool TryGetDirectory(string id, out eAADirectory directory)
		{
			foreach (KeyValuePair<eAADirectory, string> pair in m_directories)
			{
				if (pair.Value == id)
				{
					directory = pair.Key;
					return true;
				}
			}
			directory = eAADirectory.Root;
			return false;
		}

		public static bool TryGetObject(string mediaId, out eAAObject aaObject)
		{
			int slash = mediaId.IndexOf('/');
			if (slash >= 0)
			{
				mediaId = mediaId.Substring(0, slash);
			}

			foreach (KeyValuePair<eAAObject, string> pair in m_objects)
			{
				if (pair.Value == mediaId)
				{
					aaObject = pair.Key;
					return true;
				}
			}
			aaObject = eAAObject.Album;
			return false;
		}

		// Play/shuffle mediaIds carry the object type in the prefix so the
		// service can route them back to the right ThumpData fetch when AA
		// fires OnSetMediaItems. Shape: "<type>play/<id>" or "<type>shuffle/<id>".
		public static string BuildPlayMediaId(eAAObject objectType, string objectId)
		{
			return m_objects[objectType] + "play/" + objectId;
		}

		public static string BuildShuffleMediaId(eAAObject objectType, string objectId)
		{
			return m_objects[objectType] + "shuffle/" + objectId;
		}

		// Inverse of Build*MediaId. Returns false (and leaves outs at defaults)
		// for anything that isn't an "<type>play/" or "<type>shuffle/" id.
		public static bool TryParsePlayMediaId(string mediaId, out eAAObject objectType, out string objectId, out bool isShuffle)
		{
			objectType = eAAObject.Album;
			objectId = null;
			isShuffle = false;
			if (string.IsNullOrEmpty(mediaId))
			{
				return false;
			}
			int slash = mediaId.IndexOf('/');
			if (slash < 0)
			{
				return false;
			}
			string prefix = mediaId.Substring(0, slash);
			string value = mediaId.Substring(slash + 1);

			foreach (KeyValuePair<eAAObject, string> pair in m_objects)
			{
				if (prefix == pair.Value + "play")
				{
					objectType = pair.Key;
					objectId = value;
					isShuffle = false;
					return true;
				}
				if (prefix == pair.Value + "shuffle")
				{
					objectType = pair.Key;
					objectId = value;
					isShuffle = true;
					return true;
				}
			}
			return false;
		}


		public static List<MediaItem> BuildTrackItems(List<PulseTrack> tracks)
		{
			List<MediaItem> items = new List<MediaItem>();
			if (tracks == null)
			{
				return items;
			}
			for (int i = 0; i < tracks.Count; i++)
			{
				PulseTrack track = tracks[i];
				items.Add(BuildPlayableItem("track/" + track.Id, track.Title, track.Artist, track.ImageID));
			}
			return items;
		}

		public static List<MediaItem> BuildContainerChildren(eAAObject objectType, string objectId, List<PulseTrack> tracks)
		{
			string playMediaId = BuildPlayMediaId(objectType, objectId);
			string shuffleMediaId = BuildShuffleMediaId(objectType, objectId);
			List<MediaItem> items = new List<MediaItem>();
			items.Add(BuildPlayableItem(playMediaId, "Play all", ""));
			items.Add(BuildPlayableItem(shuffleMediaId, "Shuffle", ""));
			List<MediaItem> trackItems = BuildTrackItems(tracks);
			for (int idx = 0; idx < trackItems.Count; idx++)
			{
				items.Add(trackItems[idx]);
			}
			return items;
		}

		public static List<MediaItem> BuildMixedItems(List<PulseObject> objects)
		{
			List<MediaItem> items = new List<MediaItem>();
			if (objects == null)
			{
				return items;
			}
			for (int idx = 0; idx < objects.Count; idx++)
			{
				PulseObject pulseObject = objects[idx];
				switch (pulseObject.Kind)
				{
					case eDataType.Album:
						{
							PulseAlbum album = (PulseAlbum)pulseObject;
							items.Add(BuildAlbumItem("album/" + album.Id, album.Name, album.Artist, album.CoverArt));
							break;
						}
					case eDataType.Artist:
						{
							PulseArtist artist = (PulseArtist)pulseObject;
							items.Add(BuildBrowsableItem("artist/" + artist.Id, artist.Name, artist.CoverArt));
							break;
						}
					case eDataType.Playlist:
						{
							PulsePlaylist playlist = (PulsePlaylist)pulseObject;
							items.Add(BuildBrowsableItem("playlist/" + playlist.Id, playlist.Name, playlist.CoverArt));
							break;
						}
					case eDataType.Track:
						{
							PulseTrack track = (PulseTrack)pulseObject;
							items.Add(BuildPlayableItem("track/" + track.Id, track.Title, track.Artist, track.ImageID));
							break;
						}
				}
			}
			return items;
		}

		public static List<MediaItem> BuildMixedItemsGrouped(IEnumerable<PulseObject> objects, string groupTitle)
		{
			List<MediaItem> items = new List<MediaItem>();
			if (objects == null)
			{
				return items;
			}
			foreach (PulseObject pulseObject in objects)
			{
				switch (pulseObject.Kind)
				{
					case eDataType.Album:
						{
							PulseAlbum album = (PulseAlbum)pulseObject;
							items.Add(BuildAlbumItemGrouped("album/" + album.Id, album.Name, album.Artist, album.CoverArt, groupTitle));
							break;
						}
					case eDataType.Artist:
						{
							PulseArtist artist = (PulseArtist)pulseObject;
							items.Add(BuildBrowsableItemGrouped("artist/" + artist.Id, artist.Name, artist.CoverArt, groupTitle));
							break;
						}
					case eDataType.Playlist:
						{
							PulsePlaylist playlist = (PulsePlaylist)pulseObject;
							items.Add(BuildBrowsableItemGrouped("playlist/" + playlist.Id, playlist.Name, playlist.CoverArt, groupTitle));
							break;
						}
					case eDataType.Track:
						{
							PulseTrack track = (PulseTrack)pulseObject;
							items.Add(BuildPlayableItemGrouped("track/" + track.Id, track.Title, track.Artist, track.ImageID, groupTitle));
							break;
						}
				}
			}
			return items;
		}

		public static Android.OS.Bundle BuildGroupTitleExtras(string groupTitle)
		{
			Android.OS.Bundle extras = new Android.OS.Bundle();
			extras.PutString(MediaConstants.ExtrasKeyContentStyleGroupTitle, groupTitle);
			extras.PutInt(MediaConstants.ExtrasKeyContentStyleSingleItem, MediaConstants.ExtrasValueContentStyleGridItem);
			return extras;
		}

		public static MediaItem BuildBrowsableItemGrouped(string mediaId, string title, string coverArtId, string groupTitle)
		{
			MediaMetadata.Builder metadata = new MediaMetadata.Builder();
			metadata.SetTitle(title);
			metadata.SetIsBrowsable(Java.Lang.Boolean.True);
			metadata.SetIsPlayable(Java.Lang.Boolean.False);
			metadata.SetExtras(BuildGroupTitleExtras(groupTitle));
			Android.Net.Uri artworkUri = BuildArtworkUri(coverArtId);
			if (artworkUri != null)
			{
				metadata.SetArtworkUri(artworkUri);
			}

			MediaItem.Builder builder = new MediaItem.Builder();
			builder.SetMediaId(mediaId);
			builder.SetMediaMetadata(metadata.Build());
			return builder.Build();
		}

		public static MediaItem BuildAlbumItemGrouped(string mediaId, string title, string subtitle, string coverArtId, string groupTitle)
		{
			MediaMetadata.Builder metadata = new MediaMetadata.Builder();
			metadata.SetTitle(title);
			metadata.SetSubtitle(subtitle);
			metadata.SetIsBrowsable(Java.Lang.Boolean.True);
			metadata.SetIsPlayable(Java.Lang.Boolean.False);
			metadata.SetExtras(BuildGroupTitleExtras(groupTitle));
			Android.Net.Uri artworkUri = BuildArtworkUri(coverArtId);
			if (artworkUri != null)
			{
				metadata.SetArtworkUri(artworkUri);
			}

			MediaItem.Builder builder = new MediaItem.Builder();
			builder.SetMediaId(mediaId);
			builder.SetMediaMetadata(metadata.Build());
			return builder.Build();
		}

		public static MediaItem BuildPlayableItemGrouped(string mediaId, string title, string subtitle, string coverArtId, string groupTitle)
		{
			MediaMetadata.Builder metadata = new MediaMetadata.Builder();
			metadata.SetTitle(title);
			metadata.SetArtist(subtitle);
			metadata.SetIsBrowsable(Java.Lang.Boolean.False);
			metadata.SetIsPlayable(Java.Lang.Boolean.True);
			metadata.SetExtras(BuildGroupTitleExtras(groupTitle));
			Android.Net.Uri artworkUri = BuildArtworkUri(coverArtId);
			if (artworkUri != null)
			{
				metadata.SetArtworkUri(artworkUri);
			}

			MediaItem.Builder builder = new MediaItem.Builder();
			builder.SetMediaId(mediaId);
			builder.SetMediaMetadata(metadata.Build());
			return builder.Build();
		}

		public static MediaItem BuildItemForId(string mediaId)
		{
			string trackId = AAutoHelper.StripTrackPrefix(mediaId);
			if (!string.IsNullOrEmpty(trackId))
			{
				return BuildPlayableItem(mediaId, mediaId, "");
			}
			return BuildBrowsableItem(mediaId, mediaId);
		}

		public static MediaItem BuildBrowsableItem(string mediaId, string title)
		{
			return BuildBrowsableItem(mediaId, title, null);
		}

		public static MediaItem BuildBrowsableItem(string mediaId, string title, string coverArtId)
		{
			MediaMetadata.Builder metadata = new MediaMetadata.Builder();
			metadata.SetTitle(title);
			metadata.SetIsBrowsable(Java.Lang.Boolean.True);
			metadata.SetIsPlayable(Java.Lang.Boolean.False);
			Android.Net.Uri artworkUri = BuildArtworkUri(coverArtId);
			if (artworkUri != null)
			{
				metadata.SetArtworkUri(artworkUri);
			}

			MediaItem.Builder builder = new MediaItem.Builder();
			builder.SetMediaId(mediaId);
			builder.SetMediaMetadata(metadata.Build());
			return builder.Build();
		}

		public static MediaItem BuildAlbumItem(string mediaId, string title, string subtitle, string coverArtId)
		{
			MediaMetadata.Builder metadata = new MediaMetadata.Builder();
			metadata.SetTitle(title);
			metadata.SetSubtitle(subtitle);
			metadata.SetIsBrowsable(Java.Lang.Boolean.True);
			metadata.SetIsPlayable(Java.Lang.Boolean.False);
			Android.Net.Uri artworkUri = BuildArtworkUri(coverArtId);
			if (artworkUri != null)
			{
				metadata.SetArtworkUri(artworkUri);
			}

			MediaItem.Builder builder = new MediaItem.Builder();
			builder.SetMediaId(mediaId);
			builder.SetMediaMetadata(metadata.Build());
			return builder.Build();
		}

		public static MediaItem BuildPlayableItem(string mediaId, string title, string subtitle)
		{
			return BuildPlayableItem(mediaId, title, subtitle, null);
		}

		public static MediaItem BuildPlayableItem(string mediaId, string title, string subtitle, string coverArtId)
		{
			MediaMetadata.Builder metadata = new MediaMetadata.Builder();
			metadata.SetTitle(title);
			metadata.SetArtist(subtitle);
			metadata.SetIsBrowsable(Java.Lang.Boolean.False);
			metadata.SetIsPlayable(Java.Lang.Boolean.True);
			Android.Net.Uri artworkUri = BuildArtworkUri(coverArtId);
			if (artworkUri != null)
			{
				metadata.SetArtworkUri(artworkUri);
			}

			MediaItem.Builder builder = new MediaItem.Builder();
			builder.SetMediaId(mediaId);
			builder.SetMediaMetadata(metadata.Build());
			return builder.Build();
		}

		public static Android.Net.Uri BuildArtworkUri(string coverArtId)
		{
			if (string.IsNullOrEmpty(coverArtId))
			{
				return null;
			}
			return Android.Net.Uri.Parse("content://com.therobm.thump.coverart/" + Android.Net.Uri.Encode(coverArtId));
		}

		public static MediaItem Build(PulseTrack track)
		{
			MediaMetadata.Builder metadata = new MediaMetadata.Builder();
			metadata.SetTitle(track.Title);
			metadata.SetArtist(track.Artist);
			if (!string.IsNullOrEmpty(track.Album))
			{
				metadata.SetAlbumTitle(track.Album);
			}

			Android.Net.Uri uri = GetURI(track.Id);
			MediaItem.Builder builder = new MediaItem.Builder();
			builder.SetMediaId(track.Id);
			builder.SetUri(uri);
			builder.SetMediaMetadata(metadata.Build());
			return builder.Build();
		}

		public static Android.Net.Uri GetURI(string id)
		{
			Android.Net.Uri uri = Android.Net.Uri.Parse("thump://" + id);
			return uri;
		}
	}
}
