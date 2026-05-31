using System;
using Android.Content;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using Google.Common.Util.Concurrent;

namespace Thump.Playback
{
	// =============================================================================
	// AndroidMediaController
	//
	// The minimum boundary between Thump code and Media3 / ExoPlayer. Everything
	// on this class's surface speaks in Media3 types (MediaItem, ints, longs);
	// nothing here knows about PulseTrack, PulsePlaylist, MainView, ThumpData,
	// or any other Thump concept. The class:
	//
	//   1. Bootstraps an AndroidX.Media3.Session.MediaController bound to a
	//      named MediaSessionService (the host passes the service Type).
	//   2. Exposes the slice of MediaController operations the rest of Thump
	//      actually uses (load / play / pause / seek / append / insert / mode).
	//   3. Reports state through read-only getters (poll) plus one optional
	//      Action that fires when the controller has bound to the session.
	//
	// What it deliberately does NOT do:
	//   - Convert Thump types to MediaItems. The host builds MediaItems.
	//   - Track a parallel queue of Thump objects. The MediaController already
	//     holds the queue; the host can keep its own parallel structure if it
	//     needs to map MediaItem.MediaId back to a Thump entity.
	//   - Tick / poll on a timer. The host decides if and how to poll the
	//     getters or wire up a Media3 listener.
	//   - Fire "track changed" / "playback state changed" / "ended" signals.
	//     Same reason: those are derivations on top of the getters, and the
	//     host owns the derivation policy.
	//
	// The only outbound signal is `m_onConnected`. Everything else is pull.
	// =============================================================================
	public class AndroidMediaController
	{
		// Media3 PlaybackState integer values, surfaced so callers can compare
		// against the result of PlaybackState() without re-reaching into Media3.
		// Mirrors the values returned by IPlayer.PlaybackState.
		public const int PlaybackStateIdle = 1;
		public const int PlaybackStateBuffering = 2;
		public const int PlaybackStateReady = 3;
		public const int PlaybackStateEnded = 4;

		// Media3 RepeatMode integer values.
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

		// Optional one-shot callback. Set by the host before or after
		// construction; the class null-checks before invoking. Fires once on
		// the main thread when the controller has bound to its session.
		// If construction completes after the host already sets this, the call
		// still fires (it runs from OnControllerConnected, which executes after
		// BuildAsync resolves).
		public Action m_onConnected;

		// ---------------------------------------------------------------------
		// Construction
		// ---------------------------------------------------------------------

		// Begins the async bind to the MediaSessionService identified by
		// `serviceType` (e.g. typeof(ThumpPlaybackService)). The constructor
		// returns immediately; the controller is not usable until
		// `m_onConnected` fires (or IsConnected() returns true).
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

		// ---------------------------------------------------------------------
		// State (read-only getters)
		// ---------------------------------------------------------------------

		// True once the async bind has completed and the controller is usable.
		// False before connect and after Release. Every other getter and every
		// mutator returns / no-ops sensibly while disconnected, so callers can
		// skip this guard if they don't care to distinguish.
		public bool IsConnected()
		{
			return m_controller != null;
		}

		// True if the controller currently has playback active (Media3 considers
		// "playing" to require Ready state + playWhenReady + no suppression).
		// Returns false while disconnected.
		public bool IsPlaying()
		{
			if (m_controller == null)
			{
				return false;
			}
			return m_controller.IsPlaying;
		}

		// Media3 PlaybackState integer (see the PlaybackState* constants above).
		// Returns 0 while disconnected — 0 isn't a defined Media3 value, so the
		// host can treat it as "unknown / not yet bound."
		public int PlaybackState()
		{
			if (m_controller == null)
			{
				return 0;
			}
			return m_controller.PlaybackState;
		}

		// Current playback position in the active item, in milliseconds.
		// Returns 0 while disconnected.
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

		// Duration of the active item in milliseconds. Returns 0 while
		// disconnected or while Media3 reports an unknown duration
		// (the player gives -1 / TIME_UNSET in those cases — normalized to 0).
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

		// The MediaItem the player is currently positioned at. Null when
		// disconnected or when the queue is empty.
		public MediaItem CurrentItem()
		{
			if (m_controller == null)
			{
				return null;
			}
			return m_controller.CurrentMediaItem;
		}

		// Index of the current item within the queue. Returns -1 while
		// disconnected.
		public int CurrentItemIndex()
		{
			if (m_controller == null)
			{
				return -1;
			}
			return m_controller.CurrentMediaItemIndex;
		}

		// Number of items in the queue. Returns 0 while disconnected.
		public int ItemCount()
		{
			if (m_controller == null)
			{
				return 0;
			}
			return m_controller.MediaItemCount;
		}

		// ---------------------------------------------------------------------
		// Queue mutation
		// ---------------------------------------------------------------------

		// Replaces the queue with a single item. Does NOT auto-prepare or play;
		// the host must call Prepare() (and then Play()) when ready.
		public void SetSingleItem(MediaItem item)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.SetMediaItem(item);
		}

		// Appends an item to the end of the queue. Does not affect playback
		// position. Safe to call while playing.
		public void AppendItem(MediaItem item)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.AddMediaItem(item);
		}

		// Inserts an item at the given index. Items at and after that index
		// shift forward by one. Safe to call while playing; if `index` is the
		// current position, behaviour follows Media3's documented semantics
		// for AddMediaItem(int, MediaItem).
		public void InsertItem(int index, MediaItem item)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.AddMediaItem(index, item);
		}

		// Empties the queue. Playback stops.
		public void ClearQueue()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.ClearMediaItems();
		}

		// ---------------------------------------------------------------------
		// Playback control
		// ---------------------------------------------------------------------

		// Tells Media3 to prepare the currently loaded queue for playback.
		// Required after SetSingleItem / AppendItem / InsertItem before Play()
		// will succeed.
		public void Prepare()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.Prepare();
		}

		// Starts or resumes playback at the current position.
		public void Play()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.Play();
		}

		// Pauses playback. Position is retained.
		public void Pause()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.Pause();
		}

		// Stops playback and releases the prepared player resources. The
		// queue is retained; Prepare() + Play() will resume from the current
		// item at position 0.
		public void Stop()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.Stop();
		}

		// Seeks within the current item to the given position.
		public void SeekTo(long positionMs)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.SeekTo(positionMs);
		}

		// Jumps to the item at `queueIndex` and starts at position 0.
		// Does not change play/pause state on its own.
		public void SeekToItem(int queueIndex)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.SeekTo(queueIndex, 0L);
		}

		// Skips to the next item in the queue.
		public void SkipNext()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.SeekToNextMediaItem();
		}

		// Skips to the previous item in the queue.
		public void SkipPrev()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.SeekToPreviousMediaItem();
		}

		// ---------------------------------------------------------------------
		// Modes
		// ---------------------------------------------------------------------

		// Toggles Media3's queue shuffle mode. When enabled the player walks
		// the queue in a shuffled order without mutating the underlying queue.
		public void SetShuffleEnabled(bool enabled)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.ShuffleModeEnabled = enabled;
		}

		// Sets the repeat mode. Use the RepeatMode* constants on this class.
		public void SetRepeatMode(int mode)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.RepeatMode = mode;
		}

		// ---------------------------------------------------------------------
		// Lifecycle
		// ---------------------------------------------------------------------

		// Releases the controller and lets Media3 unbind from the session.
		// After Release, every method is a no-op and IsConnected() returns
		// false. Must be called by whoever owns this instance.
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
