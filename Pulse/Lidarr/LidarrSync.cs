using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Pulse.Lidarr
{
	public class LidarrSync
	{


		private string m_lidarrUrl = "";
		private string m_apiKey = "";
		private HttpClient m_httpClient;

		// Cached from Lidarr on each sync pass
		private HashSet<string> m_lidarrArtistNames;
		private HashSet<string> m_lidarrArtistMBIDs;

		// Results from last sync
		private List<string> m_lastAdded;
		private List<string> m_lastFailed;
		private List<string> m_lastSkipped;


		public LidarrSync(string lidarrUrl, string apiKey)
		{
			m_lidarrUrl = lidarrUrl.TrimEnd('/');
			m_apiKey = apiKey;
			m_httpClient = new HttpClient();
			m_lidarrArtistNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			m_lidarrArtistMBIDs = new HashSet<string>();
			m_lastAdded = new List<string>();
			m_lastFailed = new List<string>();
			m_lastSkipped = new List<string>();
			
		}

		/// <summary>
		/// Converts songs into artist requests for lidarr 
		/// </summary>
		public int RequestArtists(HashSet<string> requestedSongs)
		{
			m_lastAdded.Clear();
			m_lastFailed.Clear();
			m_lastSkipped.Clear();

			// Extract unique artist names from "Artist - Title" entries
			HashSet<string> missingArtistNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string entry in requestedSongs)
			{
				int dashIndex = entry.IndexOf(" - ");
				if (dashIndex <= 0)
				{
					continue;
				}
				string artistName = entry.Substring(0, dashIndex).Trim();
				if (artistName.Length > 0)
				{
					missingArtistNames.Add(artistName);
				}
			}

			Console.WriteLine("LidarrSync: " + missingArtistNames.Count + " unique artists from " + requestedSongs.Count + " missing songs");

			// Fetch existing artists from Lidarr
			if (!FetchExistingArtists())
			{
				Console.WriteLine("LidarrSync: Failed to fetch existing artists from Lidarr, aborting");
				return 0;
			}

			Console.WriteLine("LidarrSync: Lidarr has " + m_lidarrArtistNames.Count + " artists");

			// Fetch root folder and quality/metadata profile IDs
			int qualityProfileId = FetchFirstId("qualityprofile");
			int metadataProfileId = FetchFirstId("metadataprofile");
			string rootFolderPath = FetchRootFolderPath();

			if (qualityProfileId < 0 || metadataProfileId < 0 || rootFolderPath == null)
			{
				Console.WriteLine("LidarrSync: Failed to fetch Lidarr config (profiles/root folder), aborting");
				return 0;
			}

			Console.WriteLine("LidarrSync: rootFolder=" + rootFolderPath + " qualityProfile=" + qualityProfileId + " metadataProfile=" + metadataProfileId);

			int addedCount = 0;

			foreach (string artistName in missingArtistNames)
			{
				if (m_lidarrArtistNames.Contains(artistName))
				{
					m_lastSkipped.Add(artistName);
					continue;
				}

				// Lookup artist in Lidarr (which searches MusicBrainz)
				JsonElement lookupResult;
				bool found = TryLookupArtist(artistName, out lookupResult);
				if (!found)
				{
					Console.WriteLine("LidarrSync: FAIL lookup - " + artistName);
					m_lastFailed.Add(artistName);
					Thread.Sleep(1500);
					continue;
				}

				// --- MBID check goes here ---
				JsonElement mbidElement;
				if (lookupResult.TryGetProperty("foreignArtistId", out mbidElement))
				{
					string mbid = mbidElement.GetString();
					if (mbid != null && m_lidarrArtistMBIDs.Contains(mbid))
					{
						//Console.WriteLine("LidarrSync: SKIP (MBID match) - " + artistName);
						m_lastSkipped.Add(artistName);
						continue;
					}
				}


				// Add artist to Lidarr
				bool added = AddArtist(lookupResult, rootFolderPath, qualityProfileId, metadataProfileId);
				if (added)
				{
					Console.WriteLine("LidarrSync: ADDED - " + artistName);
					m_lastAdded.Add(artistName);
					m_lidarrArtistNames.Add(artistName);
					addedCount++;
				}
				else
				{
					Console.WriteLine("LidarrSync: FAIL add - " + artistName);
					m_lastFailed.Add(artistName);
				}

				Thread.Sleep(1500);
			}

			Console.WriteLine("LidarrSync: Done. Added=" + addedCount + " Skipped=" + m_lastSkipped.Count + " Failed=" + m_lastFailed.Count);
			return addedCount;
		}

		private bool FetchExistingArtists()
		{
			m_lidarrArtistNames.Clear();

			string json = LidarrGet("artist");
			if (json == null)
			{
				return false;
			}

			JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;

			for (int index = 0; index < root.GetArrayLength(); index++)
			{
				JsonElement artistElement = root[index];
				JsonElement nameElement;
				if (artistElement.TryGetProperty("artistName", out nameElement))
				{
					string name = nameElement.GetString();
					if (!string.IsNullOrEmpty(name))
					{
						m_lidarrArtistNames.Add(name);
					}
				}
				JsonElement foreignIdElement;
				if (artistElement.TryGetProperty("foreignArtistId", out foreignIdElement))
				{
					string foreignId = foreignIdElement.GetString();
					if (!string.IsNullOrEmpty(foreignId))
					{
						m_lidarrArtistMBIDs.Add(foreignId);
					}
				}
			}

			doc.Dispose();
			return true;
		}

		private bool TryLookupArtist(string artistName, out JsonElement result)
		{
			result = default(JsonElement);
			string encoded = Uri.EscapeDataString(artistName);
			string json = LidarrGet("artist/lookup?term=" + encoded);
			if (json == null)
			{
				return false;
			}

			JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;

			if (root.GetArrayLength() == 0)
			{
				doc.Dispose();
				return false;
			}

			// Clone so we can dispose the document
			result = root[0].Clone();
			doc.Dispose();
			return true;
		}

		private bool AddArtist(JsonElement lookupResult, string rootFolderPath, int qualityProfileId, int metadataProfileId)
		{
			// Rebuild the object with our config bolted on
			Dictionary<string, object> artistData = JsonSerializer.Deserialize<Dictionary<string, object>>(lookupResult.GetRawText());
			artistData["rootFolderPath"] = rootFolderPath;
			artistData["qualityProfileId"] = qualityProfileId;
			artistData["metadataProfileId"] = metadataProfileId;
			artistData["monitored"] = true;
			artistData["monitorNewItems"] = "all";

			Dictionary<string, object> addOptions = new Dictionary<string, object>();
			addOptions["monitor"] = "all";
			addOptions["searchForMissingAlbums"] = true;
			artistData["addOptions"] = addOptions;

			string body = JsonSerializer.Serialize(artistData);
			string response = LidarrPost("artist", body);
			return response != null;
		}

		private int FetchFirstId(string endpoint)
		{
			string json = LidarrGet(endpoint);
			if (json == null)
			{
				return -1;
			}

			JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;

			if (root.GetArrayLength() == 0)
			{
				doc.Dispose();
				return -1;
			}

			int id = root[0].GetProperty("id").GetInt32();
			doc.Dispose();
			return id;
		}

		private string FetchRootFolderPath()
		{
			string json = LidarrGet("rootfolder");
			if (json == null)
			{
				return null;
			}

			JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;

			if (root.GetArrayLength() == 0)
			{
				doc.Dispose();
				return null;
			}

			string path = root[0].GetProperty("path").GetString();
			doc.Dispose();
			return path;
		}

		private string LidarrGet(string endpoint)
		{
			string url = m_lidarrUrl + "/api/v1/" + endpoint;
			if (endpoint.Contains("?"))
			{
				url = url + "&apikey=" + m_apiKey;
			}
			else
			{
				url = url + "?apikey=" + m_apiKey;
			}

			HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);

			try
			{
				HttpResponseMessage response = m_httpClient.Send(request);
				if (!response.IsSuccessStatusCode)
				{
					string errorBody = ReadResponseBody(response);
					Console.WriteLine("LidarrSync: GET " + endpoint + " failed - " + response.StatusCode + " - " + errorBody);
					return null;
				}
				return ReadResponseBody(response);
			}
			catch (Exception ex)
			{
				Console.WriteLine("LidarrSync: GET " + endpoint + " exception - " + ex.Message);
				return null;
			}
		}

		private string LidarrPost(string endpoint, string body)
		{
			string url = m_lidarrUrl + "/api/v1/" + endpoint + "?apikey=" + m_apiKey;
			HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
			request.Content = new StringContent(body, Encoding.UTF8, "application/json");

			try
			{
				HttpResponseMessage response = m_httpClient.Send(request);
				if (!response.IsSuccessStatusCode)
				{
					string errorBody = ReadResponseBody(response);
					Console.WriteLine("LidarrSync: POST " + endpoint + " failed - " + response.StatusCode + " - " + errorBody);
					return null;
				}
				return ReadResponseBody(response);
			}
			catch (Exception ex)
			{
				Console.WriteLine("LidarrSync: POST " + endpoint + " exception - " + ex.Message);
				return null;
			}
		}

		private string ReadResponseBody(HttpResponseMessage response)
		{
			Stream stream = response.Content.ReadAsStream();
			StreamReader reader = new StreamReader(stream);
			string body = reader.ReadToEnd();
			reader.Dispose();
			return body;
		}
	}
}
