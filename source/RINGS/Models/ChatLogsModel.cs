using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using aframe;
using Prism.Mvvm;
using RINGS.Common;

namespace RINGS.Models
{
    public class ChatLogsModel :
        BindableBase,
        IDisposable
    {
        public static List<ChatLogsModel> ActiveBuffers { get; private set; } = new List<ChatLogsModel>();

        public static void AddToBuffers(
            ChatLogModel log)
            => AddToBuffers(new[] { log });

        public static void AddToBuffers(
            IEnumerable<ChatLogModel> logs)
        {
            var buffers = default(IEnumerable<ChatLogsModel>);

            lock (ActiveBuffers)
            {
                buffers = ActiveBuffers.ToArray();
            }

            foreach (var buffer in buffers)
            {
                var targets = logs.Where(x => buffer?.FilterCallback(x) ?? false);
                buffer.AddRange(targets);
            }
        }

        public ChatLogsModel()
        {
            lock (ActiveBuffers)
            {
                ActiveBuffers.Add(this);
            }

            this.buffer = new Lazy<SuspendableObservableCollection<ChatLogModel>>(() =>
            {
                var b = new SuspendableObservableCollection<ChatLogModel>(InnerList);

                if (WPFHelper.IsDesignMode)
                {
                    this.CreateDesigntimeChatLogs(b);
                }

                b.CollectionChanged += this.Buffer_CollectionChanged;

                return b;
            });
        }

        public void Dispose()
        {
            lock (ActiveBuffers)
            {
                this.Buffer.CollectionChanged -= this.Buffer_CollectionChanged;
                this.Buffer.Clear();
                ActiveBuffers.Remove(this);
            }
        }

        public SuspendableObservableCollection<ChatLogModel> Buffer => this.buffer.Value;

        public event EventHandler<ChatLogAddedEventArgs> ChatLogAdded;

        protected void OnChatLogAdded(
            ChatLogAddedEventArgs e)
            => this.ChatLogAdded?.Invoke(this, e);

        public Predicate<ChatLogModel> FilterCallback { get; set; }

        private void Buffer_CollectionChanged(
            object sender,
            NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null &&
                e.NewItems.Count > 0)
            {
                foreach (var item in e.NewItems)
                {
                    this.OnChatLogAdded(new ChatLogAddedEventArgs(
                        this.parentPageSettings,
                        item as ChatLogModel));
                }
            }
        }

        private static readonly int BufferSize = 5120;

        private static List<ChatLogModel> InnerList => new List<ChatLogModel>(BufferSize + (BufferSize / 10));

        private Lazy<SuspendableObservableCollection<ChatLogModel>> buffer;

        private ChatOverlaySettingsModel parentOverlaySettings;

        public ChatOverlaySettingsModel ParentOverlaySettings
        {
            get => this.parentOverlaySettings;
            set => this.SetProperty(ref this.parentOverlaySettings, value);
        }

        private ChatPageSettingsModel parentPageSettings;

        public ChatPageSettingsModel ParentPageSettings
        {
            get => this.parentPageSettings;
            set => this.SetProperty(ref this.parentPageSettings, value);
        }

        public void Add(
            ChatLogModel log)
        {
            lock (this.Buffer)
            {
                if (this.IsDuplicate(log))
                {
                    return;
                }

                log.ParentOverlaySettings = this.ParentOverlaySettings;
                log.ParentPageSettings = this.ParentPageSettings;
                this.Buffer.Add(log);
            }
        }

        public void AddRange(
            IEnumerable<ChatLogModel> logs)
        {
            lock (this.Buffer)
            {
                foreach (var log in logs)
                {
                    if (this.IsDuplicate(log))
                    {
                        return;
                    }

                    log.ParentOverlaySettings = this.ParentOverlaySettings;
                    log.ParentPageSettings = this.ParentPageSettings;
                    this.Buffer.Add(log);
                }
            }
        }

        public void Clear()
        {
            lock (this.Buffer)
            {
                this.Buffer.Clear();
            }
        }

        private bool IsDuplicate(
            ChatLogModel chatLog)
            => this.Buffer.Any(x =>
            {
                var time = (chatLog.Timestamp - x.Timestamp).TotalSeconds;
                if (time <= 0.5)
                {
                    return
                        x.ChatCode == chatLog.ChatCode &&
                        x.Message == chatLog.Message;
                }

                return false;
            });

        public void Garbage()
        {
            lock (this.Buffer)
            {
                if (this.Buffer.Count <= BufferSize)
                {
                    return;
                }

                for (int i = 0; i < BufferSize; i++)
                {
                    this.Buffer.RemoveAt(0);
                }
            }
        }

        public void LoadDummyLogs()
        {
            this.RemoveDummyLogs();

            lock (this.Buffer)
            {
                this.CreateDesigntimeChatLogs(this.Buffer);
            }
        }

        public void RemoveDummyLogs()
        {
            lock (this.Buffer)
            {
                var dummys = this.Buffer.Where(x => x.IsDummy).ToArray();
                foreach (var item in dummys)
                {
                    this.Buffer.Remove(item);
                }
            }
        }

        private void CreateDesigntimeChatLogs(
            SuspendableObservableCollection<ChatLogModel> buffer)
        {
            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.SystemMessage,
                OriginalSpeaker = "SYSTEM",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = $"ダミーログ {this.ParentPageSettings.Name}"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.Say,
                OriginalSpeaker = "Naoki Yoshida",
                SpeakerAlias = "Yoshi-P",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "本日は晴天なり。"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.Say,
                OriginalSpeaker = "Naoki Yoshida",
                SpeakerAlias = "Yoshi-P",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "明日も晴天かな？"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.Say,
                OriginalSpeaker = "Naoki Yoshida",
                SpeakerAlias = "Yoshi-P",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "あのイーハトーヴォのすきとおった風、夏でも底に冷たさをもつ青いそら、うつくしい森で飾られたモリーオ市、郊外のぎらぎらひかる草の波。"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.Party,
                OriginalSpeaker = "Naoki Yoshida",
                SpeakerAlias = "Yoshi-P",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "よろしくおねがいします～ ＞＜"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.Linkshell1,
                OriginalSpeaker = "Naoki Yoshida",
                SpeakerAlias = "Yoshi-P",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "リンクシェル1の皆さん、こんにちは。"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.Linkshell2,
                OriginalSpeaker = "Naoki Yoshida",
                SpeakerAlias = "Yoshi-P",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "リンクシェル2の皆さん、こんにちは。"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.Linkshell3,
                OriginalSpeaker = "Naoki Yoshida",
                SpeakerAlias = "Yoshi-P",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "リンクシェル3の皆さん、こんにちは。"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.Linkshell4,
                OriginalSpeaker = "Naoki Yoshida",
                SpeakerAlias = "Yoshi-P",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "リンクシェル4の皆さん、こんにちは。"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.Linkshell5,
                OriginalSpeaker = "Naoki Yoshida",
                SpeakerAlias = "Yoshi-P",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "リンクシェル5の皆さん、こんにちは。"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.Linkshell6,
                OriginalSpeaker = "Naoki Yoshida",
                SpeakerAlias = "Yoshi-P",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "リンクシェル6の皆さん、こんにちは。"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.Linkshell7,
                OriginalSpeaker = "Naoki Yoshida",
                SpeakerAlias = "Yoshi-P",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "リンクシェル7の皆さん、こんにちは。"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.Linkshell8,
                OriginalSpeaker = "Naoki Yoshida",
                SpeakerAlias = "Yoshi-P",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "リンクシェル8の皆さん、こんにちは。"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.CrossWorldLinkshell,
                OriginalSpeaker = "Naoki Yoshida",
                SpeakerAlias = "Yoshi-P",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "CWLSの皆さん、こんにちは。"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.FreeCompany,
                OriginalSpeaker = "Naoki Yoshida",
                SpeakerAlias = "Yoshi-P",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "フリーカンパニーの皆さん、こんにちは。"
            });

            buffer.Add(new ChatLogModel()
            {
                IsDummy = true,
                ParentOverlaySettings = this.ParentOverlaySettings,
                ParentPageSettings = this.ParentPageSettings,
                ChatCode = ChatCodes.NPCAnnounce,
                OriginalSpeaker = "ネール・デウス・ダーナス",
                SpeakerType = SpeakerTypes.XIVPlayer,
                Message = "チャリオッツいくおー ^ ^"
            });
        }
    }

    public class ChatLogAddedEventArgs : EventArgs
    {
        public ChatLogAddedEventArgs()
        {
        }

        public ChatLogAddedEventArgs(
            ChatPageSettingsModel parentPage,
            ChatLogModel addedLog)
        {
            this.ParentPage = parentPage;
            this.AddedLog = addedLog;
        }

        public ChatPageSettingsModel ParentPage { get; set; }

        public ChatLogModel AddedLog { get; set; }
    }
}
