using Microsoft.Data.Sqlite;
using Pulse.MusicLibrary;
using PulseAPI.CSharp;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Pulse.DataStorage
{
	/// <summary>
	/// Key-value SQLite store for PulseObjects. One table, two columns:
	/// (kind TEXT, id TEXT) as composite primary key, json TEXT as the payload.
	/// Everything is serialized on write and deserialized on load — the DB is
	/// pure persistence, the in-memory dictionaries are authoritative.
	/// </summary>
	public class PulseDataStore
	{
		private string m_databaseFilePath;
		private JsonSerializerOptions m_jsonOptions;

		public PulseDataStore(string databaseFilePath)
		{
			m_databaseFilePath = databaseFilePath;
			m_jsonOptions = new JsonSerializerOptions
			{
				WriteIndented = false,
				IncludeFields = true
			};
			EnsureTable();
		}

		private SqliteConnection OpenConnection()
		{
			SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder();
			builder.DataSource = m_databaseFilePath;
			SqliteConnection connection = new SqliteConnection(builder.ToString());
			connection.Open();

			SqliteCommand pragma = connection.CreateCommand();
			pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
			pragma.ExecuteNonQuery();

			return connection;
		}

		private void EnsureTable()
		{
			SqliteConnection connection = OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = @"
					CREATE TABLE IF NOT EXISTS objects (
						kind TEXT NOT NULL,
						id   TEXT NOT NULL,
						json TEXT NOT NULL,
						PRIMARY KEY (kind, id)
					);";
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

		public void Save<T>(eDataType kind, T value) where T : PulseDataObject
		{
			SqliteConnection connection = OpenConnection();
			try
			{
				SqliteTransaction transaction = connection.BeginTransaction();
				try
				{
					Save(connection, transaction, kind.ToString(), value);
					transaction.Commit();
				}
				catch
				{
					transaction.Rollback();
					throw;
				}
			}
			finally
			{
				connection.Close();
			}
		}

		public void SaveList<T>(eDataType kind, List<T> items) where T : PulseDataObject
		{
			if (items == null || items.Count == 0)
				return;

			string kindStr = kind.ToString();
			SqliteConnection connection = OpenConnection();
			try
			{
				SqliteTransaction transaction = connection.BeginTransaction();
				try
				{
					for (int index = 0; index < items.Count; index++)
					{
						Save(connection, transaction, kindStr, items[index]);
					}
					transaction.Commit();
				}
				catch
				{
					transaction.Rollback();
					throw;
				}
			}
			finally
			{
				connection.Close();
			}
		}

		private void Save<T>(SqliteConnection connection, SqliteTransaction transaction, string kind, T value) where T : PulseDataObject
		{
			string json = JsonSerializer.Serialize(value, m_jsonOptions);
			SqliteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = @"INSERT INTO objects (kind, id, json)
				VALUES ($kind, $id, $json)
				ON CONFLICT(kind, id) DO UPDATE SET json = excluded.json;";
			command.Parameters.AddWithValue("$kind", kind);
			command.Parameters.AddWithValue("$id", value.Id);
			command.Parameters.AddWithValue("$json", json);
			command.ExecuteNonQuery();
		}

		/// <summary>
		/// Load all objects of a given kind.
		/// </summary>
		public List<T> LoadList<T>(eDataType kind) where T : PulseDataObject
		{
			List<T> result = new List<T>();
			SqliteConnection connection = OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT json FROM objects WHERE kind = $kind;";
				command.Parameters.AddWithValue("$kind", kind.ToString());
				SqliteDataReader reader = command.ExecuteReader();
				while (reader.Read())
				{
					T item = JsonSerializer.Deserialize<T>(reader.GetString(0), m_jsonOptions);
					result.Add(item);
				}
				reader.Close();
			}
			finally
			{
				connection.Close();
			}
			for (int i = 0; i < result.Count; i++)
			{
				result[i].m_bIsDirty = false;
			}
			return result;
		}

		/// <summary>
		/// Load a single object by kind and id. Returns default if not found.
		/// </summary>
		public T Load<T>(eDataType kind, string id) where T : PulseDataObject
		{
			SqliteConnection connection = OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT json FROM objects WHERE kind = $kind AND id = $id;";
				command.Parameters.AddWithValue("$kind", kind.ToString());
				command.Parameters.AddWithValue("$id", id);
				object result = command.ExecuteScalar();
				if (result != null)
				{
					T item = JsonSerializer.Deserialize<T>((string)result, m_jsonOptions);
					item.m_bIsDirty = false;
					return item;
				}
			}
			finally
			{
				connection.Close();
			}
			return default;
		}

		/// <summary>
		/// Remove a single object.
		/// </summary>
		public void Delete(eDataType kind, string id)
		{
			SqliteConnection connection = OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "DELETE FROM objects WHERE kind = $kind AND id = $id;";
				command.Parameters.AddWithValue("$kind", kind.ToString());
				command.Parameters.AddWithValue("$id", id);
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

	}
}
