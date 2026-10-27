using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace KinshipPlugin;

/// <summary>Downloads images from the website once and keeps them as game textures.</summary>
public sealed class ImageCache : IDisposable
{
    // Images live on the website, so changing one there changes it in game too.
    public const string SiteImages = "https://the-kinship.github.io/kinship/images/";

    private sealed class Entry
    {
        public IDalamudTextureWrap? Wrap;
        public bool Failed;
        public bool Loading;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly ConcurrentDictionary<string, Entry> items = new();

    /// <summary>Returns the texture once it has loaded, otherwise null (it keeps loading in the background).</summary>
    public IDalamudTextureWrap? Get(string url)
    {
        var e = items.GetOrAdd(url, _ => new Entry());
        lock (e)
        {
            if (e.Wrap != null) return e.Wrap;
            if (e.Failed || e.Loading) return null;
            e.Loading = true;
        }
        Task.Run(async () =>
        {
            try
            {
                // Players can paste their own portrait links, so refuse anything unreasonably large.
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                if (resp.Content.Headers.ContentLength is > 6_000_000) throw new InvalidOperationException("Image is too large.");
                var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (bytes.Length > 6_000_000) throw new InvalidOperationException("Image is too large.");
                var wrap = await Plugin.TextureProvider.CreateFromImageAsync(bytes).ConfigureAwait(false);
                lock (e) { e.Wrap = wrap; }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Could not load image {Url}", url);
                lock (e) { e.Failed = true; }
            }
            finally { lock (e) { e.Loading = false; } }
        });
        return null;
    }

    public bool HasFailed(string url) => items.TryGetValue(url, out var e) && e.Failed;

    public void Dispose()
    {
        foreach (var e in items.Values) { lock (e) { e.Wrap?.Dispose(); e.Wrap = null; } }
        items.Clear();
    }
}
