using System;
using System.IO;
using UnityEngine;

namespace ArcadiusModLoader
{
	// Logs to the Unity player log (Player.log) and to Mods/modlog.txt, prefixed with the mod's id.
	public sealed class ModLog
	{
		private static readonly object FileLock = new object();

		private static string _filePath;

		private readonly string _prefix;

		public ModLog(string source)
		{
			_prefix = "[" + source + "] ";
		}

		// Starts a fresh Mods/modlog.txt for this session.
		internal static void StartFile(string path)
		{
			_filePath = path;
			try
			{
				File.WriteAllText(path, "Arcadius mod loader log, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
			}
			catch (Exception ex)
			{
				_filePath = null;
				Debug.LogWarning("[ModLoader] Could not write " + path + ": " + ex.Message);
			}
		}

		public void Info(string message)
		{
			Debug.Log(_prefix + message);
			WriteFile("INFO  ", message);
		}

		public void Warning(string message)
		{
			Debug.LogWarning(_prefix + message);
			WriteFile("WARN  ", message);
		}

		public void Error(string message)
		{
			Debug.LogError(_prefix + message);
			WriteFile("ERROR ", message);
		}

		public void Error(string message, Exception ex)
		{
			Error(message + "\n" + ex);
		}

		private void WriteFile(string level, string message)
		{
			if (_filePath == null)
			{
				return;
			}
			lock (FileLock)
			{
				try
				{
					File.AppendAllText(_filePath, DateTime.Now.ToString("HH:mm:ss ") + level + _prefix + message + "\n");
				}
				catch (IOException)
				{
				}
			}
		}
	}
}
