using AndroidX.Media3.Common;
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
		public void OnMediaItemTransition(MediaItem mediaItem, int reason) { }
		public void OnMediaMetadataChanged(MediaMetadata mediaMetadata) { }
		public void OnPlaybackParametersChanged(PlaybackParameters playbackParameters) { }
		public void OnPlayWhenReadyChanged(bool playWhenReady, int reason) { }
		public void OnPositionDiscontinuity(PlayerPositionInfo oldPosition, PlayerPositionInfo newPosition, int reason) { }
		public void OnRepeatModeChanged(int repeatMode) { }
		public void OnShuffleModeEnabledChanged(bool shuffleModeEnabled) { }
		public void OnTimelineChanged(Timeline timeline, int reason) { }
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
		public void OnIsLoadingChanged(bool isLoading) { }
		public void OnSurfaceSizeChanged(int width, int height) { }
		//public void OnCues(CueGroup cueGroup) { }
	}
}
