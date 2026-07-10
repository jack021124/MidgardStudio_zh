using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MidgardStudio.App.Common;
using MidgardStudio.Core.Grf;
using MidgardStudio.Grf;

namespace MidgardStudio.App.Services;

/// <summary>Resolves client asset images (item icon / illustration / sprite animation) from the layered GRFs.</summary>
public sealed class GrfImageService
{
    private readonly GrfService _grf;
    private readonly AppSettingsService _settings;
    private int _lastCodepage; // tracks the global codepage so a change can flush the icon cache

    // Decoding a GRF image (decompress + bitmap build) is expensive and was repeated on every selection /
    // resource-name commit. Cache the frozen ImageSource keyed by GRF path and drop it whenever the configured
    // sources change. Frozen images are safe to share/cross-thread. The cache is bounded by BOTH an entry count
    // and an approximate byte budget — a flat count cap let a few large sprites/illustrations grow it to GBs.
    private const int CacheCap = 2048;
    private const long ByteBudget = 128L * 1024 * 1024; // ~128 MB of decoded pixels
    private readonly Dictionary<string, (ImageSource? Img, long Bytes)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _order = new();
    private readonly object _lock = new();
    private long _bytes;

    public GrfImageService(GrfService grf, AppSettingsService settings)
    {
        _grf = grf;
        _settings = settings;
        _lastCodepage = settings.Settings.GlobalCodepage;
        _grf.SourcesChanged += ClearCache;
    }

    private void ClearCache()
    {
        lock (_lock) { _cache.Clear(); _order.Clear(); _bytes = 0; }
    }

    /// <summary>The global codepage used to decode client lua text. Resource names (identifiedResourceName)
    /// are stored as 1252 bytes in the GRF; when the client codec is something else (e.g. 936 for Chinese
    /// text), the decoded name must be re-projected back to 1252 so the GRF path lookup matches.</summary>
    private int ClientCodepage => _settings.Settings.GlobalCodepage;

    /// <summary>If the global encoding differs from 1252, re-projects a resource name from the client
    /// codepage back to 1252 (byte-preserving round-trip) so it matches the GRF's 1252 entry keys.
    /// A no-op when the codepage is already 1252. Safe on any string (returns input unchanged on failure).</summary>
    private string ReprojectToGrf(string? name)
    {
        int cp = ClientCodepage;
        // Drop the icon cache if the codepage changed since the last lookup (paths are now different).
        if (cp != _lastCodepage) { _lastCodepage = cp; ClearCache(); }
        if (cp == ViewEncoding.DefaultCodePage || string.IsNullOrEmpty(name)) return name ?? string.Empty;
        return ViewEncoding.Reproject(name, ViewEncoding.DefaultCodePage, cp);
    }

    private ImageSource? Cached(string path, Func<ImageSource?> decode)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var hit)) return hit.Img;
            var img = decode();
            long size = Estimate(img);
            _cache[path] = (img, size);
            _bytes += size;
            _order.Enqueue(path);
            // Evict oldest until both bounds hold. The just-added entry is evicted last, so an image larger
            // than the whole budget degrades to "not cached" rather than evicting everything else first.
            while (_order.Count > 0 && (_cache.Count > CacheCap || _bytes > ByteBudget))
            {
                if (_cache.Remove(_order.Dequeue(), out var ev)) _bytes -= ev.Bytes;
            }
            return img;
        }
    }

    /// <summary>Approximate decoded size in bytes of a bitmap (pixels × bytes-per-pixel); small const otherwise.</summary>
    private static long Estimate(ImageSource? img) =>
        img is BitmapSource b ? (long)b.PixelWidth * b.PixelHeight * Math.Max(1, (b.Format.BitsPerPixel + 7) / 8) : 4096;

    // ----- Icon picker: enumerate + decode icons from ONE chosen source (separate transient cache) -----

    private readonly Dictionary<string, ImageSource?> _iconPick = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpriteAnimation?> _spriteAnimPick = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The configured layered sources (GRFs / loose folders) the user can pick from.</summary>
    public IReadOnlyList<string> Sources => _grf.Sources;

    /// <summary>Open one source for icon browsing (clears the picker thumbnail + sprite caches).</summary>
    public void OpenIconSource(string source) { lock (_lock) { _iconPick.Clear(); _spriteAnimPick.Clear(); } _grf.OpenIconSource(source); }

    public void CloseIconSource() { lock (_lock) { _iconPick.Clear(); _spriteAnimPick.Clear(); } _grf.CloseIconSource(); }

    /// <summary>Base resource names of every item icon in the chosen source (item-icon folder only, .bmp only).</summary>
    public IReadOnlyList<string> IconResourceNames() => _grf.ItemIconResourceNames();

    /// <summary>Base names of every headgear accessory sprite in the chosen source (accessory sprite folder
    /// only, .spr only, "여_" prefix stripped) — i.e. the values the sprite-name field accepts.</summary>
    public IReadOnlyList<string> HeadgearSpriteNames() => _grf.HeadgearSpriteNames();

    /// <summary>A thumbnail for one icon resource from the chosen source (small transient cache; lazy per row).</summary>
    public ImageSource? IconThumbnail(string resourceName)
    {
        lock (_lock)
        {
            if (_iconPick.TryGetValue(resourceName, out var hit)) return hit;
            var img = GrfImaging.ToImageSource(_grf.ItemIconFromSource(resourceName));
            _iconPick[resourceName] = img;
            return img;
        }
    }

    /// <summary>The animated headgear accessory sprite (by base name, female .spr + .act) from the chosen source,
    /// so the sprite picker shows the moving sprite instead of a static frame sheet. Falls back to a single frame
    /// when there's no .act. Cached per source (cleared on source switch) so scrolling doesn't re-decode.</summary>
    public SpriteAnimation? HeadgearSpriteAnimation(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName)) return null;
        lock (_lock)
        {
            if (_spriteAnimPick.TryGetValue(baseName, out var hit)) return hit;
            string sprPath = GrfAssetPaths.HeadgearSpriteFemale(ReprojectToGrf(baseName));
            byte[]? spr = _grf.GetDataFromSource(sprPath);
            var anim = spr is null ? null
                : SpriteRenderer.Build(spr, _grf.GetDataFromSource(Path.ChangeExtension(sprPath, ".act")));
            _spriteAnimPick[baseName] = anim;
            return anim;
        }
    }

    public ImageSource? ItemIcon(string? resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName)) return null;
        var path = GrfAssetPaths.ItemIcon(ReprojectToGrf(resourceName));
        return Cached(path, () => GrfImaging.ToImageSource(_grf.GetImage(path)));
    }

    public ImageSource? ItemCollection(string? resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName)) return null;
        var path = GrfAssetPaths.ItemCollection(ReprojectToGrf(resourceName));
        return Cached(path, () => GrfImaging.ToImageSource(_grf.GetImage(path)));
    }

    public ImageSource? MonsterSprite(string? spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName)) return null;
        var path = GrfAssetPaths.MonsterSprite(ReprojectToGrf(spriteName));
        return Cached(path, () => GrfImaging.ToImageSource(_grf.GetImage(path)));
    }

    /// <summary>Decodes the monster's .spr/.act from the GRF into an animatable sequence of frames.</summary>
    public SpriteAnimation? MonsterAnimation(string? spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName)) return null;
        string sprPath = GrfAssetPaths.MonsterSprite(ReprojectToGrf(spriteName));
        byte[]? spr = _grf.GetData(sprPath);
        if (spr is null) return null;
        byte[]? act = _grf.GetData(Path.ChangeExtension(sprPath, ".act"));
        return SpriteRenderer.Build(spr, act);
    }
}
