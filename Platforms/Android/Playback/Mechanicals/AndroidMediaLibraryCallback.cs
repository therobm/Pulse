using System;
using System.Collections.Generic;
using AndroidX.Concurrent.Futures;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using Google.Common.Util.Concurrent;

namespace Thump.Playback.AndroidOS
{
	/// <summary>
	/// The minimum boundary between Thump code and the Media3 session callback
	/// surface (<see cref="MediaLibraryService.MediaLibrarySession.ICallback"/>).
	/// Media3 calls into this class for every browse / item / queue operation
	/// it needs the app to fulfill; the class doesn't know anything Thump-side,
	/// it just forwards each callback to a pluggable <c>Func</c> or
	/// <c>Action</c> field. The host wires up whatever fields it cares about,
	/// passes the instance to <c>MediaLibrarySession.Builder</c>, and answers
	/// the requests on its own terms.
	///
	/// Any unset field falls back to a benign default (empty list, "not
	/// supported" result, etc.) so the host can wire up only the callbacks
	/// it actually serves.
	/// </summary>
	public class AndroidMediaLibraryCallback : Java.Lang.Object, MediaLibraryService.MediaLibrarySession.ICallback
	{
		// Media3 result code constants exposed so callers can compare without
		// re-reaching into the framework. These are the values Media3's
		// LibraryResult uses for error reporting.
		public const int ResultSuccess = 0;
		public const int ResultErrorNotSupported = -6;
		public const int ResultErrorBadValue = -3;

		/// <summary>Fired when a controller connects. Host returns the granted command set or null to accept the framework defaults.</summary>
		public Func<MediaSession, MediaSession.ControllerInfo, MediaSession.ConnectionResult> m_onConnect;

		/// <summary>Fired after the controller's initial commands have been delivered. Hook for one-off "controller is fully attached" work.</summary>
		public Action<MediaSession, MediaSession.ControllerInfo> m_onPostConnect;

		/// <summary>Fired when a controller disconnects. The host inspects <c>controller</c> to decide whether to pause, save state, etc.</summary>
		public Action<MediaSession, MediaSession.ControllerInfo> m_onDisconnected;

		/// <summary>Fired before a player command runs. Return <see cref="ResultSuccess"/> to allow, an error code to block.</summary>
		public Func<MediaSession, MediaSession.ControllerInfo, int, int> m_onPlayerCommandRequest;

		/// <summary>Fired after the player has finished processing a command.</summary>
		public Action<MediaSession, MediaSession.ControllerInfo, PlayerCommands> m_onPlayerInteractionFinished;

		/// <summary>
		/// Fired when Media3 wants the app to resume a previously persisted
		/// playback session. Host returns the queue + start position, or
		/// returns null to decline.
		/// </summary>
		public Func<MediaSession, MediaSession.ControllerInfo, bool, MediaSession.MediaItemsWithStartPosition> m_onPlaybackResumption;

		/// <summary>Fired when a browser asks for the library root. Host returns the root MediaItem.</summary>
		public Func<MediaLibraryService.MediaLibrarySession, MediaSession.ControllerInfo, MediaLibraryService.LibraryParams, MediaItem> m_onGetLibraryRoot;

		/// <summary>Fired when a browser asks for a specific item by id. Host returns the MediaItem or null.</summary>
		public Func<MediaLibraryService.MediaLibrarySession, MediaSession.ControllerInfo, string, MediaItem> m_onGetItem;

		/// <summary>
		/// Fired when a browser wants the children of a parent node. Host
		/// receives the parentId and the framework's paging params and returns
		/// the children synchronously. Async fetches need to block here or
		/// return an empty list and push later via a different mechanism.
		/// </summary>
		public Func<MediaLibraryService.MediaLibrarySession, MediaSession.ControllerInfo, string, int, int, MediaLibraryService.LibraryParams, IListenableFuture> m_onGetChildren;

		/// <summary>Fired when a browser subscribes to a parent node. Most hosts can ignore this.</summary>
		public Action<MediaLibraryService.MediaLibrarySession, MediaSession.ControllerInfo, string, MediaLibraryService.LibraryParams> m_onSubscribe;

		/// <summary>Fired when a browser unsubscribes from a parent node.</summary>
		public Action<MediaLibraryService.MediaLibrarySession, MediaSession.ControllerInfo, string> m_onUnsubscribe;

		/// <summary>
		/// Fired when a controller asks to append items to the queue. Host
		/// returns the resolved (URI-attached) items synchronously. The
		/// framework appends whatever the host hands back.
		/// </summary>
		public Func<MediaSession, MediaSession.ControllerInfo, IList<MediaItem>, IList<MediaItem>> m_onAddMediaItems;

		/// <summary>
		/// Fired when a controller asks to replace the queue. Host returns the
		/// resolved items along with the start index and start position the
		/// framework should jump to.
		/// </summary>
		public Func<MediaSession, MediaSession.ControllerInfo, IList<MediaItem>, int, long, IListenableFuture> m_onSetMediaItems;

		public MediaSession.ConnectionResult OnConnect(MediaSession session, MediaSession.ControllerInfo controller)
		{
			if (m_onConnect != null)
			{
				MediaSession.ConnectionResult result = m_onConnect(session, controller);
				if (result != null)
				{
					return result;
				}
			}
			return MediaSession.ConnectionResult.Accept(MediaSession.ConnectionResult.DefaultSessionAndLibraryCommands, MediaSession.ConnectionResult.DefaultPlayerCommands);
		}

		public void OnPostConnect(MediaSession session, MediaSession.ControllerInfo controller)
		{
			if (m_onPostConnect != null)
			{
				m_onPostConnect(session, controller);
			}
		}

		public void OnDisconnected(MediaSession session, MediaSession.ControllerInfo controller)
		{
			if (m_onDisconnected != null)
			{
				m_onDisconnected(session, controller);
			}
		}

		public int OnPlayerCommandRequest(MediaSession session, MediaSession.ControllerInfo controller, int playerCommand)
		{
			if (m_onPlayerCommandRequest != null)
			{
				return m_onPlayerCommandRequest(session, controller, playerCommand);
			}
			return ResultSuccess;
		}

		public void OnPlayerInteractionFinished(MediaSession session, MediaSession.ControllerInfo controller, PlayerCommands playerCommands)
		{
			if (m_onPlayerInteractionFinished != null)
			{
				m_onPlayerInteractionFinished(session, controller, playerCommands);
			}
		}

		public IListenableFuture OnPlaybackResumption(MediaSession session, MediaSession.ControllerInfo controller, bool isForPlayback)
		{
			if (m_onPlaybackResumption != null)
			{
				MediaSession.MediaItemsWithStartPosition resumption = m_onPlaybackResumption(session, controller, isForPlayback);
				if (resumption != null)
				{
					return ImmediateFuture(resumption);
				}
			}
			return FailedFuture("Playback resumption not supported.");
		}

		public IListenableFuture OnGetLibraryRoot(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, MediaLibraryService.LibraryParams libraryParams)
		{
			if (m_onGetLibraryRoot != null)
			{
				MediaItem root = m_onGetLibraryRoot(session, browser, libraryParams);
				if (root != null)
				{
					return ImmediateFuture(LibraryResult.OfItem(root, libraryParams));
				}
			}
			return ImmediateFuture(LibraryResult.OfError(ResultErrorNotSupported));
		}

		public IListenableFuture OnGetItem(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, string mediaId)
		{
			if (m_onGetItem != null)
			{
				MediaItem item = m_onGetItem(session, browser, mediaId);
				if (item != null)
				{
					return ImmediateFuture(LibraryResult.OfItem(item, null));
				}
			}
			return ImmediateFuture(LibraryResult.OfError(ResultErrorBadValue));
		}

		public IListenableFuture OnSubscribe(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, string parentId, MediaLibraryService.LibraryParams libraryParams)
		{
			if (m_onSubscribe != null)
			{
				m_onSubscribe(session, browser, parentId, libraryParams);
			}
			return ImmediateFuture(LibraryResult.OfVoid(libraryParams));
		}

		public IListenableFuture OnUnsubscribe(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, string parentId)
		{
			if (m_onUnsubscribe != null)
			{
				m_onUnsubscribe(session, browser, parentId);
			}
			return ImmediateFuture(LibraryResult.OfVoid(null));
		}

		public IListenableFuture OnGetChildren(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, string parentId, int page, int pageSize, MediaLibraryService.LibraryParams libraryParams)
		{
			IList<MediaItem> children;
			if (m_onGetChildren != null)
			{
				return m_onGetChildren(session, browser, parentId, page, pageSize, libraryParams);
			}
			else
			{
				children = new List<MediaItem>();
			}
			if (children == null)
			{
				children = new List<MediaItem>();
			}
			return ImmediateFuture(LibraryResult.OfItemList(children, libraryParams));
		}

		public IListenableFuture OnAddMediaItems(MediaSession session, MediaSession.ControllerInfo controller, IList<MediaItem> mediaItems)
		{
			IList<MediaItem> resolved;
			if (m_onAddMediaItems != null)
			{
				resolved = m_onAddMediaItems(session, controller, mediaItems);
			}
			else
			{
				resolved = mediaItems;
			}
			if (resolved == null)
			{
				resolved = new List<MediaItem>();
			}
			Java.Util.ArrayList resolvedList = new Java.Util.ArrayList();
			for (int idx = 0; idx < resolved.Count; idx++)
			{
				resolvedList.Add(resolved[idx]);
			}
			return ImmediateFuture(resolvedList);
		}

		public IListenableFuture OnSetMediaItems(MediaSession session, MediaSession.ControllerInfo controller, IList<MediaItem> mediaItems, int startIndex, long startPositionMs)
		{
			if (m_onSetMediaItems != null)
			{
				return m_onSetMediaItems(session, controller, mediaItems, startIndex, startPositionMs);
			}
			return ImmediateFuture(new MediaSession.MediaItemsWithStartPosition(new List<MediaItem>(), startIndex, startPositionMs));
		}

		// Wraps a synchronously-available value as a resolved IListenableFuture.
		// Used for every "no host callback / sync host callback" return path.
		private static IListenableFuture ImmediateFuture(Java.Lang.Object value)
		{
			ImmediateResolver resolver = new ImmediateResolver(value);
			return (IListenableFuture)CallbackToFutureAdapter.GetFuture(resolver);
		}

		// Returns an IListenableFuture that completes with an exception.
		// Used for "this callback isn't wired up so refuse the request" paths.
		private static IListenableFuture FailedFuture(string message)
		{
			FailedResolver resolver = new FailedResolver(message);
			return (IListenableFuture)CallbackToFutureAdapter.GetFuture(resolver);
		}

		private sealed class ImmediateResolver : Java.Lang.Object, CallbackToFutureAdapter.IResolver
		{
			private readonly Java.Lang.Object m_value;

			public ImmediateResolver(Java.Lang.Object value)
			{
				m_value = value;
			}

			public Java.Lang.Object AttachCompleter(CallbackToFutureAdapter.Completer completer)
			{
				completer.Set(m_value);
				return null;
			}
		}

		private sealed class FailedResolver : Java.Lang.Object, CallbackToFutureAdapter.IResolver
		{
			private readonly string m_message;

			public FailedResolver(string message)
			{
				m_message = message;
			}

			public Java.Lang.Object AttachCompleter(CallbackToFutureAdapter.Completer completer)
			{
				completer.SetException(new Java.Lang.UnsupportedOperationException(m_message));
				return null;
			}
		}
	}
}
