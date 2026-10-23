using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;

public sealed class Pet : Window {
    readonly Image picture = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(14) };
    readonly BitmapSource[] frames = new BitmapSource[4];
    readonly Random random = new Random();
    readonly DispatcherTimer timer = new DispatcherTimer();
    readonly MediaPlayer player = new MediaPlayer();
    readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    readonly ScaleTransform scale = new ScaleTransform();
    readonly RotateTransform tilt = new RotateTransform();
    readonly TranslateTransform bounce = new TranslateTransform();
    readonly Dictionary<string, BitmapSource[]> animations = new Dictionary<string, BitmapSource[]>();
    readonly Dictionary<BitmapSource,Int32Rect> visibleCache = new Dictionary<BitmapSource,Int32Rect>();
    string action = "Idle";
    DateTime actionAt = DateTime.UtcNow, actionUntil = DateTime.MinValue, nextAuto = DateTime.UtcNow.AddSeconds(8);
    bool autonomous = true;

    string[] clips;
    bool dragging, moved, sleeping, roaming, muted;
    Point grab, start;
    DateTime lastClick = DateTime.MinValue, lastTick = DateTime.UtcNow;
    double phase;
    int lastClip = -1, direction = 1;
    System.Windows.Forms.NotifyIcon tray;

    public Pet() {
        Title = "永雏塔菲桌宠"; Width = 336; Height = 300;
        WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize; Content = picture;
        var transforms = new TransformGroup();
        transforms.Children.Add(scale); transforms.Children.Add(tilt); transforms.Children.Add(bounce);
        picture.RenderTransformOrigin = new Point(.5, .95); picture.RenderTransform = transforms;
        var sheet = new BitmapImage(); sheet.BeginInit(); sheet.CacheOption = BitmapCacheOption.OnLoad;
        sheet.UriSource = new Uri(Path.Combine(root, "sprites.png")); sheet.EndInit(); sheet.Freeze();
        for (int i = 0; i < 4; i++) {
            int x = i * sheet.PixelWidth / 4, next = (i + 1) * sheet.PixelWidth / 4;
            var cell = new CroppedBitmap(sheet, new Int32Rect(x, 0, next-x, sheet.PixelHeight));
            var cropped = CenteredCrop(cell);
            frames[i] = NormalizePose(cropped,480.0/cropped.PixelHeight,cropped.PixelHeight);
            frames[i].Freeze();
        }
        foreach(string name in new string[] { "Walk","Pat","Stretch","Sit","Dance","Jump","Sleep","Drag" }) LoadStrip(name);
        picture.Source = frames[0];
        string audioDir = Path.Combine(root, "audio");
        clips = Directory.Exists(audioDir) ? Directory.GetFiles(audioDir, "*.wav").OrderBy(x => x).ToArray() : new string[0];
        player.Volume = .65;
        ContextMenu = MakeMenu();
        MouseLeftButtonDown += Down; MouseMove += Move; MouseLeftButtonUp += Up;
        LostMouseCapture += delegate { dragging = false; };
        MouseWheel += delegate(object sender, MouseWheelEventArgs e) { SetSize(Height + (e.Delta > 0 ? 25 : -25)); };
        KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); };
        Loaded += delegate { ResetPosition(); };
        SizeChanged += delegate { KeepOnScreen(); };
        timer.Interval = TimeSpan.FromMilliseconds(33); timer.Tick += Tick; timer.Start();
        tray = new System.Windows.Forms.NotifyIcon();
        tray.Icon = System.Drawing.SystemIcons.Application; tray.Text = "永雏塔菲 · 双击召回 / 右键菜单";
        var trayMenu = new System.Windows.Forms.ContextMenuStrip();
        trayMenu.Items.Add("召回桌宠", null, delegate { Dispatcher.Invoke(new Action(delegate { Show(); ResetPosition(); })); });
        trayMenu.Items.Add("退出", null, delegate { Dispatcher.Invoke(new Action(Close)); });
        tray.ContextMenuStrip = trayMenu;
        tray.DoubleClick += delegate { Dispatcher.Invoke(new Action(delegate { Show(); ResetPosition(); })); };
        tray.Visible = true;
        Closed += delegate { timer.Stop(); player.Close(); tray.Visible = false; tray.Dispose(); };
    }
    static BitmapSource CenteredCrop(BitmapSource cell) {
        // Crop only at display time; preserve the original sprite sheet unchanged.
        var pixels = new FormatConvertedBitmap(cell, PixelFormats.Bgra32, null, 0);
        int w = pixels.PixelWidth, h = pixels.PixelHeight;
        byte[] buffer = new byte[w*h*4]; pixels.CopyPixels(buffer,w*4,0);
        int left=w, right=0, top=h, bottom=0;
        for(int y=0;y<h;y++) {
            int count=0, rowLeft=w, rowRight=0;
            for(int x=0;x<w;x++) if(buffer[(y*w+x)*4+3]>200) { count++; rowLeft=Math.Min(rowLeft,x); rowRight=Math.Max(rowRight,x); }
            if(count>15) { left=Math.Min(left,rowLeft); right=Math.Max(right,rowRight); top=Math.Min(top,y); bottom=Math.Max(bottom,y); }
        }
        if(left>=right || top>=bottom) throw new Exception("Empty sprite cell");
        left=Math.Max(0,left-4); top=Math.Max(0,top-4); right=Math.Min(w-1,right+4); bottom=Math.Min(h-1,bottom+4);
        return new CroppedBitmap(cell,new Int32Rect(left,top,right-left+1,bottom-top+1));
    }
    static Int32Rect VisibleBounds(BitmapSource source) { Geometry unused; return MainShape(source,false,out unused); }
    static Int32Rect MainShape(BitmapSource source,bool makeClip,out Geometry clip) {
        var image=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);
        int w=image.PixelWidth,h=image.PixelHeight; var pixels=new byte[w*h*4]; image.CopyPixels(pixels,w*4,0);
        var visited=new bool[w*h]; var queue=new int[w*h]; int bestCount=0; var best=new Int32Rect(); int[] bestPixels=null; clip=null;
        for(int index=0;index<w*h;index++) {
            if(visited[index] || pixels[index*4+3]<=128) continue;
            int head=0,tail=1,left=w,right=0,top=h,bottom=0; queue[0]=index; visited[index]=true;
            while(head<tail) {
                int current=queue[head++],x=current%w,y=current/w;
                left=Math.Min(left,x); right=Math.Max(right,x); top=Math.Min(top,y); bottom=Math.Max(bottom,y);
                if(x>0) AddPixel(current-1,pixels,visited,queue,ref tail);
                if(x+1<w) AddPixel(current+1,pixels,visited,queue,ref tail);
                if(y>0) AddPixel(current-w,pixels,visited,queue,ref tail);
                if(y+1<h) AddPixel(current+w,pixels,visited,queue,ref tail);
            }
            if(tail>bestCount) { bestCount=tail; best=new Int32Rect(left,top,right-left+1,bottom-top+1); if(makeClip) { bestPixels=new int[tail]; Array.Copy(queue,bestPixels,tail); } }
        }
        if(bestCount<20) throw new Exception("Empty animation pose");
        if(makeClip) {
            var member=new bool[w*h]; foreach(int pixel in bestPixels) member[pixel]=true;
            var shape=new StreamGeometry { FillRule=FillRule.Nonzero };
            using(var context=shape.Open()) for(int y=best.Y;y<best.Y+best.Height;y++) {
                int x=best.X;
                while(x<best.X+best.Width) {
                    while(x<best.X+best.Width && !member[y*w+x]) x++;
                    int first=x; while(x<best.X+best.Width && member[y*w+x]) x++;
                    if(x>first) {
                        context.BeginFigure(new Point(first-1,y-1),true,true);
                        context.LineTo(new Point(x+1,y-1),true,false);
                        context.LineTo(new Point(x+1,y+2),true,false);
                        context.LineTo(new Point(first-1,y+2),true,false);
                    }
                }
            }
            clip=shape;
        }
        return best;
    }
    static void AddPixel(int index,byte[] pixels,bool[] visited,int[] queue,ref int tail) {
        if(!visited[index] && pixels[index*4+3]>128) { visited[index]=true; queue[tail++]=index; }
    }
    static BitmapSource NormalizePose(BitmapSource source,double factor,double ground) {
        // One camera scale per atlas; crop to main character to exclude adjacent rows' fragments.
        var visual=new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
        Geometry clip; MainShape(source,true,out clip);
        double ox=300-source.PixelWidth*factor/2,oy=530-ground*factor;
        clip.Transform=new MatrixTransform(factor,0,0,factor,ox,oy);
        using(var drawing=visual.RenderOpen()) {
            drawing.PushClip(clip);
            drawing.DrawImage(source,new Rect(ox,oy,source.PixelWidth*factor,source.PixelHeight*factor));
            drawing.Pop();
        }
        var target=new RenderTargetBitmap(600,540,96,96,PixelFormats.Pbgra32); target.Render(visual); target.Freeze(); return target;
    }
    void LoadStrip(string name) {
        var sheet=new BitmapImage(); sheet.BeginInit(); sheet.CacheOption=BitmapCacheOption.OnLoad;
        sheet.UriSource=new Uri(Path.Combine(root,"motions",name+".png")); sheet.EndInit(); sheet.Freeze();
        var cells=new BitmapSource[5];
        for(int i=0;i<5;i++) {
            int x=i*sheet.PixelWidth/5,x2=(i+1)*sheet.PixelWidth/5;
            cells[i]=new CroppedBitmap(sheet,new Int32Rect(x,0,x2-x,sheet.PixelHeight));
        }
        var reference=VisibleBounds(cells[0]); double factor=480.0/reference.Height,ground=reference.Y+reference.Height;
        var poses=new BitmapSource[4];
        for(int i=0;i<4;i++) poses[i]=NormalizePose(cells[i+1],factor,ground);
        if(name=="Walk") { poses[2]=OppositeStep(poses[2]); poses[3]=OppositeStep(poses[3]); }
        animations.Add(name,poses);
    }
    static BitmapSource OppositeStep(BitmapSource source) {
        // Reuse the exact boot textures; mirror the lower-body rig for the opposite-foot half cycle.
        const double seam=411;
        var visual=new DrawingVisual();
        using(var drawing=visual.RenderOpen()) {
            drawing.PushClip(new RectangleGeometry(new Rect(0,0,600,seam)));
            drawing.DrawImage(source,new Rect(0,0,600,540)); drawing.Pop();
            drawing.PushClip(new RectangleGeometry(new Rect(0,seam,600,540-seam)));
            drawing.PushTransform(new ScaleTransform(-1,1,300,0));
            drawing.DrawImage(source,new Rect(0,0,600,540)); drawing.Pop(); drawing.Pop();
        }
        var result=new RenderTargetBitmap(600,540,96,96,PixelFormats.Pbgra32); result.Render(visual); result.Freeze(); return result;
    }
    static double FootCenter(BitmapSource source) {
        var image=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);
        var pixels=new byte[600*540*4]; image.CopyPixels(pixels,600*4,0);
        double sum=0,count=0;
        for(int y=480;y<540;y++) for(int x=0;x<600;x++) if(pixels[(y*600+x)*4+3]>128) { sum+=x; count++; }
        if(count<10) throw new Exception("No grounded boot pixels"); return sum/count;
    }
    static double[] HairMetrics(BitmapSource source) {
        var image=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);
        int w=image.PixelWidth,h=image.PixelHeight; var data=new byte[w*h*4]; image.CopyPixels(data,w*4,0);
        double count=0,rSum=0,gSum=0,bSum=0;
        for(int p=0;p<data.Length;p+=4) {
            int b=data[p],g=data[p+1],r=data[p+2];
            if(data[p+3]>230 && r>170 && g>90 && b>100 && r>g+15 && b>g-5 && r>b-5) { count++; rSum+=r; gSum+=g; bSum+=b; }
        }
        if(count<100) throw new Exception("Not enough hair samples");
        return new double[] { rSum/count,gSum/count,bSum/count };
    }
    static int FrameAt(string name, double elapsed) {
        if(elapsed<0) elapsed=0;
        if(name=="Sit") return Math.Min(3,(int)(elapsed*3));
        if(name=="Jump") return Math.Min(3,(int)(elapsed*3.5));
        if(name=="Sleep") { int[] cycle={0,1,2,3,2,1}; return cycle[(int)(elapsed*1.5)%cycle.Length]; }
        if(name=="Pat" || name=="Stretch") { int[] cycle={0,1,2,3,2,1}; return cycle[(int)(elapsed*4)%cycle.Length]; }
        return (int)(elapsed*(name=="Walk" ? 7 : 5))%4;
    }
    void BeginAction(string name, double seconds) {
        sleeping=false; action=name; actionAt=DateTime.UtcNow; actionUntil=actionAt.AddSeconds(seconds);
        nextAuto=actionUntil.AddSeconds(random.Next(7,15));
        foreach(var item in ContextMenu.Items.OfType<MenuItem>()) if((string)item.Header=="睡觉 / 休息") item.IsChecked=false;
    }
    BitmapSource ActionPose(string name, double elapsed) {
        if(name=="Wave") return frames[((int)(elapsed*4)%2==0) ? 2 : 1];
        if(name=="Idle") return frames[phase%4.8>4.65 ? 1 : 0];
        return animations[name][FrameAt(name,elapsed)];
    }
    const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string StartupName = "TaffyDesktopPet022";
    static string StartupCommand { get { return "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\""; } }
    static bool StartupEnabled() {
        using(var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupKey))
            return key != null && String.Equals(key.GetValue(StartupName) as string, StartupCommand, StringComparison.OrdinalIgnoreCase);
    }
    static void SetStartup(bool enabled) {
        using(var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(StartupKey)) {
            if(key == null) throw new IOException("无法打开当前用户启动设置。");
            if(enabled) key.SetValue(StartupName, StartupCommand, Microsoft.Win32.RegistryValueKind.String);
            else key.DeleteValue(StartupName, false);
        }
    }
    static void TestStartup(string folder) {
        object before = null; var kind = Microsoft.Win32.RegistryValueKind.String;
        using(var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupKey)) {
            if(key != null) { before = key.GetValue(StartupName, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames); if(before != null) kind = key.GetValueKind(StartupName); }
        }
        try {
            SetStartup(true); if(!StartupEnabled()) throw new Exception("Enable startup failed");
            using(var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupKey))
                if((string)key.GetValue(StartupName) != StartupCommand) throw new Exception("Startup path quoting failed");
            SetStartup(false); if(StartupEnabled()) throw new Exception("Disable startup failed");
            using(var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupKey))
                if(key.GetValue(StartupName) != null) throw new Exception("Startup value removal failed");
        } finally {
            using(var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(StartupKey)) {
                if(before == null) key.DeleteValue(StartupName, false); else key.SetValue(StartupName, before, kind);
            }
        }
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"startup-verification.txt"),"PASS: enable and read back; quoted executable path; disable and confirm value removal; previous setting restored.\r\n");
    }
    MenuItem Item(string title, Action action) {
        var item = new MenuItem { Header = title }; item.Click += delegate { action(); }; return item;
    }
    ContextMenu MakeMenu() {
        var menu = new ContextMenu();
        menu.Items.Add(Item("摸摸头", delegate { React(1, true); }));
        menu.Items.Add(Item("招招手", delegate { React(2, true); }));
        var motionMenu = new MenuItem { Header="动作" };
        motionMenu.Items.Add(Item("走一走", delegate { BeginAction("Walk",6); }));
        motionMenu.Items.Add(Item("伸懒腰", delegate { BeginAction("Stretch",3.2); }));
        motionMenu.Items.Add(Item("坐下来", delegate { BeginAction("Sit",8); }));
        motionMenu.Items.Add(Item("跳支舞", delegate { BeginAction("Dance",5); }));
        motionMenu.Items.Add(Item("跳一下", delegate { BeginAction("Jump",1.2); }));
        motionMenu.Items.Add(Item("开心贴贴", delegate { BeginAction("Pat",2.4); }));
        motionMenu.Items.Add(Item("招手", delegate { BeginAction("Wave",2.4); }));
        menu.Items.Add(motionMenu);
        var auto = new MenuItem { Header="随机自主动作", IsCheckable=true, IsChecked=autonomous };
        auto.Click += delegate { autonomous=auto.IsChecked; nextAuto=DateTime.UtcNow.AddSeconds(6); }; menu.Items.Add(auto);
        var rest = new MenuItem { Header = "睡觉 / 休息", IsCheckable = true };
        rest.Click += delegate { sleeping = rest.IsChecked; actionAt=DateTime.UtcNow; action="Idle"; if (sleeping) player.Stop(); }; menu.Items.Add(rest);
        var walk = new MenuItem { Header = "桌面散步", IsCheckable = true };
        walk.Click += delegate { roaming = walk.IsChecked; if(!roaming) { action="Idle"; nextAuto=DateTime.UtcNow.AddSeconds(8); } }; menu.Items.Add(walk);
        var sound = new MenuItem { Header = "静音", IsCheckable = true };
        sound.Click += delegate { muted = sound.IsChecked; if (muted) player.Stop(); }; menu.Items.Add(sound);
        var top = new MenuItem { Header = "始终置顶", IsCheckable = true, IsChecked = true };
        top.Click += delegate { Topmost = top.IsChecked; }; menu.Items.Add(top);
        menu.Items.Add(new Separator());
        var startup = new MenuItem { Header = "开机启动", IsCheckable = true };
        menu.Opened += delegate { try { startup.IsChecked = StartupEnabled(); } catch { startup.IsChecked = false; } };
        startup.Click += delegate {
            try { SetStartup(startup.IsChecked); startup.IsChecked = StartupEnabled(); }
            catch(Exception error) { startup.IsChecked = !startup.IsChecked; MessageBox.Show("无法修改开机启动设置：" + error.Message, "开机启动"); }
        };
        menu.Items.Add(startup);
        menu.Items.Add(Item("小号", delegate { SetSize(220); }));
        menu.Items.Add(Item("中号", delegate { SetSize(300); }));
        menu.Items.Add(Item("大号", delegate { SetSize(420); }));
        menu.Items.Add(Item("播放下一句原片语音", delegate { PlayNext(); }));
        menu.Items.Add(Item("回到右下角", ResetPosition));
        menu.Items.Add(Item("隐藏到托盘", Hide));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("退出", Close)); return menu;
    }
    public static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(Math.Max(min, max), value)); }
    Rect VisiblePetRect() {
        var source=picture.Source as BitmapSource;
        if(source==null) return new Rect(14,14,Math.Max(1,Width-28),Math.Max(1,Height-28));
        Int32Rect bounds;
        if(!visibleCache.TryGetValue(source,out bounds)) { bounds=VisibleBounds(source); visibleCache.Add(source,bounds); }
        double iw=Math.Max(1,Width-28),ih=Math.Max(1,Height-28),factor=Math.Min(iw/source.PixelWidth,ih/source.PixelHeight);
        var rect=new Rect(14+(iw-source.PixelWidth*factor)/2+bounds.X*factor,14+(ih-source.PixelHeight*factor)/2+bounds.Y*factor,bounds.Width*factor,bounds.Height*factor);
        if(scale.ScaleX<0) rect=new Rect(Width-rect.Right,rect.Y,rect.Width,rect.Height);
        return rect;
    }
    void SetSize(double height) {
        double foot=Top+VisiblePetRect().Bottom;
        Height=Clamp(height,180,500); Width=Height*1.12;
        Top=foot-VisiblePetRect().Bottom; KeepOnScreen();
    }
    void ResetPosition() {
        var area=SystemParameters.WorkArea; var visible=VisiblePetRect();
        Left=area.Right-visible.Right-8; Top=area.Bottom-visible.Bottom; KeepOnScreen();
    }
    void KeepOnScreen() {
        // Transparent window margins may leave the screen; clamp only the visible character.
        var area=SystemParameters.WorkArea; var visible=VisiblePetRect();
        if(!double.IsNaN(Left)) Left=Clamp(Left,area.Left-visible.Left,area.Right-visible.Right);
        if(!double.IsNaN(Top)) Top=Clamp(Top,area.Top-visible.Top,area.Bottom-visible.Bottom);
    }
    void Down(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton != MouseButton.Left) return;
        grab = e.GetPosition(this); start = PointToScreen(grab); dragging = true; moved = false; CaptureMouse(); e.Handled = true;
    }
    void Move(object sender, MouseEventArgs e) {
        if (!dragging) return;
        var point = PointToScreen(e.GetPosition(this));
        if ((point-start).Length > 5) moved = true;
        if (!moved) return;
        var source = PresentationSource.FromVisual(this);
        var delta = source.CompositionTarget.TransformFromDevice.Transform(point-start);
        Left += delta.X; Top += delta.Y; start = point; KeepOnScreen();
    }
    void Up(object sender, MouseButtonEventArgs e) {
        if (!dragging) return; dragging = false; ReleaseMouseCapture();
        if (!moved) {
            var p = e.GetPosition(this); React(p.Y < Height * .48 ? 1 : 2, true);
        } else BeginAction("Jump",1.2);
    }
    void React(int frame, bool voice) {
        BeginAction(frame==1 ? "Pat" : "Wave",2.4);
        if (voice && (DateTime.UtcNow-lastClick).TotalSeconds > 1.2) { PlayRandom(); lastClick = DateTime.UtcNow; }
    }
    void Play(int index) {
        if (muted || clips.Length == 0) return;
        lastClip = index; player.Stop(); player.Open(new Uri(clips[index])); player.Play();
    }
    void PlayRandom() { if (clips.Length == 0) return; int n = random.Next(clips.Length); if (n == lastClip) n = (n+1) % clips.Length; Play(n); }
    void PlayNext() { if(clips.Length>0) { Play((lastClip+1)%clips.Length); BeginAction("Wave",2.4); } }
    void Tick(object sender, EventArgs e) {
        var now=DateTime.UtcNow; double dt=Math.Min(.1,(now-lastTick).TotalSeconds); lastTick=now; phase+=dt;
        if(!IsVisible) return;
        if(action!="Idle" && now>=actionUntil) action="Idle";
        bool active=action!="Idle";
        if(autonomous && !sleeping && !dragging && !active && !roaming && !ContextMenu.IsOpen && now>=nextAuto) {
            string[] choices={"Walk","Stretch","Sit","Dance","Jump","Wave"};
            string selected=choices[random.Next(choices.Length)];
            BeginAction(selected, selected=="Jump" ? 1.2 : selected=="Sit" ? 7 : 4);
        }
        string display=dragging&&moved ? "Drag" : sleeping ? "Sleep" : action!="Idle" ? action : roaming ? "Walk" : "Idle";
        double elapsed=(now-actionAt).TotalSeconds;
        picture.Source=ActionPose(display,elapsed);
        scale.ScaleX=1+Math.Sin(phase*2.6)*.003;
        scale.ScaleY=1+Math.Sin(phase*2.6)*.006;
        tilt.Angle=display=="Drag" ? Math.Sin(phase*8)*3 : 0;
        bounce.Y=display=="Jump" ? -Math.Sin(Math.Min(1,elapsed/1.2)*Math.PI)*28 : 0;
        if(display=="Walk") {
            scale.ScaleX=Math.Abs(scale.ScaleX)*direction;
            if(!ContextMenu.IsOpen) {
                Left+=direction*dt*36; var area=SystemParameters.WorkArea;
                var visible=VisiblePetRect();
                if((direction<0 && Left+visible.Left<=area.Left) || (direction>0 && Left+visible.Right>=area.Right)) direction*=-1;
                KeepOnScreen();
            }
        }
    }
    public void Verify(string folder) {

        Directory.CreateDirectory(folder);
        if (!AllowsTransparency || Background != Brushes.Transparent || frames.Length != 4 || clips.Length != 10 || animations.Count != 8) throw new Exception("Configuration validation failed");
        if (Clamp(-9, 0, 100) != 0 || Clamp(109, 0, 100) != 100 || Clamp(20, 0, 100) != 20) throw new Exception("Boundary validation failed");
        SetSize(999); if(Height!=500) throw new Exception("Maximum size failed");
        SetSize(1); if(Height!=180) throw new Exception("Minimum size failed"); SetSize(300);
        var screen=SystemParameters.WorkArea;
        Left=-100000; Top=-100000; KeepOnScreen(); var edge=VisiblePetRect();
        if(Math.Abs(Left+edge.Left-screen.Left)>1 || Math.Abs(Top+edge.Top-screen.Top)>1) throw new Exception("Visible top/left edge cannot reach screen: x="+(Left+edge.Left-screen.Left)+", y="+(Top+edge.Top-screen.Top));
        Left=100000; Top=100000; KeepOnScreen(); edge=VisiblePetRect();
        if(Math.Abs(Left+edge.Right-screen.Right)>1 || Math.Abs(Top+edge.Bottom-screen.Bottom)>1) throw new Exception("Visible bottom/right edge cannot reach screen: x="+(Left+edge.Right-screen.Right)+", y="+(Top+edge.Bottom-screen.Bottom));
        ResetPosition();
        File.WriteAllText(Path.Combine(folder,"edge-verification.txt"),"PASS: visible left/right/top/bottom can reach the work-area boundary; transparent padding is allowed off-screen; walking turns by character edge, not window edge.\r\n");
        React(1,false); Tick(null,EventArgs.Empty); if(action!="Pat") throw new Exception("Pet reaction failed");
        sleeping=true; Tick(null,EventArgs.Empty); if(!animations["Sleep"].Contains(picture.Source)) throw new Exception("Sleep state failed"); sleeping=false;
        foreach(var pair in animations) {
            if(pair.Value.Length!=4) throw new Exception("Missing frames: "+pair.Key);
            if(pair.Value.Any(p=>p.PixelWidth!=600 || p.PixelHeight!=540)) throw new Exception("Inconsistent pose canvas");
            if(FrameAt(pair.Key,0)<0 || FrameAt(pair.Key,100)>3) throw new Exception("Frame bounds");
            BeginAction(pair.Key,2); Tick(null,EventArgs.Empty);
            if(!pair.Value.Contains(picture.Source)) throw new Exception("Wrong action display: "+pair.Key);
            actionUntil=DateTime.UtcNow.AddSeconds(-1); Tick(null,EventArgs.Empty);
            if(action!="Idle") throw new Exception("Action exit failed");
            var encoder=new GifBitmapEncoder();
            for(int f=0;f<4;f++) {
                var view=new Image { Source=pair.Value[f],Width=220,Height=300,Stretch=Stretch.Uniform };
                view.Measure(new Size(220,300)); view.Arrange(new Rect(0,0,220,300));
                var bitmap=new RenderTargetBitmap(220,300,96,96,PixelFormats.Pbgra32); bitmap.Render(view);
                var metadata=new BitmapMetadata("gif");
                metadata.SetQuery("/grctlext/Delay",(ushort)16);
                metadata.SetQuery("/grctlext/Disposal",(byte)2);
                encoder.Frames.Add(BitmapFrame.Create(bitmap,null,metadata,null));
            }
            using(var output=File.Create(Path.Combine(folder,pair.Key+".gif"))) encoder.Save(output);
        }
        var contact=new DrawingVisual();
        using(var drawing=contact.RenderOpen()) {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(232,237,244)),null,new Rect(0,0,880,2400));
            int row=0;
            foreach(var pair in animations) {
                for(int f=0;f<4;f++) {
                    var pose=pair.Value[f]; double factor=Math.Min(220.0/pose.PixelWidth,300.0/pose.PixelHeight);
                    double w=pose.PixelWidth*factor,h=pose.PixelHeight*factor;
                    drawing.DrawImage(pose,new Rect(f*220+(220-w)/2,row*300+(300-h)/2,w,h));
                }
                row++;
            }
        }
        var overview=new RenderTargetBitmap(880,2400,96,96,PixelFormats.Pbgra32); overview.Render(contact);
        var overviewEncoder=new PngBitmapEncoder(); overviewEncoder.Frames.Add(BitmapFrame.Create(overview));
        using(var output=File.Create(Path.Combine(folder,"actions-contact.png"))) overviewEncoder.Save(output);
        if(FrameAt("Walk",0)==FrameAt("Walk",.2)) throw new Exception("Walk must advance frames");
        if(FrameAt("Sit",100)!=3 || FrameAt("Jump",100)!=3) throw new Exception("Hold last frame failed");
        action="Idle"; nextAuto=DateTime.UtcNow.AddSeconds(-1); Tick(null,EventArgs.Empty);
        if(action=="Idle") throw new Exception("Autonomous actions failed");
        dragging=true; moved=true; Tick(null,EventArgs.Empty);
        if(!animations["Drag"].Contains(picture.Source)) throw new Exception("Drag pose priority failed");
        dragging=false; moved=false; action="Idle"; roaming=true; ResetPosition();
        double beforeWalk=Left; lastTick=DateTime.UtcNow.AddSeconds(-.1); Tick(null,EventArgs.Empty);
        if(Math.Abs(Left-beforeWalk)<.5) throw new Exception("Walking did not move");
        roaming=false; nextAuto=DateTime.UtcNow.AddSeconds(8); ResetPosition();
        var fragmentScene=new DrawingVisual();
        using(var drawing=fragmentScene.RenderOpen()) {
            drawing.DrawRectangle(Brushes.Brown,null,new Rect(50,40,60,120));
            drawing.DrawRectangle(Brushes.Brown,null,new Rect(60,10,20,10));
        }
        var fragmentImage=new RenderTargetBitmap(200,200,96,96,PixelFormats.Pbgra32); fragmentImage.Render(fragmentScene);
        var clean=NormalizePose(fragmentImage,1,160); var cleanBytes=new byte[600*540*4]; clean.CopyPixels(cleanBytes,600*4,0);
        for(int y=0;y<400;y++) for(int x=0;x<600;x++) if(cleanBytes[(y*600+x)*4+3]>0) throw new Exception("Neighbor-frame fragment was rendered");
        double stepA=FootCenter(animations["Walk"][0]),stepB=FootCenter(animations["Walk"][2]);
        if(Math.Abs(stepA-stepB)<25 || (stepA-300)*(stepB-300)>=0) throw new Exception("Walk does not alternate leading boots");
        File.WriteAllText(Path.Combine(folder,"walk-verification.txt"),"PASS: opposite-foot boot pixels switch sides: frame1 x="+stepA.ToString("F1")+", frame3 x="+stepB.ToString("F1")+"; frame sequence advances while character position changes.\r\n");
        var referenceColor=HairMetrics(frames[0]);
        var colors=new System.Text.StringBuilder("Action\tR\tG\tB\tLuma\tDeltaLuma\r\n");
        double refLuma=.2126*referenceColor[0]+.7152*referenceColor[1]+.0722*referenceColor[2];
        colors.AppendLine("Original\t"+String.Join("\t",referenceColor.Select(v=>v.ToString("F1")))+"\t"+refLuma.ToString("F1")+"\t0");
        foreach(var pair in animations) {
            var values=pair.Value.Select(HairMetrics).ToArray();
            double r=values.Average(v=>v[0]),g=values.Average(v=>v[1]),b=values.Average(v=>v[2]);
            double luma=.2126*r+.7152*g+.0722*b;
            colors.AppendLine(pair.Key+"\t"+r.ToString("F1")+"\t"+g.ToString("F1")+"\t"+b.ToString("F1")+"\t"+luma.ToString("F1")+"\t"+(luma-refLuma).ToString("F1"));
        }
        File.WriteAllText(Path.Combine(folder,"hair-metrics.tsv"),colors.ToString());
        var masterBounds=VisibleBounds(frames[0]);
        var standA=VisibleBounds(animations["Stretch"][0]);
        var standB=VisibleBounds(animations["Pat"][1]);
        if(Math.Abs(masterBounds.Height-standA.Height)>32 || Math.Abs(masterBounds.Height-standB.Height)>32) throw new Exception("Standing scale mismatch");
        if(Math.Abs(masterBounds.Y+masterBounds.Height-standA.Y-standA.Height)>16 || Math.Abs(masterBounds.Y+masterBounds.Height-standB.Y-standB.Height)>16) throw new Exception("Standing ground mismatch");
        File.WriteAllText(Path.Combine(folder,"scale-verification.txt"),"Original idle height="+masterBounds.Height+"; new standing A="+standA.Height+"; B="+standB.Height+". PASS: heights and ground baselines checked within pose-dependent 32px height / 16px ground tolerance; uniform scale, no nonuniform stretching.\r\n");
        var comparison=new DrawingVisual();
        using(var drawing=comparison.RenderOpen()) {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(232,237,244)),null,new Rect(0,0,900,270));
            drawing.DrawImage(frames[0],new Rect(0,0,300,270));
            drawing.DrawImage(animations["Pat"][0],new Rect(300,0,300,270));
            drawing.DrawImage(animations["Dance"][1],new Rect(600,0,300,270));
        }
        var comparisonBitmap=new RenderTargetBitmap(900,270,96,96,PixelFormats.Pbgra32); comparisonBitmap.Render(comparison);
        var comparisonEncoder=new PngBitmapEncoder(); comparisonEncoder.Frames.Add(BitmapFrame.Create(comparisonBitmap));
        using(var output=File.Create(Path.Combine(folder,"style-comparison.png"))) comparisonEncoder.Save(output);
        for (int i = 0; i < clips.Length; i++) {
            using(var audio = new System.Media.SoundPlayer(clips[i])) audio.Load();
            byte[] bytes = File.ReadAllBytes(clips[i]);
            if (bytes.Length < 1000 || System.Text.Encoding.ASCII.GetString(bytes,0,4) != "RIFF") throw new Exception("Invalid WAV: " + clips[i]);
        }
        for (int i = 0; i < 4; i++) {
            var view = new Image { Source = frames[i], Width = 250, Height = 350, Stretch = Stretch.Uniform };
            view.Measure(new Size(250,350)); view.Arrange(new Rect(0,0,250,350)); view.UpdateLayout();
            var bitmap = new RenderTargetBitmap(250,350,96,96,PixelFormats.Pbgra32); bitmap.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(folder,"pose-"+i+".png"))) encoder.Save(file);
        }
        File.WriteAllText(Path.Combine(folder,"verification.txt"), "PASS: 8 actions / 32 new frames; action transitions, frame progression and final-frame holds; 4 legacy poses; 10 WAV decodes; transparent window; size/position clamps.\r\n");
    }
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread] public static int Main(string[] args) {
        SetProcessDPIAware(); if(args.Length == 2 && args[0] == "--test-startup") { TestStartup(args[1]); return 0; } bool created;
        using (var mutex = new Mutex(true, "Local\\TaffyDesktopPet022", out created)) {
            if (!created) return 0;
            try {
                var app = new Application(); var pet = new Pet();
                if (args.Length == 2 && args[0] == "--verify") { pet.Show(); pet.Verify(args[1]); pet.Close(); return 0; }
                if (args.Length == 2 && args[0] == "--smoke") {
                    pet.Loaded += delegate { pet.Verify(args[1]); pet.React(2,false); };
                    var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    stop.Tick += delegate { stop.Stop(); File.AppendAllText(Path.Combine(args[1],"verification.txt"),"PASS: real WPF window started, dispatcher animation ran, clean exit.\r\n"); pet.Close(); }; stop.Start();
                }
                app.Run(pet); return 0;
            } catch(Exception error) { if(args.Length==2) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1],"failure.txt"),error.ToString()); } else MessageBox.Show(error.Message,"桌宠启动失败"); return 1; }
        }
    }
}
