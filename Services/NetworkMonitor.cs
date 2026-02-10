using Serilog;
using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace Yellowcake.Services;

public class NetworkMonitor
{
    private static readonly Lazy<NetworkMonitor> _lazy = new(() => new NetworkMonitor());
    public static NetworkMonitor Instance => _lazy.Value;

    private bool _isOnline = true;
    private readonly Timer _pingTimer;

    public bool IsOnline
    {
        get => _isOnline;
        private set
        {
            if (_isOnline != value)
            {
                _isOnline = value;
                OnlineStatusChanged?.Invoke(_isOnline);
                Log.Information("[NetworkMonitor] Status changed: {Status}", value ? "Online" : "Offline");
            }
        }
    }

    public event Action<bool>? OnlineStatusChanged;

    private NetworkMonitor()
    {
        NetworkChange.NetworkAvailabilityChanged += (s, e) => 
        {
            IsOnline = e.IsAvailable;
        };

        _pingTimer = new Timer(_ => CheckConnectivity(), null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    private async void CheckConnectivity()
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("8.8.8.8", 3000);
            IsOnline = reply.Status == IPStatus.Success;
        }
        catch
        {
            IsOnline = false;
        }
    }

    public async Task<bool> TestManifestConnectivity(string url)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}