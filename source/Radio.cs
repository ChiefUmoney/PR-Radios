using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("PR Radios")]
[assembly: System.Reflection.AssemblyProduct("PR Radios")]
[assembly: System.Reflection.AssemblyDescription("TeamSpeak 3 radio overlay for Windows")]
[assembly: System.Reflection.AssemblyVersion("0.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.2.0.0")]

public class Channel { public string id {get;set;} public string name {get;set;} }
public class RadioState {
    public bool connected {get;set;} public string server {get;set;} public string channel {get;set;}
    public string name {get;set;} public string speaker {get;set;} public bool tx {get;set;} public bool rx {get;set;}
    public double volume {get;set;} public string message {get;set;} public Channel[] channels {get;set;}
}
public class Preferences { public bool handheld {get;set;} public double scale {get;set;} public double left {get;set;} public double top {get;set;} }

public class RadioWindow : Window {
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h,int index,int value);
    IntPtr handle; HwndSource source; Forms.NotifyIcon tray;
    bool handheld, locked, polling, closing, rendering; double scale=1;
    Canvas panel; Viewbox view; TextBlock channelText, speakerText, stateText, footer, volumeText;
    Ellipse indicator; Button lockButton; RadioState state=new RadioState();
    bool bridgeAvailable; string persistentNotice="";
    DispatcherTimer timer;
    readonly List<UIElement> chrome=new List<UIElement>();
    readonly ConcurrentQueue<string> commands=new ConcurrentQueue<string>();
    readonly string preferences=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preferences.json");
    public RadioWindow(bool render) {
        rendering=render; Title="PR Radios"; Icon=Logo(); WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true; Background=Brushes.Transparent; Topmost=true; ShowInTaskbar=false; ShowActivated=false;
        Left=SystemParameters.WorkArea.Right-660; Top=SystemParameters.WorkArea.Bottom-340;
        if(!render) try {
            if(File.Exists(preferences)) { var p=new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(preferences));
                handheld=p.handheld; scale=Clamp(p.scale,.65,1.65); Left=p.left; Top=p.top; }
        } catch { }
        Build();
        SourceInitialized += delegate {
            handle=new WindowInteropHelper(this).Handle; source=HwndSource.FromHwnd(handle); source.AddHook(Hook);
            SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x08000000|0x80);
            if(!RegisterHotKey(handle,1,0x4003,0x52)) persistentNotice="Ctrl+Alt+R unavailable. Use tray to unlock.";
            if(!RegisterHotKey(handle,2,0x4003,0x48)) persistentNotice="Ctrl+Alt+H unavailable. Use tray to show/hide.";
            if(!RegisterHotKey(handle,3,0x4003,0x53)) persistentNotice="Ctrl+Alt+S unavailable. Use SKIN button.";
        };
        Loaded += delegate {
            if(render) return;
            ClampPosition();
            tray=new Forms.NotifyIcon { Icon=System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location), Text="PR Radios — TeamSpeak 3", Visible=true };
            var menu=new Forms.ContextMenuStrip();
            menu.Items.Add("Show / hide radio",null,delegate { Dispatcher.Invoke(new Action(ToggleVisible)); });
            menu.Items.Add("Unlock and show radio",null,delegate { Dispatcher.Invoke(new Action(delegate { Show(); if(locked) ToggleLock(); })); });
            menu.Items.Add("Switch radio design",null,delegate { Dispatcher.Invoke(new Action(ToggleSkin)); });
            menu.Items.Add("Setup help",null,delegate { Dispatcher.Invoke(new Action(Help)); });
            menu.Items.Add("Exit",null,delegate { Dispatcher.Invoke(new Action(Close)); }); tray.ContextMenuStrip=menu;
            tray.DoubleClick += delegate { Dispatcher.Invoke(new Action(ToggleVisible)); };
            timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(350) }; timer.Tick += async delegate { await Poll(); }; timer.Start();
        };
        Closed += delegate {
            closing=true; if(timer!=null) timer.Stop();
            if(tray!=null) { tray.Visible=false; tray.Dispose(); }
            if(handle!=IntPtr.Zero) { for(int i=1;i<=3;i++) UnregisterHotKey(handle,i); if(source!=null) source.RemoveHook(Hook); }
            if(!rendering) try { File.WriteAllText(preferences,new JavaScriptSerializer().Serialize(new Preferences { handheld=handheld,scale=scale,left=Left,top=Top })); } catch { }
        };
    }
    static double Clamp(double n,double min,double max) { return double.IsNaN(n)||double.IsInfinity(n) ? min : Math.Max(min,Math.Min(max,n)); }
    void ClampPosition() { Left=Clamp(Left,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-Width); Top=Clamp(Top,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-Height); }
    IntPtr Hook(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled) {
        if(msg==0x0312) { if(w.ToInt32()==1) ToggleLock(); if(w.ToInt32()==2) ToggleVisible(); if(w.ToInt32()==3) ToggleSkin(); handled=true; }
        return IntPtr.Zero;
    }
    void ToggleVisible() { if(IsVisible) Hide(); else Show(); }
    void ToggleLock() {
        locked=!locked;
        if(handle!=IntPtr.Zero) { int s=GetWindowLong(handle,-20); SetWindowLong(handle,-20,locked?s|0x20:s&~0x20); }
        lockButton.Content=locked?"UNLOCK":"LOCK"; UpdateDisplay();
    }
    void ToggleSkin() { handheld=!handheld; Build(); ClampPosition(); }
    void Resize(double delta) { scale=Clamp(scale+delta,.65,1.65); SizeWindow(); ClampPosition(); }
    void SizeWindow() { Width=(handheld?280:720)*scale; Height=(handheld?824:324)*scale; }
    static Brush Solid(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
    static BitmapImage Logo() {
        using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PRRadios.Logo")) {
            var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.StreamSource=stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        }
    }
    void Brand(double x,double y,double size) { Place(new Image {Source=Logo(),Width=size,Height=size,Stretch=Stretch.Uniform},x,y); }
    static Brush Metal(string a,string b) { return new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a),(Color)ColorConverter.ConvertFromString(b),new Point(0,0),new Point(1,1)); }
    void Place(UIElement e,double x,double y) { Canvas.SetLeft(e,x); Canvas.SetTop(e,y); panel.Children.Add(e); }
    Border Box(double x,double y,double w,double h,double radius,Brush fill,string edge) {
        var b=new Border { Width=w,Height=h,CornerRadius=new CornerRadius(radius),Background=fill,BorderBrush=Solid(edge),BorderThickness=new Thickness(1) }; Place(b,x,y); return b;
    }
    TextBlock Label(string text,double x,double y,double w,double size,string color,bool bold=false) {
        var t=new TextBlock { Text=text,Width=w,FontFamily=new FontFamily("Segoe UI"),FontSize=size,Foreground=Solid(color),FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=TextAlignment.Center }; Place(t,x,y); return t;
    }
    Button Key(string text,double x,double y,double w,double h,Action action,string tip) {
        var button=new Button { Content=text,Width=w,Height=h,Foreground=Solid("#D6DDD9"),FontFamily=new FontFamily("Segoe UI"),FontSize=10,FontWeight=FontWeights.SemiBold,Cursor=Cursors.Hand,ToolTip=tip,Focusable=false,IsTabStop=false };
        var template=new ControlTemplate(typeof(Button));
        var border=new FrameworkElementFactory(typeof(Border)); border.Name="shell";
        border.SetValue(Border.BackgroundProperty,Metal("#363D3F","#1A1F21")); border.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));
        border.SetValue(Border.BorderBrushProperty,Solid("#51595B")); border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        var content=new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center); content.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center); border.AppendChild(content); template.VisualTree=border;
        var hover=new Trigger { Property=Button.IsMouseOverProperty,Value=true }; hover.Setters.Add(new Setter(Border.BackgroundProperty,Solid("#465355"),"shell")); template.Triggers.Add(hover);
        var press=new Trigger { Property=Button.IsPressedProperty,Value=true }; press.Setters.Add(new Setter(Border.BackgroundProperty,Solid("#137862"),"shell")); template.Triggers.Add(press); button.Template=template;
        button.Click += delegate { action(); }; Place(button,x,y); return button;
    }
    static BitmapImage Skin(string name) {
        using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PRRadios."+name)) {
            var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.StreamSource=stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        }
    }
    Button Hit(double x,double y,double w,double h,Action action,string tip) {
        var button=new Button { Width=w,Height=h,Cursor=Cursors.Hand,ToolTip=tip,Focusable=false,IsTabStop=false };
        var template=new ControlTemplate(typeof(Button));
        var border=new FrameworkElementFactory(typeof(Border)); border.Name="hit";
        border.SetValue(Border.BackgroundProperty,Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty,new CornerRadius(6));
        border.SetValue(Border.BorderThicknessProperty,new Thickness(1)); border.SetValue(Border.BorderBrushProperty,Brushes.Transparent);
        template.VisualTree=border;
        var hover=new Trigger {Property=Button.IsMouseOverProperty,Value=true};
        hover.Setters.Add(new Setter(Border.BackgroundProperty,Solid("#12FFFFFF"),"hit"));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty,Solid("#36D5E5F0"),"hit")); template.Triggers.Add(hover);
        var press=new Trigger {Property=Button.IsPressedProperty,Value=true};
        press.Setters.Add(new Setter(Border.BackgroundProperty,Solid("#60000000"),"hit")); template.Triggers.Add(press);
        button.Template=template; button.Click += delegate {action();}; Place(button,x,y); return button;
    }
    Button Hardware(double x,double y,double w,double h,Action action,string tip) {
        double s=handheld?.5:720.0/2073.0;
        return Hit((handheld?-115:0)+x*s,y*s,w*s,h*s,action,tip);
    }
    void Rotary(double x,double y,double w,double h,bool channel) {
        var hit=Hardware(x,y,w,h,channel?(Action)(()=>ChooseChannels(panel)):(Action)Help,
            channel?"CHANNEL · scroll to change / click to choose":"VOLUME · scroll to adjust TeamSpeak playback");
        hit.MouseWheel += delegate(object sender,MouseWheelEventArgs e) { if(channel) Step(e.Delta>0?1:-1); else Volume(e.Delta>0?2:-2); e.Handled=true; };
    }
    void DragSurface(double x,double y,double w,double h) {
        var drag=new Border {Width=w,Height=h,Background=Brushes.Transparent,Cursor=Cursors.SizeAll,ToolTip="Drag radio"};
        drag.MouseLeftButtonDown += delegate { if(!locked && Mouse.LeftButton==MouseButtonState.Pressed) try {DragMove();} catch(InvalidOperationException){} };
        Place(drag,x,y);
    }
    void Screen(double x,double y,double w,double h) {
        var lcd=Box(x,y,w,h,1,Metal("#E9F0EC","#CEDBD9"),"#87928E");
        lcd.ClipToBounds=true;
        double small=handheld?7:10;
        Label("TS3",x+4,y+3,handheld?18:29,small,"#394649",true);
        volumeText=Label("—",x+w-(handheld?34:65),y+3,handheld?30:58,small,"#394649");
        channelText=Label("OFFLINE",x+3,y+(handheld?18:26),w-6,handheld?10:20,"#162A31",true);
        channelText.FontFamily=new FontFamily("Consolas");
        speakerText=Label("Connect TS3",x+3,y+(handheld?34:51),w-6,handheld?7.5:11,"#33464C");
        double band=handheld?12:18, menu=handheld?10:13;
        Box(x,y+h-band-menu,w,band,0,Solid("#12628F"),"#12628F");
        stateText=Label("OFFLINE",x+2,y+h-band-menu+1,w-4,handheld?7:10,"#F0F7FB",true);
        Box(x,y+h-menu,w,menu,0,Metal("#697671","#414C48"),"#56605B");
        string[] captions=handheld?new[]{"Chan","Vol−","Vol+"}:new[]{"Channel","Prev","Next","Lock","Radio"};
        for(int i=0;i<captions.Length;i++) {
            Label(captions[i],x+i*w/captions.Length,y+h-menu+.3,w/captions.Length,handheld?6.7:8,"#F1F4ED");
            if(i>0) Box(x+i*w/captions.Length,y+h-menu+2,.5,menu-4,0,Solid("#85918B"),"#85918B");
        }
        // Thin translucent reflections keep the live display within the physical glass.
        var glare=new System.Windows.Shapes.Path {Data=Geometry.Parse("M0,0 L"+w.ToString(System.Globalization.CultureInfo.InvariantCulture)+",0 L0,"+(h*.4).ToString(System.Globalization.CultureInfo.InvariantCulture)+" Z"),Fill=Solid("#09FFFFFF"),IsHitTestVisible=false};
        Place(glare,x,y);
    }
    void Build() {
        chrome.Clear();
        panel=new Canvas {Width=handheld?280:720,Height=handheld?824:324,Background=Brushes.Transparent};
        TextOptions.SetTextFormattingMode(panel,TextFormattingMode.Display);
        RenderOptions.SetBitmapScalingMode(panel,BitmapScalingMode.HighQuality);
        view=new Viewbox {Child=panel,Stretch=Stretch.Uniform}; Content=view; SizeWindow();
        if(handheld) DrawHandheld(); else DrawMobile();
        int firstChrome=panel.Children.Count;
        double y=handheld?767:275, x=handheld?21:241;
        Box(x,y,238,27,6,Solid("#ED15191D"),"#3B4248");
        Brand(x+6,y+5,17);
        Key("RADIO",x+29,y+4,43,19,ToggleSkin,"Switch handheld / mobile · Ctrl+Alt+S");
        lockButton=Key(locked?"UNLOCK":"LOCK",x+77,y+4,47,19,ToggleLock,"Click-through · Ctrl+Alt+R");
        Key("−",x+129,y+4,22,19,delegate {Resize(-.1);},"Make smaller");
        Key("+",x+155,y+4,22,19,delegate {Resize(.1);},"Make larger");
        Key("?",x+181,y+4,22,19,Help,"Setup / push-to-talk help");
        Key("×",x+207,y+4,24,19,Close,"Exit PR Radios");
        footer=Label("",handheld?9:100,y+31,handheld?262:520,9,"#E3E9ED");
        footer.TextWrapping=TextWrapping.Wrap; footer.Height=25;
        footer.Effect=new System.Windows.Media.Effects.DropShadowEffect {Color=Colors.Black,BlurRadius=3,ShadowDepth=1,Opacity=1};
        for(int i=firstChrome;i<panel.Children.Count;i++) chrome.Add(panel.Children[i]);
        UpdateDisplay();
    }
    void DrawMobile() {
        var image=new Image {Source=Skin("Mobile"),Width=720,Height=758*720.0/2073.0,Stretch=Stretch.Fill,IsHitTestVisible=false}; Place(image,0,0);
        DragSurface(25,7,670,64); DragSurface(181,71,353,16);
        double s=720.0/2073.0;
        Screen(544*s,272*s,981*s,277*s);
        indicator=new Ellipse {Width=8,Height=8,Fill=Solid("#1D3548"),Stroke=Solid("#071722"),StrokeThickness=1,IsHitTestVisible=false};
        Place(indicator,1927*s-4,425*s-4);
        Rotary(225,226,247,246,false); Rotary(1598,226,247,246,true);
        Hardware(525,614,178,94,()=>ChooseChannels(panel),"Choose TeamSpeak channel");
        Hardware(735,614,183,94,()=>Step(-1),"Previous channel");
        Hardware(950,614,183,94,()=>Step(1),"Next channel");
        Hardware(1164,614,183,94,ToggleLock,"Lock / click-through · Ctrl+Alt+R to unlock");
        Hardware(1380,614,174,94,ToggleSkin,"Switch to handheld radio");
        Hardware(1618,525,97,128,()=>Step(-1),"Previous channel");
        Hardware(1750,525,95,128,()=>Step(1),"Next channel");
        Hardware(1660,478,147,69,()=>Volume(2),"Volume +2 dB");
        Hardware(1660,644,147,67,()=>Volume(-2),"Volume −2 dB");
        Hardware(44,247,113,112,ToggleVisible,"Hide radio · Ctrl+Alt+H to show");
        Hardware(320,473,143,96,Help,"Setup / push-to-talk help");
        Hardware(340,588,126,118,ToggleSkin,"Switch radio model");
        Hardware(1900,245,130,129,Help,"PR Radios settings and help");
        Hardware(1890,581,143,134,()=>ChooseChannels(panel),"Channel menu");
    }
    void DrawHandheld() {
        var skin=new ImageBrush(Skin("Handheld")) {ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(300,0,420,1536),Stretch=Stretch.Fill};
        Place(new Rectangle {Width=210,Height=768,Fill=skin,IsHitTestVisible=false},35,0);
        DragSurface(83,380,110,44); DragSurface(75,700,120,18);
        Screen(95,439,85,75);
        indicator=new Ellipse {Width=4,Height=4,Fill=Solid("#193344"),Stroke=Solid("#07101A"),StrokeThickness=.5,IsHitTestVisible=false};
        Place(indicator,212,435);
        Rotary(465,629,90,119,false); Rotary(596,647,97,105,true);
        Hardware(405,1068,62,36,()=>ChooseChannels(panel),"Choose TeamSpeak channel");
        Hardware(477,1068,63,36,()=>Volume(-2),"Volume −2 dB");
        Hardware(547,1068,63,36,()=>Volume(2),"Volume +2 dB");
        Hardware(454,1139,38,33,()=>Step(-1),"Previous channel");
        Hardware(523,1139,35,33,()=>Step(1),"Next channel");
        Hardware(479,1119,48,23,()=>Volume(2),"Volume +2 dB");
        Hardware(479,1170,48,20,()=>Volume(-2),"Volume −2 dB");
        Hardware(492,1144,30,27,()=>ChooseChannels(panel),"Channel menu");
        Hardware(405,1118,39,72,()=>ChooseChannels(panel),"Channel menu");
        Hardware(567,1118,40,72,ToggleSkin,"Switch to mobile radio");
        for(int i=0;i<9;i++) {int index=i; Hardware(403+(i%3)*72,1201+(i/3)*46,63,38,()=>JoinAt(index),"Select channel "+(i+1));}
        Hardware(403,1336,63,42,()=>Volume(-2),"Volume −2 dB");
        Hardware(475,1336,63,42,ToggleLock,"Lock / click-through · Ctrl+Alt+R to unlock");
        Hardware(547,1336,63,42,()=>Volume(2),"Volume +2 dB");
        Hardware(393,700,72,41,Help,"PR Radios settings and help");
        Hardware(325,853,42,224,Help,"Use your TeamSpeak push-to-talk hotkey · click for setup");
    }

    void UpdateDisplay() {
        if(channelText==null) return;
        channelText.Text=state.connected ? state.name : "OFFLINE";
        channelText.ToolTip=state.connected?state.name:"TeamSpeak is not connected";
        speakerText.Text=state.connected ? (state.tx?(handheld?"Transmitting":"You are transmitting"):state.rx?(state.speaker??"Receiving"):"Listening") : (bridgeAvailable?"Join TS3 server":(handheld?"Connect TS3":"Open TS3 + enable bridge"));
        speakerText.ToolTip=speakerText.Text;
        stateText.Text=state.connected ? (state.tx?(handheld?"TX · TALKING":"●  TRANSMITTING"):state.rx?(handheld?"RX · RECEIVING":"●  RECEIVING"):"STANDBY") : "NO CONNECTION";
        indicator.Fill=Solid(state.connected?(state.tx?"#F9804B":state.rx?"#72E7A6":"#6D967C"):"#64726D");
        volumeText.Text=state.connected?state.volume.ToString("+0;-0;0")+" dB":"VOL —";
        footer.Text=!string.IsNullOrEmpty(state.message)?state.message:!string.IsNullOrEmpty(persistentNotice)?persistentNotice:locked?"CLICK-THROUGH  ·  Ctrl+Alt+R to unlock":"Ctrl+Alt+R: lock  ·  Ctrl+Alt+H: hide";
        foreach(var control in chrome) control.Visibility=locked?Visibility.Hidden:Visibility.Visible;
    }
    void Send(string command) { if(state.connected && commands.Count<8) commands.Enqueue(command); }
    void Volume(int delta) { Send("VOLUME "+state.server+" "+delta); }
    void JoinAt(int index) { if(state.connected && state.channels!=null && index>=0 && index<state.channels.Length) Send("JOIN "+state.server+" "+state.channels[index].id); }
    void Step(int delta) {
        if(!state.connected||state.channels==null||state.channels.Length==0) return;
        int index=Array.FindIndex(state.channels,c=>c.id==state.channel); if(index<0) index=0;
        JoinAt((index+delta+state.channels.Length)%state.channels.Length);
    }
    void ChooseChannels(FrameworkElement anchor) {
        var menu=new ContextMenu();
        if(!state.connected||state.channels==null) menu.Items.Add(new MenuItem {Header="Connect TeamSpeak 3 first",IsEnabled=false});
        else foreach(var channel in state.channels) {
            string id=channel.id, server=state.server;
            var item=new MenuItem { Header=channel.name,IsCheckable=true,IsChecked=channel.id==state.channel };
            item.Click += delegate {Send("JOIN "+server+" "+id);}; menu.Items.Add(item);
        }
        menu.PlacementTarget=anchor; menu.IsOpen=true;
    }
    void Help() {
        MessageBox.Show("1. Close TeamSpeak 3 and double-click PR-Radios-Bridge.ts3_plugin. Install it, then reopen TeamSpeak.\n\n2. Enable PR Radios Bridge in Tools > Options > Addons, and join your server.\n\n3. Set Push-To-Talk in TeamSpeak's Capture settings. TeamSpeak handles the microphone; the overlay shows live transmit status.\n\nScroll the left knob for volume and the right knob for channels. The first key below the display opens the channel list. Drag the upper casing or speaker grille to move the radio.\n\nCtrl+Alt+R: toggle click-through\nCtrl+Alt+H: show/hide\nCtrl+Alt+S: switch radio\n\nThe toolbar hides in click-through mode. The tray icon can always unlock or close the overlay. Use windowed/borderless Roblox. This does not connect to ERLC's built-in radio.\n\nPrototype: channel passwords must be handled in TeamSpeak; up to 128 channels shown. No scan, zones, or radio audio filter yet.","PR Radios — Setup",MessageBoxButton.OK,MessageBoxImage.Information);
    }
    async Task Poll() {
        if(polling||closing) return; polling=true;
        string command; if(!commands.TryDequeue(out command)) command="STATE";
        try {
            RadioState next=await Task.Run(()=>Query(command));
            if(!closing) { state=next; bridgeAvailable=true; }
        } catch {
            if(!closing) { state=new RadioState(); bridgeAvailable=false; string old; while(commands.TryDequeue(out old)){} }
        } finally { polling=false; if(!closing) UpdateDisplay(); }
    }
    static RadioState Query(string command) {
        string pipe="PRRadios-"+WindowsIdentity.GetCurrent().User.Value;
        using(var client=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.None)) {
            client.Connect(150);
            using(var timeout=new System.Threading.Timer(delegate {try {client.Dispose();} catch{}},null,1200,Timeout.Infinite)) {
                byte[] bytes=Encoding.UTF8.GetBytes(command+"\n"); client.Write(bytes,0,bytes.Length); client.Flush();
                using(var reader=new StreamReader(client,Encoding.UTF8)) {
                    string line=reader.ReadLine(); if(string.IsNullOrEmpty(line)||line.Length>131072) throw new IOException("Invalid bridge reply");
                    var s=new JavaScriptSerializer().Deserialize<RadioState>(line); if(s==null) throw new IOException("Empty state"); return s;
                }
            }
        }
    }
    public void Render(string directory) {
        Directory.CreateDirectory(directory);
        foreach(bool portable in new[]{false,true}) {
            handheld=portable; scale=1; Build(); panel.Measure(new Size(panel.Width,panel.Height)); panel.Arrange(new Rect(0,0,panel.Width,panel.Height)); panel.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)panel.Width,(int)panel.Height,96,96,PixelFormats.Pbgra32); bitmap.Render(panel);
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var file=File.Create(System.IO.Path.Combine(directory,portable?"handheld-preview.png":"mobile-preview.png"))) encoder.Save(file);
        }
    }
    [STAThread] public static void Main(string[] args) {
        if(args.Length==2 && args[0]=="--render") { new RadioWindow(true).Render(args[1]); return; }
        if(args.Length==1 && args[0]=="--smoke-test") {
            var testApp=new Application(); var w=new RadioWindow(false); w.rendering=true;
            var done=new DispatcherTimer {Interval=TimeSpan.FromSeconds(3)};
            done.Tick += delegate { done.Stop(); w.ToggleLock(); w.ToggleLock(); w.ToggleSkin(); w.Close(); }; done.Start(); testApp.Run(w); return;
        }
        bool created;
        using(var mutex=new Mutex(true,"Local\\PRRadiosOverlay-"+WindowsIdentity.GetCurrent().User.Value,out created)) {
            if(!created) {MessageBox.Show("The radio is already running. Use its tray icon or Ctrl+Alt+H to show it."); return;}
            new Application().Run(new RadioWindow(false));
        }
    }
}
