
namespace Pulse.MusicLibrary
{
	public class SmartPlaylist
	{
		private static List<TrackInfo> WeightedSample(List<TrackInfo> candidates, int count, string userName, Dictionary<string, float> artistScores, Random rng)
		{
			if (candidates.Count <= count)
			{
				return new List<TrackInfo>(candidates);
			}

			// Build parallel weight array
			float[] weights = new float[candidates.Count];
			for (int i = 0; i < candidates.Count; i++)
			{
				float weight = candidates[i].GetScore(userName);
				
				float artistScore;
				if (artistScores != null && artistScores.TryGetValue(candidates[i].ArtistId, out artistScore))
				{
					weight += artistScore;
				}

				weights[i] = weight;
			}

			// Pick by repeatedly selecting and removing from the pool
			List<int> candidateIndices = new List<int>(candidates.Count);
			for (int i = 0; i < candidates.Count; i++)
			{
				candidateIndices.Add(i);
			}

			List<TrackInfo> result = new List<TrackInfo>();
			float totalWeight = 0f;
			for (int index = 0; index < weights.Length; index++)
			{
				totalWeight = totalWeight + weights[index];
			}

			for (int i = 0; i < count; i++)
			{
				float roll = (float)(rng.NextDouble() * totalWeight);
				float cumulative = 0f;

				for (int j = 0; j < candidateIndices.Count; j++)
				{
					cumulative = cumulative + weights[candidateIndices[j]];
					if (cumulative >= roll)
					{
						result.Add(candidates[candidateIndices[j]]);
						totalWeight = totalWeight - weights[candidateIndices[j]];
						candidateIndices.RemoveAt(j);
						break;
					}
				}
			}

			return result;
		}

		public static PlaylistInfo BuildSmartPlaylist(string playlistId, string playlistName, List<TrackInfo> scoredTracks, List<ArtistInfo> scoredArtists, List<TrackInfo> unplayedTracks, string userName, Random rng)
		{			
			int maxTracks = 200;
			int scoredSlots = Math.Min((int)(maxTracks * 0.8f), scoredTracks.Count);

			Dictionary<string, float> artistScores = new Dictionary<string, float>();
			for (int i = 0; i < scoredArtists.Count; i++)
			{
				float score;
				if (!string.IsNullOrEmpty(userName) && scoredArtists[i].UserWeightedScore != null && scoredArtists[i].UserWeightedScore.TryGetValue(userName, out score))
				{
					artistScores[scoredArtists[i].Id] = score;
				}
				else
				{
					artistScores[scoredArtists[i].Id] = scoredArtists[i].WeightedScore;
				}
			}

			//Shuffle our scored tracks
			List<TrackInfo> selectedScored = WeightedSample(scoredTracks, scoredSlots, userName, artistScores, rng);

			PlaylistInfo playlist = new PlaylistInfo();
			playlist.Id = playlistId;
			playlist.Name = playlistName;
			playlist.Comment = "Auto-generated from listening history";

			long totalDuration = 0;
			for (int i = 0; i < selectedScored.Count; i++)
			{
				playlist.TrackIds.Add(selectedScored[i].Id);
				totalDuration = totalDuration + selectedScored[i].DurationSeconds;
			}
		
			int openSlots = Math.Min(maxTracks - playlist.TrackIds.Count, unplayedTracks.Count);
			List<TrackInfo> selectedUnplayed = WeightedSample(unplayedTracks, openSlots, userName, artistScores, rng);
			for (int i = 0; i < selectedUnplayed.Count; i++)
			{
				playlist.TrackIds.Add(selectedUnplayed[i].Id);
				totalDuration = totalDuration + selectedUnplayed[i].DurationSeconds;
			}

			ShuffleList(playlist.TrackIds, rng);

			playlist.DurationSeconds = totalDuration;
			return playlist;
		}

		private static void ShuffleList<T>(List<T> list, Random rng)
		{
			for (int index = list.Count - 1; index > 0; index--)
			{
				int swapIndex = rng.Next(index + 1);
				T temp = list[index];
				list[index] = list[swapIndex];
				list[swapIndex] = temp;
			}
		}

		public static void CategorizeTracks(ICollection<TrackInfo> allTracks, string userName, List<TrackInfo> scoredTracks, List<TrackInfo> unplayedTracks)
		{
			foreach (TrackInfo track in allTracks)
			{
				if (track.IsStarredBy(userName))
				{
					scoredTracks.Add(track);
					continue;
				}

				int totalSessions = track.GetTotalSessions(userName);
				if (totalSessions == 0)
				{
					unplayedTracks.Add(track);
					continue;
				}
				scoredTracks.Add(track);
			}
		}
	}
}
