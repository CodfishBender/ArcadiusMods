using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using Unity.Services.CloudCode.Subscriptions;

namespace ArcadiusOffline
{
	// Installed as CloudCodeService.Instance: answers the game's Cloud Code calls locally.
	public sealed class OfflineCloudCodeService : ICloudCodeService
	{
		public Task<TResult> CallEndpointAsync<TResult>(string function, Dictionary<string, object> args = null)
		{
			return OfflineMode.CallCloudCode<TResult>(function, args);
		}

		public Task<string> CallEndpointAsync(string function, Dictionary<string, object> args = null)
		{
			return OfflineMode.Unavailable<string>("Cloud Code endpoint '" + function + "'");
		}

		public Task<TResult> CallModuleEndpointAsync<TResult>(string module, string function, Dictionary<string, object> args = null)
		{
			return OfflineMode.Unavailable<TResult>("Cloud Code module '" + module + "'");
		}

		public Task<string> CallModuleEndpointAsync(string module, string function, Dictionary<string, object> args = null)
		{
			return OfflineMode.Unavailable<string>("Cloud Code module '" + module + "'");
		}

		public Task<ISubscriptionEvents> SubscribeToPlayerMessagesAsync(SubscriptionEventCallbacks callbacks)
		{
			return OfflineMode.Unavailable<ISubscriptionEvents>("Cloud Code messages");
		}

		public Task<ISubscriptionEvents> SubscribeToProjectMessagesAsync(SubscriptionEventCallbacks callbacks)
		{
			return OfflineMode.Unavailable<ISubscriptionEvents>("Cloud Code messages");
		}
	}
}
