using PulseAPI.CSharp;
using System.Collections.Generic;
using PulseApp.Pulse;

namespace PulseApp.Playback
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
		// startFromBeginning forces the start item to ignore any saved resume
		// position (used by the "Play from start" controls); false resumes series
		// items at their saved position.
		void Play(List<PulseTrack> tracks, int startIndex, bool startFromBeginning);
		void Pause();
		void Resume();
		void SeekTo(long positionMilliseconds);
		void SeekRelative(long deltaMilliseconds);
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
