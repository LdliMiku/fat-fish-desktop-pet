// Headless checks for the chat engine: settings round trip, encrypted key
// storage, local history file, offline fallback lines, and the HTTP request /
// response path against a local mock endpoint (no real API key needed).
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using FatFishPet;

internal static class ChatChecks
{
    private static int failures;

    private static void Check(bool ok,string name)
    {
        Console.WriteLine((ok?"PASS: ":"FAIL: ")+name);
        if(!ok)failures++;
    }

    private static int Main(string[] args)
    {
        string dir=args.Length>0?args[0]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"chat-check-runtime");
        if(Directory.Exists(dir))Directory.Delete(dir,true);
        Directory.CreateDirectory(dir);
        ConfigChecks(dir);
        StoreChecks(dir);
        FallbackChecks(dir);
        HttpChecks(dir);
        Console.WriteLine(failures==0?"PASS: chat engine checks":("FAIL: chat engine checks ("+failures+")"));
        return failures==0?0:1;
    }

    private static void ConfigChecks(string dir)
    {
        ChatConfig config=new ChatConfig();
        config.BaseUrl="https://api.deepseek.com/v1";
        config.Model="deepseek-chat";
        config.ApiKey="sk-test-1234567890";
        config.Save(dir);
        Check(File.Exists(ChatConfig.ConfigPath(dir)),"config file written");
        Check(File.Exists(ChatConfig.KeyPath(dir)),"encrypted key file written");
        string text=File.ReadAllText(ChatConfig.ConfigPath(dir),Encoding.UTF8);
        Check(text.Contains("baseUrl=https://api.deepseek.com/v1"),"base url persisted");
        Check(text.Contains("model=deepseek-chat"),"model persisted");
        Check(!text.Contains("sk-test-1234567890"),"key not stored in plain config");
        string stored=File.ReadAllText(ChatConfig.KeyPath(dir),Encoding.UTF8);
        Check(!stored.Contains("sk-test-1234567890"),"key file does not contain plaintext key");
        ChatConfig loaded=new ChatConfig();
        loaded.Load(dir);
        Check(loaded.ApiKey=="sk-test-1234567890","encrypted key round trip");
        Check(loaded.BaseUrl=="https://api.deepseek.com/v1"&&loaded.Model=="deepseek-chat","settings round trip");
        Check(loaded.ApiKey.StartsWith("sk-test"),"loaded key keeps value");
        ChatConfig cleared=new ChatConfig();
        cleared.Load(dir);
        cleared.ApiKey="";
        cleared.Save(dir);
        Check(!File.Exists(ChatConfig.KeyPath(dir)),"clearing the key deletes the file");
        ChatConfig defaults=new ChatConfig();
        Check(defaults.BaseUrl==ChatConfig.DefaultBaseUrl&&defaults.Model==ChatConfig.DefaultModel,"defaults are DeepSeek");
        ChatEngine engine=new ChatEngine();
        engine.Initialize(dir);
        Check(engine.Endpoint()=="https://api.deepseek.com/v1/chat/completions","endpoint composed from base url");
        engine.Config.BaseUrl="https://api.deepseek.com/v1/chat/completions";
        Check(engine.Endpoint()=="https://api.deepseek.com/v1/chat/completions","endpoint not doubled");
    }

    private static void StoreChecks(string dir)
    {
        ChatStore store=new ChatStore();
        store.Load(dir,400);
        Check(store.Turns.Count==0,"history starts empty");
        store.Append("user","你好呀");
        store.Append("assistant","我在的，摸摸头。");
        string path=Path.Combine(dir,"data","chat-history.jsonl");
        Check(File.Exists(path),"history file written");
        Check(File.ReadAllLines(path,Encoding.UTF8).Length==2,"history keeps one line per message");
        ChatStore reloaded=new ChatStore();
        reloaded.Load(dir,400);
        Check(reloaded.Turns.Count==2,"history reloaded");
        Check(reloaded.Turns[0].Role=="user"&&reloaded.Turns[0].Text=="你好呀","first turn preserved");
        Check(reloaded.Turns[1].Role=="assistant"&&reloaded.Turns[1].Text=="我在的，摸摸头。","second turn preserved");
        for(int i=0;i<40;i++)reloaded.Append("user","第"+i+"句");
        ChatStore trimmed=new ChatStore();
        trimmed.Load(dir,8);
        Check(trimmed.Turns.Count==8,"history load honours the cap");
        Check(trimmed.Turns[7].Text=="第39句","newest messages kept");
        trimmed.Clear();
        Check(!File.Exists(path),"clear removes the history file");
    }

    private static void FallbackChecks(string dir)
    {
        string hello=ChatEngine.LocalReply("你好");
        string tired=ChatEngine.LocalReply("今天好累啊");
        string unknown=ChatEngine.LocalReply("量子力学和烤面包有什么关系");
        Check(hello.Length>0&&tired.Length>0&&unknown.Length>0,"local lines are never empty");
        Check(hello!=tired&&hello!=unknown,"local lines react to keywords");
        Check(ChatEngine.LocalReply("你好").Length<=40&&tired.Length<=40,"local lines stay short");
        ChatEngine engine=new ChatEngine();
        engine.Initialize(dir);
        engine.Config.ApiKey="";
        ChatReply reply=engine.Send(new List<ChatTurn>(),"你好");
        Check(reply.Ok&&reply.Fallback&&reply.Text.Length>0,"missing key falls back to local line");
        Check(reply.Error.Contains("API Key"),"missing key explains itself");
        Check(engine.Send(new List<ChatTurn>(),"   ").Error.Length>0,"blank input rejected");
    }

    private static void HttpChecks(string dir)
    {
        ChatEngine engine=new ChatEngine();
        engine.Initialize(dir);

        string okBody="{\"id\":\"x\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"我在的，摸摸头。\"}}]}";
        MockServer ok=new MockServer(200,"OK",okBody);
        ok.Start();
        engine.Config.BaseUrl="http://127.0.0.1:"+ok.Port+"/v1";
        engine.Config.ApiKey="sk-mock";
        List<ChatTurn> context=new List<ChatTurn>();
        ChatTurn previous=new ChatTurn();previous.Role="user";previous.Text="上次说的那个";context.Add(previous);
        ChatTurn answer=new ChatTurn();answer.Role="assistant";answer.Text="我记得的。";context.Add(answer);
        ChatReply reply=engine.Send(context,"你好呀");
        ok.Stop();
        Check(reply.Ok&&!reply.Fallback,"http reply parsed");
        Check(reply.Text=="我在的，摸摸头。","http reply text");
        Check(ok.LastRequest.StartsWith("POST /v1/chat/completions"),"posts to chat completions path");
        Check(ok.LastRequest.Contains("Authorization: Bearer sk-mock"),"sends bearer token");
        Check(ok.LastBody.Contains("\"model\":\"deepseek-chat\""),"sends configured model");
        Check(ok.LastBody.Contains("上次说的那个")&&ok.LastBody.Contains("我记得的。"),"sends recent context");
        Check(ok.LastBody.Contains("你好呀"),"sends the new message");
        Check(ok.LastBody.Contains("大肥鱼")&&ok.LastBody.Contains("system"),"sends persona as system message");
        Check(ok.LastBody.Contains("\"stream\":false"),"non streaming request");

        string errorBody="{\"error\":{\"message\":\"Authentication Fails, Your api key is invalid\"}}";
        MockServer denied=new MockServer(401,"Unauthorized",errorBody);
        denied.Start();
        engine.Config.BaseUrl="http://127.0.0.1:"+denied.Port+"/v1";
        ChatReply rejected=engine.Send(new List<ChatTurn>(),"你好");
        denied.Stop();
        Check(rejected.Ok&&rejected.Fallback,"invalid key falls back to local line");
        Check(rejected.Error.Contains("API Key")&&rejected.Error.Contains("invalid"),"invalid key error is explained");

        engine.Config.BaseUrl="http://127.0.0.1:1/v1";
        ChatReply unreachable=engine.Send(new List<ChatTurn>(),"你好");
        Check(unreachable.Ok&&unreachable.Fallback&&unreachable.Error.Length>0,"unreachable endpoint falls back with a reason");
    }
}

// Minimal HTTP/1.1 server so the request path can be checked without a key.
internal sealed class MockServer
{
    private readonly TcpListener listener;
    private readonly string body;
    private readonly int status;
    private readonly string statusText;
    public string LastRequest="";
    public string LastBody="";
    public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }

    public MockServer(int status,string statusText,string body)
    {
        this.status=status;this.statusText=statusText;this.body=body;
        listener=new TcpListener(IPAddress.Loopback,0);
    }

    public void Start()
    {
        listener.Start();
        ThreadPool.QueueUserWorkItem(delegate { while(true){ TcpClient client; try{client=listener.AcceptTcpClient();}catch(Exception){return;} try{Handle(client);}catch(Exception){} finally{try{client.Close();}catch(Exception){}} } });
    }

    public void Stop() { try { listener.Stop(); } catch (Exception) { } }

    private static int FindHeaderEnd(byte[] data,int length)
    {
        for(int i=0;i+3<length;i++) if(data[i]==13&&data[i+1]==10&&data[i+2]==13&&data[i+3]==10) return i;
        return -1;
    }

    private void Handle(TcpClient client)
    {
        NetworkStream stream=client.GetStream();
        stream.ReadTimeout=5000;
        byte[] buffer=new byte[8192];
        MemoryStream received=new MemoryStream();
        int headerEnd=-1;
        while(headerEnd<0)
        {
            int read=stream.Read(buffer,0,buffer.Length);
            if(read<=0)return;
            received.Write(buffer,0,read);
            headerEnd=FindHeaderEnd(received.ToArray(),(int)received.Length);
        }
        byte[] all=received.ToArray();
        string head=Encoding.UTF8.GetString(all,0,headerEnd);
        LastRequest=head;
        int contentLength=0;
        foreach(string line in head.Split('\n'))
        {
            string trimmed=line.Trim();
            if(trimmed.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))int.TryParse(trimmed.Substring(15).Trim(),out contentLength);
        }
        if(head.IndexOf("Expect: 100-continue",StringComparison.OrdinalIgnoreCase)>=0)
        {
            byte[] cont=Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
            stream.Write(cont,0,cont.Length);stream.Flush();
        }
        byte[] payload=new byte[contentLength];
        int have=Math.Min(Math.Max(0,all.Length-(headerEnd+4)),contentLength);
        Array.Copy(all,headerEnd+4,payload,0,have);
        while(have<contentLength)
        {
            int read=stream.Read(payload,have,contentLength-have);
            if(read<=0)break;
            have+=read;
        }
        LastBody=Encoding.UTF8.GetString(payload,0,have);
        byte[] responseBody=Encoding.UTF8.GetBytes(body);
        string header="HTTP/1.1 "+status+" "+statusText+"\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: "+responseBody.Length+"\r\nConnection: close\r\n\r\n";
        byte[] headerBytes=Encoding.ASCII.GetBytes(header);
        stream.Write(headerBytes,0,headerBytes.Length);
        stream.Write(responseBody,0,responseBody.Length);
        stream.Flush();
    }
}
