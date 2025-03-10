﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="CacheStorage.cs" company="Catel development team">
//   Copyright (c) 2008 - 2015 Catel development team. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Catel.Caching
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading;
    using System.Threading.Tasks;
    using Policies;
    using Threading;

    /// <summary>
    /// The cache storage.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    public class CacheStorage<TKey, TValue> : ICacheStorage<TKey, TValue>
    {
        #region Fields
        private readonly Func<ExpirationPolicy> _defaultExpirationPolicyInitCode;

        /// <summary>
        /// Determines whether the cache storage can store null values.
        /// </summary>
        private readonly bool _storeNullValues;

        /// <summary>
        /// The dictionary.
        /// </summary>
        private readonly ConcurrentDictionary<TKey, CacheStorageValueInfo<TValue>> _dictionary;

        /// <summary>
        /// The synchronization object.
        /// </summary>
        private readonly object _syncObj = new object();

        /// <summary>
        /// The async locks.
        /// </summary>
        private readonly Dictionary<TKey, AsyncLock> _locksByKey = new Dictionary<TKey, AsyncLock>();

        /// <summary>
        /// The lock used when the key is <c>null</c>.
        /// </summary>
        private readonly AsyncLock _nullKeyLock = new AsyncLock();

        /// <summary>
        /// The timer that is being executed to invalidate the cache.
        /// </summary>
#pragma warning disable IDISP006 // Implement IDisposable.
        private Timer _expirationTimer;
#pragma warning restore IDISP006 // Implement IDisposable.

        /// <summary>
        /// The expiration timer interval.
        /// </summary>
        private TimeSpan _expirationTimerInterval;

        /// <summary>
        /// Determines whether the cache storage can check for expired items.
        /// </summary>
        private bool _checkForExpiredItems;
        #endregion

        /// <summary>
        /// Occurs when the item is expiring.
        /// </summary>
        public event EventHandler<ExpiringEventArgs<TKey, TValue>> Expiring;

        /// <summary>
        /// Occurs when the item has expired.
        /// </summary>
        public event EventHandler<ExpiredEventArgs<TKey, TValue>> Expired;

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="CacheStorage{TKey,TValue}" /> class.
        /// </summary>
        /// <param name="defaultExpirationPolicyInitCode">The default expiration policy initialization code.</param>
        /// <param name="storeNullValues">Allow store null values on the cache.</param>
        /// <param name="equalityComparer">The equality comparer.</param>
        public CacheStorage(Func<ExpirationPolicy> defaultExpirationPolicyInitCode = null, bool storeNullValues = false,
            IEqualityComparer<TKey> equalityComparer = null)
        {
            _dictionary = new ConcurrentDictionary<TKey, CacheStorageValueInfo<TValue>>(equalityComparer ?? EqualityComparer<TKey>.Default);
            _storeNullValues = storeNullValues;
            _defaultExpirationPolicyInitCode = defaultExpirationPolicyInitCode;

            _expirationTimerInterval = TimeSpan.FromSeconds(1);
        }

        #endregion

        #region ICacheStorage<TKey,TValue> Members
        /// <summary>
        /// Gets or sets whether values should be disposed on removal.
        /// </summary>
        /// <value><c>true</c> if values should be disposed on removal; otherwise, <c>false</c>.</value>
        public bool DisposeValuesOnRemoval { get; set; }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The value associated with the specified key, or default value for the type of the value if the key do not exists.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="key" /> is <c>null</c>.</exception>
        public TValue this[TKey key]
        {
            get { return Get(key); }
        }

        /// <summary>
        /// Gets the keys so it is possible to enumerate the cache.
        /// </summary>
        /// <value>The keys.</value>
        public IEnumerable<TKey> Keys
        {
            get
            {
                lock (_syncObj)
                {
                    return _dictionary.Keys;
                }
            }
        }

        /// <summary>
        /// Gets or sets the expiration timer interval.
        /// <para />
        /// The default value is <c>TimeSpan.FromSeconds(1)</c>.
        /// </summary>
        /// <value>The expiration timer interval.</value>
        public TimeSpan ExpirationTimerInterval
        {
            get { return _expirationTimerInterval; }
            set
            {
                _expirationTimerInterval = value;
                UpdateTimer();
            }
        }

        private void UpdateTimer()
        {
            lock (_syncObj)
            {
                if (!_checkForExpiredItems)
                {
                    if (_expirationTimer is not null)
                    {
                        _expirationTimer.Dispose();
                        _expirationTimer = null;
                    }
                }
                else
                {
                    var timeSpan = _expirationTimerInterval;

                    if (_expirationTimer is null)
                    {
                        // Always create timers with infinite, then update them later so we always have a populated timer field
                        _expirationTimer = new Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
                    }

                    _expirationTimer.Change(timeSpan, timeSpan);
                }
            }
        }

        /// <summary>
        /// Gets the value associated with the specified key
        /// </summary>
        /// <param name="key">The key of the value to get.</param>
        /// <returns>The value associated with the specified key, or default value for the type of the value if the key do not exists.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="key" /> is <c>null</c>.</exception>
        public TValue Get(TKey key)
        {
            Argument.IsNotNull("key", key);

            return ExecuteInLock(key, () =>
            {
                _dictionary.TryGetValue(key, out var valueInfo);

                return (valueInfo is not null) ? valueInfo.Value : default;
            });
        }

        /// <summary>
        /// Determines whether the cache contains a value associated with the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns><c>true</c> if the cache contains an element with the specified key; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="key" /> is <c>null</c>.</exception>
        public bool Contains(TKey key)
        {
            Argument.IsNotNull("key", key);

            return ExecuteInLock(key, () =>
            {
                return _dictionary.ContainsKey(key);
            });
        }

        /// <summary>
        /// Adds a value to the cache associated with to a key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="code">The deferred initialization code of the value.</param>
        /// <param name="expirationPolicy">The expiration policy.</param>
        /// <param name="override">Indicates if the key exists the value will be overridden.</param>
        /// <returns>The instance initialized by the <paramref name="code" />.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="key" /> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">If <paramref name="code" /> is <c>null</c>.</exception>
        [SuppressMessage("StyleCop.CSharp.SpacingRules", "SA1027:TabsMustNotBeUsed", Justification = "Reviewed. Suppression is OK here.")]
        public TValue GetFromCacheOrFetch(TKey key, Func<TValue> code, ExpirationPolicy expirationPolicy, bool @override = false)
        {
            Argument.IsNotNull("key", key);
            Argument.IsNotNull("code", code);

            return ExecuteInLock(key, () =>
            {
                if (!@override && _dictionary.TryGetValue(key, out var cacheStorageValueInfo))
                {
                    return cacheStorageValueInfo.Value;
                }

                var value = code();
                if (!(value is null) || _storeNullValues)
                {
                    if (expirationPolicy is null && _defaultExpirationPolicyInitCode is not null)
                    {
                        expirationPolicy = _defaultExpirationPolicyInitCode();
                    }

                    var valueInfo = new CacheStorageValueInfo<TValue>(value, expirationPolicy);
                    lock (_syncObj)
                    {
                        _dictionary[key] = valueInfo;
                    }

                    if (valueInfo.CanExpire)
                    {
                        _checkForExpiredItems = true;
                    }

                    if (expirationPolicy is not null && _expirationTimer is null)
                    {
                        UpdateTimer();
                    }
                }

                return value;
            });
        }

        /// <summary>
        /// Adds a value to the cache associated with to a key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="code">The deferred initialization code of the value.</param>
        /// <param name="override">Indicates if the key exists the value will be overridden.</param>
        /// <param name="expiration">The timespan in which the cache item should expire when added.</param>
        /// <returns>The instance initialized by the <paramref name="code" />.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="key" /> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">If <paramref name="code" /> is <c>null</c>.</exception>
        public TValue GetFromCacheOrFetch(TKey key, Func<TValue> code, bool @override = false, TimeSpan expiration = default(TimeSpan))
        {
            return GetFromCacheOrFetch(key, code, ExpirationPolicy.Duration(expiration), @override);
        }

        /// <summary>
        /// Adds a value to the cache associated with to a key asynchronously.
        /// <para />
        /// Note that this is a wrapper around <see cref="GetFromCacheOrFetch(TKey,System.Func{TValue},ExpirationPolicy,bool)"/>.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="code">The deferred initialization code of the value.</param>
        /// <param name="expirationPolicy">The expiration policy.</param>
        /// <param name="override">Indicates if the key exists the value will be overridden.</param>
        /// <returns>The instance initialized by the <paramref name="code" />.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="key" /> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">If <paramref name="code" /> is <c>null</c>.</exception>
        public Task<TValue> GetFromCacheOrFetchAsync(TKey key, Func<Task<TValue>> code, ExpirationPolicy expirationPolicy, bool @override = false)
        {
            Argument.IsNotNull("key", key);
            Argument.IsNotNull("code", code);

            return ExecuteInLockAsync(key, async () =>
            {
                if (!@override && _dictionary.TryGetValue(key, out var cacheStorageValueInfo))
                {
                    return cacheStorageValueInfo.Value;
                }

                var value = await code();

                if (!(value is null) || _storeNullValues)
                {
                    if (expirationPolicy is null && _defaultExpirationPolicyInitCode is not null)
                    {
                        expirationPolicy = _defaultExpirationPolicyInitCode();
                    }

                    var valueInfo = new CacheStorageValueInfo<TValue>(value, expirationPolicy);
                    lock (_syncObj)
                    {
                        _dictionary[key] = valueInfo;
                    }

                    if (valueInfo.CanExpire)
                    {
                        _checkForExpiredItems = true;
                    }

                    if (expirationPolicy is not null && _expirationTimer is null)
                    {
                        UpdateTimer();
                    }
                }

                return value;
            });
        }

        /// <summary>
        /// Adds a value to the cache associated with to a key asynchronously.
        /// <para />
        /// Note that this is a wrapper around <see cref="GetFromCacheOrFetch(TKey,System.Func{TValue},bool,TimeSpan)"/>.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="code">The deferred initialization code of the value.</param>
        /// <param name="override">Indicates if the key exists the value will be overridden.</param>
        /// <param name="expiration">The timespan in which the cache item should expire when added.</param>
        /// <returns>The instance initialized by the <paramref name="code" />.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="key" /> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">If <paramref name="code" /> is <c>null</c>.</exception>
        public Task<TValue> GetFromCacheOrFetchAsync(TKey key, Func<Task<TValue>> code, bool @override = false, TimeSpan expiration = default(TimeSpan))
        {
            return GetFromCacheOrFetchAsync(key, code, ExpirationPolicy.Duration(expiration), @override);
        }

        /// <summary>
        /// Adds a value to the cache associated with to a key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="override">Indicates if the key exists the value will be overridden.</param>
        /// <param name="expiration">The timespan in which the cache item should expire when added.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="key" /> is <c>null</c>.</exception>
        public void Add(TKey key, TValue @value, bool @override = false, TimeSpan expiration = default(TimeSpan))
        {
            Add(key, value, ExpirationPolicy.Duration(expiration), @override);
        }

        /// <summary>
        /// Adds a value to the cache associated with to a key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="expirationPolicy">The expiration policy.</param>
        /// <param name="override">Indicates if the key exists the value will be overridden.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="key" /> is <c>null</c>.</exception>
        public void Add(TKey key, TValue @value, ExpirationPolicy expirationPolicy, bool @override = false)
        {
            Argument.IsNotNull("key", key);

            if (!_storeNullValues)
            {
                Argument.IsNotNull("value", value);
            }

            GetFromCacheOrFetch(key, () => @value, expirationPolicy, @override);
        }

        /// <summary>
        /// Removes an item from the cache.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="action">The action that need to be executed in synchronization with the item cache removal.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="key" /> is <c>null</c>.</exception>
        public void Remove(TKey key, Action action = null)
        {
            Argument.IsNotNull("key", key);

            ExecuteInLock(key, () =>
            {
                RemoveItem(key, false, action);
            });
        }

        /// <summary>
        /// Clears all the items currently in the cache.
        /// </summary>
        public void Clear()
        {
            var keysToRemove = new List<TKey>();

            lock (_syncObj)
            {
                keysToRemove.AddRange(_dictionary.Keys);

                foreach (var keyToRemove in keysToRemove)
                {
                    ExecuteInLock(keyToRemove, () =>
                    {
                        RemoveItem(keyToRemove, false);
                    });
                }

                _checkForExpiredItems = false;

                UpdateTimer();
            }
        }

        /// <summary>
        /// Removes the expired items from the cache.
        /// </summary>
        [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1409:RemoveUnnecessaryCode", Justification = "Reviewed. Suppression is OK here.")]
        private void RemoveExpiredItems()
        {
            var containsItemsThatCanExpire = false;

            var keysToRemove = new List<TKey>();

            lock (_syncObj)
            {
                foreach (var cacheItem in _dictionary)
                {
                    var valueInfo = cacheItem.Value;
                    if (valueInfo.IsExpired)
                    {
                        keysToRemove.Add(cacheItem.Key);
                    }
                    else
                    {
                        if (!containsItemsThatCanExpire && valueInfo.CanExpire)
                        {
                            containsItemsThatCanExpire = true;
                        }
                    }
                }
            }

            foreach (var keyToRemove in keysToRemove)
            {
                ExecuteInLock(keyToRemove, () =>
                {
                    var removed = RemoveItem(keyToRemove, true);

                    if (!removed && !containsItemsThatCanExpire && _dictionary[keyToRemove].CanExpire)
                    {
                        containsItemsThatCanExpire = true;
                    }
                });
            }

            lock (_syncObj)
            {
                if (_checkForExpiredItems != containsItemsThatCanExpire)
                {
                    _checkForExpiredItems = containsItemsThatCanExpire;
                    UpdateTimer();
                }
            }
        }

        private void ExecuteInLock(TKey key, Action action)
        {
            ExecuteInLock(key, () =>
            {
                action();
                return true;
            });
        }

        private T ExecuteInLock<T>(TKey key, Func<T> action)
        {
            // Note: check comments below, this lock is required like this
            var asyncLock = GetLockByKey(key);
            lock (asyncLock)
            {
                // Looks complex, but we need to ensure that we can enter the same lock multiple times in non-async scenarios
                var taken = asyncLock.IsTaken;
                IDisposable unlockDisposable = null;

                try
                {
                    if (!taken)
                    {
                        unlockDisposable = asyncLock.Lock();
                    }

                    return action();
                }
                finally
                {
#pragma warning disable IDISP007 // Don't dispose injected.
                    unlockDisposable?.Dispose();
#pragma warning restore IDISP007 // Don't dispose injected.
                }
            }
        }

        private Task ExecuteInLockAsync(TKey key, Func<Task> action)
        {
            return ExecuteInLockAsync(key, async () =>
            {
                await action();
                return true;
            });
        }

        private async Task<T> ExecuteInLockAsync<T>(TKey key, Func<Task<T>> action)
        {
            var asyncLock = GetLockByKey(key);
            using (await asyncLock.LockAsync())
            {
                return await action();
            }
        }

        /// <summary>
        /// Gets the lock by key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The lock object.</returns>
        private AsyncLock GetLockByKey(TKey key)
        {
            if (key is null)
            {
                return _nullKeyLock;
            }

            // Note: we never clear items from the key locks, but this is so they can be re-used in the future without the cost 
            // of garbage collection
            lock (_syncObj)
            {
                if (!_locksByKey.TryGetValue(key, out var asyncLock))
                {
                    _locksByKey[key] = asyncLock = new AsyncLock();
                }

                return asyncLock;
            }
        }

        /// <summary>
        /// Called when the timer to clean up the cache elapsed.
        /// </summary>
        /// <param name="state">The timer state.</param>
        private void OnTimerElapsed(object state)
        {
            if (!_checkForExpiredItems)
            {
                return;
            }

            RemoveExpiredItems();
        }

        /// <summary>
        /// Remove item from cache by key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="raiseEvents">Indicates whether events should be raised.</param>
        /// <param name="action">The action that need to be executed in synchronization with the item cache removal.</param>
        /// <returns>The value indicating whether the item was removed.</returns>
        private bool RemoveItem(TKey key, bool raiseEvents, Action action = null)
        {
            // Try to get item, if there is no item by that key then return true to indicate that item was removed.
            if (!_dictionary.TryGetValue(key, out var item))
            {
                return true;
            }

            action?.Invoke();

            var cancel = false;
            var expirationPolicy = item.ExpirationPolicy;
            if (raiseEvents)
            {
                var expiringEventArgs = new ExpiringEventArgs<TKey, TValue>(key, item.Value, expirationPolicy);
                Expiring?.Invoke(this, expiringEventArgs);

                cancel = expiringEventArgs.Cancel;
                expirationPolicy = expiringEventArgs.ExpirationPolicy;
            }

            if (cancel)
            {
                if (expirationPolicy is null && _defaultExpirationPolicyInitCode is not null)
                {
                    expirationPolicy = _defaultExpirationPolicyInitCode.Invoke();
                }

                _dictionary[key] = new CacheStorageValueInfo<TValue>(item.Value, expirationPolicy);

                return false;
            }

            _dictionary.TryRemove(key, out _);

            var dispose = DisposeValuesOnRemoval;
            if (raiseEvents)
            {
                var expiredEventArgs = new ExpiredEventArgs<TKey, TValue>(key, item.Value, dispose);
                Expired?.Invoke(this, expiredEventArgs);

                dispose = expiredEventArgs.Dispose;
            }

            if (dispose)
            {
                item.DisposeValue();
            }

            return true;
        }
        #endregion
    }
}
