// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace UWPVoiceAssistantSample
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using UWPVoiceAssistantSample.AudioCommon;
    using Windows.Storage;
    using Windows.Storage.Search;
    using Windows.UI.Xaml;

    /// <summary>
    /// Bot specific application settings obtained for config.json.
    /// </summary>
    public class AppSettings
    {
        private static ILogProvider logger = LogRouter.GetClassLogger();
        private static Lazy<AppSettings> instance;
        private StorageFile sourceFile;
        private StorageFolder sourceFolder;
        private FileSystemWatcher configFolderWatcher;

        public static AppSettings Instance { get => instance.Value; }

        public static StorageFile InstanceSourceFile { get => instance.Value.sourceFile; }

        public event Action FileChanged;

        /// <summary>
        /// Gets or sets Speech Subscription Key.
        /// </summary>
        public string SpeechSubscriptionKey { get; set; }

        /// <summary>
        /// Gets or sets Azure Region.
        /// </summary>
        public string AzureRegion { get; set; }

        /// <summary>
        /// Gets or sets CustomSpeechId.
        /// </summary>
        public string CustomSpeechId { get; set; }

        /// <summary>
        /// Gets or sets CustomVoiceId.
        /// </summary>
        public string CustomVoiceIds { get; set; }

        /// <summary>
        /// Gets or sets Custom Commands App Id.
        /// </summary>
        public string CustomCommandsAppId { get; set; }

        /// <summary>
        /// Gets or sets Bot Id.
        /// </summary>
        public string BotId { get; set; }

        /// <summary>
        /// Gets or sets KeywordActivationModelPath.
        /// </summary>
        public string KeywordActivationModelPath { get; set; }

        public Version KeywordActivationModelVersion { get; set; } = new Version(1, 0);

        public Version LastUpdatedKeywordActivationModelVersion { get; set; } = new Version(0, 0);

        /// <summary>
        /// Gets or sets KeywordConfirmationModelPath.
        /// </summary>
        public string KeywordConfirmationModelPath { get; set; }

        public bool EnableSecondStageKws { get; set; } = true;

        public bool EnableSdkLogging { get; set; } = false;

        public DialogAudio OutputFormat { get; set; } = DialogAudio.Mpeg24KHz96KBitRateMono;

        public bool EnableAudioCaptureFiles { get; set; } = false;

        static AppSettings()
        {
            Reload();
        }

        private AppSettings(StorageFile sourceFile, StorageFolder sourceFolder)
        {
            this.sourceFile = sourceFile;
            this.sourceFolder = sourceFolder;
            this.configFolderWatcher = new FileSystemWatcher(sourceFolder.Path, sourceFile.Name);
            this.configFolderWatcher.Changed += (s, e) =>
            {
                this.FileChanged.Invoke();
            };
            this.configFolderWatcher.NotifyFilter = NotifyFilters.LastWrite;
            this.configFolderWatcher.EnableRaisingEvents = true;

            var lastTime = File.GetLastWriteTime(sourceFile.Path);

            var query = this.sourceFolder.CreateFileQueryWithOptions(new QueryOptions(CommonFileQuery.DefaultQuery, new string[] { sourceFile.FileType }));
            query.ContentsChanged += (s, e) =>
            {
                var newLastTime = File.GetLastWriteTime(sourceFile.Path);
                if (newLastTime > lastTime)
                {
                    lastTime = newLastTime;
                    this.FileChanged?.Invoke();
                }
            };
            _ = query.GetFilesAsync();
        }

        private static readonly Uri DefaultConfigSourceUri = new Uri("ms-appx:///assets/defaultConfig.json");
        private static readonly StorageFolder ConfigFolder = ApplicationData.Current.LocalFolder;
        private static readonly string ConfigFilename = "config.json";

        public static async Task<AppSettings> LoadAsync()
        {
            var configFile = await ConfigFolder.TryGetItemAsync(ConfigFilename) as StorageFile;
            if (configFile == null)
            {
                var defaultConfig = await StorageFile.GetFileFromApplicationUriAsync(DefaultConfigSourceUri);
                await defaultConfig.CopyAsync(ConfigFolder, ConfigFilename);
                configFile = await ConfigFolder.TryGetItemAsync(ConfigFilename) as StorageFile;
            }

            var settings = await LoadFromStorageFileAsync(configFile);
            return settings;
        }

        public static async Task<AppSettings> LoadFromStorageFileAsync(StorageFile configFile)
        {
            var configFolder = await configFile.GetParentAsync();

            using (var configStream = await configFile.OpenStreamForReadAsync())
            using (var streamReader = new StreamReader(configStream))
            using (var jsonReader = new JsonTextReader(streamReader))
            {
                var deserializer = new JsonSerializer();
                var result = new AppSettings(configFile, configFolder);
                deserializer.Populate(jsonReader, result);
                return result;
            }
        }

        public static void Reload() => instance = new Lazy<AppSettings>(() =>
        {
            var loadTask = Task.Run(async () =>
            {
                return await LoadAsync();
            });
            loadTask.Wait();
            return loadTask.Result;
        });

        /// <summary>
        /// Verifies if Speech Subscription Key is provided and parses it to a GUID.
        /// </summary>
        /// <param name="key">Speech Subscription Key.</param>
        /// <returns>Bool - true if speech key is a valid guid.</returns>
        public static bool ValidateSubscriptionKey(string key)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrWhiteSpace(key))
            {
                string message = $"Failed to obtain Speech Subscription Key";
                logger.Log(message);
                throw new MissingFieldException(message);
            }

            if (Guid.TryParse(key, out Guid parsedGuid) &&
                parsedGuid.ToString("N", null).Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Verifies if Azure Region provided is listed within the speechRegions.
        /// </summary>
        /// <param name="region">Azure Region.</param>
        /// <returns>Bool - true if region is within the speechRegions.</returns>
        public static bool ValidateAzureRegion(string region)
        {
            List<string> speechRegions = new List<string>() { "westus", "westus2", "eastus", "eastus2", "westeurope", "northeurope", "southeastasia" };

            return speechRegions.Contains(region.ToLower(CultureInfo.CurrentCulture));
        }

        /// <summary>
        /// Verifies if Custom Speech Id and Custom Voice Ids provided are GUID's.
        /// </summary>
        /// <param name="id">Custom Speech Id or Custom Voice Id.</param>
        /// <returns>Bool - true if id is a valid Guid.</returns>
        public static bool ValidateCustomId(string id)
        {
            if (Guid.TryParse(id, out Guid parsedGuid))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Verfies keyword activation and confirmation paths are not null and start with ms-appx:///.
        /// </summary>
        /// <param name="path">Keyword activation and confirmation file paths, relative to ms-appx:///.</param>
        /// <returns>Bool - true if path is not null and starts with ms-appx:///.</returns>
        public static bool ValidateModelFilePath(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                if (path.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Validate AppSettings instance.
        /// </summary>
        /// <param name="instance">An instance of AppSettings.</param>
        public static void ValidateAppSettings(AppSettings instance)
        {
            if (ValidateSubscriptionKey(instance.SpeechSubscriptionKey) == false)
            {
                logger.Log("Failed to validate Speech Key");
            }

            if (string.IsNullOrWhiteSpace(instance.AzureRegion) ||
                ValidateAzureRegion(instance.AzureRegion) == false)
            {
                logger.Log("Failed to validate Azure Region");
            }

            if (!string.IsNullOrWhiteSpace(instance.CustomSpeechId) || !string.IsNullOrWhiteSpace(instance.CustomVoiceIds))
            {
                if (ValidateCustomId(instance.CustomSpeechId) == false)
                {
                    logger.Log("Failed to validate Custom Speech Id");
                }

                if (ValidateCustomId(instance.CustomVoiceIds) == false)
                {
                    logger.Log("Failed to validate Custom Voice Id");
                }
            }

            if (!string.IsNullOrWhiteSpace(instance.CustomCommandsAppId))
            {
                if (ValidateCustomId(instance.CustomCommandsAppId) == false)
                {
                    logger.Log("Failed to validate Custom Commands App Id");
                }
            }

            if (!string.IsNullOrWhiteSpace(instance.BotId))
            {
                if (ValidateCustomId(instance.BotId) == false)
                {
                    logger.Log("Failed to validate Bot Id");
                }
            }

            if (!string.IsNullOrWhiteSpace(instance.KeywordActivationModelPath))
            {
                if (ValidateModelFilePath(instance.KeywordActivationModelPath) == false)
                {
                    logger.Log(LogMessageLevel.Error, "Failed to validate KeywordActivationModelPath. Verify path starts with ms-appx:///");
                }
            }

            if (!string.IsNullOrWhiteSpace(instance.KeywordConfirmationModelPath))
            {
                if (ValidateModelFilePath(instance.KeywordConfirmationModelPath) == false)
                {
                    logger.Log(LogMessageLevel.Error, "Failed to validate KeywordConfirmationModelPath. Verify path starts with with ms-appx:///");
                }
            }
        }
    }
}
