using System.Collections.Generic;
using System.Threading.Tasks;
using System.Timers;
using Android.Content;
using Android.Runtime;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using Microsoft.Maui.ApplicationModel;
using PulseAPI.CSharp;
using Thump.Data;
using Thump.Pulse;

namespace Thump.Playback.AndroidOS
{
	public class ThumpAndroidPlayer : IMediaPlayer
	{
		private const int s_stateIdle = 1;
		private const int s_stateBuffering = 2;
		private const int s_stateReady = 3;
		private const int s_stateEnded = 4;
		private const int s_repeatOff = 0;
		private const int s_repeatOne = 1;
		private const int s_repeatAll = 2;
		private const double s_tickIntervalMs = 500;

		private MainView m_mainView;
		private MediaController m_controller;
		private Google.Common.Util.Concurrent.IListenableFuture m_onControllerConnected;
		private Timer m_ticker;

		private List<PulseTrack> m_queue = new List<PulseTrack>();
	

		/// <summary>
		/// A callback identifier used to discard late arrivals
		/// when the queue has been changed/reset.
		/// </summary>
		private int m_currentQueueID;

		private ePlaybackState m_lastState = ePlaybackState.Idle;
		private eRepeatMode m_repeatMode = eRepeatMode.Off;
		private string m_lastMediaId;
		private bool m_pendingPlay;
		private bool m_endHandled;
		private bool m_shuffleEnabled;
		private MediaClient m_data;

		private Queue<PulseTrack> m_cacheQueue = new Queue<PulseTrack>();

		public ThumpAndroidPlayer(MainView mainView, MediaClient thumpData)
		{
			m_mainView = mainView;
			m_data = thumpData;

			ThumpMediaLibraryService.s_mediaClient = m_data;

			Context context = Android.App.Application.Context;
			ComponentName componentName = new ComponentName(context, Java.Lang.Class.FromType(typeof(ThumpMediaLibraryService)));
			SessionToken token = new SessionToken(context, componentName);
			MediaController.Builder controllerBuilder = new MediaController.Builder(context, token);
			m_onControllerConnected = controllerBuilder.BuildAsync();
			m_onControllerConnected.AddListener(new Java.Lang.Runnable(OnControllerConnected), AndroidX.Core.Content.ContextCompat.GetMainExecutor(context));

			m_ticker = new Timer(s_tickIntervalMs);
			m_ticker.AutoReset = true;
			m_ticker.Elapsed += OnTickerElapsed;
		}

		private void OnControllerConnected()
		{
			try
			{
				Java.Lang.Object androidControllerObject = m_onControllerConnected.Get();
				m_controller = androidControllerObject.JavaCast<MediaController>();
			}
			catch (System.Exception ex)
			{
				Log.Exception(ex);
				return;
			}
			ApplyShuffleMode();
			ApplyRepeatMode();
			if (m_pendingPlay)
			{
				m_pendingPlay = false;
				StartQueue();
			}
		}

		public void Play(List<PulseTrack> tracks, int startIndex)
		{
			if (tracks == null || tracks.Count == 0)
			{
				return;
			}
			m_queue = tracks;
			m_currentQueueID = m_currentQueueID + 1;
			m_endHandled = false;
			m_lastMediaId = null;

			if (m_controller == null)
			{
				m_pendingPlay = true;
				return;
			}
			StartQueue(startIndex);
		}

		private void StartQueue(int startIndex = 0)
		{
			if (startIndex < 0 || startIndex >= m_queue.Count)
			{
				return;
			}
			int taskQueueID = m_currentQueueID;
			m_controller.ClearMediaItems();

			PulseTrack startTrack = m_queue[startIndex];
			bool isOnline = m_data.IsOnline();

			m_cacheQueue.Clear();

			//build playlist
			int startItemIndex = 0;
			int builtCount = 0;

			
			for (int i = 0; i < m_queue.Count; i++)
			{
				//If we're not online and we don't have this track locally cached skip it
				if (!isOnline && !m_data.IsTrackCached(m_queue[i].Id))
					continue;

				if (i == startIndex)
				{
					startItemIndex = builtCount;
					startTrack = m_queue[i];
				}
				else
					m_cacheQueue.Enqueue(m_queue[i]);


				MediaItem item = MediaItemBuilder.Build(m_queue[i]);
				m_controller.AddMediaItem(item);
				builtCount++;
			}

			//some other queue has been started, we'll just bail on this
			if (m_currentQueueID != taskQueueID)
				return;

			//kick off caching and start when startIndex is ready

			//player can start playing
			m_controller.Prepare();
			m_controller.Play();
			m_ticker.Start();

			m_lastMediaId = startTrack.Id;
			m_mainView.OnCurrentTrackChanged(startTrack);
			m_controller.SeekTo(startItemIndex, 0);

			//kick of cache requests for the rest
			Task.Delay(5000).ContinueWith((_) => CacheQueued(taskQueueID));
			
		}

		private void CacheQueued(int queueID)
		{
			if (m_cacheQueue == null || m_cacheQueue.Count <= 0 || m_currentQueueID != queueID)
			{
				Log.Info("ThumpPlayer: Cancelled existing download queue for replacement");
				return;
			}

			PulseTrack nextTrack = m_cacheQueue.Dequeue();

			m_data.CacheTrackAudio(nextTrack.Id, (success)=>
			{
				CacheQueued(queueID);
			});
		}

		public void Pause()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.Pause();
			m_ticker.Stop();
		}

		public void Resume()
		{
			if (m_controller == null)
			{
				return;
			}
			m_endHandled = false;
			m_controller.Play();
			m_ticker.Start();
		}

		public void SeekTo(long positionMilliseconds)
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.SeekTo(positionMilliseconds);
		}

		// Seek relative to the current position, clamped to [0, duration]. Used by
		// the in-app series skip buttons: SeekTo is an always-available command, so
		// this avoids the controller-command authorization that blocks
		// SeekToNextMediaItem on a one-item queue.
		public void SeekRelative(long deltaMilliseconds)
		{
			if (m_controller == null)
			{
				return;
			}
			long position = m_controller.CurrentPosition + deltaMilliseconds;
			if (position < 0)
			{
				position = 0;
			}
			long duration = m_controller.Duration;
			if (duration > 0 && position > duration)
			{
				position = duration;
			}
			m_controller.SeekTo(position);
		}

		public void Next()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.SeekToNextMediaItem();
		}

		public void Previous()
		{
			if (m_controller == null)
			{
				return;
			}
			m_controller.SeekToPreviousMediaItem();
		}

		public void SetShuffleEnabled(bool enabled)
		{
			m_shuffleEnabled = enabled;
			if (m_controller == null)
			{
				return;
			}
			ApplyShuffleMode();
		}

		private void ApplyShuffleMode()
		{
			m_controller.ShuffleModeEnabled = m_shuffleEnabled;
		}

		public void SetRepeatMode(eRepeatMode mode)
		{
			m_repeatMode = mode;
			if (m_controller == null)
			{
				return;
			}
			ApplyRepeatMode();
		}

		private void ApplyRepeatMode()
		{
			int mapped;
			if (m_repeatMode == eRepeatMode.One)
			{
				mapped = s_repeatOne;
			}
			else if (m_repeatMode == eRepeatMode.All)
			{
				mapped = s_repeatAll;
			}
			else
			{
				mapped = s_repeatOff;
			}
			m_controller.RepeatMode = mapped;
		}

		public void AddToQueue(List<PulseTrack> tracks)
		{
			if (m_controller == null || tracks == null || tracks.Count == 0)
			{
				return;
			}
			int generation = m_currentQueueID;
			AppendQueueItem(tracks, 0, generation);
			m_queue.AddRange(tracks);
		}

		private void AppendQueueItem(List<PulseTrack> tracks, int index, int queueID)
		{
			if (queueID != m_currentQueueID || index >= tracks.Count)
			{
				return;
			}
			PulseTrack track = tracks[index];
			m_data.CacheTrackAudio(track.Id, (isAvailable) =>
			{
				//ditch stale callbacks
				if (queueID != m_currentQueueID)
				{
					return;
				}
				if (isAvailable)
				{
					m_controller.AddMediaItem(MediaItemBuilder.Build(track));
				}
				AppendQueueItem(tracks, index + 1, queueID);
			});
		}

		public void PlayNext(List<PulseTrack> tracks)
		{
			if (m_controller == null || tracks == null || tracks.Count == 0)
			{
				return;
			}
			int generation = m_currentQueueID;
			int insertAt = m_controller.CurrentMediaItemIndex + 1;
			int count = m_controller.MediaItemCount;
			if (insertAt > count)
			{
				insertAt = count;
			}
			InsertQueueItem(tracks, 0, insertAt, generation);
			m_queue.InsertRange(insertAt, tracks);
		}

		private void InsertQueueItem(List<PulseTrack> tracks, int index, int insertAt, int queueID)
		{
			if (queueID != m_currentQueueID || index >= tracks.Count)
			{
				return;
			}
			PulseTrack track = tracks[index];
			m_data.CacheTrackAudio(track.Id, (isAvailable) =>
			{
				//ditch stale callbacks
				if (queueID != m_currentQueueID)
				{
					return;
				}
				if (isAvailable)
				{
					m_controller.AddMediaItem(insertAt, MediaItemBuilder.Build(track));
				}
				InsertQueueItem(tracks, index + 1, insertAt + 1, queueID);
			});
		}

		public void SeekToQueueItem(int index)
		{
			if (m_controller == null || index < 0)
			{
				return;
			}
			m_endHandled = false;
			m_controller.SeekTo(index, 0L);
			m_controller.Play();
			m_ticker.Start();
		}

		public void Stop()
		{
			m_ticker.Stop();
			if (m_controller == null)
			{
				return;
			}
			m_controller.Stop();
			ReportState(ePlaybackState.Idle);
		}

		public void Release()
		{
			m_ticker.Stop();
			if (m_controller != null)
			{
				m_controller.Release();
				m_controller = null;
			}
		}

		private void OnTickerElapsed(object sender, ElapsedEventArgs e)
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				Tick();
			});
		}

		private void Tick()
		{
			if (m_controller == null)
			{
				return;
			}

			int playbackState = m_controller.PlaybackState;
			bool isPlaying = m_controller.IsPlaying;

			ePlaybackState mapped;
			if (playbackState == s_stateEnded)
			{
				mapped = ePlaybackState.Ended;
			}
			else if (playbackState == s_stateBuffering)
			{
				mapped = ePlaybackState.Buffering;
			}
			else if (playbackState == s_stateReady)
			{
				if (isPlaying)
				{
					mapped = ePlaybackState.Playing;
				}
				else
				{
					mapped = ePlaybackState.Paused;
				}
			}
			else
			{
				mapped = ePlaybackState.Idle;
			}
			ReportState(mapped);

			long position = m_controller.CurrentPosition;
			long duration = m_controller.Duration;
			if (position < 0)
			{
				position = 0;
			}
			if (duration < 0)
			{
				duration = 0;
			}
			m_mainView.OnPlaybackPositionChanged(position, duration);

			DetectTrackChange();

			if (playbackState == s_stateEnded)
			{
				HandleQueueEnded();
			}
		}

		private void DetectTrackChange()
		{
			MediaItem current = m_controller.CurrentMediaItem;
			if (current == null)
			{
				return;
			}
			string mediaId = current.MediaId;
			if (string.IsNullOrEmpty(mediaId))
			{
				return;
			}
			if (mediaId == m_lastMediaId)
			{
				return;
			}
			m_lastMediaId = mediaId;
			PulseTrack track = FindTrackById(mediaId);
			if (track != null)
			{
				m_mainView.OnCurrentTrackChanged(track);
			}
		}

		private PulseTrack FindTrackById(string trackId)
		{
			for (int idx = 0; idx < m_queue.Count; idx++)
			{
				if (m_queue[idx].Id == trackId)
				{
					return m_queue[idx];
				}
			}
			return null;
		}

		private void HandleQueueEnded()
		{
			if (m_endHandled)
			{
				return;
			}
			m_endHandled = true;
			m_ticker.Stop();
			m_mainView.OnTrackEnded();
		}

		private void ReportState(ePlaybackState state)
		{
			if (state == m_lastState)
			{
				return;
			}
			m_lastState = state;
			m_mainView.OnPlaybackStateChanged(state);
		}

	}
}
