/// 
// Code taken from "Microsoft.Azure.KeyVault.Samples\samples\SampleAzureWebService\SampleKeyVaultConfigurationManager", and modified.
// Original license and copyright are as follows:
//
// Copyright © Microsoft Corporation, All Rights Reserved
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION
// ANY IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A
// PARTICULAR PURPOSE, MERCHANTABILITY OR NON-INFRINGEMENT.
//
// See the Apache License, Version 2.0 for the specific language
// governing permissions and limitations under the License.

using System;
using System.Runtime.Caching;
using System.Threading.Tasks;
using Microsoft.Azure;
using Microsoft.Azure.KeyVault;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

namespace AzureServiceConfigurationManager
{
    public static class ServiceConfigurationManager
    {
        private const string CacheName = "AllianceHealth.Azure.ServiceConfigurationManager.Cache";

        // The cache will store both the role setting value and Key Vault secret value
        private static MemoryCache _memoryCache = new MemoryCache( CacheName );
        private static KeyVaultClient _keyVaultClient = null;
        private static string _clientIdSettingName = null;
        private static string _certThumbprintSettingName = null;

        private static TimeSpan _defaultCacheExpirationTimeSpan = TimeSpan.Zero;

        #region public

        /// <summary>
        /// Initializes settings for authentication to Key Vault 
        /// </summary>
        /// <param name="authenticationCallback"> Key Vault authentication callback </param>
        /// <param name="defaultCacheLifespanSettingName">Default cache's lifespan setting name </param>
        public static void InitializeSecrets( ClientAssertionCertificate assertionCert, TimeSpan? cacheDefaultTimeout )
        {
            _keyVaultClient = new KeyVaultClient( new KeyVaultClient.AuthenticationCallback( ( authority, resource, scope ) =>
                GetAccessToken( authority, resource, scope, assertionCert ) ) );

            _defaultCacheExpirationTimeSpan = cacheDefaultTimeout ?? TimeSpan.Zero;
        }

        public static void InitializeSecrets( string clientIdSettingName, string certThumbprintSettingName, TimeSpan? cacheDefaultTimeout = null )
        {
            _clientIdSettingName = clientIdSettingName;
            _certThumbprintSettingName = certThumbprintSettingName;

            var clientId = ServiceConfigurationManager.GetSetting( clientIdSettingName );

            if ( String.IsNullOrEmpty( clientId ) )
                throw new ApplicationException( String.Format( "Missing configuration for '{0}'.", clientIdSettingName ) );

            var certificateThumbprint = ServiceConfigurationManager.GetSetting( certThumbprintSettingName );

            if ( String.IsNullOrEmpty( certificateThumbprint ) )
                throw new ApplicationException( String.Format( "Missing configuration for '{0}'.", certThumbprintSettingName ) );

            var certificate = CertificateHelper.FindCertificateByThumbprint( certificateThumbprint );
            var assertionCertificate = new ClientAssertionCertificate( clientId, certificate );

            /// Initialize the ServiceConfigurationManager to retrieve secrets held by an Azure Key Vault
            ServiceConfigurationManager.InitializeSecrets( assertionCertificate, cacheDefaultTimeout );
        }

        private static void InitializeSecrets()
        {
            InitializeSecrets( _clientIdSettingName, _certThumbprintSettingName, _defaultCacheExpirationTimeSpan );
        }

        /// <summary>
        /// Retrieves a configuration value or resolves the settings to its corresponding object
        /// Uses an in-memory cache in which throws away content after a certain time.
        /// </summary>
        /// <param name="settingName"> The setting name to get resolved or retrieved </param>
        /// <param name="cachedExpirationTimeSpan"> The cache expiration time span </param>
        /// <returns> The retrieved or resolved setting </returns>
        public static async Task<string> GetSettingAsync( string settingName, TimeSpan? cachedExpirationTimeSpan = null )
        {
            var settingValue = GetConfigurationSetting( settingName );

            // The secret value is cached along with the secret URL as a seperate cached entry because otherwise each time the secret URL would be 
            // retrieved from configuration file and if the secret value overwrites the secret URL, from different threads, the secret could be retrieved multiple times
            if ( SecretIdentifier.IsSecretIdentifier( settingValue ) )
                return await ResolveSecretSettingAsync( settingValue, cachedExpirationTimeSpan ).ConfigureAwait( false );

            // this could be extended to other types of resolution

            return settingValue;
        }


        /// <summary>
        /// Retrieves a configuration value or resolves the settings to its corresponding object
        /// Uses an in-memory cache in which throws away content after a certain time.
        /// </summary>
        /// <param name="settingName"> The setting name to get resolved or retrieved </param>
        /// <param name="cachedExpirationTimeSpan"> The cache expiration time span </param>
        /// <returns> The retrieved or resolved setting </returns>
        public static string GetSetting( string settingName, TimeSpan? cachedExpirationTimeSpan = null )
        {
            return GetSettingAsync( settingName, cachedExpirationTimeSpan ).Result;
        }

        /// <summary>
        /// Removes all the cache entries.
        /// </summary>
        public static void Reset()
        {
            var oldCache = _memoryCache;

            _memoryCache = new MemoryCache( CacheName );
            oldCache.Dispose();

            if ( _clientIdSettingName is String && _certThumbprintSettingName is String )
                InitializeSecrets();
        }

        #endregion

        #region private

        /// <summary>
        /// Get the configuration setting from cache or if not available from cloud configuration
        /// </summary>
        /// <param name="settingName"> the setting name </param>
        /// <returns> the configuration value</returns>
        private static string GetConfigurationSetting( string settingName )
        {
            var configurationSettingFactory = new Lazy<string>(
                    () => CloudConfigurationManager.GetSetting( settingName ) );

            return CacheAddOrGet( settingName, configurationSettingFactory, ObjectCache.InfiniteAbsoluteExpiration );
        }

        /// <summary>
        /// Resolves secret setting by calling Key Vault
        /// </summary>
        /// <param name="secretUrl"> The secret URL </param>
        /// <param name="cachedExpirationTimeSpan"> the time span to keep the secret value in the cache </param>
        /// <returns> the secret value </returns>
        public static async Task<string> ResolveSecretSettingAsync( string secretUrl, TimeSpan? cachedExpirationTimeSpan )
        {
            if ( _keyVaultClient == null )
                throw new Exception( "ServiceConfigurationManager.InitializeSecrets must be called to retrieve secrets." );

            // set the expiry time to infinity if not specified
            var absoluteExpiration = DateTimeOffset.Now + ( cachedExpirationTimeSpan ?? _defaultCacheExpirationTimeSpan );

            var keyvaultSettingFactory = new Lazy<Task<string>>(
                async () =>
                {
                    var secret = await _keyVaultClient.GetSecretAsync( secretUrl ).ConfigureAwait( false );
                    return secret.Value;
                } );

            //Resolve the value
            return await CacheAddOrGet( secretUrl, keyvaultSettingFactory, absoluteExpiration ).ConfigureAwait( false );
        }

        /// <summary>
        /// Gets a cache value. If the value does not exists add that to the cache and then return
        /// </summary>
        /// <typeparam name="T"> The type of the cached value </typeparam>
        /// <param name="settingName"> The setting name </param>
        /// <param name="newValueFactory"> A factory method to resolve the setting name to its value </param>
        /// <param name="policy"> The caching policy </param>
        /// <returns> Cached value corresponds to the setting </returns>
        private static T CacheAddOrGet<T>( string settingName, Lazy<T> newValueFactory, DateTimeOffset absoluteExpiration )
        {
            // Get the existing cache member or if not available add new value. 
            // AddOrGetExisting is a thread-safe atomic operation which handles the locking mechanism for thread safty. 
            // To use the operation, the value is passes in as Lazy initialization to only be calculated when the key is not in the cache and to be atomic and thread-safe.
            var cachedValue = _memoryCache.AddOrGetExisting(
                               settingName,
                               newValueFactory,
                               absoluteExpiration ) as Lazy<T>;

            try
            {
                // For the first time adding the cache entry, cachedValue is set to null so the lazy object will be initialized
                return ( cachedValue ?? newValueFactory ).Value;
            }
            catch
            {
                // Evict from cache the secret that caused the exception to throw
                _memoryCache.Remove( settingName );
                throw;
            }
        }

        /// <summary>
        /// Authentication callback that gets a token using the X509 certificate
        /// </summary>
        /// <param name="authority">Address of the authority</param>
        /// <param name="resource">Identifier of the target resource that is the recipient of the requested token</param>
        /// <param name="scope">Scope</param>
        /// <param name="assertionCert">The assertion certificate</param>
        /// <returns> The access token </returns>
        private static async Task<string> GetAccessToken( string authority, string resource, string scope, ClientAssertionCertificate assertionCert )
        {
            var context = new AuthenticationContext( authority, TokenCache.DefaultShared );
            var result = await context.AcquireTokenAsync( resource, assertionCert );

            return result.AccessToken;
        }

        #endregion
    }
}
