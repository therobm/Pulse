using AndroidX.Media3.Common;
using PulseAPI.CSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace Thump.Playback.AndroidOS
{
	public class ThumpPlayerListener : Java.Lang.Object, IPlayerListener
	{
		public void OnPlaybackStateChanged(int playbackState)
		{
			string state = playbackState switch
			{
				1 => "IDLE",
				2 => "BUFFERING",
				3 => "READY",
				4 => "ENDED",
				_ => "UNKNOWN(" + playbackState + ")"
			};
			Log.Info("ExoPlayer: state=" + state);
		}

		public void OnPlayerError(PlaybackException error)
		{
			Log.Error("ExoPlayer: error code=" + error.ErrorCode + " msg=" + error.Message);
		}

		public void OnIsPlayingChanged(bool isPlaying)
		{
			Log.Info("ExoPlayer: isPlaying=" + isPlaying);
		}
		public void OnAudioSessionIdChanged(int audioSessionId)
		{ 

		}
		
		public void OnEvents(IPlayer player, PlayerEvents events) { }
		public void OnMediaItemTransition(MediaItem mediaItem, int reason)
		{
			string reasonStr = reason switch
			{
				0 => "REPEAT",
				1 => "AUTO",
				2 => "SEEK",
				3 => "PLAYLIST_CHANGED",
				_ => "UNKNOWN(" + reason + ")"
			};
			Log.Info("ExoPlayer: mediaItemTransition reason=" + reasonStr + " item=" + (mediaItem?.MediaId ?? "null"));
		}
		public void OnMediaMetadataChanged(MediaMetadata mediaMetadata) { }
		public void OnPlaybackParametersChanged(PlaybackParameters playbackParameters) { }
		public void OnPlayWhenReadyChanged(bool playWhenReady, int reason)
		{
			string reasonStr = reason switch
			{
				1 => "USER_REQUEST",
				2 => "AUDIO_FOCUS_LOSS",
				3 => "AUDIO_BECOMING_NOISY",
				4 => "REMOTE",
				5 => "END_OF_MEDIA_ITEM",
				6 => "SUPPRESSED_TOO_LONG",
				_ => "UNKNOWN(" + reason + ")"
			};
			Log.Info("ExoPlayer: playWhenReady=" + playWhenReady + " reason=" + reasonStr);
		}
		public void OnPositionDiscontinuity(PlayerPositionInfo oldPosition, PlayerPositionInfo newPosition, int reason)
		{
			string reasonStr = reason switch
			{
				0 => "AUTO_TRANSITION",
				1 => "SEEK",
				2 => "SEEK_ADJUSTMENT",
				3 => "SKIP",
				4 => "REMOVE",
				5 => "INTERNAL",
				_ => "UNKNOWN(" + reason + ")"
			};
			Log.Info("ExoPlayer: discontinuity reason=" + reasonStr + " pos=" + oldPosition.PositionMs + "->" + newPosition.PositionMs);
		}
		public void OnRepeatModeChanged(int repeatMode) { }
		public void OnShuffleModeEnabledChanged(bool shuffleModeEnabled) { }
		public void OnTimelineChanged(Timeline timeline, int reason)
		{
			Log.Info("ExoPlayer: timelineChanged reason=" + (reason == 0 ? "SOURCE_UPDATE" : "PLAYLIST_CHANGED") + " windows=" + timeline.WindowCount);
		}
		public void OnTracksChanged(Tracks tracks) { }
		public void OnVolumeChanged(float volume) { }
		public void OnDeviceInfoChanged(DeviceInfo deviceInfo) { }
		public void OnDeviceVolumeChanged(int volume, bool muted) { }
		public void OnVideoSizeChanged(VideoSize videoSize) { }
		public void OnRenderedFirstFrame() { }
		public void OnMaxSeekToPreviousPositionChanged(long maxSeekToPreviousPositionMs) { }
		public void OnSeekBackIncrementChanged(long seekBackIncrementMs) { }
		public void OnSeekForwardIncrementChanged(long seekForwardIncrementMs) { }
		public void OnAvailableCommandsChanged(PlayerCommands availableCommands) { }
		public void OnPlaylistMetadataChanged(MediaMetadata mediaMetadata) { }
		public void OnIsLoadingChanged(bool isLoading)
		{
			Log.Info("ExoPlayer: isLoading=" + isLoading);
		}
		public void OnSurfaceSizeChanged(int width, int height) { }
		public void OnPlayerStateChanged(bool playWhenReady, int playbackState) { }
		public void OnLoadingChanged(bool isLoading) { }
		public void OnCues(AndroidX.Media3.Common.Text.CueGroup cueGroup) { }

		public void OnPlayerErrorChanged(PlaybackException error) { Log.Error(error.Message); }

		public void OnAudioAttributesChanged(AudioAttributes audioAttributes) { }
		public void OnPlaybackSuppressionReasonChanged(int playbackSuppressionReason)
		{
			Log.Info("ExoPlayer: suppressionReason=" + playbackSuppressionReason);
		}
		public void OnSkipSilenceEnabledChanged(bool skipSilenceEnabled) { }
		public void OnMetadata(Metadata metadata) { }
		public void OnTrackSelectionParametersChanged(TrackSelectionParameters parameters) { }
	}
}
