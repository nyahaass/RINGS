using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using aframe;
using aframe.Updater;
using aframe.ViewModels;
using Microsoft.Win32;
using Newtonsoft.Json;
using RINGS.Common;
using RINGS.Models;

namespace RINGS
{
    public partial class Config : JsonConfigBase
    {
        #region Lazy Singleton

        private readonly static Lazy<Config> instance = new Lazy<Config>(Load);

        public static Config Instance => instance.Value;

        public Config()
        {
            this.DiscordChannelList.CollectionChanged += (_, __) => this.RaisePropertyChanged(nameof(this.DiscordChannelItemsSource));
            this.DiscordBotList.CollectionChanged += (_, __) => this.RaisePropertyChanged(nameof(this.DiscordBotItemsSource));
        }

        #endregion Lazy Singleton

        public static string FileName => Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "RINGS.config.json");

        public static Config Load()
        {
            MigrateConfig(FileName);

            var config = Config.Load<Config>(
                FileName,
                out bool isFirstLoad);

            // チャットページに親オブジェクトを設定する
            foreach (var overlay in config.ChatOverlaySettings)
            {
                foreach (var page in overlay.ChatPages)
                {
                    page.ParentOverlaySettings = overlay;
                }
            }

            if (isFirstLoad)
            {
                config.CharacterProfileList = CreateDefaultCharacterProfile();
                config.CharacterProfileList.First().ChannelLinkerList.AddRange(
                    CharacterProfileModel.CreateDefaultChannelLinkers());

                config.DiscordBotList = CreateDefaultDiscordBots();
                config.DiscordChannelList = CreateDefaultDiscordChannels();
            }

            return config;
        }

        public void Save() => this.Save(FileName);

        #region Migration

        /// <summary>
        /// バージョンアップ等による設定ファイルの追加、変更を反映する
        /// </summary>
        private static void MigrateConfig(
            string fileName)
        {
            fileName = SwitchFileName(fileName);

            if (!File.Exists(fileName))
            {
                return;
            }

            var config = Config.Load<Config>(
                fileName);

            var i = 0;

            // チャットページのチャンネルを整備する
            i = 0;
            var channels = ChatCodes.All
                .Select(x => new
                {
                    ChatCode = x,
                    Order = i++,
                })
                .ToArray();

            foreach (var overlay in config.ChatOverlaySettings)
            {
                foreach (var page in overlay.ChatPages)
                {
                    var handledChannels = new List<HandledChatChannelModel>(page.HandledChannels);

                    handledChannels
                        .Where(x => !ChatCodes.All.Contains(x.ChatCode))
                        .ToArray()
                        .Walk(x => handledChannels.Remove(x));

                    handledChannels.AddRange(ChatCodes.All
                        .Where(x => !handledChannels.Any(y => y.ChatCode == x))
                        .Select(x => new HandledChatChannelModel()
                        {
                            ChatCode = x,
                            IsEnabled = true,
                        }));

                    handledChannels.Sort((x, y) =>
                    {
                        var orderX = channels.FirstOrDefault(z => z.ChatCode == x.ChatCode)?.Order ?? int.MaxValue;
                        var orderY = channels.FirstOrDefault(z => z.ChatCode == y.ChatCode)?.Order ?? int.MaxValue;
                        return orderX - orderY;
                    });

                    page.HandledChannels = handledChannels.ToArray();

                    // 除外フィルタを設定する
                    if (page.IgnoreFilters == null)
                    {
                        page.IgnoreFilters = FilterModel.CreateDefualtIgnoreFilters();
                    }
                    else
                    {
                        for (int j = 0; j < page.IgnoreFilters.Length; j++)
                        {
                            if (page.IgnoreFilters[j] == null)
                            {
                                page.IgnoreFilters[j] = new FilterModel();
                            }
                        }
                    }
                }
            }

            // ログカラー設定を整備する
            var colors = new List<ChatChannelSettingsModel>(config.ChatChannelsSettings);

            colors
                .Where(x => !ChatCodes.All.Contains(x.ChatCode))
                .ToArray()
                .Walk(x => colors.Remove(x));

            colors.AddRange(ChatCodes.All
                .Where(x => !colors.Any(y => y.ChatCode == x))
                .Select(x => new ChatChannelSettingsModel()
                {
                    ChatCode = x,
                }));

            colors.Sort((x, y) =>
            {
                var orderX = channels.FirstOrDefault(z => z.ChatCode == x.ChatCode)?.Order ?? int.MaxValue;
                var orderY = channels.FirstOrDefault(z => z.ChatCode == y.ChatCode)?.Order ?? int.MaxValue;
                return orderX - orderY;
            });

            config.ChatChannelsSettings = colors.ToArray();

            // キャラクタープロファイルを整備する
            i = 0;
            var linkers = ChatCodes.LinkableChannels
                .Select(x => new
                {
                    ChatCode = x,
                    Order = i++,
                })
                .ToArray();

            foreach (var prof in config.CharacterProfileList)
            {
                prof.ChannelLinkerList
                    .Where(x => !ChatCodes.LinkableChannels.Contains(x.ChatCode))
                    .ToArray()
                    .Walk(x => prof.ChannelLinkerList.Remove(x));

                prof.ChannelLinkerList.AddRange(ChatCodes.LinkableChannels
                    .Where(x => !prof.ChannelLinkerList.Any(y => y.ChatCode == x))
                    .Select(x => new ChannelLinkerModel()
                    {
                        ChatCode = x,
                    }));

                prof.ChannelLinkerList.Sort((x, y) =>
                {
                    var orderX = linkers.FirstOrDefault(z => z.ChatCode == x.ChatCode)?.Order ?? int.MaxValue;
                    var orderY = linkers.FirstOrDefault(z => z.ChatCode == y.ChatCode)?.Order ?? int.MaxValue;
                    return orderX - orderY;
                });
            }

            // 保存する
            config.Save();
        }

        #endregion Migration

        #region Update Checker

        private string updateSourceUri = DefaultUpdateSourceUri;

        [JsonProperty(PropertyName = "update_source_uri")]
        public string UpdateSourceUri
        {
            get => this.updateSourceUri;
            set => this.SetProperty(ref this.updateSourceUri, value);
        }

        private ReleaseChannels updateChannel = ReleaseChannels.Stable;

        [JsonProperty(PropertyName = "update_channel")]
        public ReleaseChannels UpdateChannel
        {
            get => this.updateChannel;
            set
            {
                if (this.SetProperty(ref this.updateChannel, value))
                {
                    HelpViewModel.Instance.RaiseCurrentReleaseChannelChanged();
                }
            }
        }

        private DateTimeOffset lastUpdateTimestamp = DateTimeOffset.MinValue;

        [JsonIgnore]
        public DateTimeOffset LastUpdateTimestamp
        {
            get => this.lastUpdateTimestamp;
            set => this.SetProperty(ref this.lastUpdateTimestamp, value);
        }

        [JsonProperty(PropertyName = "last_update_timestamp")]
        public string LastUpdateTimestampCrypted
        {
            get => Crypter.EncryptString(this.lastUpdateTimestamp.ToString("o"));
            set
            {
                DateTime d;
                if (DateTime.TryParse(value, out d))
                {
                    if (d > DateTime.Now)
                    {
                        d = DateTime.Now;
                    }

                    this.lastUpdateTimestamp = d;
                    return;
                }

                try
                {
                    var decrypt = Crypter.DecryptString(value);
                    if (DateTime.TryParse(decrypt, out d))
                    {
                        if (d > DateTime.Now)
                        {
                            d = DateTime.Now;
                        }

                        this.lastUpdateTimestamp = d;
                        return;
                    }
                }
                catch (Exception)
                {
                }

                this.lastUpdateTimestamp = DateTime.MinValue;
            }
        }

        #endregion Update Checker

        #region Data

        [JsonIgnore]
        public string AppName => Assembly.GetExecutingAssembly().GetTitle();

        [JsonIgnore]
        public string AppNameWithVersion => $"{this.AppName} - {this.AppVersionString}";

        [JsonIgnore]
        public Version AppVersion => Assembly.GetExecutingAssembly().GetVersion();

        [JsonIgnore]
        public ReleaseChannels AppReleaseChannel => Assembly.GetExecutingAssembly().GetReleaseChannels();

        [JsonIgnore]
        public string AppVersionString => $"v{this.AppVersion.ToString()}";

        private double scale = 1.0;

        [JsonProperty(PropertyName = "scale")]
        public double Scale
        {
            get => this.scale;
            set => this.SetProperty(ref this.scale, Math.Round(value, 2));
        }

        private double x;

        [JsonProperty(PropertyName = "X")]
        public double X
        {
            get => this.x;
            set => this.SetProperty(ref this.x, Math.Round(value, 1));
        }

        private double y;

        [JsonProperty(PropertyName = "Y")]
        public double Y
        {
            get => this.y;
            set => this.SetProperty(ref this.y, Math.Round(value, 1));
        }

        private double w;

        [JsonProperty(PropertyName = "W")]
        public double W
        {
            get => this.w;
            set
            {
                if (App.Current.MainWindow?.WindowState != WindowState.Minimized)
                {
                    this.SetProperty(ref this.w, Math.Round(value, 1));
                }
            }
        }

        private double h;

        [JsonProperty(PropertyName = "H")]
        public double H
        {
            get => this.h;
            set
            {
                if (App.Current.MainWindow?.WindowState != WindowState.Minimized)
                {
                    this.SetProperty(ref this.h, Math.Round(value, 1));
                }
            }
        }

        private bool isStartupWithWindows;

        [JsonProperty(PropertyName = "is_startup_with_windows")]
        public bool IsStartupWithWindows
        {
            get => this.isStartupWithWindows;
            set
            {
                if (this.SetProperty(ref this.isStartupWithWindows, value))
                {
                    this.SetStartup(value);
                }
            }
        }

        public async void SetStartup(
            bool isStartup) =>
            await Task.Run(() =>
            {
                using (var regkey = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run",
                    true))
                {
                    if (isStartup)
                    {
                        regkey.SetValue(
                            Assembly.GetExecutingAssembly().GetProduct(),
                            $"\"{Assembly.GetExecutingAssembly().Location}\"");
                    }
                    else
                    {
                        regkey.DeleteValue(
                            Assembly.GetExecutingAssembly().GetProduct(),
                            false);
                    }
                }
            });

        private bool isMinimizeStartup;

        [JsonProperty(PropertyName = "is_minimize_startup")]
        public bool IsMinimizeStartup
        {
            get => this.isMinimizeStartup;
            set => this.SetProperty(ref this.isMinimizeStartup, value);
        }

        private bool isShutdownWhenMissingFFXIV;

        [JsonProperty(PropertyName = "is_shutdown_when_missing_ffxiv")]
        public bool IsShutdownWhenMissingFFXIV
        {
            get => this.isShutdownWhenMissingFFXIV;
            set => this.SetProperty(ref this.isShutdownWhenMissingFFXIV, value);
        }

        private bool isUseBuiltInBrowser = true;

        [JsonProperty(PropertyName = "use_builtin_browser", DefaultValueHandling = DefaultValueHandling.Include)]
        public bool IsUseBuiltInBrowser
        {
            get => this.isUseBuiltInBrowser;
            set => this.SetProperty(ref this.isUseBuiltInBrowser, value);
        }

        private bool isEnabledChatRawLog = false;

        [JsonProperty(PropertyName = "is_enabled_chat_raw_log", DefaultValueHandling = DefaultValueHandling.Include)]
        public bool IsEnabledChatRawLog
        {
            get => this.isEnabledChatRawLog;
            set => this.SetProperty(ref this.isEnabledChatRawLog, value);
        }

        private double builtinBrowserSize = 80.0d;

        [JsonProperty(PropertyName = "builtin_browser_size", DefaultValueHandling = DefaultValueHandling.Include)]
        public double BuiltinBrowserSize
        {
            get => this.builtinBrowserSize;
            set => this.SetProperty(ref this.builtinBrowserSize, Math.Round(value));
        }

        private double chatLogPollingInterval = 10.0d;

        [JsonProperty(PropertyName = "chatlog_polling_interval")]
        public double ChatLogPollingInterval
        {
            get => this.chatLogPollingInterval;
            set => this.SetProperty(ref this.chatLogPollingInterval, value);
        }

        private double duplicateLogDue = 300.0d;

        [JsonProperty(PropertyName = "duplicate_log_due")]
        public double DuplicateLogDue
        {
            get => this.duplicateLogDue;
            set => this.SetProperty(ref this.duplicateLogDue, value);
        }

        private ThreadPriority chatLogSubscriberThreadPriority = ThreadPriority.BelowNormal;

        [JsonProperty(PropertyName = "chatlog_subscriber_threadpriority")]
        public ThreadPriority ChatLogSubscriberThreadPriority
        {
            get => this.chatLogSubscriberThreadPriority;
            set => this.SetProperty(ref this.chatLogSubscriberThreadPriority, value);
        }

        private double chatLogScrollBarWidth = 6.0d;

        [JsonProperty(PropertyName = "chatlog_scrollbar_width")]
        public double ChatLogScrollBarWidth
        {
            get => this.chatLogScrollBarWidth;
            set => this.SetProperty(ref this.chatLogScrollBarWidth, value);
        }

        private string fileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        [JsonProperty(PropertyName = "file_directory")]
        public string FileDirectory
        {
            get => this.fileDirectory;
            set => this.SetProperty(ref this.fileDirectory, value);
        }

        private double imageOpacity = 1.0d;

        [JsonProperty(PropertyName = "image_opacity")]
        public double ImageOpacity
        {
            get => this.imageOpacity;
            set => this.SetProperty(ref this.imageOpacity, value);
        }

        private int chatLogBufferSize = 1024;

        [JsonProperty(PropertyName = "chatlog_buffer_size")]
        public int ChatLogBufferSize
        {
            get => this.chatLogBufferSize;
            set => this.SetProperty(ref this.chatLogBufferSize, value);
        }

        private readonly Dictionary<string, ChatOverlaySettingsModel> chatOverlaySettings = new Dictionary<string, ChatOverlaySettingsModel>();

        [JsonProperty(PropertyName = "chat_overlays", DefaultValueHandling = DefaultValueHandling.Include)]
        public ChatOverlaySettingsModel[] ChatOverlaySettings
        {
            get => this.chatOverlaySettings.Values.ToArray();
            set
            {
                this.chatOverlaySettings.Clear();

                if (value != null)
                {
                    foreach (var item in value)
                    {
                        this.chatOverlaySettings[item.Name] = item;
                    }
                }

                this.RaisePropertyChanged();
            }
        }

        public void AddChatOverlaySettings(
            ChatOverlaySettingsModel settings)
        {
            this.chatOverlaySettings[settings.Name] = settings;
            this.RaisePropertyChanged(nameof(this.ChatOverlaySettings));
        }

        public void RemoveChatOverlaySettings(
            ChatOverlaySettingsModel settings)
        {
            if (this.chatOverlaySettings.ContainsKey(settings.Name))
            {
                foreach (var page in settings.ChatPages)
                {
                    page.DisposeLogBuffer();
                }

                this.chatOverlaySettings.Remove(settings.Name);
                settings = null;

                this.RaisePropertyChanged(nameof(this.ChatOverlaySettings));
            }
        }

        private readonly Dictionary<string, ChatChannelSettingsModel> chatChannelsSettings = new Dictionary<string, ChatChannelSettingsModel>();

        [JsonProperty(PropertyName = "chat_channels", DefaultValueHandling = DefaultValueHandling.Include)]
        public ChatChannelSettingsModel[] ChatChannelsSettings
        {
            get => this.chatChannelsSettings.Values.ToArray();
            set
            {
                this.chatChannelsSettings.Clear();

                if (value != null)
                {
                    foreach (var item in value)
                    {
                        this.chatChannelsSettings[item.ChatCode] = item;
                    }
                }

                this.RaisePropertyChanged();
            }
        }

        public ChatOverlaySettingsModel GetChatOverlaySettings(
            string name)
        {
            if (!string.IsNullOrEmpty(name) &&
                this.chatOverlaySettings.ContainsKey(name))
            {
                return this.chatOverlaySettings[name];
            }

            return null;
        }

        public ChatChannelSettingsModel GetChatChannelsSettings(
            string chatCode)
        {
            if (!string.IsNullOrEmpty(chatCode) &&
                this.chatChannelsSettings.ContainsKey(chatCode))
            {
                return this.chatChannelsSettings[chatCode];
            }

            return null;
        }

        [JsonProperty(PropertyName = "character_profiles", DefaultValueHandling = DefaultValueHandling.Include)]
        public SuspendableObservableCollection<CharacterProfileModel> CharacterProfileList
        {
            get;
            private set;
        } = new SuspendableObservableCollection<CharacterProfileModel>();

        [JsonIgnore]
        public CharacterProfileModel ActiveProfile
        {
            get
            {
                lock (this.CharacterProfileList)
                {
                    var fixedProf = this.CharacterProfileList.FirstOrDefault(x =>
                        x.IsEnabled &&
                        x.IsFixedActivate);
                    if (fixedProf != null)
                    {
                        return fixedProf;
                    }

                    return this.CharacterProfileList.FirstOrDefault(x =>
                        x.IsEnabled &&
                        x.IsActive);
                }
            }
        }

        [JsonProperty(PropertyName = "discord_channels", DefaultValueHandling = DefaultValueHandling.Include)]
        public SuspendableObservableCollection<DiscordChannelModel> DiscordChannelList
        {
            get;
            private set;
        } = new SuspendableObservableCollection<DiscordChannelModel>();

        [JsonProperty(PropertyName = "discord_bots", DefaultValueHandling = DefaultValueHandling.Include)]
        public SuspendableObservableCollection<DiscordBotModel> DiscordBotList
        {
            get;
            private set;
        } = new SuspendableObservableCollection<DiscordBotModel>();

        [JsonIgnore]
        public IEnumerable<DiscordChannelModel> DiscordChannelItemsSource
            => new[]
            {
                new DiscordChannelModel()
                {
                    ID = string.Empty,
                    Name = "NO LINKED",
                },
            }.Concat(this.DiscordChannelList);

        [JsonIgnore]
        public static readonly string EmptyBotName = "NO ASSIGNED";

        [JsonIgnore]
        public IEnumerable<DiscordBotModel> DiscordBotItemsSource
            => new[]
            {
                new DiscordBotModel()
                {
                    Name = EmptyBotName,
                    Token = string.Empty,
                },
            }.Concat(this.DiscordBotList);

        #region TTS Settings

        private bool isTTSEnabled;

        [JsonProperty(PropertyName = "tts_enabled")]
        public bool IsTTSEnabled
        {
            get => this.isTTSEnabled;
            set => this.SetProperty(ref this.isTTSEnabled, value);
        }

        private string ttsServerAddress;

        [JsonProperty(PropertyName = "tts_server_address")]
        public string TTSServerAddress
        {
            get => this.ttsServerAddress;
            set => this.SetProperty(ref this.ttsServerAddress, value);
        }

        private int ttsServerPort;

        [JsonProperty(PropertyName = "tts_server_port")]
        public int TTSServerPort
        {
            get => this.ttsServerPort;
            set => this.SetProperty(ref this.ttsServerPort, value);
        }

        private int ttsSpeed;

        [JsonProperty(PropertyName = "tts_speed")]
        public int TTSSpeed
        {
            get => this.ttsSpeed;
            set => this.SetProperty(ref this.ttsSpeed, value);
        }

        private int ttsVolume;

        [JsonProperty(PropertyName = "tts_volume")]
        public int TTSVolume
        {
            get => this.ttsVolume;
            set => this.SetProperty(ref this.ttsVolume, value);
        }

        private bool isTTSIgnoreSelf;

        [JsonProperty(PropertyName = "tts_ignore_self")]
        public bool IsTTSIgnoreSelf
        {
            get => this.isTTSIgnoreSelf;
            set => this.SetProperty(ref this.isTTSIgnoreSelf, value);
        }

        #endregion TTS Settings

        #endregion Data
    }
}
