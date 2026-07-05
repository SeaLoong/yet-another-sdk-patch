using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using UnityEngine;
using VRC;
using YetAnotherPatchForVRChatSdk.Extensions;

namespace YetAnotherPatchForVRChatSdk.Patches.NetworkResilience;

internal sealed class VrcApiHttpClientFactory
{
    public delegate void SetupCookieContainerGetCookiesDelegate(CookieContainer cookieContainer);

    private readonly SetupCookieContainerGetCookiesDelegate _setupCookieContainer;

    private readonly Dictionary<string, string> _defaultRequestHeaders = new()
    {
        { "User-Agent", "VRC.Core.BestHTTP" },
        { "X-SDK-Version", Tools.SdkVersion },
        { "X-Platform", Tools.Platform },
        { "X-Unity-Version", Application.unityVersion },
        { "Accept", "application/json" }
    };

    private readonly object _lock = new();
    private readonly HttpClient _client;
    private readonly CookieContainer _cookieContainer;

    public VrcApiHttpClientFactory(SetupCookieContainerGetCookiesDelegate? setupCookieContainer = null)
    {
        _setupCookieContainer = setupCookieContainer ?? (_ => { });

        _cookieContainer = new CookieContainer();
        _client = CreateClientInternal(_cookieContainer);
    }

    // Exposes the underlying HttpClient instance without mutating any shared state, so callers
    // that only need to compare the client reference (e.g. to identify which client an HttpClient
    // instance belongs to) don't trigger cookie/header refreshes as a side effect.
    public HttpClient Client => _client;

    public HttpClient GetOrCreateClient()
    {
        lock (_lock)
        {
            _cookieContainer.Clear();
            _setupCookieContainer(_cookieContainer);

            return _client;
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

        var handler = new MacAddressHeaderHandler(new ResilienceHttpHandler(new HttpLoggingHandler(innerHandler)));
        var client = new HttpClient(handler);
        client.Timeout = Timeout.InfiniteTimeSpan;

        foreach (var header in _defaultRequestHeaders)
        {
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        return client;
    }
}