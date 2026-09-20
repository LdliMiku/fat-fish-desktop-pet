using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace FatFishPet
{
    // 一条对话记录（用户或大肥鱼）。
    internal sealed class ChatTurn
    {
        public string Role = "user";
        public string Text = "";
        public DateTime Time = DateTime.Now;
    }

    // 对话配置：接口地址、模型名和人设保存为纯文本；API Key 单独用 DPAPI 加密保存。
    internal sealed class ChatConfig
    {
        public const string DefaultBaseUrl = "https://api.deepseek.com/v1";
        public const string DefaultModel = "deepseek-chat";
        public const string DefaultPersona =
            "你是住在用户桌面上的桌宠「大肥鱼」，一位蓝头发的小女仆。语气活泼、亲切、口语化，用「你」称呼对方。" +
            "回答保持简短：一般 1–3 句、合计不超过 80 个字，不用列表、标题、Markdown 或编号。" +
            "不知道的事就直说不知道，不要编造。可以自然地关心对方在忙什么、累不累，偶尔提到你就待在他的桌面上。";

        public string BaseUrl = DefaultBaseUrl;
        public string Model = DefaultModel;
        public string Persona = DefaultPersona;
        private string apiKey = "";

        public string ApiKey { get { return apiKey == null ? "" : apiKey; } set { apiKey = value == null ? "" : value.Trim(); } }
        public bool HasKey { get { return ApiKey.Length > 0; } }
        public bool IsDefaultBaseUrl { get { return string.Equals(BaseUrl, DefaultBaseUrl, StringComparison.OrdinalIgnoreCase); } }

        public static string ConfigPath(string baseDirectory) { return Path.Combine(baseDirectory, "data", "chat-config.txt"); }
        public static string KeyPath(string baseDirectory) { return Path.Combine(baseDirectory, "data", "chat-key.dat"); }

        public void Load(string baseDirectory)
        {
            try
            {
                string path = ConfigPath(baseDirectory);
                if (File.Exists(path))
                {
                    foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        int split = raw.IndexOf('=');
                        if (split <= 0) continue;
                        string key = raw.Substring(0, split).Trim();
                        string value = raw.Substring(split + 1).Trim();
                        if (key == "baseUrl" && value.Length > 0) BaseUrl = value;
                        else if (key == "model" && value.Length > 0) Model = value;
                        else if (key == "persona" && value.Length > 0) Persona = value.Replace("\\n", "\n");
                    }
                }
            }
            catch (Exception) { BaseUrl = DefaultBaseUrl; Model = DefaultModel; }
            LoadKey(baseDirectory);
        }

        public void Save(string baseDirectory)
        {
            Directory.CreateDirectory(Path.Combine(baseDirectory, "data"));
            File.WriteAllLines(ConfigPath(baseDirectory), new[]{
                "baseUrl=" + BaseUrl,
                "model=" + Model,
                "persona=" + Persona.Replace("\r", "").Replace("\n", "\\n")
            }, new UTF8Encoding(false));
            SaveKey(baseDirectory);
        }

        private void LoadKey(string baseDirectory)
        {
            try
            {
                string path = KeyPath(baseDirectory);
                if (!File.Exists(path)) { apiKey = ""; return; }
                byte[] encrypted = Convert.FromBase64String(File.ReadAllText(path, Encoding.UTF8).Trim());
                byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                apiKey = Encoding.UTF8.GetString(plain).Trim();
            }
            catch (Exception) { apiKey = ""; }
        }

        private void SaveKey(string baseDirectory)
        {
            string path = KeyPath(baseDirectory);
            try
            {
                if (apiKey.Length == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), null, DataProtectionScope.CurrentUser);
                File.WriteAllText(path, Convert.ToBase64String(encrypted), new UTF8Encoding(false));
            }
            catch (Exception) { /* 加密不可用时保留内存中的 Key，不写明文 */ }
        }
    }

    // 聊天记录：追加写入 data/chat-history.jsonl，一行一条，便于自己查看和备份。
    internal sealed class ChatStore
    {
        public const int ContextTurns = 8;
        private readonly List<ChatTurn> turns = new List<ChatTurn>();
        private string path = "";
        public IList<ChatTurn> Turns { get { return turns; } }

        public void Load(string baseDirectory, int maxTurns)
        {
            turns.Clear();
            path = Path.Combine(baseDirectory, "data", "chat-history.jsonl");
            try
            {
                if (!File.Exists(path)) return;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                int from = Math.Max(0, lines.Length - maxTurns);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                for (int i = from; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0) continue;
                    try
                    {
                        Dictionary<string, object> o = serializer.DeserializeObject(line) as Dictionary<string, object>;
                        if (o == null) continue;
                        ChatTurn turn = new ChatTurn();
                        turn.Role = o.ContainsKey("role") ? Convert.ToString(o["role"]) : "user";
                        turn.Text = o.ContainsKey("text") ? Convert.ToString(o["text"]) : "";
                        if (o.ContainsKey("time"))
                        {
                            DateTime parsed;
                            if (DateTime.TryParse(Convert.ToString(o["time"]), CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) turn.Time = parsed;
                        }
                        if (turn.Text.Length > 0) turns.Add(turn);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        public void Append(string role, string text)
        {
            ChatTurn turn = new ChatTurn();
            turn.Role = role; turn.Text = text; turn.Time = DateTime.Now;
            turns.Add(turn);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string line = serializer.Serialize(new Dictionary<string, object>{
                    {"time", turn.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)},
                    {"role", turn.Role},
                    {"text", turn.Text}
                });
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        public void Clear()
        {
            turns.Clear();
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }
    }

    internal sealed class ChatReply
    {
        public bool Ok;
        public bool Fallback;
        public string Text = "";
        public string Error = "";
    }

    // 负责拼请求、发请求和本地兜底台词；不持有界面状态，Send 在后台线程调用。
    internal sealed class ChatEngine
    {
        public const int TimeoutMilliseconds = 60000;
        public readonly ChatConfig Config = new ChatConfig();
        public readonly ChatStore History = new ChatStore();
        private string baseDirectory = ".";

        public void Initialize(string directory)
        {
            baseDirectory = directory;
            Config.Load(directory);
            History.Load(directory, 400);
        }

        public void SaveConfig() { Config.Save(baseDirectory); }
        public void Record(string role, string text) { History.Append(role, text); }
        public void ClearHistory() { History.Clear(); }

        // 取最近若干轮作为上下文，供后台线程只读使用。
        public List<ChatTurn> BuildContext()
        {
            List<ChatTurn> context = new List<ChatTurn>();
            int from = Math.Max(0, History.Turns.Count - ChatStore.ContextTurns * 2);
            for (int i = from; i < History.Turns.Count; i++) context.Add(History.Turns[i]);
            return context;
        }

        public string Endpoint()
        {
            string url = (Config.BaseUrl ?? "").Trim();
            if (url.Length == 0) url = ChatConfig.DefaultBaseUrl;
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return url;
            return url.TrimEnd('/') + "/chat/completions";
        }

        public string BuildRequestJson(List<ChatTurn> context, string userText)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            List<object> messages = new List<object>();
            messages.Add(new Dictionary<string, object> { { "role", "system" }, { "content", Config.Persona } });
            foreach (ChatTurn turn in context)
                messages.Add(new Dictionary<string, object> { { "role", turn.Role == "assistant" ? "assistant" : "user" }, { "content", turn.Text } });
            messages.Add(new Dictionary<string, object> { { "role", "user" }, { "content", userText } });
            Dictionary<string, object> body = new Dictionary<string, object>{
                {"model", Config.Model},
                {"messages", messages},
                {"stream", false},
                {"temperature", 0.8},
                {"max_tokens", 400}
            };
            return serializer.Serialize(body);
        }

        public ChatReply Send(List<ChatTurn> context, string userText)
        {
            ChatReply reply = new ChatReply();
            if (userText == null || userText.Trim().Length == 0) { reply.Error = "还没有输入内容。"; return reply; }
            if (!Config.HasKey)
            {
                reply.Fallback = true; reply.Ok = true; reply.Text = LocalReply(userText);
                reply.Error = "还没有设置 API Key，先用本地台词回你：右键 →「和我说话…」→「接口设置」里填 DeepSeek 的 Key。";
                return reply;
            }
            try
            {
                byte[] payload = Encoding.UTF8.GetBytes(BuildRequestJson(context, userText));
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Endpoint());
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Accept = "application/json";
                request.Headers["Authorization"] = "Bearer " + Config.ApiKey;
                request.Timeout = TimeoutMilliseconds;
                request.ReadWriteTimeout = TimeoutMilliseconds;
                request.UserAgent = "fat-fish-desktop-pet";
                request.ContentLength = payload.Length;
                request.ServicePoint.Expect100Continue = false;
                using (Stream stream = request.GetRequestStream()) stream.Write(payload, 0, payload.Length);
                using (WebResponse response = request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    return ParseReply(reader.ReadToEnd());
            }
            catch (WebException error)
            {
                reply.Fallback = true; reply.Ok = true; reply.Text = LocalReply(userText);
                reply.Error = DescribeWebError(error);
                return reply;
            }
            catch (Exception error)
            {
                reply.Fallback = true; reply.Ok = true; reply.Text = LocalReply(userText);
                reply.Error = "请求失败：" + error.Message;
                return reply;
            }
        }

        private static ChatReply ParseReply(string body)
        {
            ChatReply reply = new ChatReply();
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> root = serializer.DeserializeObject(body) as Dictionary<string, object>;
                if (root != null && root.ContainsKey("error"))
                {
                    Dictionary<string, object> error = root["error"] as Dictionary<string, object>;
                    reply.Error = "接口返回错误：" + (error != null && error.ContainsKey("message") ? Convert.ToString(error["message"]) : body);
                    return reply;
                }
                object[] choices = root != null && root.ContainsKey("choices") ? root["choices"] as object[] : null;
                if (choices == null || choices.Length == 0) { reply.Error = "接口没有返回内容。"; return reply; }
                Dictionary<string, object> first = choices[0] as Dictionary<string, object>;
                Dictionary<string, object> message = first != null && first.ContainsKey("message") ? first["message"] as Dictionary<string, object> : null;
                string content = message != null && message.ContainsKey("content") ? Convert.ToString(message["content"]) : "";
                content = content == null ? "" : content.Trim();
                if (content.Length == 0) { reply.Error = "接口返回了空回复。"; return reply; }
                reply.Ok = true; reply.Text = content; return reply;
            }
            catch (Exception error) { reply.Error = "解析回复失败：" + error.Message; return reply; }
        }

        private static string DescribeWebError(WebException error)
        {
            HttpWebResponse response = error.Response as HttpWebResponse;
            if (response == null)
                return error.Status == WebExceptionStatus.Timeout ? "请求超时，网络或接口没有及时回应。" : "网络请求失败：" + error.Message;
            string detail = "";
            try
            {
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) detail = reader.ReadToEnd();
            }
            catch (Exception) { }
            int code = (int)response.StatusCode;
            string hint = code == 401 ? "API Key 无效或已过期。" : code == 402 ? "账户余额不足。" : code == 429 ? "请求太频繁，稍后再试。" : "接口返回状态 " + code + "。";
            string message = "";
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> root = serializer.DeserializeObject(detail) as Dictionary<string, object>;
                Dictionary<string, object> err = root != null && root.ContainsKey("error") ? root["error"] as Dictionary<string, object> : null;
                if (err != null && err.ContainsKey("message")) message = Convert.ToString(err["message"]);
            }
            catch (Exception) { }
            return hint + (message.Length > 0 ? "（" + message + "）" : "");
        }

        // 断网或没有 Key 时使用的本地台词，保持短句和角色语气。
        public static string LocalReply(string userText)
        {
            string text = (userText ?? "").Trim();
            string[] hello = { "我在呢，今天也陪着你哦。", "嗨，我一直在桌面这儿等着你。", "你回来啦，我一直都在的。" };
            string[] pat = { "嘿嘿……再摸摸头也可以。", "呜，被摸到了，好舒服。", "唔……头要被你摸乱啦。" };
            string[] praise = { "真的吗？被你夸我好开心。", "嘿嘿，我会继续努力的。", "谢谢～你也很厉害呀。" };
            string[] tired = { "辛苦啦，先喝口水歇一会儿吧。", "累的话就休息一下，我在这儿看着桌面。", "别硬撑呀，等一下我陪你继续。" };
            string[] ask = { "这个我还不太懂，等接上网络我再好好回你。", "唔……我脑子有点空，你先教教我吧。", "这个问题我先记下啦，下次再答你。" };
            string[] bye = { "好，我在这儿等你回来。", "去吧去吧，回来记得看看我。", "嗯，那我先自己待着啦。" };
            if (Contains(text, "你好") || Contains(text, "嗨") || Contains(text, "在吗") || Contains(text, "hi")) return Pick(hello);
            if (Contains(text, "摸") || Contains(text, "头")) return Pick(pat);
            if (Contains(text, "可爱") || Contains(text, "好看") || Contains(text, "喜欢") || Contains(text, "厉害")) return Pick(praise);
            if (Contains(text, "累") || Contains(text, "困") || Contains(text, "加班") || Contains(text, "难受") || Contains(text, "烦")) return Pick(tired);
            if (Contains(text, "再见") || Contains(text, "拜拜") || Contains(text, "走了")) return Pick(bye);
            return Pick(ask);
        }

        private static bool Contains(string text, string word) { return text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0; }

        private static readonly Random localRandom = new Random();
        private static string Pick(string[] options) { return options[localRandom.Next(options.Length)]; }
    }
}
