using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
using Microsoft.Maui.Storage;
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
	public class ThumpMediaLibraryService : MediaLibraryService
	{
		/// <summary>
		/// A special sneaky global so the media service can access our data
		/// </summary>
		public static ThumpData s_thumpData;

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

		public override void OnCreate()
		{
			base.OnCreate();

			if (s_thumpData == null)
			{
				s_thumpData = BuildThumpData();
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

			AndroidMediaDataSourceFactory dataSourceFactory = new AndroidMediaDataSourceFactory(s_thumpData);
			DefaultMediaSourceFactory mediaSourceFactory = new DefaultMediaSourceFactory(dataSourceFactory, extractorsFactory);
			builder.SetMediaSourceFactory(mediaSourceFactory);

			m_player = builder.Build();

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
				AAutoHelper.LoadContainerFunc loadContainer = new AAutoHelper.LoadContainerFunc(this, dir, parentId);
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
				AAutoHelper.LoadObjectFunc loadObject = new AAutoHelper.LoadObjectFunc(this, aaObject, mediaId);
				return (IListenableFuture)CallbackToFutureAdapter.GetFuture(loadObject);
			}
		}

		// AA's OnSetMediaItems hands us a mixed bag of input MediaItems: a
		// single "track/<id>" (one song the user tapped), or a single
		// "<type>play/<id>" / "<type>shuffle/<id>" placeholder (the user
		// tapped "Play all" / "Shuffle" on a container), or in principle a
		// list combining both. Container ids need a ThumpData fetch to expand
		// into actual tracks before we can build the player queue; that fetch
		// is done synchronously per item via GetTrackIds so this function
		// stays linear except for the start-track CacheTrack hop at the end.
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

				bool isOnline = s_thumpData.IsOnline();

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
						if (!isOnline && !s_thumpData.IsTrackCached(tracks[j]))
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
				s_thumpData.CacheTrack(startTrack, (success) =>
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
			s_thumpData.CacheTrack(next, (success)=>
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
				
				s_thumpData.GetTrack(trackId, (pulseTrack)=>
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
					s_thumpData.GetTracksForAlbum(objectId, onDataComplete);
					break;
				case eAAObject.Playlist:
					s_thumpData.GetTracksForPlaylist(objectId, onDataComplete);
					break;
				case eAAObject.Artist:
					s_thumpData.GetTracksForArtist(objectId, onDataComplete);
					break;
				case eAAObject.Genre:
					s_thumpData.GetTracksForGenre(objectId, onDataComplete);
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
						s_thumpData.GetRecentlyPlayed((A)=>
						{
							List<PulseObject> recentlyPlayed = new List<PulseObject>();
							for(int i = 0; i < A.Count && i < tileLimit; i++)
							{
								recentlyPlayed.Add(A[i]);
							}

							s_thumpData.GetRecentlyAdded((B)=>
							{
								List<PulseObject> added = new List<PulseObject>();
								for (int i = 0; i < B.Count && i < tileLimit; i++)
								{
									added.Add(B[i]);
								}
								s_thumpData.GetTopPlaylists((C)=>
								{
									List<PulseObject> topPlaylists = new List<PulseObject>();
									for (int i = 0; i < C.Count && i < tileLimit; i++)
									{
										topPlaylists.Add(C[i]);
									}
									s_thumpData.GetPopularArtists((D)=>
									{
										List<PulseObject> artists = new List<PulseObject>();
										for (int i = 0; i < D.Count && i < tileLimit; i++)
										{
											artists.Add(D[i]);
										}

										combined.AddRange(MediaItemBuilder.BuildMixedItemsGrouped(recentlyPlayed, "Recently Played"));
										combined.AddRange(MediaItemBuilder.BuildMixedItemsGrouped(added, "Recently Added"));
										combined.AddRange(MediaItemBuilder.BuildMixedItemsGrouped(topPlaylists, "Top Playlists"));
										combined.AddRange(MediaItemBuilder.BuildMixedItemsGrouped(artists, "Popular Artists"));

										request.OnComplete(combined);
									});
								});
							});
						});
						break;
					}
				case eAADirectory.Podcasts:
					{
						s_thumpData.GetPodcasts((podcasts) =>
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
						s_thumpData.GetRecentlyPlayed((recentlyPlayed) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItemsGrouped(recentlyPlayed, "Recently Played");
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.RecentlyAdded:
					{
						s_thumpData.GetRecentlyAdded((recentlyAdded) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItemsGrouped(recentlyAdded, "Recently Added");
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.TopPlaylists:
					{
						s_thumpData.GetTopPlaylists((topPlaylists) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItemsGrouped(topPlaylists, "Top Playlists");
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.PopularArtists:
					{
						s_thumpData.GetPopularArtists((popularArtists) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItemsGrouped(popularArtists, "Popular Artists");
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.Albums:
					{
						s_thumpData.GetAlbums((albums) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItemsGrouped(albums, "Albums");
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.Playlists:
					{
						s_thumpData.GetPlaylists((playlists) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItemsGrouped(playlists, "Playlists");
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.Artists:
					{
						s_thumpData.GetArtists((artists) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItemsGrouped(artists, "Artists");
							request.OnComplete(items);
						});
						break;
					}
				case eAADirectory.Genres:
					{
						s_thumpData.GetGenres((genres) =>
						{
							List<MediaItem> items = MediaItemBuilder.BuildMixedItemsGrouped(genres, "Genres");
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
					s_thumpData.GetAlbum(objectID, (album)=>
					{
						request.OnComplete<PulseAlbum>(album.Tracks, objectType, objectID);
					});
					break;
				case eAAObject.Artist:
					s_thumpData.GetAlbumsForArtist(objectID, (albums) =>
					{
						List<MediaItem> items = MediaItemBuilder.BuildArtistChildren(objectID, albums);
						request.OnComplete(items);
					});
					break;
				case eAAObject.Playlist:
					s_thumpData.GetPlaylist(objectID, (playlist) =>
					{
						request.OnComplete<PulseAlbum>(playlist.Tracks, objectType, objectID);
					});
					break;
				case eAAObject.Genre:
					s_thumpData.GetTracksForGenre(objectID, (genreTracks) =>
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

		private static ThumpData BuildThumpData()
		{
			IMediaClient pulseClient = new PulseAPI();
			pulseClient.SetServerParams(ThumpSettings.GetServerIp(), ThumpSettings.GetServerPort(), ThumpSettings.GetUsername(), ThumpSettings.GetPassword(), ThumpSettings.GetAuthType(), ThumpSettings.GetUseHttps());
			string cacheRoot = FileSystem.CacheDirectory;
			string databasePath = Path.Combine(cacheRoot, "thump.db");
			string blobDirectory = Path.Combine(cacheRoot, "blobs");
			ThumpCache cache = new ThumpCache(databasePath, blobDirectory);
			return new ThumpData(pulseClient, cache);
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
