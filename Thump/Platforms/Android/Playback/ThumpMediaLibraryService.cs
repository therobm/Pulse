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
using AndroidX.Concurrent.Futures;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Source;
using AndroidX.Media3.Extractor;
using AndroidX.Media3.Extractor.Mp3;
using AndroidX.Media3.Session;
using Google.Common.Util.Concurrent;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using PulseAPI.CSharp;
using Thump.Data;
using Thump.Pulse;

namespace Thump.Playback.AndroidOS
{
	

	/// <summary>
	/// A lightweight process for Android's media player to leverage so it can access
	/// Thump specific data and functionality
	/// </summary>
	[Service(Exported = true, Enabled = true, Name = "com.therobm.thump.ThumpPlaybackService", ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
	[IntentFilter(new string[] { "androidx.media3.session.MediaLibraryService", "android.media.browse.MediaBrowserService" })]
	public class ThumpMediaLibraryService : MediaLibraryService, IMediaClientHost
	{
		/// <summary>
		/// A special sneaky global so the media service can access our data
		/// </summary>
		public static MediaClient s_mediaClient;

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

		// Two-step search: OnSearch kicks off the async fetch and parks the
		// resolved items under the query string; OnGetSearchResult reads them
		// out. Keyed by exact query text since AA calls back with the same
		// string it gave us.
		private System.Collections.Concurrent.ConcurrentDictionary<string, List<MediaItem>> m_searchResults = new System.Collections.Concurrent.ConcurrentDictionary<string, List<MediaItem>>();

		// Tracks the currently-playing trackId so we can fire analytics for the
		// OUTGOING track when ExoPlayer transitions to a new item or hits
		// STATE_ENDED, and a Started for the incoming one. Cleared after we
		// report on STATE_ENDED (which sticks for as many ticks as the user
		// leaves it sitting) so it can't re-fire the same trackId.
		private string m_lastReportedTrackId;

		// Prior-tick snapshot of the current track's playback state. When the
		// track changes, this tick's position already belongs to the NEW track
		// (~0), so the outgoing track is classified Completed-vs-Skipped from
		// the position/duration we stored on the previous tick. m_wasPlaying
		// drives pause detection (a playing->paused edge on the same track).
		private long m_lastPositionMs;
		private long m_lastDurationMs;
		private bool m_wasPlaying;

		// A track that advanced past this fraction of its duration counts as
		// Completed; anything earlier was a Skip. Mirrors the server's existing
		// <50%-is-a-skip heuristic but biased high so only deliberate skips of
		// the tail get counted against a track.
		private const double s_completedThreshold = 0.95;

		// Polled at this interval against m_player to detect track transitions
		// and queue-end. ExoPlayer's playback state is the source of truth.
		private const double s_analyticsTickIntervalMs = 500;
		private System.Timers.Timer m_analyticsTicker;
		private const int s_playbackStateEnded = 4;

		public override void OnCreate()
		{
			base.OnCreate();

			if (s_mediaClient == null)
			{
				s_mediaClient = BuildMediaClient(this);
			}

			ExoPlayerBuilder builder = new ExoPlayerBuilder(this);
			builder.SetHandleAudioBecomingNoisy(true);

			// Request audio focus so ExoPlayer activates the audio route on play; without this the player streams but is silent until another app grabs focus.
			AudioAttributes.Builder audioAttributesBuilder = new AudioAttributes.Builder();
			audioAttributesBuilder.SetUsage(C.UsageMedia);
			audioAttributesBuilder.SetContentType(C.AudioContentTypeMusic);
			AudioAttributes audioAttributes = audioAttributesBuilder.Build();
			builder.SetAudioAttributes(audioAttributes, true);

			DefaultExtractorsFactory extractorsFactory = new DefaultExtractorsFactory();
			extractorsFactory.SetMp3ExtractorFlags( Mp3Extractor.FlagEnableConstantBitrateSeeking | Mp3Extractor.FlagDisableId3Metadata);

			AndroidMediaDataSourceFactory dataSourceFactory = new AndroidMediaDataSourceFactory(s_mediaClient);
			DefaultMediaSourceFactory mediaSourceFactory = new DefaultMediaSourceFactory(dataSourceFactory, extractorsFactory);
			builder.SetMediaSourceFactory(mediaSourceFactory);

			m_player = builder.Build();

			//Authoritative track-analytics fire point. ExoPlayer drives the
			//actual playback regardless of who started the queue (in-app MAUI
			//via the MediaController, or Android Auto via its own browser),
			//so polling its state from a single timer here catches every
			//transition exactly once. The in-app ThumpAndroidPlayer used to
			//fire its own track analytics from a ticker but that
			//double-counted with this one and missed the AA-only case where
			//the MAUI app never opens - so it's gone, this is the single
			//source of truth. Polling instead of a Player.Listener because
			//the Xamarin binding doesn't expose Listener callbacks as C#
			//events on IExoPlayer and implementing IPlayerListener requires
			//stubbing all 38 methods.
			m_analyticsTicker = new System.Timers.Timer(s_analyticsTickIntervalMs);
			m_analyticsTicker.AutoReset = true;
			m_analyticsTicker.Elapsed += OnAnalyticsTick;
			m_analyticsTicker.Start();

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


			MediaLibrarySession.Builder sessionBuilder = new MediaLibrarySession.Builder(this, m_player, library);
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
			PausePlayback();
		}

		public MediaItem OnGetLibraryRoot(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, MediaLibraryService.LibraryParams libraryParams)
		{
			MediaItem root = MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Root), "Thump");
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
		public IListenableFuture OnGetChildren(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, string parentId, int page, int pageSize, MediaLibraryService.LibraryParams libraryParams)
		{
			eAADirectory dir = eAADirectory.Root;
			bool isDir = MediaItemBuilder.TryGetDirectory(parentId, out dir);
			
			if (isDir && dir == eAADirectory.Root)
			{
				List<MediaItem> categories = new List<MediaItem>();
				categories.Add(MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Home), "Home"));
				categories.Add(MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Playlists), "Playlists"));
				categories.Add(MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Library), "Library"));
				categories.Add(MediaItemBuilder.BuildBrowsableItem(MediaItemBuilder.GetId(eAADirectory.Podcasts), "Podcasts"));

				AAutoHelper.LoadJavaObjectFunc loadHome = new AAutoHelper.LoadJavaObjectFunc(LibraryResult.OfItemList(categories, MediaItemBuilder.BuildContentStyleParams()));
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
					//parentId didn't match any known object prefix, return empty so AA shows nothing rather than dispatching a guess to ThumpData
					AAutoHelper.LoadJavaObjectFunc unknown = new AAutoHelper.LoadJavaObjectFunc(LibraryResult.OfItemList(new List<MediaItem>(), MediaItemBuilder.BuildContentStyleParams()));
					return (IListenableFuture)CallbackToFutureAdapter.GetFuture(unknown);
				}
				string mediaId = AAutoHelper.ParseValue(parentId);
				AAutoHelper.LoadObjectFunc loadObject = new AAutoHelper.LoadObjectFunc(this, aaObject, mediaId, page, pageSize);
				return (IListenableFuture)CallbackToFutureAdapter.GetFuture(loadObject);
			}
		}

		// Timer fires on a thread-pool thread; ExoPlayer requires its API to
		// be called on the application's main thread, so bounce there before
		// reading state.
		private void OnAnalyticsTick(object sender, ElapsedEventArgs e)
		{
			MainThread.BeginInvokeOnMainThread(AnalyticsTickMainThread);
		}

		// Polls ExoPlayer once per tick and emits the full set of track-level
		// analytics state changes:
		//  - Started   on a new current MediaItem (auto-advance, next/prev/seek,
		//              queue replacement, and the first track all surface here),
		//              including the very first track played.
		//  - Skipped /
		//    Completed for the OUTGOING track on that same transition, decided
		//              from the PREVIOUS tick's position/duration (this tick's
		//              position already belongs to the new track).
		//  - Completed on STATE_ENDED, the queue-end case where the last item
		//              stays sitting as current; m_lastReportedTrackId is cleared
		//              so steady ticks don't re-fire.
		//  - Paused    on a playing->paused edge for the same track.
		// Collection-level Started (album/artist/playlist) is reported from the
		// UI side in MainView, the only place that knows the collection id.
		private void AnalyticsTickMainThread()
		{
			if (m_player == null)
			{
				return;
			}

			MediaItem currentItem = m_player.CurrentMediaItem;
			string currentMediaId = null;
			if (currentItem != null)
			{
				currentMediaId = currentItem.MediaId;
			}

			bool isPlaying = m_player.IsPlaying;
			long positionMs = m_player.CurrentPosition;
			long durationMs = m_player.Duration;

			if (currentMediaId != m_lastReportedTrackId)
			{
				if (!string.IsNullOrEmpty(m_lastReportedTrackId))
				{
					PulseAnalytics.eAction action = PulseAnalytics.eAction.Skipped;
					if (m_lastDurationMs > 0 && m_lastPositionMs >= (long)(m_lastDurationMs * s_completedThreshold))
					{
						action = PulseAnalytics.eAction.Completed;
					}
					s_mediaClient.ReportAnalytics(m_lastReportedTrackId, eDataType.Track, action);
				}
				if (!string.IsNullOrEmpty(currentMediaId))
				{
					s_mediaClient.ReportAnalytics(currentMediaId, eDataType.Track, PulseAnalytics.eAction.Started);
				}
				m_lastReportedTrackId = currentMediaId;
				m_wasPlaying = isPlaying;
				m_lastPositionMs = positionMs;
				m_lastDurationMs = durationMs;
				return;
			}

			if (m_player.PlaybackState == s_playbackStateEnded)
			{
				if (!string.IsNullOrEmpty(m_lastReportedTrackId))
				{
					string previous = m_lastReportedTrackId;
					m_lastReportedTrackId = null;
					s_mediaClient.ReportAnalytics(previous, eDataType.Track, PulseAnalytics.eAction.Completed);
				}
				m_wasPlaying = false;
				m_lastPositionMs = 0;
				m_lastDurationMs = 0;
				return;
			}

			if (m_wasPlaying && !isPlaying && !string.IsNullOrEmpty(m_lastReportedTrackId))
			{
				s_mediaClient.ReportAnalytics(m_lastReportedTrackId, eDataType.Track, PulseAnalytics.eAction.Paused);
			}

			m_wasPlaying = isPlaying;
			m_lastPositionMs = positionMs;
			m_lastDurationMs = durationMs;
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
		/// list combining both. Container ids need a ThumpData fetch to expand
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

				bool isOnline = s_mediaClient.IsOnline();

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


				int cacheQueueID = m_currentQueueID;
				s_mediaClient.CacheTrackAudio(startTrack.Id, (success) =>
				{
					if (cacheQueueID != m_currentQueueID)
						return;

					MediaSession.MediaItemsWithStartPosition result = new MediaSession.MediaItemsWithStartPosition(outputTracks, finalStartIndex, finalStartPosition);
					callback.OnComplete(result);

					// Wait for the initial spinup then start caching the rest
					// kicking this off immidiately was stealing cycles from the codec bootup causing delays
					Task.Delay(1000).ContinueWith((_) => CacheQueued(cacheQueue, cacheQueueID));
					//CacheQueued(cacheQueue, cacheQueueID);
				});
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

			eAAObject objectType;
			string objectId;
			bool isShuffle;
			if (!MediaItemBuilder.TryParsePlayMediaId(mediaId, out objectType, out objectId, out isShuffle))
			{
				Log.Warn("ThumpMediaLibraryService.LoadMediaItems: unknown mediaId prefix '" + mediaId + "', skipping");
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
			// ThumpData NetworkAuthorative routes can fire the callback twice
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
				default:
					onDataComplete(new List<PulseTrack>());
					break;
			}
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
							List<MediaItem> items = MediaItemBuilder.BuildMixedItemsGrouped(podcasts, "Podcasts");
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
			ThumpCache cache = new ThumpCache();
			MediaClient pulseClient = new PulseClient(cache, host);
			pulseClient.SetServerParams(ThumpSettings.GetServerIp(), ThumpSettings.GetServerPort(), ThumpSettings.GetUsername(), ThumpSettings.GetPassword(), ThumpSettings.GetUseHttps());

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
			if (m_carReceiver != null)
			{
				try
				{
					UnregisterReceiver(m_carReceiver);
				}
				catch (Java.Lang.IllegalArgumentException exception)
				{
					Log.Warn("ThumpPlaybackService: car connection receiver was not registered: " + exception.Message);
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
				if (m_onDisconnected == null)
				{
					return;
				}
				m_onDisconnected();
			}
		}
	}
}
