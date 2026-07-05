using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using UnityEngine;
using VRC;
using VRC.Core;
using YesPatchFrameworkForVRChatSdk.PatchApi.Logging;
using YetAnotherPatchForVRChatSdk.Extensions;

namespace YetAnotherPatchForVRChatSdk.Patches.NetworkResilience;

internal sealed class VrcApiHttpClientFactory
{
    public delegate void SetupCookieContainerGetCookiesDelegate(CookieContainer cookieContainer);

    private static readonly YesLogger Logger = new(nameof(VrcApiHttpClientFactory));

    private const string MacAddressHeaderName = "X-MacAddress";

    private readonly SetupCookieContainerGetCookiesDelegate _setupCookieContainer;

    private readonly Dictionary<string, string> _defaultRequestHeaders = new()
    {
        { "User-Agent", "VRC.Core.BestHTTP" },
        { "X-SDK-Version", Tools.SdkVersion },
        { "X-Platform", Tools.Platform },
        { "X-Unity-Version", Application.unityVersion },
        { "Accept", "application/json" }
    };

    // API.DeviceID is deliberately not read eagerly (e.g. as a field initializer). VRC.Core.API's
    // internal state may not be fully initialized yet when this factory is constructed during patch
    // application, which can cause API.DeviceID to throw a NullReferenceException. Instead, the value
    // is looked up lazily, right before it is actually needed (see GetOrCreateClient below), and it is
    // re-fetched every time so the header always reflects the real, current device id.
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
            // request and try again the next time a client is requested.
            Logger.LogWarning(ex, "Failed to get VRC.Core.API.DeviceID, skipping the X-MacAddress header for this request.");
            deviceId = string.Empty;
            return false;
        }
    }

    private readonly HttpClient _client;
    private readonly CookieContainer _cookieContainer;

    public VrcApiHttpClientFactory(SetupCookieContainerGetCookiesDelegate? setupCookieContainer = null)
    {
        _setupCookieContainer = setupCookieContainer ?? (_ => { });

        _cookieContainer = new CookieContainer();
        _client = CreateClientInternal(_cookieContainer);
    }

    public HttpClient GetOrCreateClient()
    {
        _cookieContainer.Clear();
        _setupCookieContainer(_cookieContainer);

        UpdateMacAddressHeader(_client);

        return _client;
    }

    private static void UpdateMacAddressHeader(HttpClient client)
    {
        // Refresh the device id right before the client is handed out, rather than once at
        // construction time, so a transient failure to read it doesn't permanently affect the client,
        // and the header always carries the real, up-to-date device id when it's available.
        client.DefaultRequestHeaders.Remove(MacAddressHeaderName);

        if (TryGetDeviceId(out var deviceId))
        {
            client.DefaultRequestHeaders.Add(MacAddressHeaderName, deviceId);
        }
    }

    private HttpClient CreateClientInternal(CookieContainer cookieContainer)
    {
        _setupCookieContainer(cookieContainer);

        var innerHandler = new StandardSocketsHttpHandler
        {
            CookieContainer = cookieContainer,
            Proxy = new NetworkResilienceWebProxy(),
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionIdleTimeout = TimeSpan.Zero
        };

        var handler = new ResilienceHttpHandler(new HttpLoggingHandler(innerHandler));
        var client = new HttpClient(handler);
        client.Timeout = Timeout.InfiniteTimeSpan;

        foreach (var header in _defaultRequestHeaders)
        {
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        return client;
    }
}