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
		private int m_page;
		private int m_pageSize;


		public JObjectCallback(CallbackToFutureAdapter.Completer completer)
			: this(completer, 0, 0)
		{
		}

		public JObjectCallback(CallbackToFutureAdapter.Completer completer, int page, int pageSize)
		{
			m_completer = completer;
			m_done = false;
			m_page = page;
			m_pageSize = pageSize;
		}

		public void OnComplete<T>(List<LegacyPulseTrack> data, eAAObject objectType, string objectId)
		{
			List<MediaItem> items = MediaItemBuilder.BuildContainerChildren(objectType, objectId, data);
			List<MediaItem> sliced = SlicePage(items);
			Java.Lang.Object result = LibraryResult.OfItemList(sliced, MediaItemBuilder.BuildContentStyleParams());
			OnComplete(result);
		}

		public void OnComplete(List<MediaItem> data)
		{
			List<MediaItem> sliced = SlicePage(data);
			Java.Lang.Object result = LibraryResult.OfItemList(sliced, MediaItemBuilder.BuildContentStyleParams());
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

		// Media3 / AA browse is paged: the framework requests children with
		// (page, pageSize) and expects only that slice in the response. AA
		// follows up with page=1, page=2... as the user scrolls. When the
		// host returns the full list anyway, the framework clips to pageSize
		// so the user only ever sees the first page. Slice here so every
		// page sits at the right offset. Callers that don't paginate
		// (root list, the unknown-id fallback) construct us with
		// pageSize <= 0 and the slice becomes a pass-through.
		private List<MediaItem> SlicePage(List<MediaItem> items)
		{
			if (items == null)
			{
				return new List<MediaItem>();
			}
			if (m_pageSize <= 0)
			{
				return items;
			}
			int start = m_page * m_pageSize;
			if (start < 0)
			{
				start = 0;
			}
			if (start >= items.Count)
			{
				return new List<MediaItem>();
			}
			int end = start + m_pageSize;
			if (end > items.Count)
			{
				end = items.Count;
			}
			List<MediaItem> slice = new List<MediaItem>(end - start);
			for (int idx = start; idx < end; idx++)
			{
				slice.Add(items[idx]);
			}
			return slice;
		}
	}

	public class AAutoHelper
	{
		public class LoadContainerFunc : Java.Lang.Object, CallbackToFutureAdapter.IResolver
		{
			private ThumpMediaLibraryService m_owner;
			private string m_parentId;
			private eAADirectory m_parent;
			private int m_page;
			private int m_pageSize;

			public LoadContainerFunc(ThumpMediaLibraryService owner, eAADirectory parent, string parentId, int page, int pageSize)
			{
				m_owner = owner;
				m_parentId = parentId;
				m_parent = parent;
				m_page = page;
				m_pageSize = pageSize;
			}

			public Java.Lang.Object AttachCompleter(CallbackToFutureAdapter.Completer completer)
			{
				JObjectCallback onComplete = new JObjectCallback(completer, m_page, m_pageSize);
				m_owner.LoadContainer(m_parent, onComplete);
				return null;
			}
		}
		public class LoadObjectFunc : Java.Lang.Object, CallbackToFutureAdapter.IResolver
		{
			private ThumpMediaLibraryService m_owner;
			private string m_parentId;
			private eAAObject m_object;
			private int m_page;
			private int m_pageSize;

			public LoadObjectFunc(ThumpMediaLibraryService owner, eAAObject aaObject, string parentId, int page, int pageSize)
			{
				m_owner = owner;
				m_parentId = parentId;
				m_object = aaObject;
				m_page = page;
				m_pageSize = pageSize;
			}

			public Java.Lang.Object AttachCompleter(CallbackToFutureAdapter.Completer completer)
			{
				JObjectCallback onComplete = new JObjectCallback(completer, m_page, m_pageSize);
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
