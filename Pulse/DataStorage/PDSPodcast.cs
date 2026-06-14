using Pulse.Podcasts;
using PulseAPI.CSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace Pulse.DataStorage
{
	public enum eRetentionPolicy
	{
		KeepAll,
		KeepN,
		KeepDays,
		KeepExisting,
	}

	public enum eDownloadState
	{
		Discovered,
		Downloading,
		Downloaded,
		Failed,
	}

	public class Podcast : PulseDataObject
	{
		public class UserData
		{
			public bool Subscribed;
			public string LastEpisodeId = "";
			public DateTime LastPlayed;
		}

		public string Title = "";
		public string Author = "";
		public string Description = "";
		public string ArtworkPath = "";
		public string DateAdded = "";
		public Dictionary<string, UserData> Users = new Dictionary<string, UserData>();
		public string FeedUrl = "";
		public eRetentionPolicy Retention = eRetentionPolicy.KeepN;
		public int RetentionValue = 10;
		public bool AutoDownload = true;

		public PulsePodcast BuildPulsePodcast(PodcastManager podcastManager, string userId)
		{
			PulsePodcast pulsePodcast = new PulsePodcast();
			pulsePodcast.Id = Id;
			pulsePodcast.Title = Title;
			pulsePodcast.Author = Author;
			pulsePodcast.Description = Description;
			pulsePodcast.CoverArt = "se-" + Id;	

			List<Episode> downloaded = podcastManager.GetDownloadedItems(Id);
			pulsePodcast.EpisodeCount = downloaded.Count;
			pulsePodcast.ItemCount = downloaded.Count;
			pulsePodcast.UnplayedCount = podcastManager.GetUnplayedCount(Id, userId);

			Podcast.UserData userSeries;
			bool hasUser = Users.TryGetValue(userId, out userSeries);
			if (hasUser)
			{
				pulsePodcast.Subscribed = userSeries.Subscribed;
				pulsePodcast.LastItemId = userSeries.LastEpisodeId;
				pulsePodcast.LastPlayed = userSeries.LastPlayed.ToString("o");
			}

			pulsePodcast.FeedUrl = FeedUrl;
			pulsePodcast.AutoDownload = AutoDownload;
			pulsePodcast.RetentionPolicy = Retention.ToString();
			pulsePodcast.RetentionValue = RetentionValue;

			return pulsePodcast;
		}
	}

	public class Episode : PulseDataObject
	{
		public class UserData
		{
			public int PositionSeconds;
			public bool Completed;
			public DateTime LastPlayed;
		}

		public string PodcastId = "";
		public string Guid = "";
		public string Title = "";
		public string Description = "";
		public int OrderIndex;
		public string PublishedDate = "";
		public string MediaSourceUrl = "";
		public int StartMs;
		public int EndMs;
		public Dictionary<string, UserData> Users = new Dictionary<string, UserData>();
		public int DurationSeconds = 0;
		public string LocalPath = "";
		public long FileSizeBytes = 0;
		public eDownloadState DownloadState = eDownloadState.Discovered;
		

		public bool NeedsDownload()
		{
			if (DownloadState == eDownloadState.Downloading)
			{
				return false;
			}
			if (DownloadState != eDownloadState.Downloaded)
			{
				return true;
			}
			if (string.IsNullOrEmpty(LocalPath) || !File.Exists(LocalPath))
			{
				return true;
			}
			return false;
		}

		public PulsePodcastEpisode BuildPulsePodcastEpisode(PodcastManager podcastManager, string userId)
		{
			PulsePodcastEpisode episode = new PulsePodcastEpisode();
			episode.Id = Id;
			episode.SeriesId = PodcastId;
			episode.Title = Title;
			episode.Description = Description;
			episode.OrderIndex = OrderIndex;
			episode.PublishedDate = PublishedDate;
			episode.Duration = DurationSeconds;
			episode.CoverArt = "se-" + PodcastId;

			Episode.UserData progress;
			if (Users.TryGetValue(userId, out progress))
			{
				episode.PositionSeconds = progress.PositionSeconds;
				episode.Completed = progress.Completed;
			}
			return episode;
		}
	}
}
