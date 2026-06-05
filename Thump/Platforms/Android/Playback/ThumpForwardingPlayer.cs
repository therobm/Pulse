using AndroidX.Media3.Common;

namespace Thump.Playback.AndroidOS
{
	// Sits between the MediaLibrarySession and the underlying ExoPlayer so the
	// in-app MediaController and Android Auto both see the same next/prev
	// behavior: for series items (podcasts now, audiobooks later) those buttons
	// become a +/- 10s seek instead of changing track. Non-series tracks pass
	// straight through to the wrapped player.
	public class ThumpForwardingPlayer : ForwardingPlayer
	{
		private const long s_seekStepMs = 10000;

		public ThumpForwardingPlayer(IPlayer player) : base(player)
		{
		}

		private bool CurrentIsSeries()
		{
			MediaItem current = CurrentMediaItem;
			if (current == null)
			{
				return false;
			}
			MediaMetadata metadata = current.MediaMetadata;
			if (metadata == null || metadata.Extras == null)
			{
				return false;
			}
			return metadata.Extras.GetBoolean("is_series", false);
		}

		private void SeekRelative(long deltaMs)
		{
			long position = CurrentPosition + deltaMs;
			if (position < 0)
			{
				position = 0;
			}
			long duration = Duration;
			if (duration > 0 && position > duration)
			{
				position = duration;
			}
			SeekTo(position);
		}

		public override void SeekToNext()
		{
			if (CurrentIsSeries())
			{
				SeekRelative(s_seekStepMs);
			}
			else
			{
				base.SeekToNext();
			}
		}

		public override void SeekToNextMediaItem()
		{
			if (CurrentIsSeries())
			{
				SeekRelative(s_seekStepMs);
			}
			else
			{
				base.SeekToNextMediaItem();
			}
		}

		public override void SeekToPrevious()
		{
			if (CurrentIsSeries())
			{
				SeekRelative(-s_seekStepMs);
			}
			else
			{
				base.SeekToPrevious();
			}
		}

		public override void SeekToPreviousMediaItem()
		{
			if (CurrentIsSeries())
			{
				SeekRelative(-s_seekStepMs);
			}
			else
			{
				base.SeekToPreviousMediaItem();
			}
		}
	}
}
