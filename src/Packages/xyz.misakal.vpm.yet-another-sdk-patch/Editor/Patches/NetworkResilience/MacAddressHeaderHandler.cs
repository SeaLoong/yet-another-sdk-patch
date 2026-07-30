using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VRC.Core;
using YesPatchFrameworkForVRChatSdk.PatchApi.Logging;

namespace YetAnotherPatchForVRChatSdk.Patches.NetworkResilience;

// Adds the X-MacAddress header to each outgoing HttpRequestMessage instead of mutating
// HttpClient.DefaultRequestHeaders. DefaultRequestHeaders are shared, client-wide state, so
// setting the header there is not safe when the same HttpClient instance is used concurrently
// by multiple in-flight requests. Setting it per-request here keeps the header isolated to the
// request it was computed for.
internal sealed class MacAddressHeaderHandler : DelegatingHandler
{
    private const string MacAddressHeaderName = "X-MacAddress";

    private static readonly YesLogger Logger = new(nameof(MacAddressHeaderHandler));

    public MacAddressHeaderHandler(HttpMessageHandler innerHandler) : base(innerHandler)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Remove(MacAddressHeaderName);

        if (TryGetDeviceId(out var deviceId))
        {
            request.Headers.TryAddWithoutValidation(MacAddressHeaderName, deviceId);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool TryGetDeviceId(out string deviceId)
    {
        try
        {
            deviceId = API.DeviceID;
            return true;
        }
        catch (Exception ex)
        {
            // VRC.Core.API.DeviceID can throw a NullReferenceException when accessed too early
            // (e.g. before VRChat's internal API state has finished initializing). Don't substitute
            // a made-up value here, as that would be incorrect; just skip the header for this
            // request and try again the next time a request is sent.
            Logger.LogWarning(ex, "Failed to get VRC.Core.API.DeviceID, skipping the X-MacAddress header for this request.");
            deviceId = string.Empty;
            return false;
        }
    }
}
