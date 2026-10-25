using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.Services.CloudCode;
using UnityEngine;

namespace ArcadiusOffline
{
	// Note: Assembly-CSharp defines a global JsonConvert (a JsonUtility wrapper that can't
	// serialize strings or dictionaries), so Newtonsoft's is always fully qualified here.
	// Capture build only: replaces UnityServicesGateway.CallCloudCodeAsync<T> with the same call
	// plus a log of every request and response, so offline stand-ins can mirror real server data.
	public static class CloudCodeCapture
	{
		private static readonly object FileLock = new object();

		private static string _path;

		public static string CapturePath
		{
			get
			{
				if (_path == null)
				{
					_path = Path.Combine(Application.persistentDataPath, "cloudcode_capture.jsonl");
					Debug.Log("[Capture] Logging Cloud Code traffic to " + _path);
				}
				return _path;
			}
		}

		public static async Task<T> CallCloudCode<T>(string endpoint, Dictionary<string, object> args)
		{
			// Same as the original body: sign in, then call the endpoint.
			await UnityServicesGateway.InitializeAndSignInAsync();
			Dictionary<string, object> sentArgs = args ?? new Dictionary<string, object>();
			string request = Serialize(sentArgs);
			try
			{
				// Fetch untyped so the log keeps fields the client's model would drop.
				object raw = await CloudCodeService.Instance.CallEndpointAsync<object>(endpoint, sentArgs);
				Write(endpoint, request, Serialize(raw), null);
				if (raw == null)
				{
					return default;
				}
				JToken token = raw as JToken ?? JToken.FromObject(raw);
				return token.ToObject<T>();
			}
			catch (Exception ex)
			{
				Write(endpoint, request, null, ex.Message);
				throw;
			}
		}

		private static string Serialize(object value)
		{
			try
			{
				return Newtonsoft.Json.JsonConvert.SerializeObject(value);
			}
			catch (Exception ex)
			{
				return Newtonsoft.Json.JsonConvert.SerializeObject("<unserializable: " + ex.Message + ">");
			}
		}

		private static void Write(string endpoint, string request, string response, string error)
		{
			// Hand-assembled so request/response stay raw JSON rather than escaped strings.
			string line = "{\"time\":" + Newtonsoft.Json.JsonConvert.SerializeObject(DateTime.UtcNow.ToString("o"))
				+ ",\"endpoint\":" + Newtonsoft.Json.JsonConvert.SerializeObject(endpoint)
				+ ",\"totalLevel\":" + Playfab_PlayerData.TotalLevel
				+ ",\"request\":" + request
				+ ",\"response\":" + (response ?? "null")
				+ ",\"error\":" + Newtonsoft.Json.JsonConvert.SerializeObject(error) + "}";
			try
			{
				lock (FileLock)
				{
					File.AppendAllText(CapturePath, line + "\n");
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Capture] Could not write capture file. " + ex.Message);
			}
		}
	}
}
