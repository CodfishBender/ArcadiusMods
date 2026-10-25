using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArcadiusModLoader
{
	// A mod's user-editable settings, stored as a flat JSON object (config.json in the mod folder).
	// Get() adds missing keys with their default values, so the file always lists every setting.
	public sealed class ModConfig
	{
		private readonly string _path;

		private readonly ModLog _log;

		private JObject _values;

		public ModConfig(string path, ModLog log)
		{
			_path = path;
			_log = log;
			Reload();
		}

		public string FilePath => _path;

		public T Get<T>(string key, T defaultValue)
		{
			if (_values.TryGetValue(key, out JToken token))
			{
				try
				{
					return token.ToObject<T>();
				}
				catch (Exception ex)
				{
					_log.Warning("Setting '" + key + "' in " + _path + " is not a valid " + typeof(T).Name + "; using " + defaultValue + ". " + ex.Message);
					return defaultValue;
				}
			}
			_values[key] = defaultValue == null ? JValue.CreateNull() : JToken.FromObject(defaultValue);
			Save();
			return defaultValue;
		}

		public void Set<T>(string key, T value)
		{
			_values[key] = value == null ? JValue.CreateNull() : JToken.FromObject(value);
			Save();
		}

		// Re-reads the file; called for every mod when the reload hotkey is pressed.
		public void Reload()
		{
			_values = new JObject();
			if (!File.Exists(_path))
			{
				return;
			}
			try
			{
				_values = JObject.Parse(File.ReadAllText(_path));
			}
			catch (Exception ex)
			{
				_log.Error("Could not read " + _path + "; using defaults. " + ex.Message);
			}
		}

		private void Save()
		{
			try
			{
				File.WriteAllText(_path, _values.ToString(Formatting.Indented) + "\n");
			}
			catch (Exception ex)
			{
				_log.Warning("Could not write " + _path + ": " + ex.Message);
			}
		}
	}
}
