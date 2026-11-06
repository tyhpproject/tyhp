using System.Runtime.CompilerServices;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Emitter
{
    internal enum EmitLookupKind
    {
        /// <summary><see cref="StructEmissionHelper.FindStructSymbol"/>.</summary>
        Struct,

        /// <summary>Non-struct object declaration by short name (<c>AliasConverter.FindObjectSymbol</c>).</summary>
        Object,
    }

    /// <summary>
    /// Memoizes whole-scope-tree name searches while <see cref="TyhpEmitter.Emit"/> runs. The tree
    /// includes every tyhpdef stub, so one search visits tens of thousands of scopes, and emit asks
    /// the same question for every type reference. Emit never adds or removes scopes or symbols, so
    /// a search's answer cannot change while a cache is active. Outside an active emit (or on
    /// another thread) searches run uncached.
    /// </summary>
    internal sealed class EmitLookupCache
    {
        [ThreadStatic]
        private static EmitLookupCache? _current;

        private readonly Dictionary<LookupKey, ObjectDeclarationSymbol?> _results = new();

        /// <summary>
        /// Activates a cache on this thread until the returned handle is disposed. A nested call
        /// reuses the active cache.
        /// </summary>
        public static Activation Activate()
        {
            if (_current is not null)
            {
                return default;
            }

            _current = new EmitLookupCache();
            return new Activation(ownsCache: true);
        }

        public static ObjectDeclarationSymbol? GetOrSearch(
            EmitLookupKind kind,
            IBaseScope scope,
            string name,
            Func<IBaseScope, string, ObjectDeclarationSymbol?> search)
        {
            var cache = _current;
            if (cache is null)
            {
                return search(scope, name);
            }

            var key = new LookupKey(kind, scope, name);
            if (!cache._results.TryGetValue(key, out var found))
            {
                found = search(scope, name);
                cache._results[key] = found;
            }

            return found;
        }

        public readonly struct Activation : IDisposable
        {
            private readonly bool _ownsCache;

            public Activation(bool ownsCache)
            {
                this._ownsCache = ownsCache;
            }

            public void Dispose()
            {
                if (this._ownsCache)
                {
                    _current = null;
                }
            }
        }

        private readonly struct LookupKey : IEquatable<LookupKey>
        {
            private readonly EmitLookupKind _kind;
            private readonly IBaseScope _scope;
            private readonly string _name;

            public LookupKey(EmitLookupKind kind, IBaseScope scope, string name)
            {
                this._kind = kind;
                this._scope = scope;
                this._name = name;
            }

            public bool Equals(LookupKey other)
                => this._kind == other._kind
                    && ReferenceEquals(this._scope, other._scope)
                    && string.Equals(this._name, other._name, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is LookupKey other && this.Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(
                    this._kind,
                    RuntimeHelpers.GetHashCode(this._scope),
                    StringComparer.Ordinal.GetHashCode(this._name));
        }
    }
}
