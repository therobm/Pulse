using System.Collections.Generic;
using AndroidX.Concurrent.Futures;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using Google.Common.Util.Concurrent;
using Thump.Data;
using Thump.Platforms;
using Thump.Pulse;

namespace Thump.Playback
{
	public class JObjectCallback
	{
		private CallbackToFutureAdapter.Completer m_completer;
		private bool m_done;


		public JObjectCallback(CallbackToFutureAdapter.Completer completer)
		{
			m_completer = completer;
			m_done = false;
		}

		public void OnComplete<T>(List<PulseTrack> data, eAAObject objectType, string objectId)
		{
			List<MediaItem> items = AAutoHelper.BuildContainerChildren("albumplay/" + objectId, "albumshuffle/" + objectId, data);
			Java.Lang.Object result = LibraryResult.OfItemList(items, AAStyles.BuildContentStyleParams());
			OnComplete(result);
		}

		public void OnComplete(List<MediaItem> data) 
		{
			Java.Lang.Object result = LibraryResult.OfItemList(data, AAStyles.BuildContentStyleParams());
			OnComplete(result);
		}

		public void OnComplete(Java.Lang.Object result)
		{
			if (m_done)
			{
				return;
			}
			m_done = true;
			m_completer.Set(result);
		}
	}

	public class AAutoHelper
	{
		public class LoadContainerFunc : Java.Lang.Object, CallbackToFutureAdapter.IResolver
		{
			private ThumpMediaLibraryService m_owner;
			private string m_parentId;
			private eAADirectory m_parent;

			public LoadContainerFunc(ThumpMediaLibraryService owner, eAADirectory parent, string parentId)
			{
				m_owner = owner;
				m_parentId = parentId;
				m_parent = parent;
			}

			public Java.Lang.Object AttachCompleter(CallbackToFutureAdapter.Completer completer)
			{
				JObjectCallback onComplete = new JObjectCallback(completer);
				m_owner.LoadContainer(m_parent, onComplete);
				return null;
			}
		}
		public class LoadObjectFunc : Java.Lang.Object, CallbackToFutureAdapter.IResolver
		{
			private ThumpMediaLibraryService m_owner;
			private string m_parentId;
			private eAAObject m_object;

			public LoadObjectFunc(ThumpMediaLibraryService owner, eAAObject aaObject, string parentId)
			{
				m_owner = owner;
				m_parentId = parentId;
				m_object = aaObject;
			}

			public Java.Lang.Object AttachCompleter(CallbackToFutureAdapter.Completer completer)
			{
				JObjectCallback onComplete = new JObjectCallback(completer);
				m_owner.LoadObject(m_object, m_parentId, onComplete);
				return null;
			}
		}
		public class LoadMediaSetFunc : Java.Lang.Object, CallbackToFutureAdapter.IResolver
		{
			private ThumpMediaLibraryService m_owner;
			private string m_parentId;
			private IList<MediaItem> m_items;
			private int m_startIndex;
			private long m_startPosition;

			public LoadMediaSetFunc(ThumpMediaLibraryService owner, IList<MediaItem> items, int startIndex, long startPositionMs)
			{
				m_owner = owner;
				m_items = items;
				m_startIndex = startIndex;
				m_startPosition = startPositionMs;
			}

			public Java.Lang.Object AttachCompleter(CallbackToFutureAdapter.Completer completer)
			{
				JObjectCallback onComplete = new JObjectCallback(completer);
				m_owner.LoadMediaItems(m_items, m_startIndex, m_startPosition, onComplete);
				return null;
			}
		}

		public class LoadJavaObjectFunc : Java.Lang.Object, CallbackToFutureAdapter.IResolver
		{
			private Java.Lang.Object m_value;

			public LoadJavaObjectFunc(Java.Lang.Object value)
			{
				m_value = value;
			}

			public Java.Lang.Object AttachCompleter(CallbackToFutureAdapter.Completer completer)
			{
				JObjectCallback guard = new JObjectCallback(completer);
				guard.OnComplete(m_value);
				return null;
			}
		}

		public static List<MediaItem> BuildTrackItems(List<PulseTrack> tracks)
		{
			List<MediaItem> items = new List<MediaItem>();
			if (tracks == null)
			{
				return items;
			}
			for(int i=0;i<tracks.Count;i++)
			{
				PulseTrack track = tracks[i];
				items.Add(BuildPlayableItem("track/" + track.Id, track.Title, track.Artist, track.ImageID));
			}
			return items;
		}

		public static List<MediaItem> BuildContainerChildren(string playMediaId, string shuffleMediaId, List<PulseTrack> tracks)
		{
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
			foreach(PulseObject pulseObject in objects)
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
			string trackId = StripTrackPrefix(mediaId);
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

		public static string ParsePrefix(string mediaId)
		{
			int slash = mediaId.IndexOf('/');
			if (slash < 0)
			{
				return mediaId;
			}
			return mediaId.Substring(0, slash);
		}

		public static string ParseValue(string mediaId)
		{
			int slash = mediaId.IndexOf('/');
			if (slash < 0)
			{
				return "";
			}
			return mediaId.Substring(slash + 1);
		}

		public static string StripTrackPrefix(string mediaId)
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
