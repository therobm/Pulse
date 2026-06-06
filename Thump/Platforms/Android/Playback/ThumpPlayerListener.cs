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
	}
}
