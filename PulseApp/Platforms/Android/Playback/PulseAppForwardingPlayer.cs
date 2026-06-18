using AndroidX.Media3.Common;
using PulseAPI.CSharp;

namespace PulseApp.Playback.AndroidOS
{
	// Sits between the MediaLibrarySession and the underlying ExoPlayer so the
	// in-app MediaController and Android Auto both see the same next/prev
	// behavior: for series items (podcasts now, audiobooks later) those buttons
	// become a +/- 10s seek instead of changing track. Non-series tracks pass
	// straight through to the wrapped player.
	public class PulseAppForwardingPlayer : ForwardingPlayer
	{
		private const long s_seekStepMs = 10000;

		public PulseAppForwardingPlayer(IPlayer player) : base(player)
		{
		}

		// A single-file audiobook is a one-item queue, so the wrapped ExoPlayer
		// reports no next/prev and the system/AA disable those buttons - the +/-10s
		// overrides below never get a chance to fire. For series content, advertise
		// the seek-next/prev commands ourselves so the buttons stay live.
		public override PlayerCommands AvailableCommands
		{
			get
			{
				PlayerCommands baseCommands = base.AvailableCommands;
				if (!CurrentIsSeries())
				{
					return baseCommands;
				}
				PlayerCommands.Builder builder = new PlayerCommands.Builder();
				builder.AddAll(baseCommands);
				builder.Add(BasePlayer.InterfaceConsts.CommandSeekToNext);
				builder.Add(BasePlayer.InterfaceConsts.CommandSeekToNextMediaItem);
				builder.Add(BasePlayer.InterfaceConsts.CommandSeekToPrevious);
				builder.Add(BasePlayer.InterfaceConsts.CommandSeekToPreviousMediaItem);

				return builder.Build();
			}
		}

		public override bool HasNextMediaItem
		{
			get
			{
				if (CurrentIsSeries())
				{
					return true;
				}
				return base.HasNextMediaItem;
			}
		}

		public override bool HasPreviousMediaItem
		{
			get
			{
				if (CurrentIsSeries())
				{
					return true;
				}
				return base.HasPreviousMediaItem;
			}
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
				string outgoingId = CurrentTrackId();
				long position = CurrentPosition;
				base.SeekToNext();
				ReportTrackChange(outgoingId, position);
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
				string outgoingId = CurrentTrackId();
				long position = CurrentPosition;
				base.SeekToNextMediaItem();
				ReportTrackChange(outgoingId, position);
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
				string outgoingId = CurrentTrackId();
				long position = CurrentPosition;
				base.SeekToPrevious();
				ReportTrackChange(outgoingId, position);
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
				string outgoingId = CurrentTrackId();
				long position = CurrentPosition;
				base.SeekToPreviousMediaItem();
				ReportTrackChange(outgoingId, position);
			}
		}

		public override void Stop()
		{
			string outgoingId = CurrentTrackId();
			long position = CurrentPosition;
			base.Stop();
			if (!string.IsNullOrEmpty(outgoingId))
			{
				MainView.Analytics.Event(eAction.Stop, eResult.OK, ePulseWireType.Track, outgoingId, position);
			}
		}

		private string CurrentTrackId()
		{
			MediaItem current = CurrentMediaItem;
			if (current == null)
			{
				return "";
			}
			return current.MediaId;
		}

		private void ReportTrackChange(string outgoingId, long outgoingPositionMs)
		{
			string newId = CurrentTrackId();
			if (outgoingId == newId)
			{
				return;
			}
			if (!string.IsNullOrEmpty(outgoingId))
			{
				MainView.Analytics.Event(eAction.Stop, eResult.OK, ePulseWireType.Track, outgoingId, outgoingPositionMs);
			}
		}
	}
}
