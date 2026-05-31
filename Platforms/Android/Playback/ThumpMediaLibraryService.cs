using System.Collections.Generic;
using System.IO;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Eap;
using AndroidX.Annotations;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Source;
using AndroidX.Media3.Extractor;
using AndroidX.Media3.Extractor.Mp3;
using AndroidX.Media3.Session;
using Java.Util;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using Thump.Data;
using Thump.Pulse;

namespace Thump.Playback
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

		
		
		private IExoPlayer m_player;
		private MediaLibraryService.MediaLibrarySession m_session;
		private CarConnectionReceiver m_carReceiver;

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
			library.m_onGetChildren = null;
			library.m_onGetItem = null;
			library.m_onGetLibraryRoot = null;
			library.m_onPlaybackResumption = null;
			library.m_onPlayerCommandRequest = null;
			library.m_onPlayerInteractionFinished = null;
			library.m_onPostConnect = null;
			library.m_onSetMediaItems = null;
			library.m_onSubscribe = null;
			library.m_onUnsubscribe = null;


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
			MediaItem root = AAutoHelper.BuildBrowsableItem(AAudoNavigation.GetId(eAADirectory.Root), "Thump");
			return root;
		}
		public MediaItem OnGetItem(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, string mediaId)
		{
			MediaItem item = AAutoHelper.BuildItemForId(mediaId);
			return item;
		}

		public IList<MediaItem> OnGetChildren(MediaLibraryService.MediaLibrarySession session, MediaSession.ControllerInfo browser, string parentId, int page, int pageSize, MediaLibraryService.LibraryParams libraryParams)
		{
			eAADirectory dir = eAADirectory.Root;
			if (!AAudoNavigation.TryGetDirectory(parentId, out dir))
				return new List<MediaItem>();
			/*
			if (dir == eAADirectory.Root)
			{
				List<MediaItem> categories = new List<MediaItem>();
				categories.Add(AAutoHelper.BuildBrowsableItem(s_homeId, "Home"));
				categories.Add(AAutoHelper.BuildBrowsableItem(s_playlistsId, "Playlists"));
				categories.Add(AAutoHelper.BuildBrowsableItem(s_libraryId, "Library"));
				categories.Add(AAutoHelper.BuildBrowsableItem(s_podcastsId, "Podcasts"));
				return categories;
			}

			
			ChildrenResolver resolver = new ChildrenResolver(this, parentId, libraryParams);
			return (IListenableFuture)CallbackToFutureAdapter.GetFuture(resolver);*/

			return new List<MediaItem>();
		}


		public void LoadContainer(eAADirectory parent)
		{
			switch (parent)
			{
				case eAADirectory.Home:
					break;
				case eAADirectory.Podcasts:
					break;
				case eAADirectory.Library:
					break;
				case eAADirectory.RecentlyPlayed:
					break;
				case eAADirectory.RecentlyAdded:
					break;
				case eAADirectory.TopPlaylists:
					break;
				case eAADirectory.PopularArtists:
					break;
				case eAADirectory.Albums:
					break;
				case eAADirectory.Playlists:
					break;
				case eAADirectory.Artists:
					break;
				case eAADirectory.Genres:
					break;
			}
		}

		public void LoadObject(eAAObject objectType, string objectID, JObjectCallback request)
		{
			switch (objectType)
			{
				case eAAObject.Album:
					s_thumpData.GetAlbum(objectID, (album)=>
					{
						request.SendObject<PulseAlbum>(album.Tracks, objectType, objectID);
					});
					break;
				case eAAObject.Artist:
					s_thumpData.GetTracksForArtist(objectID, (artistTracks) =>
					{
						request.SendObject<PulseTrack>(artistTracks, objectType, objectID);
					});
					break;
				case eAAObject.Playlist:
					s_thumpData.GetPlaylist(objectID, (playlist) =>
					{
						request.SendObject<PulseAlbum>(playlist.Tracks, objectType, objectID);
					});
					break;
				case eAAObject.Genre:
					s_thumpData.GetTracksForGenre(objectID, (genreTracks) =>
					{
						request.SendObject<PulseAlbum>(genreTracks, objectType, objectID);
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
					Thump.Log.Warn("ThumpPlaybackService: car connection receiver was not registered: " + exception.Message);
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
