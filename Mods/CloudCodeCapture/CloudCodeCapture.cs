using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ArcadiusModLoader;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Unity.Services.CloudCode;
using Unity.Services.CloudCode.Subscriptions;
using UnityEngine;

namespace ArcadiusCapture
{
	// Note: Assembly-CSharp defines a global JsonConvert (a JsonUtility wrapper that can't
	// serialize strings or dictionaries), so Newtonsoft's is always fully qualified here.
	// Wraps CloudCodeService.Instance in a proxy that logs every request and response, so offline
	// stand-ins can mirror real server data. The game stays online.
	public sealed class CaptureEntry : IMod
	{
		public void OnLoad(ModContext context)
		{
			// Unity Services sets CloudCodeService.Instance during sign-in; wrap it once sign-in finishes,
			// before UnityServicesGateway.CallCloudCodeAsync uses it.
			context.Harmony.Patch(AccessTools.Method(typeof(UnityServicesGateway), nameof(UnityServicesGateway.InitializeAndSignInAsync)),
				postfix: new HarmonyMethod(typeof(CaptureEntry).GetMethod(nameof(SignInPostfix), BindingFlags.NonPublic | BindingFlags.Static)));
		}

		private static void SignInPostfix(ref Task __result)
		{
			__result = WrapAfter(__result);
		}

		private static async Task WrapAfter(Task signIn)
		{
			await signIn;
			ICloudCodeService current = CloudCodeService.Instance;
			if (current != null && !(current is CloudCodeCapture))
			{
				AccessTools.PropertySetter(typeof(CloudCodeService), nameof(CloudCodeService.Instance)).Invoke(null, new object[] { new CloudCodeCapture(current) });
			}
		}
	}

	public sealed class CloudCodeCapture : ICloudCodeService
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
				}
				return _path;
			}
		}

		private readonly ICloudCodeService _inner;

		public CloudCodeCapture(ICloudCodeService inner)
		{
			_inner = inner;
			Debug.Log("[Capture] Logging Cloud Code traffic to " + CapturePath);
		}

		public async Task<TResult> CallEndpointAsync<TResult>(string function, Dictionary<string, object> args = null)
		{
			Dictionary<string, object> sentArgs = args ?? new Dictionary<string, object>();
			string request = Serialize(sentArgs);
			try
			{
				// Fetch untyped so the log keeps fields the client's model would drop.
				object raw = await _inner.CallEndpointAsync<object>(function, sentArgs);
				Write(function, request, Serialize(raw), null);
				if (raw == null)
				{
					return default;
				}
				JToken token = raw as JToken ?? JToken.FromObject(raw);
				return token.ToObject<TResult>();
			}
			catch (Exception ex)
			{
				Write(function, request, null, ex.Message);
				throw;
			}
		}

		public async Task<string> CallEndpointAsync(string function, Dictionary<string, object> args = null)
		{
			string request = Serialize(args ?? new Dictionary<string, object>());
			try
			{
				string response = await _inner.CallEndpointAsync(function, args);
				Write(function, request, Serialize(response), null);
				return response;
			}
			catch (Exception ex)
			{
				Write(function, request, null, ex.Message);
				throw;
			}
		}

		// Not used by the game; passed through unlogged.
		public Task<TResult> CallModuleEndpointAsync<TResult>(string module, string function, Dictionary<string, object> args = null)
		{
			return _inner.CallModuleEndpointAsync<TResult>(module, function, args);
		}

		public Task<string> CallModuleEndpointAsync(string module, string function, Dictionary<string, object> args = null)
		{
			return _inner.CallModuleEndpointAsync(module, function, args);
		}

		public Task<ISubscriptionEvents> SubscribeToPlayerMessagesAsync(SubscriptionEventCallbacks callbacks)
		{
			return _inner.SubscribeToPlayerMessagesAsync(callbacks);
		}

		public Task<ISubscriptionEvents> SubscribeToProjectMessagesAsync(SubscriptionEventCallbacks callbacks)
		{
			return _inner.SubscribeToProjectMessagesAsync(callbacks);
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
