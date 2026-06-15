using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;
using PulseAPI.CSharp;
using Thump.Data;
using Thump.Playback;
#if ANDROID
using Thump.Playback.AndroidOS;
#endif
using Thump.Pulse;
using Thump.Views;

namespace Thump
{
	public enum eQueueSource
	{
		Track,
		Album,
		Artist,
		Playlist,
		Genre,
		Podcast,
		Audiobook,
	}
	public enum eTab
	{
		Home,
		Library,
		Search,
		Settings,
	}

	public class MainView : ContentPage, IMediaClientHost
	{
		public const string ServerUrl = "https://192.168.5.5:32458";
		public const string ServerUser = "Rob";

		public static MainView Self { get { return s_self; } }
		public static MediaClient MediaClient { get { return Self.m_mediaClient; } }
		public static Analytics Analytics { get { return Self.m_analytics; } }
		private static MainView s_self;
		private MediaClient m_mediaClient;
		private Analytics m_analytics;
		private ThumpCache m_cache;
		private Grid m_rootGrid;
		private OfflineBanner m_offlineBanner;
		private ContentView m_contentHost;
		private HomeView m_homeView;
		private LibraryView m_libraryView;
		private SearchView m_searchView;
		private SettingsView m_settingsView;
		private MiniPlayer m_miniPlayer;
		private NavFooter m_navFooter;

		private eTab m_activeTab = eTab.Home;
		private List<View> m_detailStack = new List<View>();

		private List<PulseTrack> m_currentQueue = new List<PulseTrack>();
		private int m_currentQueueIndex;
		private PulseTrack m_currentTrack;
		private IMediaPlayer m_player;
		private ePlaybackState m_playbackState = ePlaybackState.Idle;
		private long m_currentDurationMs;
		private NowPlayingView m_nowPlayingView;
		private bool m_shuffleEnabled;
		private eRepeatMode m_repeatMode = eRepeatMode.Off;

		/// <summary>True while a sleep timer is counting down. Ephemeral, lives only for the app session.</summary>
		private bool m_sleepTimerActive;
		/// <summary>Seconds left before the sleep timer pauses playback. Zero when no timer is active.</summary>
		private int m_sleepTimerRemainingSeconds;
		/// <summary>Once-per-second tick that decrements the sleep timer and pauses playback when it elapses.</summary>
		private IDispatcherTimer m_sleepTimerTick;

		private bool m_bIsOnline = true;

		public MainView()
		{
			s_self = this;

			Shell.SetNavBarIsVisible(this, false);
			Shell.SetTabBarIsVisible(this, false);
			BackgroundColor = ThumpColors.Background;

			m_rootGrid = new Grid();
			RowDefinition bannerRow = new RowDefinition();
			bannerRow.Height = GridLength.Auto;
			m_rootGrid.RowDefinitions.Add(bannerRow);
			RowDefinition contentRow = new RowDefinition();
			contentRow.Height = GridLength.Star;
			m_rootGrid.RowDefinitions.Add(contentRow);
			RowDefinition miniPlayerRow = new RowDefinition();
			miniPlayerRow.Height = GridLength.Auto;
			m_rootGrid.RowDefinitions.Add(miniPlayerRow);
			RowDefinition navFooterRow = new RowDefinition();
			navFooterRow.Height = GridLength.Auto;
			m_rootGrid.RowDefinitions.Add(navFooterRow);

			m_offlineBanner = new OfflineBanner(this);
			Grid.SetRow(m_offlineBanner, 0);
			m_rootGrid.Children.Add(m_offlineBanner);

			m_contentHost = new ContentView();
			Grid.SetRow(m_contentHost, 1);
			m_rootGrid.Children.Add(m_contentHost);

			Content = m_rootGrid;


			m_cache = new ThumpCache();
			m_mediaClient = new PulseClient(m_cache, this);
			m_analytics = new Analytics(m_mediaClient);

			m_mediaClient.SetServerParams(ThumpSettings.GetServerIp(), ThumpSettings.GetServerPort(), ThumpSettings.GetUsername(), ThumpSettings.GetPassword(), ThumpSettings.GetUseHttps());
#if ANDROID
			m_player = new ThumpAndroidPlayer(this, m_mediaClient);
#else
			m_player = new StubThumpPlayer();
#endif

			m_shuffleEnabled = ThumpSettings.GetShuffleEnabled();
			m_repeatMode = ThumpSettings.GetRepeatMode();
			m_player.SetShuffleEnabled(m_shuffleEnabled);
			m_player.SetRepeatMode(m_repeatMode);

			m_sleepTimerTick = this.Dispatcher.CreateTimer();
			m_sleepTimerTick.Interval = TimeSpan.FromSeconds(1);
			m_sleepTimerTick.Tick += OnSleepTimerTick;

			m_homeView = new HomeView(this);
			m_libraryView = new LibraryView(this);
			m_searchView = new SearchView(this);
			m_settingsView = new SettingsView(this);

			m_miniPlayer = new MiniPlayer(this);
			Grid.SetRow(m_miniPlayer, 2);
			m_miniPlayer.IsVisible = false;
			m_rootGrid.Children.Add(m_miniPlayer);

			m_navFooter = new NavFooter(this);
			Grid.SetRow(m_navFooter, 3);
			m_rootGrid.Children.Add(m_navFooter);

			m_homeView.Initialize();
			m_libraryView.Initialize();
			m_searchView.Initialize();
			m_settingsView.Initialize();
			m_miniPlayer.Initialize();
			m_navFooter.Initialize();

			TryAutoSignIn();
			NavigateToHome();
		}

		public ThumpCache GetCache()
		{
			return m_cache;
		}

		// OnlineStateChanged fires from the client's background threads, so hop to
		// the main thread before touching the banner's visibility.
		public void OnOnlineStateChanged(bool online)
		{
			if (online == m_bIsOnline)
				return;

			m_bIsOnline = online;
			string connectivityDetail = "offline";
			if (online)
			{
				connectivityDetail = "online";
			}

			MainThread.BeginInvokeOnMainThread(() =>
			{
				m_offlineBanner.SetIsOnline(online);
			});
		}
		// Single choke point for swapping the active content so OnNavigatedTo
		// fires every time, no matter which navigation path got us here. The view
		// forwards the signal down to its children (see ThumpView).
		private void SetActiveContent(View view)
		{
			m_contentHost.Content = view;
			ThumpView thumpView = view as ThumpView;
			if (thumpView != null)
			{
				thumpView.OnNavigatedTo();
			}
		}

		/// <summary>
		/// Redirects to the sign-in (Settings) surface when the client has no uid.
		/// The data API is uid-only and the server refuses unauthenticated requests
		/// under enforcement, so the browse tabs would otherwise show empty. Returns
		/// false (and navigates to Settings) when not signed in.
		/// </summary>
		/// <summary>
		/// At boot, re-authenticate from stored credentials in the background to
		/// validate and refresh the uid against the current server. Non-fatal on
		/// failure (e.g. server unreachable): the persisted uid stands. Does nothing
		/// when no password is stored -- the sign-in guard then routes to Settings.
		/// </summary>
		private void TryAutoSignIn()
		{
			string username = ThumpSettings.GetUsername();
			string password = ThumpSettings.GetPassword();
			if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
			{
				return;
			}
			Task.Run(() =>
			{
				m_mediaClient.Login(username, password, true, (loginResult) =>
				{
					if (loginResult != null && loginResult.Outcome == eAuthOutcome.Ok)
					{
						ThumpSettings.SetUserID(loginResult.Id);
					}
				});
			});
		}

		private bool EnsureSignedIn()
		{
			if (string.IsNullOrEmpty(ThumpSettings.GetUserID()))
			{
				NavigateToSettings();
				return false;
			}
			return true;
		}

		public void NavigateToHome()
		{
			if (!EnsureSignedIn())
			{
				return;
			}
			m_activeTab = eTab.Home;
			m_detailStack.Clear();
			SetActiveContent(m_homeView);
			m_navFooter.SetActiveTab(eTab.Home);
			RestoreMiniPlayerIfActive();
		}

		public void NavigateToLibrary()
		{
			if (!EnsureSignedIn())
			{
				return;
			}
			m_activeTab = eTab.Library;
			m_detailStack.Clear();
			SetActiveContent(m_libraryView);
			m_navFooter.SetActiveTab(eTab.Library);
			RestoreMiniPlayerIfActive();
		}

		public void NavigateToSearch()
		{
			if (!EnsureSignedIn())
			{
				return;
			}
			m_activeTab = eTab.Search;
			m_detailStack.Clear();
			SetActiveContent(m_searchView);
			m_navFooter.SetActiveTab(eTab.Search);
			RestoreMiniPlayerIfActive();
		}

		public void NavigateToSettings()
		{
			m_activeTab = eTab.Settings;
			m_detailStack.Clear();
			SetActiveContent(m_settingsView);
			m_navFooter.SetActiveTab(eTab.Settings);
			RestoreMiniPlayerIfActive();
		}

		private void PushDetail(View detail)
		{
			m_detailStack.Add(detail);
			SetActiveContent(detail);
		}

		public void OnBackPressed()
		{
			if (m_detailStack.Count == 0)
			{
				return;
			}
			m_detailStack.RemoveAt(m_detailStack.Count - 1);
			if (m_detailStack.Count > 0)
			{
				SetActiveContent(m_detailStack[m_detailStack.Count - 1]);
				if (m_contentHost.Content != m_nowPlayingView)
				{
					RestoreMiniPlayerIfActive();
				}
				return;
			}
			if (m_activeTab == eTab.Home)
			{
				SetActiveContent(m_homeView);
			}
			else if (m_activeTab == eTab.Library)
			{
				SetActiveContent(m_libraryView);
			}
			else if (m_activeTab == eTab.Search)
			{
				SetActiveContent(m_searchView);
			}
			else if (m_activeTab == eTab.Settings)
			{
				SetActiveContent(m_settingsView);
			}
			if (m_contentHost.Content != m_nowPlayingView)
			{
				RestoreMiniPlayerIfActive();
			}
		}

		protected override bool OnBackButtonPressed()
		{
			if (m_detailStack.Count > 0)
			{
				OnBackPressed();
				return true;
			}
			if (m_activeTab != eTab.Home)
			{
				NavigateToHome();
				return true;
			}
			return base.OnBackButtonPressed();
		}

		public void OnArtistSelected(PulseArtist artist)
		{
			ArtistDetailView detail = new ArtistDetailView(this, artist);
			detail.Initialize();
			PushDetail(detail);
		}

		public void OnAlbumSelected(PulseAlbum album)
		{
			AlbumDetailView detail = new AlbumDetailView(this, album);
			detail.Initialize();
			PushDetail(detail);
		}

		public void OnPlaylistSelected(PulsePlaylist playlist)
		{
			PlaylistDetailView detail = new PlaylistDetailView(this, playlist);
			detail.Initialize();
			PushDetail(detail);
		}

		public void OnPodcastSelected(PulsePodcast podcast)
		{
			PodcastDetailView detail = new PodcastDetailView(this, podcast);
			detail.Initialize();
			PushDetail(detail);
		}

		public void OnAudiobookSelected(PulseAudiobook audiobook)
		{
			AudiobookDetailView detail = new AudiobookDetailView(this, audiobook);
			detail.Initialize();
			PushDetail(detail);
		}

		public void OnAudiobookAuthorSelected(AudiobookAuthor author)
		{
			AudiobookAuthorView view = new AudiobookAuthorView(this, author.Name);
			view.Initialize();
			PushDetail(view);
		}

		public void OnGenreSelected(PulseGenre genre)
		{
			GenreDetailView detail = new GenreDetailView(this, genre);
			detail.Initialize();
			PushDetail(detail);
		}

		public void OnHomeItemSelected(PulseObject item)
		{
			if (item.Kind == ePulseWireType.Album)
			{
				PulseAlbum album = item as PulseAlbum;
				if (album != null)
				{
					OnAlbumSelected(album);
				}
			}
			else if (item.Kind == ePulseWireType.Playlist)
			{
				PulsePlaylist playlist = item as PulsePlaylist;
				if (playlist != null)
				{
					OnPlaylistSelected(playlist);
				}
			}
			else if (item.Kind == ePulseWireType.Artist)
			{
				PulseArtist artist = item as PulseArtist;
				if (artist != null)
				{
					OnArtistSelected(artist);
				}
			}
			else if (item.Kind == ePulseWireType.Track)
			{
				PulseTrack track = item as PulseTrack;
				if (track != null)
				{
					List<PulseTrack> oneShotQueue = new List<PulseTrack>();
					oneShotQueue.Add(track);
					OnPlayTracks(oneShotQueue, 0, eQueueSource.Track, track.Id);
				}
			}
		}

		public void OnTrackSelected(PulseTrack track)
		{
			List<PulseTrack> oneShotQueue = new List<PulseTrack>();
			oneShotQueue.Add(track);
			OnPlayTracks(oneShotQueue, 0, eQueueSource.Track, track.Id);
		}

		public void OnPlayTracks(List<PulseTrack> tracks, int startIndex, eQueueSource source, string sourceId = "")
		{
			if (tracks == null || tracks.Count == 0)
			{
				return;
			}
			int clampedIndex = startIndex;
			if (clampedIndex < 0 || clampedIndex >= tracks.Count)
			{
				clampedIndex = 0;
			}
			m_currentQueue = new List<PulseTrack>(tracks);
			m_currentQueueIndex = clampedIndex;
			m_currentTrack = m_currentQueue[clampedIndex];
			m_miniPlayer.SetTrack(m_currentTrack);
			ShowMiniPlayer();
			m_player.Play(m_currentQueue, clampedIndex);



			// Collection-level Started analytics. Only albums, artists, and
			// playlists report here -- a Track source is a single-track queue
			// whose play is already captured track-side by the playback
			// service, and Genre has no collection identity to attribute a
			// play to. The per-track Started/Paused/Skipped/Completed events
			// still come from the service ticker regardless of source.
			switch (source)
			{
				case eQueueSource.Album:
					m_mediaClient.ReportAnalytics(sourceId, ePulseWireType.Album, PulseAnalytics.eAction.Started);
					break;
				case eQueueSource.Artist:
					m_mediaClient.ReportAnalytics(sourceId, ePulseWireType.Artist, PulseAnalytics.eAction.Started);
					break;
				case eQueueSource.Playlist:
					m_mediaClient.ReportAnalytics(sourceId, ePulseWireType.Playlist, PulseAnalytics.eAction.Started);
					break;
			}
		}

		/// <summary>One-second tick handler that decrements the remaining seconds and pauses playback when the timer elapses.</summary>
		private void OnSleepTimerTick(object sender, EventArgs e)
		{
			m_sleepTimerRemainingSeconds = m_sleepTimerRemainingSeconds - 1;
			if (m_sleepTimerRemainingSeconds <= 0)
			{
				m_player.Pause();
				m_sleepTimerTick.Stop();
				m_sleepTimerActive = false;
				m_sleepTimerRemainingSeconds = 0;
				if (m_nowPlayingView != null)
				{
					m_nowPlayingView.SetSleepTimerDisplay(false, 0);
				}
				return;
			}
			if (m_nowPlayingView != null)
			{
				m_nowPlayingView.SetSleepTimerDisplay(true, m_sleepTimerRemainingSeconds);
			}
		}

		public void OnPlayTracksShuffled(List<PulseTrack> tracks, eQueueSource source, string sourceId = "")
		{
			if (tracks == null || tracks.Count == 0)
			{
				return;
			}
			// Shuffle the list ONCE here and submit the shuffled queue. Do NOT toggle the
			// player's persistent ShuffleModeEnabled — that is a separate user-controlled
			// setting (the Now Playing ⇋ button). The album/playlist/etc. Shuffle button
			// is meant to randomize this one queue submission, not flip a mode.
			List<PulseTrack> shuffled = new List<PulseTrack>(tracks);
			System.Random random = new System.Random();
			for (int idx = shuffled.Count - 1; idx > 0; idx--)
			{
				int swap = random.Next(idx + 1);
				PulseTrack tmp = shuffled[idx];
				shuffled[idx] = shuffled[swap];
				shuffled[swap] = tmp;
			}
			OnPlayTracks(shuffled, 0, source, sourceId);
		}

		public void OnTogglePlayPause()
		{
			if (m_playbackState == ePlaybackState.Playing || m_playbackState == ePlaybackState.Buffering)
			{
				m_player.Pause();
			}
			else
			{
				m_player.Resume();
			}
		}

		public void OnNext()
		{
			// Series items skip +/-10s rather than changing track. Do it as a direct
			// relative seek so it isn't blocked by controller-command authorization
			// on a one-item queue (single-file audiobook).
			if (CurrentTrackIsSeries())
			{
				m_player.SeekRelative(10000);
			}
			else
			{
				m_player.Next();
			}
		}

		public void OnPrevious()
		{
			if (CurrentTrackIsSeries())
			{
				m_player.SeekRelative(-10000);
			}
			else
			{
				m_player.Previous();
			}
		}

		public void OnSeekToFraction(double fraction)
		{
			long position = (long)(fraction * m_currentDurationMs);
			m_player.SeekTo(position);
		}

		public void OnToggleShuffle()
		{
			SetShuffleState(!m_shuffleEnabled);
		}

		private void SetShuffleState(bool enabled)
		{
			m_shuffleEnabled = enabled;
			ThumpSettings.SetShuffleEnabled(enabled);
			m_player.SetShuffleEnabled(enabled);
			if (m_nowPlayingView != null)
			{
				m_nowPlayingView.SetShuffleState(enabled);
			}
		}

		public void OnCycleRepeat()
		{
			eRepeatMode next;
			if (m_repeatMode == eRepeatMode.Off)
			{
				next = eRepeatMode.All;
			}
			else if (m_repeatMode == eRepeatMode.All)
			{
				next = eRepeatMode.One;
			}
			else
			{
				next = eRepeatMode.Off;
			}
			SetRepeatState(next);
		}

		private void SetRepeatState(eRepeatMode mode)
		{
			m_repeatMode = mode;
			ThumpSettings.SetRepeatMode(mode);
			m_player.SetRepeatMode(mode);
			if (m_nowPlayingView != null)
			{
				m_nowPlayingView.SetRepeatState(mode);
			}
		}

		public bool GetShuffleEnabled()
		{
			return m_shuffleEnabled;
		}

		public eRepeatMode GetRepeatMode()
		{
			return m_repeatMode;
		}

		/// <summary>Start a sleep timer that will pause playback after the given number of minutes. Re-selecting a preset resets the countdown. minutes &lt;= 0 cancels.</summary>
		public void OnSetSleepTimer(int minutes)
		{
			if (minutes <= 0)
			{
				OnCancelSleepTimer();
				return;
			}
			m_sleepTimerRemainingSeconds = minutes * 60;
			m_sleepTimerActive = true;
			m_sleepTimerTick.Stop();
			m_sleepTimerTick.Start();
			if (m_nowPlayingView != null)
			{
				m_nowPlayingView.SetSleepTimerDisplay(true, m_sleepTimerRemainingSeconds);
			}
		}

		/// <summary>Cancel any active sleep timer. Safe to call when no timer is running.</summary>
		public void OnCancelSleepTimer()
		{
			m_sleepTimerTick.Stop();
			m_sleepTimerActive = false;
			m_sleepTimerRemainingSeconds = 0;
			if (m_nowPlayingView != null)
			{
				m_nowPlayingView.SetSleepTimerDisplay(false, 0);
			}
		}

		/// <summary>True while a sleep timer is counting down. Read by NowPlayingView.Initialize() to restore display state.</summary>
		public bool GetSleepTimerActive()
		{
			return m_sleepTimerActive;
		}

		/// <summary>Seconds remaining on the active sleep timer; zero when no timer is active.</summary>
		public int GetSleepTimerRemainingSeconds()
		{
			return m_sleepTimerRemainingSeconds;
		}

		public void OnAddToQueue(List<PulseTrack> tracks, eQueueSource source, string sourceId = "")
		{
			if (tracks == null || tracks.Count == 0)
			{
				return;
			}
			if (m_currentQueue.Count == 0)
			{
				OnPlayTracks(tracks, 0, source, sourceId);
				return;
			}
			m_currentQueue.AddRange(tracks);
			m_player.AddToQueue(tracks);
			m_miniPlayer.RefreshSkipButtons();
			if (m_nowPlayingView != null)
			{
				m_nowPlayingView.RefreshQueue();
				m_nowPlayingView.RefreshSkipButtons();
			}
		}

		public void OnPlayNext(List<PulseTrack> tracks, eQueueSource source, string sourceId = "")
		{
			if (tracks == null || tracks.Count == 0)
			{
				return;
			}
			if (m_currentQueue.Count == 0)
			{
				OnPlayTracks(tracks, 0, source, sourceId);
				return;
			}
			int insertAt = m_currentQueueIndex + 1;
			if (insertAt > m_currentQueue.Count)
			{
				insertAt = m_currentQueue.Count;
			}
			m_currentQueue.InsertRange(insertAt, tracks);
			m_player.PlayNext(tracks);
			m_miniPlayer.RefreshSkipButtons();
			if (m_nowPlayingView != null)
			{
				m_nowPlayingView.RefreshQueue();
				m_nowPlayingView.RefreshSkipButtons();
			}

			//todo this should also fire MarkAsPlayed for recency tracking
			//or things should be unified cause this is silly
		}

		public void OnSeekToQueueItem(int index)
		{
			if (index < 0 || index >= m_currentQueue.Count)
			{
				return;
			}
			m_currentQueueIndex = index;
			m_currentTrack = m_currentQueue[index];
			m_miniPlayer.SetTrack(m_currentTrack);
			ShowMiniPlayer();
			if (m_nowPlayingView != null)
			{
				m_nowPlayingView.SetTrack(m_currentTrack);
			}
			m_player.SeekToQueueItem(index);
		}

		public List<PulseTrack> GetQueue()
		{
			return m_currentQueue;
		}

		public int GetQueueIndex()
		{
			return m_currentQueueIndex;
		}

		// True when the currently-playing queue item is series content (podcast or
		// audiobook). The in-app skip buttons use this to stay enabled even on a
		// one-item queue, since prev/next become a +/-10s seek for series.
		public bool CurrentTrackIsSeries()
		{
			if (m_currentQueue == null)
			{
				return false;
			}
			if (m_currentQueueIndex < 0 || m_currentQueueIndex >= m_currentQueue.Count)
			{
				return false;
			}
			return m_currentQueue[m_currentQueueIndex].IsSeries;
		}

		// Id of the current track for analytics attribution, or "" when nothing
		// is loaded (the analytics layer treats an empty object id as no-object).
		private string CurrentTrackId()
		{
			if (m_currentTrack == null)
			{
				return "";
			}
			return m_currentTrack.Id;
		}

		public void OnQueueTrackSelected(PulseTrack track)
		{
			int index = m_currentQueue.IndexOf(track);
			if (index < 0)
			{
				return;
			}
			OnSeekToQueueItem(index);
		}

		public async void OnTrackOptions(PulseTrack track)
		{
			if (track == null)
			{
				return;
			}
			string playNext = "Play Next";
			string addToQueue = "Add to Queue";
			string choice = await DisplayActionSheet(track.Title, "Cancel", null, playNext, addToQueue);
			List<PulseTrack> single = new List<PulseTrack>();
			single.Add(track);
			if (choice == playNext)
			{
				OnPlayNext(single, eQueueSource.Track, track.Id);
			}
			else if (choice == addToQueue)
			{
				OnAddToQueue(single, eQueueSource.Track, track.Id);
			}
		}

		public async void OnAddPodcast()
		{
			string url = await DisplayPromptAsync("Add Podcast", "Enter the podcast's RSS feed URL", "Add", "Cancel", "https://...", -1, Microsoft.Maui.Keyboard.Url, "");
			if (string.IsNullOrWhiteSpace(url))
			{
				return;
			}
			string feedUrl = url.Trim();
			m_mediaClient.AddPodcast(feedUrl, true, (podcast) =>
			{
				// CompleteOnMain marshals to the UI thread already.
				if (m_libraryView != null)
				{
					m_libraryView.ReloadPodcasts();
				}
			});
		}

		public void OnPlaybackStateChanged(ePlaybackState state)
		{
			m_playbackState = state;
			bool playing = state == ePlaybackState.Playing;
			m_miniPlayer.SetPlaying(playing);
			if (m_nowPlayingView != null)
			{
				m_nowPlayingView.SetPlaying(playing);
			}
		}

		public void OnPlaybackPositionChanged(long positionMilliseconds, long durationMilliseconds)
		{
			m_currentDurationMs = durationMilliseconds;
			double fraction = 0;
			if (durationMilliseconds > 0)
			{
				fraction = (double)positionMilliseconds / (double)durationMilliseconds;
			}
			m_miniPlayer.SetProgress(fraction);
			if (m_nowPlayingView != null)
			{
				m_nowPlayingView.UpdatePosition(positionMilliseconds, durationMilliseconds);
			}
		}

		public void OnCurrentTrackChanged(PulseTrack track)
		{
			if (track == null)
			{
				return;
			}
			m_currentTrack = track;
			int foundIndex = m_currentQueue.IndexOf(track);
			if (foundIndex >= 0)
			{
				m_currentQueueIndex = foundIndex;
			}
			m_miniPlayer.SetTrack(track);
			ShowMiniPlayer();
			if (m_nowPlayingView != null)
			{
				m_nowPlayingView.SetTrack(track);
			}
		}

		public void OnTrackEnded()
		{
			m_playbackState = ePlaybackState.Ended;
			m_miniPlayer.SetPlaying(false);
			if (m_nowPlayingView != null)
			{
				m_nowPlayingView.SetPlaying(false);
			}
		}

		public void OnPlayArtist(PulseArtist artist, bool shuffle)
		{
			if (artist == null)
			{
				return;
			}
			bool started = false;
			m_mediaClient.GetArtistTracks(artist.Id, (tracks) =>
			{
				// The data route can fire its callback more than once (cache fast-path
				// then network); start the walk only once.
				if (started)
				{
					return;
				}
				started = true;
				if (tracks == null || tracks.Count == 0)
				{
					return;
				}
				if (shuffle)
				{
					OnPlayTracksShuffled(tracks, eQueueSource.Artist, artist.Id);
				}
				else
				{
					OnPlayTracks(tracks, 0, eQueueSource.Artist, artist.Id);
				}

			});
		}

		public PulseTrack GetCurrentTrack()
		{
			return m_currentTrack;
		}

		public void OpenNowPlaying()
		{
			if (m_nowPlayingView == null)
			{
				m_nowPlayingView = new NowPlayingView(this);
				m_nowPlayingView.Initialize();
			}
			if (m_currentTrack != null)
			{
				m_nowPlayingView.SetTrack(m_currentTrack);
			}
			m_nowPlayingView.SetPlaying(m_playbackState == ePlaybackState.Playing);
			PushDetail(m_nowPlayingView);
			HideMiniPlayer();
		}

		public void ShowMiniPlayer()
		{
			if (m_nowPlayingView != null && m_contentHost.Content == m_nowPlayingView)
			{
				return;
			}
			m_miniPlayer.IsVisible = true;
		}

		public void HideMiniPlayer()
		{
			m_miniPlayer.IsVisible = false;
		}

		private void RestoreMiniPlayerIfActive()
		{
			if (m_currentTrack == null)
			{
				return;
			}
			ShowMiniPlayer();
		}
	}
}
