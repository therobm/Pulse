using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using AndroidX.Concurrent.Futures;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Source;
using AndroidX.Media3.ExoPlayer.Upstream;
using AndroidX.Media3.Extractor;
using AndroidX.Media3.Extractor.Mp3;
using AndroidX.Media3.Session;
using Google.Common.Util.Concurrent;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using PulseAPI.CSharp;
using PulseApp.Data;
using PulseApp.Pulse;

namespace PulseApp.Playback.AndroidOS
{

	public class PulseAppLoadErrorPolicy : DefaultLoadErrorHandlingPolicy
	{
		private const int MAX_RETRIES = 6;

		public override long GetRetryDelayMsFor(LoadErrorHandlingPolicyLoadErrorInfo loadErrorInfo)
		{
			if (loadErrorInfo.ErrorCount > MAX_RETRIES)
			{
				return C.TimeUnset;
			}
			return 500L * (1L << (loadErrorInfo.ErrorCount - 1));
		}
	}

	/// <summary>
	/// A lightweight process for Android's media player to leverage so it can access
	/// PulseApp specific data and functionality
	/// </summary>
	[Service(Exported = true, Enabled = true, Name = "com.therobm.pulse.PulseAppPlaybackService", ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
	[IntentFilter(new string[] { "androidx.media3.session.MediaLibraryService", "android.media.browse.MediaBrowserService" })]
	public class PulseAppMediaLibraryService : MediaLibraryService, IMediaClientHost
	{
		/// <summary>
		/// A special sneaky global so the media service can access our data
		/// </summary>
		public static MediaClient s_mediaClient;

		public static PulseAppMediaLibraryService s_instance;

		/// <summary>
		/// A callback identifier used to discard late arrivals
		/// when the queue has been changed/reset.
		/// </summary>
		private int m_currentQueueID;

		private IExoPlayer m_player;
		private MediaLibraryService.MediaLibrarySession m_session;
		private CarConnectionReceiver m_carReceiver;

		// Shared shuffle RNG. Field-level rather than per-call so back-to-back
		// shuffles within the same second don't get identically seeded.
		private static Random s_shuffleRand = new Random();

		private bool m_bIsOnline = true;
		// Two-step search: OnSearch kicks off the async fetch and parks the
		// resolved items under the query string; OnGetSearchResult reads them
		// out. Keyed by exact query text since AA calls back with the same
		// string it gave us.
		private System.Collections.Concurrent.ConcurrentDictionary<string, List<MediaItem>> m_searchResults = new System.Collections.Concurrent.ConcurrentDictionary<string, List<MediaItem>>();

		public override void OnCreate()
		{
			base.OnCreate();

			s_instance = this;

			// Trust-all TLS for the native HTTP stack so Media3's DefaultHttpDataSource
			// streams from the Pulse server without depending on a valid certificate.
			InsecureTls.Install();

			if (s_mediaClient == null)
			{
				s_mediaClient = BuildMediaClient(this);
			}

			ExoPlayerBuilder builder = new ExoPlayerBuilder(this);
			builder.SetHandleAudioBecomingNoisy(true);

			int minBufferSize = 1000 * 30; //30 sec
			int maxBufferSize = 1000 * 60; //60 sec
			int minBufferStart = (int)(1000 * 2.5f);
			int minBufferRestart = (int)(1000 * 5.0f);

			DefaultLoadControl loadControl = new DefaultLoadControl.Builder().SetBufferDurationsMs(
					minBufferSize,
					maxBufferSize,  
					minBufferStart,  
					minBufferRestart  
				).Build();
			builder.SetLoadControl(loadControl);


			// Request audio focus so ExoPlayer activates the audio route on play; without this the player streams but is silent until another app grabs focus.
			AudioAttributes.Builder audioAttributesBuilder = new AudioAttributes.Builder();
			audioAttributesBuilder.SetUsage(C.UsageMedia);
			audioAttributesBuilder.SetContentType(C.AudioContentTypeMusic);
			AudioAttributes audioAttributes = audioAttributesBuilder.Build();
			builder.SetAudioAttributes(audioAttributes, true);

			DefaultExtractorsFactory extractorsFactory = new DefaultExtractorsFactory();
			extractorsFactory.SetMp3ExtractorFlags( Mp3Extractor.FlagEnableConstantBitrateSeeking | Mp3Extractor.FlagDisableId3Metadata);

			string cacheBaseDir = Android.App.Application.Context.CacheDir.AbsolutePath;
			string cacheDir = Path.Combine(cacheBaseDir, "tracks");
			if (!Directory.Exists(cacheDir))	
				Directory.CreateDirectory(cacheDir);

			AndroidMediaDataSourceFactory dataSourceFactory = new AndroidMediaDataSourceFactory(s_mediaClient, cacheDir);
			DefaultMediaSourceFactory mediaSourceFactory = new DefaultMediaSourceFactory(dataSourceFactory, extractorsFactory);
			mediaSourceFactory.SetLoadErrorHandlingPolicy(new PulseAppLoadErrorPolicy());
			builder.SetMediaSourceFactory(mediaSourceFactory);

			m_player = builder.Build();
			m_player.AddListener(new PulseAppPlayerListener());

			AndroidMediaLibraryCallback library = new AndroidMediaLibraryCallback();
			library.m_onAddMediaItems = null;
			library.m_onConnect = null;
			library.m_onDisconnected = OnPhoneDisconnected;
			library.m_onGetChildren = OnGetChildren;
			library.m_onGetItem = OnGetItem;
			library.m_onGetLibraryRoot = OnGetLibraryRoot;
			library.m_onPlaybackResumption = null;
			library.m_onPlayerCommandRequest = null;
			library.m_onPlayerInteractionFinished = null;
			library.m_onPostConnect = null;
			library.m_onSetMediaItems = OnSetMediaItems;
			library.m_onSubscribe = null;
			library.m_onUnsubscribe = null;
			library.m_onMediaButtonEvent = null;
			library.m_onSearch = OnSearch;
			library.m_onGetSearchResult = OnGetSearchResult;


			// Wrap m_player so both the in-app MediaController and Android Auto
			// route next/prev through PulseAppForwardingPlayer, which rewrites
			// those into 10s seeks for series items. m_player stays as-is for
			// the service's own direct uses (ticker, error handling).
			PulseAppForwardingPlayer sessionPlayer = new PulseAppForwardingPlayer(m_player);
			MediaLibrarySession.Builder sessionBuilder = new MediaLibrarySession.Builder(this, sessionPlayer, library);
			PendingIntent sessionActivity = BuildSessionActivity();
			if (sessionActivity != null)
			{
				sessionBuilder.SetSessionActivity(sessionActivity);
			}
			m_session = sessionBuilder.Build();

			m_carReceiver = new CarConnectionReceiver(PausePlayback);
			Android.Content.IntentFilter carFilter = new Android.Content.IntentFilter("com.google.android.gms.car.media.STATUS");
			if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
			{
				RegisterReceiver(m_carReceiver, carFilter, Android.Content.ReceiverFlags.Exported);
			}
			else
			{
				RegisterReceiver(m_carReceiver, carFilter);
			}
		}

		//Custom call for online management
		public void OnOnlineStateChanged(bool online)
		{
			if (online == m_bIsOnline)
				return;

			m_bIsOnline = online;

			if (!online)
			{
				return;
			}
			MainThread.BeginInvokeOnMainThread(() =>
			{
				if (m_player == null)
				{
					return;
				}
				if (m_player.PlayerError == null)
				{
					return;
				}

				// re-cache the stalled current item, then re-prepare from where it died
				MediaItem current = m_player.CurrentMediaItem;
				if (current == null || string.IsNullOrEmpty(current.MediaId))
				{
					return;
				}

				string trackId = AAutoHelper.StripTrackPrefix(current.MediaId);
				s_mediaClient.CacheTrackAudio(trackId, (success) =>
				{
					if (!success)
					{
						return;
					}
					MainThread.BeginInvokeOnMainThread(() =>
					{
						m_player.Prepare();  // clears the error, retries Open on current item
					});
				});
			});
		}

		public void OnPhoneDisconnected(MediaSession session, MediaSession.ControllerInfo controller)
		{
			bool isCarController = session.IsAutoCompanionController(controller) || session.IsAutomotiveController(controller);
			if (!isCarController)
			{
				return;
			}
			Log.Error("OnPhoneDisconnected - todo  pause now");
			//PausePlayback();
		}

		public MediaItem OnGetLibraryRoot(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, MediaLibraryService.LibraryParams libraryParams)
		{
			MediaItem root = MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Root), "Pulse");
			return root;
		}
		public MediaItem OnGetItem(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, string mediaId)
		{
			string mediaTitle = "";

			MediaItem item = MediaItemBuilder.BuildItemForId(mediaId, mediaTitle);
			return item;
		}
		
		public IListenableFuture OnSetMediaItems(MediaSession session, MediaSession.ControllerInfo controller, IList<MediaItem> mediaItems, int startIndex, long startPositionMs)
		{
			AAutoHelper.LoadMediaSetFunc media = new AAutoHelper.LoadMediaSetFunc(this, mediaItems, startIndex, startPositionMs);
			return (IListenableFuture)CallbackToFutureAdapter.GetFuture(media);
		}
		private bool IsSignedIn()
		{
			return !string.IsNullOrEmpty(PulseAppSettings.GetUserID());
		}

		private List<MediaItem> BuildRootCategories()
		{
			List<MediaItem> categories = new List<MediaItem>();
			categories.Add(MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Home), "Home"));
			categories.Add(MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Playlists), "Playlists"));
			categories.Add(MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Library), "Library"));
			categories.Add(MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Podcasts), "Podcasts"));
			categories.Add(MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Audiobooks), "Audiobooks"));
			return categories;
		}

		public static void NotifyBrowseChanged()
		{
			PulseAppMediaLibraryService instance = s_instance;
			if (instance != null)
			{
				instance.NotifyRootChildrenChanged();
			}
		}

		private void NotifyRootChildrenChanged()
		{
			if (m_session == null)
			{
				return;
			}
			List<MediaItem> categories = BuildRootCategories();
			m_session.NotifyChildrenChanged(MediaItemBuilder.GetId(eAADirectory.Root), categories.Count, null);
		}

		public IListenableFuture OnGetChildren(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, string parentId, int page, int pageSize, MediaLibraryService.LibraryParams libraryParams)
		{
			if (!IsSignedIn())
			{
				AAutoHelper.LoadJavaObjectFunc notReady = new AAutoHelper.LoadJavaObjectFunc(LibraryResult.OfItemList(new List<MediaItem>(), MediaItemBuilder.BuildContentStyleParams()));
				return (IListenableFuture)CallbackToFutureAdapter.GetFuture(notReady);
			}

			eAADirectory dir = eAADirectory.Root;
			bool isDir = MediaItemBuilder.TryGetDirectory(parentId, out dir);
			
			if (isDir && dir == eAADirectory.Root)
			{
				AAutoHelper.LoadJavaObjectFunc loadHome = new AAutoHelper.LoadJavaObjectFunc(LibraryResult.OfItemList(BuildRootCategories(), MediaItemBuilder.BuildContentStyleParams()));
				return (IListenableFuture)CallbackToFutureAdapter.GetFuture(loadHome);
			}
			if (isDir)
			{
				AAutoHelper.LoadContainerFunc loadContainer = new AAutoHelper.LoadContainerFunc(this, dir, parentId, page, pageSize);
				return (IListenableFuture)CallbackToFutureAdapter.GetFuture(loadContainer);
			}
			else
			{
				eAAObject aaObject;
				if (!MediaItemBuilder.TryGetObject(parentId, out aaObject))
				{
					//parentId didn't match any known object prefix, return empty so AA shows nothing rather than dispatching a guess to PulseAppData
					AAutoHelper.LoadJavaObjectFunc unknown = new AAutoHelper.LoadJavaObjectFunc(LibraryResult.OfItemList(new List<MediaItem>(), MediaItemBuilder.BuildContentStyleParams()));
					return (IListenableFuture)CallbackToFutureAdapter.GetFuture(unknown);
				}
				string mediaId = AAutoHelper.ParseValue(parentId);
				AAutoHelper.LoadObjectFunc loadObject = new AAutoHelper.LoadObjectFunc(this, aaObject, mediaId, page, pageSize);
				return (IListenableFuture)CallbackToFutureAdapter.GetFuture(loadObject);
			}
		}




		// Two-step search per the Media3 contract: OnSearch kicks off the
		// async query and returns an OfVoid future immediately so AA knows
		// the request was accepted; once results land we cache them keyed
		// by query and call NotifySearchResultChanged on the session,
		// which prompts AA to come back via OnGetSearchResult and pick the
		// items up. The cache survives across calls so a follow-up query
		// for the same string is a free hit.
		public IListenableFuture OnSearch(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, string query, MediaLibraryService.LibraryParams libraryParams)
		{
			if (!IsSignedIn())
			{
				AAutoHelper.LoadJavaObjectFunc notReady = new AAutoHelper.LoadJavaObjectFunc(LibraryResult.OfVoid(libraryParams));
				return (IListenableFuture)CallbackToFutureAdapter.GetFuture(notReady);
			}

			MediaLibraryService.MediaLibrarySession capturedSession = session;
			MediaSession.ControllerInfo capturedBrowser = browser;
			string capturedQuery = query;
			MediaLibraryService.LibraryParams capturedParams = libraryParams;
			s_mediaClient.Search(query, (results) =>
			{
				List<MediaItem> items = MediaItemBuilder.BuildSearchItems(results);
				m_searchResults[capturedQuery] = items;
				try
				{
					capturedSession.NotifySearchResultChanged(capturedBrowser, capturedQuery, items.Count, capturedParams);
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
			});
			AAutoHelper.LoadJavaObjectFunc accepted = new AAutoHelper.LoadJavaObjectFunc(LibraryResult.OfVoid(libraryParams));
			return (IListenableFuture)CallbackToFutureAdapter.GetFuture(accepted);
		}

		public IListenableFuture OnGetSearchResult(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, string query, int page, int pageSize, MediaLibraryService.LibraryParams libraryParams)
		{
			List<MediaItem> items;
			if (!m_searchResults.TryGetValue(query, out items))
			{
				items = new List<MediaItem>();
			}
			AAutoHelper.LoadJavaObjectFunc result = new AAutoHelper.LoadJavaObjectFunc(LibraryResult.OfItemList(items, MediaItemBuilder.BuildContentStyleParams()));
			return (IListenableFuture)CallbackToFutureAdapter.GetFuture(result);
		}


		/// <summary>
		/// AA's OnSetMediaItems hands us a mixed bag of input MediaItems: a
		/// single "track/<id>" (one song the user tapped), or a single
		/// "<type>play/<id>" / "<type>shuffle/<id>" placeholder (the user
		/// tapped "Play all" / "Shuffle" on a container), or in principle a
		/// list combining both. Container ids need a PulseAppData fetch to expand
		/// into actual tracks before we can build the player queue; that fetch
		/// is done synchronously per item via GetTrackIds so this function
		/// stays linear except for the start-track CacheTrack hop at the end.
		/// </summary>
		/// <param name="items"></param>
		/// <param name="startIndex"></param>
		/// <param name="startPositionMs"></param>
		/// <param name="callback"></param>
		public async void LoadMediaItems(IList<MediaItem> items, int startIndex, long startPositionMs, JObjectCallback callback)
		{
			try
			{
				m_currentQueueID = m_currentQueueID + 1;

				int cacheQueueID = m_currentQueueID;

				//silence the outgoing queue while we resolve and cache the new one,
				//Media3 will resume playback when the new queue lands
				if (m_player != null && m_player.IsPlaying)
				{
					m_player.Pause();
				}

				List<MediaItem> outputTracks = new List<MediaItem>();

				if (items == null || items.Count == 0)
				{
					MediaSession.MediaItemsWithStartPosition empty = new MediaSession.MediaItemsWithStartPosition(outputTracks, 0, startPositionMs);
					callback.OnComplete(empty);
					return;
				}

				bool isOnline = Http.IsNetworkAvailable();

				//grab all our tracks at once
				Task<List<PulseTrack>>[] fetchedTracks = new Task<List<PulseTrack>>[items.Count];
				for (int i = 0; i < items.Count; i++)
				{
					fetchedTracks[i] = GetTrackIdsAsync(items[i]);
				}
				await Task.WhenAll(fetchedTracks);


				List<PulseTrack> requestedTracks = new List<PulseTrack>();
				int startItemIndex = 0;
				for (int i = 0; i < items.Count; i++)
				{
					if (i == startIndex)
					{
						//this is the new potential index if tracks before it weren't included
						startItemIndex = requestedTracks.Count;
					}

					List<PulseTrack> tracks = fetchedTracks[i].Result;
					for (int j = 0; j < tracks.Count; j++)
					{
						//filter out unavailable tracks
						if (!isOnline && !s_mediaClient.IsTrackCached(tracks[j].Id))
						{
							continue;
						}
						requestedTracks.Add(tracks[j]);
					}
				}

				//our startItem was filtered out so we'll reset our index
				if (startItemIndex >= requestedTracks.Count)
				{
					startItemIndex = 0;
				}

				//nothing's avialable
				if (requestedTracks.Count == 0)
				{
					MediaSession.MediaItemsWithStartPosition empty = new MediaSession.MediaItemsWithStartPosition(outputTracks, 0, startPositionMs);
					callback.OnComplete(empty);
					return;
				}

				//Tracks beyond our first one that we want to precache ahead
				Queue<PulseTrack> cacheQueue = new Queue<PulseTrack>();
				for (int i = 0; i < requestedTracks.Count; i++)
				{
					outputTracks.Add(MediaItemBuilder.Build(requestedTracks[i]));
					if (i != startItemIndex)
					{
						cacheQueue.Enqueue(requestedTracks[i]);
					}
				}

				PulseTrack startTrack = requestedTracks[startItemIndex];
				int finalStartIndex = startItemIndex;
				long finalStartPosition = startPositionMs;

				//someone started another play request before we reached here, bail
				if (cacheQueueID != m_currentQueueID)
				{
					MediaSession.MediaItemsWithStartPosition empty = new MediaSession.MediaItemsWithStartPosition(outputTracks, 0, startPositionMs);
					callback.OnComplete(empty);
					return;
				}

				//start our track right away, we'll stream it live
				MediaSession.MediaItemsWithStartPosition result = new MediaSession.MediaItemsWithStartPosition(outputTracks, finalStartIndex, finalStartPosition);
				callback.OnComplete(result);

				// Wait for the initial spinup then start caching the rest
				// kicking this off immidiately was stealing cycles from the codec bootup causing delays
				Task.Delay(5000).ContinueWith((_) => CacheQueued(cacheQueue, cacheQueueID));
			}
			catch(Exception ex)
			{
				Log.Exception(ex);
			}
		}

		
		private void CacheQueued(Queue<PulseTrack> queue, int queueId)
		{
			if (queue == null || queue.Count == 0 || queueId != m_currentQueueID)
			{
				Log.Info("MediaService: Cancelled existing download queue for replacement");
				return;
			}
			PulseTrack next = queue.Dequeue();
			s_mediaClient.CacheTrackAudio(next.Id, (success)=>
			{
				CacheQueued(queue, queueId);
			});
		}

		Task<List<PulseTrack>> GetTrackIdsAsync(MediaItem item)
		{
			TaskCompletionSource<List<PulseTrack>> tcs = new TaskCompletionSource<List<PulseTrack>>();
			GetTrackIds(item, (tracks) => tcs.TrySetResult(tracks));
			return tcs.Task;
		}

		private void GetTrackIds(MediaItem input, Action<List<PulseTrack>> onComplete)
		{
			if (onComplete == null)
				return;

			string mediaId = input.MediaId;
			if (string.IsNullOrEmpty(mediaId))
			{
				onComplete(new List<PulseTrack>());
				return;
			}

			// Single track:
			if (mediaId.StartsWith("track/"))
			{
				string trackId = AAutoHelper.StripTrackPrefix(mediaId);
				
				s_mediaClient.GetTrack(trackId, (pulseTrack)=>
				{
					List<PulseTrack> trackList = new List<PulseTrack>();
					if (pulseTrack != null)
						trackList.Add(pulseTrack);
					onComplete(trackList);
				});
				return;
			}

			// Audiobook chapter tapped: queue from this chapter to the end of
			// the book so chapters auto-advance. Re-fetch the book to rebuild
			// every chapter's clip window / shared StreamId (a plain getTrack on
			// the chapter id would lose them), then slice from the tapped one.
			string chapterBookId;
			string chapterId;
			if (MediaItemBuilder.TryParseChapterMediaId(mediaId, out chapterBookId, out chapterId))
			{
				s_mediaClient.GetAudiobook(chapterBookId, (details) =>
				{
					List<PulseTrack> allChapters = BuildAudiobookChapterTracks(details);
					List<PulseTrack> fromChapter = new List<PulseTrack>();
					int start = -1;
					for (int i = 0; i < allChapters.Count; i++)
					{
						if (allChapters[i].Id == chapterId)
						{
							start = i;
							break;
						}
					}
					if (start < 0)
					{
						start = 0;
					}
					for (int i = start; i < allChapters.Count; i++)
					{
						fromChapter.Add(allChapters[i]);
					}
					onComplete(fromChapter);
				});
				return;
			}

			eAAObject objectType;
			string objectId;
			bool isShuffle;
			if (!MediaItemBuilder.TryParsePlayMediaId(mediaId, out objectType, out objectId, out isShuffle))
			{
				Log.Warn("PulseAppMediaLibraryService.LoadMediaItems: unknown mediaId prefix '" + mediaId + "', skipping");
				onComplete(new List<PulseTrack>());
				return;
			}

			GetTracksFor(objectType, objectId, (list) =>
			{
				List<PulseTrack> trackList = new List<PulseTrack>(list);
				if (isShuffle)
				{
					ShuffleInPlace(trackList);
				}
				onComplete(trackList);
			});
			
		}

		private void GetTracksFor(eAAObject objectType, string objectId, Action<List<PulseTrack>> onComplete)
		{
		
			//Lambda exception to avoid too many indirection calls
			bool fired = false;
			// PulseAppData NetworkAuthorative routes can fire the callback twice
			// (cached burst + refresh) by design; this is a one-shot consumer
			// so guard against the second hit.
			Action<List<PulseTrack>> onDataComplete = (tracks) =>
			{
				if (fired)
				{
					return;
				}

				fired = true;

				List<PulseTrack> outputTracks = new List<PulseTrack>();
				if (tracks != null)
				{
					//return this to the caller via the closure
					outputTracks = tracks;
				}
				onComplete(outputTracks);
			};

			
			switch (objectType)
			{
				case eAAObject.Album:
					s_mediaClient.GetAlbum(objectId, (album) => 
					{
						onDataComplete(album.Tracks);
					});
					break;
				case eAAObject.Playlist:
					s_mediaClient.GetPlaylist(objectId, (playlist) =>
					{
						onDataComplete(playlist.Tracks);
					});
					break;
				case eAAObject.Artist:
					s_mediaClient.GetArtistTracks(objectId, (trackList) =>
					{
						onDataComplete(trackList);
					});
					break;
				case eAAObject.Genre:
					s_mediaClient.GetTracksForGenre(objectId, (genre) =>
					{
						onDataComplete(genre);
					});
					break;
				case eAAObject.Podcast:
					s_mediaClient.GetPodcast(objectId, (details) =>
					{
						onDataComplete(BuildPodcastEpisodeTracks(details));
					});
					break;
				case eAAObject.Audiobook:
					s_mediaClient.GetAudiobook(objectId, (details) =>
					{
						onDataComplete(BuildAudiobookChapterTracks(details));
					});
					break;
				case eAAObject.SmartQueue:
					s_mediaClient.GetSmartQueue(objectId, (details) =>
					{
						List<PulseTrack> queueTracks = new List<PulseTrack>();
						if (details != null && details.Tracks != null)
						{
							queueTracks = details.Tracks;
						}
						onDataComplete(queueTracks);
					});
					break;
				default:
					onDataComplete(new List<PulseTrack>());
					break;
			}
		}

		// Adapter from the podcast details wire-type to the PulseTrack list the
		// player/queue expects. The stream endpoint resolves either a track id or
		// an episode id, so an episode's Id is the track Id. Series art is the
		// fallback when the episode payload lacks its own cover.
		private List<PulseTrack> BuildPodcastEpisodeTracks(PulsePodcastDetails details)
		{
			List<PulseTrack> tracks = new List<PulseTrack>();
			if (details == null || details.Episodes == null)
			{
				return tracks;
			}
			string podcastTitle = "";
			string podcastArt = "";
			if (details.Series != null)
			{
				podcastTitle = details.Series.Title;
				podcastArt = details.Series.CoverArt;
			}
			for (int index = 0; index < details.Episodes.Count; index++)
			{
				PulsePodcastEpisode episode = details.Episodes[index];
				PulseTrack track = new PulseTrack();
				track.Id = episode.Id;
				track.Title = episode.Title;
				track.Artist = podcastTitle;
				track.Album = podcastTitle;
				if (string.IsNullOrEmpty(episode.CoverArt))
				{
					track.CoverArt = podcastArt;
				}
				else
				{
					track.CoverArt = episode.CoverArt;
				}
				track.Duration = episode.Duration;
				track.IsSeries = true;
				track.SeriesKind = ePulseSeriesKind.Podcast;
				track.ResumePositionSeconds = episode.PositionSeconds;
				tracks.Add(track);
			}
			return tracks;
		}

		// Adapter from the audiobook details wire-type to the PulseTrack list the
		// player/queue expects, mirroring the in-app AudiobookDetailView. Each
		// chapter carries its clip window (StartMs/EndMs) and StreamId so
		// MediaItemBuilder.Build clips single-file books to the chapter and
		// collapses every chapter of one file onto a single cached stream.
		// IsSeries flags them for the ±10s seek behaviour on the media buttons.
		private List<PulseTrack> BuildAudiobookChapterTracks(PulseAudiobookDetails details)
		{
			List<PulseTrack> tracks = new List<PulseTrack>();
			if (details == null || details.Chapters == null)
			{
				return tracks;
			}
			string bookTitle = "";
			string bookAuthor = "";
			string bookArt = "";
			if (details.Book != null)
			{
				bookTitle = details.Book.Title;
				bookAuthor = details.Book.Author;
				bookArt = details.Book.CoverArt;
			}
			// The wire order isn't guaranteed; play in chapter order (matches the
			// in-app AudiobookDetailView, which sorts by OrderIndex).
			List<PulseChapter> ordered = new List<PulseChapter>(details.Chapters);
			ordered.Sort((first, second) => first.OrderIndex.CompareTo(second.OrderIndex));
			for (int index = 0; index < ordered.Count; index++)
			{
				PulseChapter chapter = ordered[index];
				PulseTrack track = new PulseTrack();
				track.Id = chapter.Id;
				track.Title = chapter.Title;
				track.Artist = bookAuthor;
				track.Album = bookTitle;
				if (string.IsNullOrEmpty(chapter.CoverArt))
				{
					track.CoverArt = bookArt;
				}
				else
				{
					track.CoverArt = chapter.CoverArt;
				}
				track.Duration = chapter.Duration;
				track.IsSeries = true;
				track.SeriesKind = ePulseSeriesKind.Audiobook;
				track.ResumePositionSeconds = chapter.PositionSeconds;
				track.StartMs = chapter.StartMs;
				track.EndMs = chapter.EndMs;
				track.StreamId = chapter.StreamId;
				tracks.Add(track);
			}
			return tracks;
		}

		private static void ShuffleInPlace<T>(List<T> list)
		{
			if (list == null || list.Count <= 1)
			{
				return;
			}
			for (int i = list.Count - 1; i > 0; i--)
			{
				int j = s_shuffleRand.Next(i + 1);
				T tmp = list[i];
				list[i] = list[j];
				list[j] = tmp;
			}
		}

		public void LoadContainer(eAADirectory parent, JObjectCallback request)
		{
			switch (parent)
			{
				case eAADirectory.Home:
					{
						int tileLimit = 10;
						List<MediaItem> combined = new List<MediaItem>();
						s_mediaClient.GetRecentlyPlayed((A)=>
						{
							List<PulseObject> recentlyPlayed = new List<PulseObject>();
							for(int i = 0; i < A.Count && i < tileLimit; i++)
							{
								recentlyPlayed.Add(A[i]);
							}

							s_mediaClient.GetTopPlaylists((C)=>
							{
								List<PulseObject> topPlaylists = new List<PulseObject>();
								for (int i = 0; i < C.Count && i < tileLimit; i++)
								{
									topPlaylists.Add(C[i]);
								}
								s_mediaClient.GetPopularArtists((D)=>
								{
									List<PulseObject> artists = new List<PulseObject>();
									for (int i = 0; i < D.Count && i < tileLimit; i++)
									{
										artists.Add(D[i]);
									}

									combined.Add(MediaItemBuilder.BuildPlayableItem(MediaItemBuilder.BuildPlayMediaId(eAAObject.SmartQueue, "personalized"), "Personalized Tracks", ""));
									combined.Add(MediaItemBuilder.BuildPlayableItem(MediaItemBuilder.BuildPlayMediaId(eAAObject.SmartQueue, "popular"), "Popular Tracks", ""));
									combined.AddRange(MediaItemBuilder.BuildMixedItemsGrouped(recentlyPlayed, "Recently Played"));
									combined.AddRange(MediaItemBuilder.BuildMixedItemsGrouped(topPlaylists, "Top Playlists"));
									combined.AddRange(MediaItemBuilder.BuildMixedItemsGrouped(artists, "Popular Artists"));

									request.OnComplete(combined);
								});
							});
							
						});
						break;
					}
				case eAADirectory.Podcasts:
					{
						s_mediaClient.GetPodcasts((podcasts) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItems(podcasts);
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.Audiobooks:
					{
						s_mediaClient.GetAudiobooks((audiobooks) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItems(audiobooks);
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.Library:
					{
						// Library is a navigation hub, not a data fetch — it lists
						// the four library sub-categories so the user can drill in.
						List<MediaItem> categories = new List<MediaItem>();
						categories.Add(MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Albums), "Albums"));
						categories.Add(MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Playlists), "Playlists"));
						categories.Add(MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Artists), "Artists"));
						categories.Add(MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Genres), "Genres"));
						request.OnComplete(categories);
						break;
					}
				case eAADirectory.RecentlyPlayed:
					{
						s_mediaClient.GetRecentlyPlayed((recentlyPlayed) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItems(recentlyPlayed);
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.TopPlaylists:
					{
						s_mediaClient.GetTopPlaylists((topPlaylists) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItems(topPlaylists);
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.PopularArtists:
					{
						s_mediaClient.GetPopularArtists((popularArtists) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItems(popularArtists);
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.Albums:
					{
						s_mediaClient.GetAlbums((albums) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItems(albums);
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.Playlists:
					{
						s_mediaClient.GetPlaylists((playlists) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItems(playlists);
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.Artists:
					{
						s_mediaClient.GetArtists((artists) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItems(artists);
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.Genres:
					{
						s_mediaClient.GetGenres((genres) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItems(genres);
							request.OnComplete(items);
						});
						break;
					}
			}
		}

		public void LoadObject(eAAObject objectType, string objectID, JObjectCallback request)
		{
			switch (objectType)
			{
				case eAAObject.Album:
					s_mediaClient.GetAlbum(objectID, (album)=>
					{
						request.OnComplete<PulseAlbum>(album.Tracks, objectType, objectID);
					});
					break;
				case eAAObject.Artist:
					s_mediaClient.GetArtistAlbums(objectID, (albums) =>
					{
						List<MediaItem> items = MediaItemBuilder.BuildArtistChildren(objectID, albums);
						request.OnComplete(items);
					});
					break;
				case eAAObject.Playlist:
					s_mediaClient.GetPlaylist(objectID, (playlist) =>
					{
						request.OnComplete<PulseAlbum>(playlist.Tracks, objectType, objectID);
					});
					break;
				case eAAObject.Genre:
					s_mediaClient.GetTracksForGenre(objectID, (genreTracks) =>
					{
						request.OnComplete<PulseAlbum>(genreTracks, objectType, objectID);
					});
					break;
				case eAAObject.Podcast:
					s_mediaClient.GetPodcast(objectID, (details) =>
					{
						List<PulseTrack> episodeTracks = BuildPodcastEpisodeTracks(details);
						request.OnComplete<PulseAlbum>(episodeTracks, objectType, objectID);
					});
					break;
				case eAAObject.Audiobook:
					s_mediaClient.GetAudiobook(objectID, (details) =>
					{
						List<PulseTrack> chapterTracks = BuildAudiobookChapterTracks(details);
						request.OnComplete<PulseAlbum>(chapterTracks, objectType, objectID);
					});
					break;
			}
		}

		private void PausePlayback()
		{
			if (m_player == null)
			{
				return;
			}
			if (!m_player.IsPlaying)
			{
				return;
			}
			m_player.Pause();
		}

		private static MediaClient BuildMediaClient(IMediaClientHost host)
		{
			PulseAppCache cache = new PulseAppCache();
			MediaClient pulseClient = new PulseClient(cache, host);
			pulseClient.SetServerParams(PulseAppSettings.GetServerIp(), PulseAppSettings.GetServerPort(), PulseAppSettings.GetUsername(), PulseAppSettings.GetPassword(), PulseAppSettings.GetUseHttps());

			return pulseClient;
		}

		private PendingIntent BuildSessionActivity()
		{
			Intent intent = PackageManager.GetLaunchIntentForPackage(PackageName);
			if (intent == null)
			{
				return null;
			}
			return PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
		}

		public override MediaLibrarySession OnGetSessionFromMediaLibraryService(MediaSession.ControllerInfo p0)
		{
			return m_session;
		}
		public override MediaLibraryService.MediaLibrarySession OnGetSession(MediaSession.ControllerInfo controllerInfo)
		{
			return m_session;
		}

		public override void OnTaskRemoved(Intent rootIntent)
		{
			bool keepRunning = false;
			if (m_player != null && m_player.PlayWhenReady && m_player.MediaItemCount > 0)
			{
				keepRunning = true;
			}
			if (!keepRunning)
			{
				StopSelf();
			}
			base.OnTaskRemoved(rootIntent);
		}

		public override void OnDestroy()
		{
			if (s_instance == this)
			{
				s_instance = null;
			}
			if (m_carReceiver != null)
			{
				try
				{
					UnregisterReceiver(m_carReceiver);
				}
				catch (Java.Lang.IllegalArgumentException exception)
				{
					Log.Warn("PulseAppPlaybackService: car connection receiver was not registered: " + exception.Message);
				}
				m_carReceiver = null;
			}
			if (m_session != null)
			{
				m_session.Release();
				m_session = null;
			}
			if (m_player != null)
			{
				m_player.Release();
				m_player = null;
			}
			base.OnDestroy();
		}

		private sealed class CarConnectionReceiver : Android.Content.BroadcastReceiver
		{
			private System.Action m_onDisconnected;

			public CarConnectionReceiver(System.Action onDisconnected)
			{
				m_onDisconnected = onDisconnected;
			}

			public override void OnReceive(Context context, Intent intent)
			{
				if (intent == null)
				{
					return;
				}
				string status = intent.GetStringExtra("media_connection_status");
				if (status != "media_disconnected")
				{
					return;
				}

				Log.Error("CarConnectionReceiver OnReceive - todo  pause now");
				return;

				if (m_onDisconnected == null)
				{
					return;
				}
				m_onDisconnected();
			}
		}
	}
}
