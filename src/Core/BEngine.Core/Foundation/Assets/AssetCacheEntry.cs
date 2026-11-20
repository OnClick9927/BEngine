namespace BEngine;

    internal sealed class AssetCacheEntry
    {
        private WeakReference<BAsset>? _asset;
        private bool _loading;
        internal bool IsLoading => _loading;

        internal BAsset? GetOrLoad(Func<BAsset?> loader)
        {
            ArgumentNullException.ThrowIfNull(loader);
            return GetOrLoad(loader, static load => load());
        }

        internal BAsset? GetOrLoad<TState>(TState state, Func<TState, BAsset?> loader)
        {
            ArgumentNullException.ThrowIfNull(loader);
            if (_asset?.TryGetTarget(out var cached) is true && !cached.isDestroyed) return cached;
            if (_loading) throw new InvalidOperationException("A cyclic asset load was detected.");
            _loading = true;

            BAsset? loaded = null;
            try
            {
                loaded = loader(state);
                return loaded;
            }
            finally
            {
                if (loaded is not null) _asset = new WeakReference<BAsset>(loaded);
                _loading = false;
            }
        }

        internal bool TryGetTarget(out BAsset asset)
        {
            if (_asset?.TryGetTarget(out asset!) is true && !asset.isDestroyed) return true;
            asset = null!;
            return false;
        }
    }
