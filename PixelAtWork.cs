using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Automation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Forms = System.Windows.Forms;

public class OverlayConfig {
    public string codexHome {get;set;}
    public string threadId {get;set;}
    public double centerX {get;set;}
    public double bottomDip {get;set;}
    public double widthDip {get;set;}
    public bool manualAnchor {get;set;}
    public double offsetXDip {get;set;}
    public double offsetYDip {get;set;}
    public bool reducedMotion {get;set;}
}

public class Activity {
    public string layout = "scene";
    public string kind = "idle";
    public string label = "等你发话";
    public List<Dictionary<string,object>> crates = new List<Dictionary<string,object>>();
    public string[] helpers = new string[0];
}

// Only activity categories leave this reader. Prompts, paths, command arguments and
// tool outputs are never written to the webview, the status file or the diagnostic log.
public class SessionReader {
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength=16*1024*1024 };
    readonly Dictionary<string,string> pending = new Dictionary<string,string>();
    readonly Activity state = new Activity();
    string file;
    long offset;
    byte[] partial = new byte[0];
    bool active;
    DateTime linger = DateTime.MinValue;
    readonly Dictionary<string,string> labels = new Dictionary<string,string> {
        {"think","正在处理"},{"code","正在编辑"},{"read","正在阅读"},{"search","正在查找"},
        {"web","正在查资料"},{"build","正在构建"},{"test","正在跑测试"},{"install","正在装依赖"},
        {"git","正在处理 Git"},{"pull","正在下载"},{"python","正在运行 Python"},{"shell","正在运行工具"},
        {"paint","正在绘图"},{"ask","等你回答"},{"plan","正在整理计划"},{"compact","正在压缩上下文"},
        {"agent","小助手正在工作"},{"party","已完成"},{"bug","这一步出错了"},{"fire","构建出错了"}
    };
    public SessionReader(string home, string thread) {
        if(String.IsNullOrWhiteSpace(thread)) { state.label="未找到 Codex 会话 · 请先开始一个任务"; return; }
        try {
            var folder = Path.Combine(home,"sessions");
            file = Directory.EnumerateFiles(folder,"*"+thread+"*.jsonl",SearchOption.AllDirectories).FirstOrDefault();
        } catch { file=null; }
        if (file==null) { state.label="未找到这条 Codex 会话"; return; }
        // Replay recent complete records once, then read new bytes without holding the file open.
        using (var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
            if (stream.Length>8*1024*1024) {
                stream.Position=stream.Length-8*1024*1024;
                int b; do { b=stream.ReadByte(); } while(b>=0 && b!=10);
            }
            offset=stream.Position;
        }
        Poll();
    }
    static string Get(Dictionary<string,object> d,string key) {
        object v; return d.TryGetValue(key,out v) && v!=null ? Convert.ToString(v) : "";
    }
    void Set(string kind) {
        state.kind=kind;
        state.label="Codex · "+(labels.ContainsKey(kind)?labels[kind]:"正在工作");
    }
    string Classify(string name,string input) {
        string s=(name+" "+input).ToLowerInvariant();
        // Match called tool names; classify shell commands only when an execution tool is present.
        if(s.Contains("apply_patch") || s.Contains("write_file") || s.Contains("edit_file")) return "code";
        if(s.Contains("imagegen") || s.Contains("image_gen") || s.Contains("generate_image")) return "paint";
        if(s.Contains("request_user_input")) return "ask";
        if(s.Contains("spawn_agent") || s.Contains("followup_task")) return "agent";
        if(s.Contains("update_plan") || s.Contains("create_goal")) return "plan";
        if(s.Contains("web__run") || s.Contains("search_query") || s.Contains("github_fetch") || s.Contains("github_search")) return "web";
        if(s.Contains("view_image") || s.Contains("read_mcp_resource")) return "read";
        if(s.Contains("exec_command")) {
            if(Regex.IsMatch(s,@"\b(pytest|vitest|jest|ctest)\b|\b(npm|pnpm|dotnet|cargo) (run )?test\b")) return "test";
            if(Regex.IsMatch(s,@"\b(csc\.exe|cmake|msbuild|tsc)\b|\b(npm|dotnet|cargo) (run )?build\b")) return "build";
            if(Regex.IsMatch(s,@"\b(npm|pip|winget) (install|add)\b")) return "install";
            if(Regex.IsMatch(s,@"\b(invoke-webrequest|curl|wget|git clone)\b")) return "pull";
            if(Regex.IsMatch(s,@"\bgit (status|diff|commit|push|fetch|pull|log)\b")) return "git";
            if(Regex.IsMatch(s,@"\brg\b")) return "search";
            if(Regex.IsMatch(s,@"\bget-content\b")) return "read";
            if(Regex.IsMatch(s,@"\bpython(\.exe)?\b")) return "python";
        }
        return "shell";
    }
    void Record(string line) {
        try {
            var o=json.Deserialize<Dictionary<string,object>>(line);
            object payload; if(!o.TryGetValue("payload",out payload)) {
                if(Get(o,"type")=="compacted") { Set("compact"); linger=DateTime.UtcNow.AddSeconds(2); }
                return;
            }
            var p=payload as Dictionary<string,object>; if(p==null) return;
            string type=Get(p,"type");
            if(Get(o,"type")=="event_msg") {
                if(type=="task_started") { active=true; pending.Clear(); state.crates.Clear(); linger=DateTime.MinValue; Set("think"); }
                if(type=="task_complete" || type=="turn_aborted" || type=="task_cancelled") {
                    active=false; pending.Clear(); Set(DateTime.Now.Hour>=22 || DateTime.Now.Hour<7?"sleep":"idle"); state.label="Codex · 等你发话";
                }
            } else if(Get(o,"type")=="response_item") {
                if(type=="function_call" || type=="custom_tool_call") {
                    string kind=Classify(Get(p,"name"),Get(p,"arguments")+" "+Get(p,"input"));
                    pending[Get(p,"call_id")]=kind; active=true; Set(kind);
                }
                if(type=="function_call_output" || type=="custom_tool_call_output") {
                    string id=Get(p,"call_id"),kind;
                    if(pending.TryGetValue(id,out kind)) {
                        pending.Remove(id);
                        string output=Get(p,"output");
                        bool failed=Regex.IsMatch(output,@"""exit_code""\s*:\s*[1-9]|""isError""\s*:\s*true|Process exited with code [1-9]|(?m)^Exit code:?\s*[1-9]");
                        state.crates.Add(new Dictionary<string,object>{{"kind",kind},{"ok",!failed}});
                        if(state.crates.Count>35) state.crates.RemoveAt(0);
                        Set(failed?(kind=="build"?"fire":"bug"):(kind=="test"?"party":kind));
                        linger=DateTime.UtcNow.AddSeconds(failed?4:1.8);
                    }
                }
                if(type=="message" && Get(p,"role")=="assistant" && Get(p,"phase")=="final") {
                    active=false; pending.Clear(); Set("idle"); state.label="Codex · 等你发话";
                }
            }
        } catch { /* Unknown and incomplete records are ignored; no raw content is logged. */ }
    }
    public Activity Poll() {
        if(file==null) return state;
        try {
            using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                if(stream.Length<offset) { offset=0; partial=new byte[0]; pending.Clear(); }
                stream.Position=offset;
                using(var bytes=new MemoryStream()) {
                    bytes.Write(partial,0,partial.Length);
                    byte[] block=new byte[65536]; int n;
                    while((n=stream.Read(block,0,block.Length))>0) bytes.Write(block,0,n);
                    offset=stream.Position;
                    var all=bytes.ToArray(); int start=0;
                    for(int i=0;i<all.Length;i++) if(all[i]==10) { Record(Encoding.UTF8.GetString(all,start,i-start)); start=i+1; }
                    partial=new byte[all.Length-start]; Array.Copy(all,start,partial,0,partial.Length);
                    if(partial.Length>16*1024*1024) partial=new byte[0];
                }
            }
            if(active && DateTime.UtcNow>linger) {
                if(pending.Count>0) Set(pending.Values.Last()); else Set("think");
            }
            state.helpers=pending.Values.Skip(1).Take(4).ToArray();
        } catch(IOException) { state.label="会话暂不可读 · 正在重连"; }
        catch(UnauthorizedAccessException) { state.label="会话读取受限"; }
        return state;
    }
}

public static class Native {
    public delegate bool EnumProc(IntPtr h,IntPtr data);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h,uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowLongPtr(IntPtr h,int index,IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback,IntPtr data);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
}

public class OverlayWindow : Window {
    readonly string folder=AppDomain.CurrentDomain.BaseDirectory;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    readonly WebView2CompositionControl view=new WebView2CompositionControl();
    readonly DispatcherTimer timer=new DispatcherTimer();
    OverlayConfig config;
    SessionReader reader;
    Forms.NotifyIcon tray;
    IntPtr handle,target;
    bool ready,paused,updating,disposed;
    string sent="", diagnostic="";
    DateTime findAfter=DateTime.MinValue;
    int targetPid;
    int lastX=-1,lastY=-1,lastW=-1,lastH=-1;
    LayoutSnapshot layout=new LayoutSnapshot();
    AutomationElement composerElement;
    Rect editBounds=Rect.Empty;
    DateTime inputAfter=DateTime.MinValue;
    bool readingInput;
    public OverlayWindow() {
        string configFile=Path.Combine(folder,"config.json");
        config=File.Exists(configFile)
            ? json.Deserialize<OverlayConfig>(File.ReadAllText(configFile))
            : new OverlayConfig { centerX=0.52,bottomDip=132,widthDip=380 };
        if(String.IsNullOrWhiteSpace(config.codexHome)) {
            config.codexHome=Environment.GetEnvironmentVariable("CODEX_HOME");
            if(String.IsNullOrWhiteSpace(config.codexHome)) config.codexHome=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
        }
        if(String.IsNullOrWhiteSpace(config.threadId)) {
            config.threadId=Environment.GetEnvironmentVariable("CODEX_THREAD_ID");
            if(String.IsNullOrWhiteSpace(config.threadId)) {
                try {
                    string latest=Directory.EnumerateFiles(Path.Combine(config.codexHome,"sessions"),"*.jsonl",SearchOption.AllDirectories)
                        .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                    if(latest!=null) config.threadId=Regex.Match(Path.GetFileName(latest),@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}").Value;
                } catch { config.threadId=""; }
            }
        }
        Save();
        reader=new SessionReader(config.codexHome,config.threadId);
        Width=380; Height=82; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true; Background=Brushes.Transparent; Topmost=true;
        ShowInTaskbar=false; ShowActivated=false; Focusable=false; IsHitTestVisible=false;
        view.DefaultBackgroundColor=System.Drawing.Color.Transparent;
        view.Focusable=false; view.IsHitTestVisible=false; view.AllowExternalDrop=false;
        Content=view;
        SourceInitialized+=delegate {
            handle=new WindowInteropHelper(this).Handle;
            long style=Native.GetWindowLongPtr(handle,-20).ToInt64();
            Native.SetWindowLongPtr(handle,-20,new IntPtr(style|0x20|0x80|0x8000000));
            HwndSource.FromHwnd(handle).AddHook(WndProc);
            // Ctrl+Alt+P aligns the bottom of the strip with the pointer over the input top edge.
            Native.RegisterHotKey(handle,1,0x4003,0x50);
            Native.RegisterHotKey(handle,2,0x4003,0x4F);
            for(int i=0;i<4;i++) Native.RegisterHotKey(handle,3+i,0x4003,(uint)(0x25+i));
        };
        Loaded+=async delegate { await Initialize(); };
        Closed+=delegate { Cleanup(); };
        CreateTray();
        timer.Interval=TimeSpan.FromMilliseconds(300);
        timer.Tick+=async delegate { await Tick(); };
        timer.Start();
    }
    void Log(string message) { File.AppendAllText(Path.Combine(folder,"diagnostic.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine); }
    async Task Initialize() {
        try {
            var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(folder,"browser-data"),new CoreWebView2EnvironmentOptions("--disable-background-networking"));
            await view.EnsureCoreWebView2Async(environment);
            var core=view.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled=false;
            core.Settings.AreDevToolsEnabled=false;
            core.Settings.IsStatusBarEnabled=false;
            core.Settings.AreBrowserAcceleratorKeysEnabled=false;
            core.SetVirtualHostNameToFolderMapping("pixel.local",folder,CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting+=delegate(object s,CoreWebView2NavigationStartingEventArgs e) {
                if(!e.Uri.StartsWith("https://pixel.local/",StringComparison.OrdinalIgnoreCase)) e.Cancel=true;
            };
            core.NewWindowRequested+=delegate(object s,CoreWebView2NewWindowRequestedEventArgs e) { e.Handled=true; };
            core.PermissionRequested+=delegate(object s,CoreWebView2PermissionRequestedEventArgs e) { e.State=CoreWebView2PermissionState.Deny; };
            core.NavigationCompleted+=delegate(object s,CoreWebView2NavigationCompletedEventArgs e) {
                ready=e.IsSuccess; Log("renderer "+(ready?"ready":"navigation failed"));
            };
            core.Navigate("https://pixel.local/overlay.html");
        } catch(Exception e) {
            Log("renderer failure "+e.GetType().Name+": "+e.Message);
            Forms.MessageBox.Show("悬浮条启动失败。请查看同目录的 diagnostic.log。"+Environment.NewLine+e.Message,"Pixel at Work");
            Close();
        }
    }
    void CreateTray() {
        tray=new Forms.NotifyIcon();
        tray.Icon=System.Drawing.SystemIcons.Application;
        tray.Text="Pixel at Work · Codex";
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏（Ctrl+Alt+O）",null,delegate { paused=!paused; });
        menu.Items.Add("定位：鼠标放输入框顶边中间，按 Ctrl+Alt+P",null,delegate { tray.ShowBalloonTip(7000,"定位悬浮条","将鼠标移到输入框顶边中间，按 Ctrl+Alt+P。Ctrl+Alt+方向键微调。",Forms.ToolTipIcon.Info); });
        menu.Items.Add("重新读取这条会话",null,delegate { reader=new SessionReader(config.codexHome,config.threadId); sent=""; });
        menu.Items.Add("自动对齐输入框",null,delegate { config.manualAnchor=false; config.offsetXDip=0; config.offsetYDip=0; inputAfter=DateTime.MinValue; Save(); });
        menu.Items.Add("绑定最近活动的 Codex 会话",null,delegate {
            try {
                var latest=Directory.EnumerateFiles(Path.Combine(config.codexHome,"sessions"),"*.jsonl",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if(latest!=null) {
                    var match=Regex.Match(Path.GetFileName(latest),@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}");
                    if(match.Success) { config.threadId=match.Value; reader=new SessionReader(config.codexHome,config.threadId); sent=""; Save(); }
                }
            } catch { tray.ShowBalloonTip(5000,"未能切换会话","请先在 Codex 中发送消息，再重试。",Forms.ToolTipIcon.Info); }
        });
        var reduce=new Forms.ToolStripMenuItem("减少动画");
        reduce.CheckOnClick=true; reduce.Checked=config.reducedMotion;
        reduce.CheckedChanged+=async delegate {
            config.reducedMotion=reduce.Checked; Save();
            if(ready) await view.CoreWebView2.ExecuteScriptAsync("window.setReducedMotion("+json.Serialize(config.reducedMotion)+")");
        };
        menu.Items.Add(reduce);
        menu.Items.Add("查看使用说明",null,delegate { Process.Start(Path.Combine(folder,"README.md")); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出悬浮条",null,delegate { Close(); });
        tray.ContextMenuStrip=menu; tray.Visible=true;
        tray.DoubleClick+=delegate { paused=!paused; };
    }
    void Save() { File.WriteAllText(Path.Combine(folder,"config.json"),json.Serialize(config),new UTF8Encoding(false)); }
    IntPtr WndProc(IntPtr h,int message,IntPtr wp,IntPtr lp,ref bool handled) {
        if(message==0x84) { handled=true; return new IntPtr(-1); }
        if(message==0x21) { handled=true; return new IntPtr(3); }
        if(message==0x312) {
            int id=wp.ToInt32();
            if(id==2) paused=!paused;
            Native.Rect r;
            if(target!=IntPtr.Zero && Native.GetWindowRect(target,out r)) {
                double scale=Math.Max(1,Native.GetDpiForWindow(target)/96.0);
                if(id==1) {
                    Native.Point p; Native.GetCursorPos(out p);
                    config.centerX=Math.Max(0.05,Math.Min(0.95,(double)(p.X-r.Left)/(r.Right-r.Left)));
                    config.bottomDip=Math.Max(20,(r.Bottom-p.Y)/scale+6);
                    config.manualAnchor=true;
                    paused=false; Save();
                }
                if(id>=3 && id<=6) {
                    if(config.manualAnchor) {
                        if(id==3) config.centerX-=12*scale/(r.Right-r.Left);
                        if(id==5) config.centerX+=12*scale/(r.Right-r.Left);
                        if(id==4) config.bottomDip+=12;
                        if(id==6) config.bottomDip-=12;
                    } else {
                        if(id==3) config.offsetXDip-=12;
                        if(id==5) config.offsetXDip+=12;
                        if(id==4) config.offsetYDip-=12;
                        if(id==6) config.offsetYDip+=12;
                    }
                    Save();
                }
            }
            handled=true;
        }
        return IntPtr.Zero;
    }
    void FindTarget() {
        if(target!=IntPtr.Zero && Native.IsWindow(target)) return;
        if(DateTime.UtcNow<findAfter) return;
        findAfter=DateTime.UtcNow.AddSeconds(2);
        var processes=Process.GetProcessesByName("ChatGPT");
        var ids=new HashSet<int>(processes.Select(p=>p.Id));
        long largest=0; IntPtr found=IntPtr.Zero; int foundPid=0;
        Native.EnumProc callback=delegate(IntPtr h,IntPtr unused) {
            uint pid; Native.GetWindowThreadProcessId(h,out pid);
            // The native pet is another ChatGPT window. Exclude tool windows so
            // the strip follows the actual conversation rather than the pet.
            if(ids.Contains((int)pid) && Native.IsWindowVisible(h) && (Native.GetWindowLongPtr(h,-20).ToInt64()&0x80)==0) {
                Native.Rect r; if(Native.GetWindowRect(h,out r)) {
                    long area=(long)(r.Right-r.Left)*(r.Bottom-r.Top);
                    if(area>largest) { largest=area; found=h; foundPid=(int)pid; }
                }
            }
            return true;
        };
        Native.EnumWindows(callback,IntPtr.Zero);
        foreach(var p in processes) p.Dispose();
        if(found!=IntPtr.Zero) {
            target=found; targetPid=foundPid;
            Native.SetWindowLongPtr(handle,-8,target);
            layout=new LayoutSnapshot(); composerElement=null; inputAfter=DateTime.MinValue;
            Log("attached conversation window pid="+targetPid);
        }
    }
    async Task ReadInputBounds() {
        if(readingInput || DateTime.UtcNow<inputAfter || target==IntPtr.Zero) return;
        // Invalidate a moved/replaced composer before the background scan finishes.
        try {
            if(composerElement!=null && (composerElement.Current.IsOffscreen || composerElement.Current.BoundingRectangle!=editBounds)) {
                layout=new LayoutSnapshot(); composerElement=null; if(IsVisible) Hide();
            }
        } catch { layout=new LayoutSnapshot(); composerElement=null; if(IsVisible) Hide(); }
        readingInput=true; inputAfter=DateTime.UtcNow.AddMilliseconds(300);
        var window=target;
        try {
            AutomationElement selected=null; Rect selectedEdit=Rect.Empty;
            var snapshot=await Task.Run(delegate {
                var next=new LayoutSnapshot();
                try {
                    var root=AutomationElement.FromHandle(window);
                    var condition=new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit);
                    var edits=root.FindAll(TreeScope.Descendants,condition);
                    foreach(AutomationElement e in edits) {
                        var current=e.Current;
                        if(current.IsOffscreen || !(current.ClassName??"").Split(' ').Contains("ProseMirror")) continue;
                        var r=current.BoundingRectangle;
                        if(r.IsEmpty || r.Width<100 || r.Height<10) continue;
                        var parent=TreeWalker.ControlViewWalker.GetParent(e);
                        if(parent!=null) {
                            var p=parent.Current.BoundingRectangle;
                            if(!p.IsEmpty && p.Width>=r.Width && p.Width<r.Width+200 && p.Height<700 && p.Top<=r.Top) r=p;
                        }
                        if(next.composer.IsEmpty || r.Top>next.composer.Top) { next.composer=r; selected=e; selectedEdit=current.BoundingRectangle; }
                    }
                    if(selected==null) return next;
                    var ancestor=TreeWalker.ControlViewWalker.GetParent(selected);
                    for(int i=0;i<12 && ancestor!=null;i++,ancestor=TreeWalker.ControlViewWalker.GetParent(ancestor)) {
                        var bounds=ancestor.Current.BoundingRectangle;
                        if(!bounds.IsEmpty && bounds.Contains(next.composer) && bounds.Width>=next.composer.Width+80 && bounds.Height>=next.composer.Height+40) {
                            next.viewport=bounds; break;
                        }
                    }
                    if(next.viewport.IsEmpty) return new LayoutSnapshot();
                    var types=new [] { ControlType.Text,ControlType.Button,ControlType.Hyperlink,ControlType.Image,ControlType.Edit,ControlType.ComboBox,ControlType.CheckBox };
                    var obstacles=root.FindAll(TreeScope.Descendants,new OrCondition(types.Select(t=>(System.Windows.Automation.Condition)new PropertyCondition(AutomationElement.ControlTypeProperty,t)).ToArray()));
                    foreach(AutomationElement e in obstacles) {
                        var current=e.Current; var bounds=current.BoundingRectangle;
                        if(!current.IsOffscreen && !bounds.IsEmpty && bounds.Width>0 && bounds.Height>0 && bounds.IntersectsWith(next.viewport)) next.obstacles.Add(bounds);
                    }
                    return next;
                } catch { selected=null; return new LayoutSnapshot(); }
            });
            if(window==target) { layout=snapshot; composerElement=selected; editBounds=selectedEdit; }
        } finally { readingInput=false; }
    }
    async Task Tick() {
        if(disposed || updating) return;
        updating=true;
        try {
            FindTarget();
            await ReadInputBounds();
            Native.Rect r;
            uint foregroundPid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out foregroundPid);
            bool visible=ready && !paused && target!=IntPtr.Zero && !Native.IsIconic(target) && foregroundPid==targetPid && Native.GetAncestor(Native.GetForegroundWindow(),2)==target && Native.GetWindowRect(target,out r);
            var placement=new Placement();
            if(!visible) placement.reason=paused?"user-hidden":!ready?"renderer-starting":"host-inactive";
            if(visible && Native.GetWindowRect(target,out r)) {
                double scale=Math.Max(1,Native.GetDpiForWindow(target)/96.0);
                placement=PlacementEngine.Choose(layout,new Rect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top),scale,config);
                visible=placement.Visible;
            }
            if(visible) {
                int x=(int)Math.Round(placement.bounds.Left),y=(int)Math.Round(placement.bounds.Top);
                int width=(int)Math.Round(placement.bounds.Width),height=(int)Math.Round(placement.bounds.Height);
                if(!IsVisible) Show();
                if(x!=lastX || y!=lastY || width!=lastW || height!=lastH) {
                    Native.SetWindowPos(handle,new IntPtr(-1),x,y,width,height,0x10|0x40);
                    lastX=x; lastY=y; lastW=width; lastH=height;
                }
            } else if(IsVisible) Hide();
            var state=reader.Poll();
            state.layout=placement.mode;
            string serialized=json.Serialize(state);
            if(ready && serialized!=sent) {
                await view.CoreWebView2.ExecuteScriptAsync("window.setReducedMotion("+json.Serialize(config.reducedMotion)+");window.updateState("+serialized+")");
                sent=serialized;
            }
            string summary=json.Serialize(new { visible=visible, rendererReady=ready, automaticAnchor=!config.manualAnchor&&!layout.composer.IsEmpty, layout=placement.mode, placementReason=placement.reason, kind=state.kind,label=state.label,x=lastX,y=lastY,width=lastW,height=lastH,windowPid=targetPid,windowHandle=target.ToInt64(),threadId=config.threadId });
            if(summary!=diagnostic) {
                File.WriteAllText(Path.Combine(folder,"status.json"),summary,new UTF8Encoding(false));
                diagnostic=summary;
            }
        } catch(Exception e) { if(IsVisible) Hide(); layout=new LayoutSnapshot(); Log("update failure "+e.GetType().Name); }
        finally { updating=false; }
    }
    void Cleanup() {
        if(disposed) return; disposed=true;
        timer.Stop();
        for(int i=1;i<=6;i++) Native.UnregisterHotKey(handle,i);
        if(tray!=null) { tray.Visible=false; tray.Dispose(); }
        view.Dispose();
        Log("overlay closed");
    }
}

public static class Program {
    [STAThread] public static void Main(string[] args) {
        var folder=AppDomain.CurrentDomain.BaseDirectory;
        if(args.Length>0 && args[0]=="--self-test") {
            try { PlacementTests.Run(); ReaderTests.Run(); File.AppendAllText(Path.Combine(folder,"self-test.txt"),"Passed: new-chat text avoidance, left/right gutters, missing composer, narrow layout, DPI, unsafe nudges and manual placement.\n"); }
            catch(Exception e) {
                File.WriteAllText(Path.Combine(folder,"self-test.txt"),"FAILED: "+e.Message);
                Environment.Exit(1);
            }
            return;
        }
        bool created;
        using(var mutex=new Mutex(true,@"Local\PixelAtWorkCodexOverlay",out created)) {
            if(!created) return;
            var app=new Application();
            app.DispatcherUnhandledException+=delegate(object s,DispatcherUnhandledExceptionEventArgs e) {
                File.AppendAllText(Path.Combine(folder,"diagnostic.log"),"Unhandled "+e.Exception.ToString()+Environment.NewLine);
                e.Handled=true; app.Shutdown(1);
            };
            app.Run(new OverlayWindow());
        }
    }
}

// This verifies event ordering and truncated UTF-8 recovery, rather than merely
// repeating the classification implementation. It creates synthetic records only.
public static class ReaderTests {
    public static void Run() {
        string folder=Path.Combine(Path.GetTempPath(),"PixelAtWorkReaderTest-"+Guid.NewGuid().ToString("N"));
        string sessions=Path.Combine(folder,"sessions"); Directory.CreateDirectory(sessions);
        string file=Path.Combine(sessions,"rollout-test.jsonl");
        var json=new JavaScriptSerializer();
        Action<object> append=delegate(object p) { File.AppendAllText(file,json.Serialize(new {type="response_item",payload=p})+"\n",new UTF8Encoding(false)); };
        File.WriteAllText(file,"{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}\n",new UTF8Encoding(false));
        var reader=new SessionReader(folder,"test");
        if(reader.Poll().kind!="think") throw new Exception("Turn start");
        append(new {type="custom_tool_call",call_id="c1",name="exec",input="tools.apply_patch"});
        if(reader.Poll().kind!="code") throw new Exception("Tool starts");
        append(new {type="custom_tool_call_output",call_id="c1",output="{\"exit_code\":0}"});
        if(reader.Poll().crates.Count!=1) throw new Exception("Tool completion");
        append(new {type="custom_tool_call",call_id="c2",name="exec",input="tools.exec_command npm test"});
        append(new {type="custom_tool_call_output",call_id="c2",output="{\"exit_code\":1}"});
        if(reader.Poll().kind!="bug" || (bool)reader.Poll().crates.Last()["ok"]) throw new Exception("Failure outcome");
        var bytes=Encoding.UTF8.GetBytes("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"note\":\"完成\"}}\n");
        using(var stream=new FileStream(file,FileMode.Append,FileAccess.Write)) stream.Write(bytes,0,bytes.Length-5);
        reader.Poll();
        using(var stream=new FileStream(file,FileMode.Append,FileAccess.Write)) stream.Write(bytes,bytes.Length-5,5);
        if(reader.Poll().label!="Codex · 等你发话") throw new Exception("Partial UTF-8 record");
        // Only remove the exact synthetic files created above.
        File.Delete(file); Directory.Delete(sessions); Directory.Delete(folder);
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),"Passed: turn start, tool start/completion, failure, partial UTF-8, turn complete.\n");
    }
}
