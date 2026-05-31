using System.Collections.Generic;
using Thump.Pulse;

namespace Thump.Playback
{
	public enum ePlaybackState
	{
		Idle,
		Buffering,
		Playing,
		Paused,
		Ended,
	}

	public enum eRepeatMode
	{
		Off,
		One,
		All,
	}

	public interface IMediaPlayer
	{
		void Play(List<PulseTrack> tracks, int startIndex);
		void Pause();
		void Resume();
		void SeekTo(long positionMilliseconds);
		void Next();
		void Previous();
		void Stop();
		void Release();
		void SetShuffleEnabled(bool enabled);
		void SetRepeatMode(eRepeatMode mode);
		void AddToQueue(List<PulseTrack> tracks);
		void PlayNext(List<PulseTrack> tracks);
		void SeekToQueueItem(int index);
	}
}
