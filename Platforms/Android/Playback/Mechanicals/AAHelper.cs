using System.Collections.Generic;
using AndroidX.Concurrent.Futures;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using Thump.Pulse;

namespace Thump.Playback.AndroidOS
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
			List<MediaItem> items = MediaItemBuilder.BuildContainerChildren("albumplay/" + objectId, "albumshuffle/" + objectId, data);
			Java.Lang.Object result = LibraryResult.OfItemList(items, MediaItemBuilder.BuildContentStyleParams());
			OnComplete(result);
		}

		public void OnComplete(List<MediaItem> data) 
		{
			Java.Lang.Object result = LibraryResult.OfItemList(data, MediaItemBuilder.BuildContentStyleParams());
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
