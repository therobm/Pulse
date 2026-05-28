using System;
using System.Collections.Generic;
using System.Timers;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using Microsoft.Maui.ApplicationModel;
using Thump.Pulse;

namespace Thump.Playback
{
	public class AndroidThumpPlayer : IThumpPlayer
	{
		private const int s_stateIdle = 1;
		private const int s_stateBuffering = 2;
		private const int s_stateReady = 3;
		private const int s_stateEnded = 4;
		private const double s_tickIntervalMs = 500;

		private MainView m_hub;
		private IExoPlayer m_player;
		private Timer m_ticker;

		private List<PulseTrack> m_queue = new List<PulseTrack>();
		private int m_index;
		private int m_requestToken;
		private ePlaybackState m_lastState = ePlaybackState.Idle;
		private bool m_endHandled;

		public AndroidThumpPlayer(MainView hub)
		{
			m_hub = hub;

			ExoPlayer.Builder builder = new ExoPlayer.Builder(Android.App.Application.Context);
			m_player = builder.Build();

			m_ticker = new Timer(s_tickIntervalMs);
			m_ticker.AutoReset = true;
			m_ticker.Elapsed += OnTickerElapsed;
		}

		public void Play(List<PulseTrack> tracks, int startIndex)
		{
			if (tracks == null || tracks.Count == 0)
			{
				return;
			}
			m_queue = tracks;
			PlayIndex(startIndex);
		}

		private void PlayIndex(int index)
		{
			if (index < 0 || index >= m_queue.Count)
			{
				return;
			}
			m_index = index;
			m_endHandled = false;
			m_requestToken = m_requestToken + 1;
			int token = m_requestToken;

			PulseTrack track = m_queue[index];
			m_hub.OnCurrentTrackChanged(track);
			ReportState(ePlaybackState.Buffering);

			MainView.Data.GetTrackAudioFile(track, (localPath) =>
			{
				OnTrackFileReady(token, localPath);
			});
		}

		private void OnTrackFileReady(int token, string localPath)
		{
			if (token != m_requestToken)
			{
				return;
			}
			if (string.IsNullOrEmpty(localPath))
			{
				Log.Error("AndroidThumpPlayer: failed to obtain audio file for current track.");
				ReportState(ePlaybackState.Idle);
				return;
			}

			Android.Net.Uri uri = Android.Net.Uri.FromFile(new Java.IO.File(localPath));
			MediaItem item = MediaItem.FromUri(uri);
			m_player.SetMediaItem(item);
			m_player.Prepare();
			m_player.PlayWhenReady = true;

			m_ticker.Start();
		}

		public void Pause()
		{
			if (m_player == null)
			{
				return;
			}
			m_player.Pause();
		}

		public void Resume()
		{
			if (m_player == null)
			{
				return;
			}
			m_player.Play();
		}

		public void SeekTo(long positionMilliseconds)
		{
			if (m_player == null)
			{
				return;
			}
			m_player.SeekTo(positionMilliseconds);
		}

		public void Next()
		{
			PlayIndex(m_index + 1);
		}

		public void Previous()
		{
			PlayIndex(m_index - 1);
		}

		public void Stop()
		{
			m_ticker.Stop();
			if (m_player == null)
			{
				return;
			}
			m_player.Stop();
			ReportState(ePlaybackState.Idle);
		}

		public void Release()
		{
			m_ticker.Stop();
			if (m_player == null)
			{
				return;
			}
			m_player.Release();
			m_player = null;
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
			if (m_player == null)
			{
				return;
			}

			int playbackState = m_player.PlaybackState;
			bool isPlaying = m_player.IsPlaying;

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

			long position = m_player.CurrentPosition;
			long duration = m_player.Duration;
			if (duration < 0)
			{
				duration = 0;
			}
			if (position < 0)
			{
				position = 0;
			}
			m_hub.OnPlaybackPositionChanged(position, duration);

			if (playbackState == s_stateEnded)
			{
				HandleTrackEnded();
			}
		}

		private void HandleTrackEnded()
		{
			if (m_endHandled)
			{
				return;
			}
			m_endHandled = true;

			if (m_index < m_queue.Count - 1)
			{
				PlayIndex(m_index + 1);
				return;
			}
			m_ticker.Stop();
			m_hub.OnTrackEnded();
		}

		private void ReportState(ePlaybackState state)
		{
			if (state == m_lastState)
			{
				return;
			}
			m_lastState = state;
			m_hub.OnPlaybackStateChanged(state);
		}
	}
}
