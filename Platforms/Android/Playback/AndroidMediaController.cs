using System;
using Android.Content;
using Android.Runtime;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using Google.Common.Util.Concurrent;

namespace Thump.Playback
{
	/// <summary>
	/// The minimum boundary between Thump code and the running Media3 session.
	/// Nothing on this class's surface knows about PulseTrack, MainView,
	/// ThumpData, or any other Thump concept — every method takes / returns
	/// Media3 types or primitives. Whoever owns an instance is responsible for
	/// building MediaItems, maintaining any parallel Thump-side queue, and
	/// deciding how (or whether) to poll for state changes. The class only
	/// emits one outbound signal — <see cref="m_onConnected"/> — when the
	/// async bind to the session completes.
	/// </summary>
	public class AndroidMediaController
	{
		/// <summary>Media3 PlaybackState integer values, mirrored so callers can compare without re-reaching into Media3.</summary>
		public const int PlaybackStateIdle = 1;
		public const int PlaybackStateBuffering = 2;
		public const int PlaybackStateReady = 3;
		public const int PlaybackStateEnded = 4;

		/// <summary>Media3 RepeatMode integer values.</summary>
		public const int RepeatModeOff = 0;
		public const int RepeatModeOne = 1;
		public const int RepeatModeAll = 2;

		// The Media3 controller, populated after the async bind in
		// OnControllerConnected. Null before connect, null after Release.
		// Every public method is a no-op (or returns a sentinel) while this
		// is null, so the host doesn't have to gate calls itself.
		private MediaController m_controller;

		// Future returned by MediaController.Builder.BuildAsync. Held so the
		// listener can call .Get on it once Media3 signals readiness.
		private IListenableFuture m_controllerFuture;

		/// <summary>
		/// Optional one-shot callback. Set by the host before or after
		/// construction; the class null-checks before invoking. Fires once on
		/// the main thread when the controller has bound to its session.
		/// </summary>
		public Action m_onConnected;

		/// <summary>
		/// Begins the async bind to the MediaSessionService identified by
		/// <paramref name="serviceType"/> (e.g. typeof(ThumpPlaybackService)).
		/// The constructor returns immediately; the controller is not usable
		/// until <see cref="m_onConnected"/> fires (or <see cref="IsConnected"/>
		/// returns true).
		/// </summary>
		public AndroidMediaController(Type serviceType)
		{
			Context context = Android.App.Application.Context;
			ComponentName componentName = new ComponentName(context, Java.Lang.Class.FromType(serviceType));
			SessionToken token = new SessionToken(context, componentName);
			MediaController.Builder controllerBuilder = new MediaController.Builder(context, token);
			m_controllerFuture = controllerBuilder.BuildAsync();
			m_controllerFuture.AddListener(new Java.Lang.Runnable(OnControllerConnected), AndroidX.Core.Content.ContextCompat.GetMainExecutor(context));
		}

		// Runs on the main thread once BuildAsync has resolved. Captures the
		// controller into m_controller and fires the host's connect callback.
		// Any exception getting the result is logged and swallowed; the
		// controller stays null and every other method becomes a no-op.
		private void OnControllerConnected()
		{
			try
			{
				Java.Lang.Object result = m_controllerFuture.Get();
				m_controller = result.JavaCast<MediaController>();
			}
			catch (System.Exception ex)
			{
				Log.Exception(ex);
				return;
			}
			if (m_onConnected != null)
			{
				m_onConnected();
			}
		}

		/// <summary>True once the async bind has completed and the controller is usable. False before connect and after <see cref="Release"/>.</summary>
		public bool IsConnected()
		{
			return m_controller != null;
		}

		/// <summary>True if playback is currently active (Media3 requires Ready state + playWhenReady + no suppression). False while disconnected.</summary>
		public bool IsPlaying()
		{
			if (m_controller == null)
			{
				return false;
			}
			return m_controller.IsPlaying;
		}

		/// <summary>Media3 PlaybackState integer (see the PlaybackState* constants). Returns 0 while disconnected.</summary>
		public int PlaybackState()
		{
			if (m_controller == null)
			{
				return 0;
			}
			return m_controller.PlaybackState;
		}

		/// <summary>Current playback position in the active item, in milliseconds. Returns 0 while disconnected or when Media3 reports a negative position.</summary>
		public long CurrentPositionMs()
		{
			if (m_controller == null)
			{
				return 0;
			}
			long position = m_controller.CurrentPosition;
			if (position < 0)
			{
				return 0;
			}
			return position;
		}

		/// <summary>Duration of the active item in milliseconds. Returns 0 while disconnected or when Media3 reports an unknown duration (TIME_UNSET).</summary>
		public long DurationMs()
		{
			if (m_controller == null)
			{
				return 0;
			}
			long duration = m_controller.Duration;
			if (duration < 0)
			{
				return 0;
			}
			return duration;
		}

		/// <summary>The MediaItem the player is currently positioned at. Null when disconnected or when the queue is empty.</summary>
		public MediaItem CurrentItem()
		{
			if (m_controller == null)
			{
				return null;
			}
			return m_controller.CurrentMediaItem;
		}

		/// <summary>Index of the current item within the queue. Returns -1 while disconnected.</summary>
		public int CurrentItemIndex()
		{
			if (m_controller == null)
			{
				return -1;
			}
			return m_controller.CurrentMediaItemIndex;
		}

		/// <summary>Number of items in the queue. Returns 0 while disconnected.</summary>
		public int ItemCount()
		{
			if (m_controller == null)
			{
				return 0;
			}
			return m_controller.MediaItemCount;
		}

		/// <summary>Replaces the queue with a single item. Does NOT auto-prepare or play; the host must call <see cref="Prepare"/> (then <see cref="Play"/>) when ready.</summary>
		public void SetSingleItem(MediaItem item)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.SetMediaItem(item);
		}

		/// <summary>Appends an item to the end of the queue. Does not affect playback position. Safe to call while playing.</summary>
		public void AppendItem(MediaItem item)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.AddMediaItem(item);
		}

		/// <summary>Inserts an item at the given index. Items at and after that index shift forward by one.</summary>
		public void InsertItem(int index, MediaItem item)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.AddMediaItem(index, item);
		}

		/// <summary>Empties the queue. Playback stops.</summary>
		public void ClearQueue()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.ClearMediaItems();
		}

		/// <summary>Tells Media3 to prepare the currently loaded queue for playback. Required after a queue mutation before <see cref="Play"/> will succeed.</summary>
		public void Prepare()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.Prepare();
		}

		/// <summary>Starts or resumes playback at the current position.</summary>
		public void Play()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.Play();
		}

		/// <summary>Pauses playback. Position is retained.</summary>
		public void Pause()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.Pause();
		}

		/// <summary>Stops playback and releases prepared player resources. The queue is retained; <see cref="Prepare"/> + <see cref="Play"/> resume from the current item at position 0.</summary>
		public void Stop()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.Stop();
		}

		/// <summary>Seeks within the current item to the given position in milliseconds.</summary>
		public void SeekTo(long positionMs)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.SeekTo(positionMs);
		}

		/// <summary>Jumps to the item at <paramref name="queueIndex"/> and starts at position 0. Does not change play/pause state.</summary>
		public void SeekToItem(int queueIndex)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.SeekTo(queueIndex, 0L);
		}

		/// <summary>Skips to the next item in the queue.</summary>
		public void SkipNext()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.SeekToNextMediaItem();
		}

		/// <summary>Skips to the previous item in the queue.</summary>
		public void SkipPrev()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.SeekToPreviousMediaItem();
		}

		/// <summary>Toggles Media3's queue shuffle mode. When enabled the player walks the queue in a shuffled order without mutating the underlying queue.</summary>
		public void SetShuffleEnabled(bool enabled)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.ShuffleModeEnabled = enabled;
		}

		/// <summary>Sets the repeat mode. Use the RepeatMode* constants on this class.</summary>
		public void SetRepeatMode(int mode)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.RepeatMode = mode;
		}

		/// <summary>Releases the controller and unbinds from the session. After Release, every method is a no-op and <see cref="IsConnected"/> returns false.</summary>
		public void Release()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.Release();
			m_controller = null;
		}
	}
}
