using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace BaliDesktop
{
    sealed partial class BaliApp : Application
    {
        readonly Store store;
        Settings settings;
        BaliLifeEngine life;
        Voice voice;
        ILocalSpeechOutput speechOutput;
        readonly BaliExpressionEngine expressions = new BaliExpressionEngine(Environment.TickCount);
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background);
        Window pet, panel;
        Face face;
        Forms.NotifyIcon tray;
        Drawing.Icon icon;
        IntPtr handle;
        double last, nextSave, nextSample, cpuTime, cpuClock, currentCpu;
        double lastLife;
        bool openLab;
        bool openAgents;
        bool openIntelligence;
        bool openLipLab;
        bool lipValidation;
        bool floatingValidation;
        bool personalValidation, testCalculator;
        bool voiceValidation;
        bool agentValidation;
        double phaseCpu, phaseClock;
        double validationSeconds=8;
        bool restartTest;
        int currentRam;
        Drawing.Point? destination;
        bool userHidden, dragging, closing;
        Drawing.Point? petPress;
        string testFolder;
        int testPhase;
        int testFrames;
        Expression? preview;
        double previewUntil;
        int demoIndex = -1;
        TextBlockBridge diagnostics;
        [STAThread] static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--test") return Tests.Run(args[1]);
                if (args.Length > 0 && args[0] == "--probe") return Tests.Probe(args[1]);
                if (args.Length > 1 && args[0] == "--deepseek-diagnostics") return DeepSeekDiagnostics.Run(args[1]);
                if (args.Length > 1 && args[0] == "--audio-check") return Tests.AudioEvidence(args[1]);
                if (args.Length > 1 && args[0] == "--speech-timing") return SpeechTimingProbe.Run(args[1]);
                if (args.Length > 1 && args[0] == "--floating-input") return Tests.FloatingInput(args[1]);
                if (args.Length > 1 && args[0] == "--stt-compare") return SpeechBenchmark.Run(args[1]);
                if (args.Length > 1 && args[0] == "--desktop-check") return Tests.DesktopEvidence(args[1]);
                if (args.Length > 1 && args[0] == "--desktop-approval-check") return SpeechBenchmark.Approval(args[1]);
                if (args.Length > 1 && args[0] == "--visual-check") return Tests.VisualEvidence(args[1]);
                if (args.Length > 1 && args[0] == "--visual-speech-check") return SpeechBenchmark.Vision(args[1]);
                if (args.Length > 1 && args[0] == "--visual-fixture") return Tests.VisualFixture(args[1]);
                if (args.Length > 1 && args[0] == "--computer-check") return Tests.ComputerEvidence(args[1]);
                if (args.Length > 1 && args[0] == "--computer-fixture") return Tests.ComputerFixture(args[1]);
                if (args.Length > 1 && args[0] == "--pdf-text") return PersonalDocuments.PdfWorker(args[1]);
                bool validate = args.Length > 1 && (args[0] == "--validate" || args[0] == "--restart-test" || args[0]=="--agent-check" || args[0]=="--voice-ui-check" || args[0]=="--lips-check" || args[0]=="--floating-check" || args[0]=="--personal-check");
                bool owner;
                using (var mutex = new Mutex(true,@"Local\BaliDesktop",out owner))
                {
                    if (!owner && !validate) return 0;
                    string path = validate ? args[1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BaliDesktop");
                    var app = new BaliApp(new Store(path));
                    app.floatingValidation=validate && args[0]=="--floating-check";
                    app.personalValidation=validate && args[0]=="--personal-check";
                    app.testCalculator=app.personalValidation && args.Contains("--calculator-check");
                    app.openLab=args.Contains("--lab");
                    app.openAgents=args.Contains("--agents");
                    app.openIntelligence=args.Contains("--intelligence");
                    app.openLipLab=args.Contains("--lips");
                    app.lipValidation=validate && args[0]=="--lips-check";
                    if(app.lipValidation) app.openLipLab=true;
                    app.voiceValidation=validate && args[0]=="--voice-ui-check";
                    if(app.voiceValidation) app.openIntelligence=true;
                    app.agentValidation=validate && args[0]=="--agent-check";
                    if (app.agentValidation) app.openAgents=true;
                    if (validate) { app.testFolder = path; app.restartTest = args[0] == "--restart-test"; }
                    if (args.Contains("--benchmark")) app.validationSeconds=45;
                    return app.Run();
                }
            }
            catch (Exception e)
            {
                if (args.Length > 1) File.AppendAllText(Path.Combine(args[1],"error.txt"),e.ToString());
                else MessageBox.Show(e.Message,"Bali Desktop");
                return 1;
            }
        }
        BaliApp(Store store) { this.store = store; ShutdownMode = ShutdownMode.OnExplicitShutdown; }
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            settings = store.Load();
            lips.Smoothness=settings.Intelligence.LipSmoothness;
            lips.Intensity=settings.Intelligence.LipIntensity;
            life = new BaliLifeEngine(settings,Environment.TickCount);
            life.Begin(DateTime.UtcNow,life.Profile.OriginalUnknown);
            voice = new Voice();
            windowsStt=new WindowsRecognizer(voice);
            speechOutput=voice;
            InitIntelligence();
            InitPersonal();
            InitDesktop();
            InitVision();
            InitComputer();
            face = new Face(expressions,settings.Appearance);
            pet = new Window { Title="Bali", Content=face, AllowsTransparency=true, Background=System.Windows.Media.Brushes.Transparent,
                WindowStyle=WindowStyle.None, ResizeMode=ResizeMode.NoResize, ShowInTaskbar=false, ShowActivated=false };
            pet.SourceInitialized += (s,a) =>
            {
                handle = new WindowInteropHelper(pet).Handle; Desktop.Styles(handle,settings.Pass);
                HwndSource.FromHwnd(handle).AddHook(VisualMouseActivate);
            };
            pet.PreviewMouseLeftButtonDown += PetClick;
            pet.PreviewMouseMove += PetMove;
            pet.PreviewMouseLeftButtonUp += PetRelease;
            pet.MouseLeave += (s,a)=>petPress=null;
            pet.MouseRightButtonUp += (s,a) => OpenPanel();
            pet.Closing += (s,a) => { if (!closing) { a.Cancel=true; HidePet(); } };
            Apply();
            pet.Show();
            if (settings.X == int.MinValue) Home();
            else Place(new Drawing.Point(settings.X,settings.Y));
            RenderTray();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            timer.Tick += Tick;
            timer.Interval = TimeSpan.FromMilliseconds(33);
            timer.Start();
            store.Log("Bali Desktop 1.8 · inicio local");
            if (openLipLab) OpenPanel(LipTab);
            else if (openIntelligence) OpenPanel(IntelligenceTab);
            else if (openAgents) OpenPanel(AgentsTab);
            else if (openLab) OpenPanel(LifeTab);
        }
        void DisplayChanged(object sender,EventArgs e) { Dispatcher.BeginInvoke(new Action(()=>Place(Desktop.Position(handle)))); }
        void PetClick(object sender,MouseButtonEventArgs e)
        {
            if(settings.Pass || dragging) return;
            RememberVisualForeground();
            RememberComputerWindow();
            petPress=Forms.Cursor.Position;
            destination = null;
            e.Handled=true;
        }
        internal static bool IsDrag(Drawing.Point start,Drawing.Point current,double xThreshold,double yThreshold)
        {
            return Math.Abs(current.X-start.X)>=xThreshold || Math.Abs(current.Y-start.Y)>=yThreshold;
        }
        bool MovedFromPress(Drawing.Point start)
        {
            double scale=Desktop.Scale(handle);
            return IsDrag(start,Forms.Cursor.Position,SystemParameters.MinimumHorizontalDragDistance*scale,
                SystemParameters.MinimumVerticalDragDistance*scale);
        }
        void PetMove(object sender,MouseEventArgs e)
        {
            if(!petPress.HasValue || e.LeftButton!=MouseButtonState.Pressed || !MovedFromPress(petPress.Value)) return;
            petPress=null;
            dragging = true;
            try { pet.DragMove(); } catch (InvalidOperationException) { }
            finally { dragging=false; }
            Place(Desktop.Position(handle));
            Save();
        }
        void PetRelease(object sender,MouseButtonEventArgs e)
        {
            if(!petPress.HasValue) return;
            bool click=!MovedFromPress(petPress.Value);
            petPress=null;
            if(click) ToggleFloatingVoice();
            e.Handled=true;
        }
        void Interact(bool surprise) { life.Interact(DateTime.UtcNow,surprise); }
        void Apply()
        {
            pet.Width = pet.Height = settings.Size;
            pet.Opacity = settings.Opacity;
            pet.Topmost = settings.Top;
            if (handle != IntPtr.Zero)
            {
                Desktop.Styles(handle,settings.Pass);
                Place(Desktop.Position(handle));
            }
        }
        int PixelSize { get { return (int)Math.Ceiling(settings.Size*Desktop.Scale(handle)); } }
        void Place(Drawing.Point point) { Desktop.Place(handle,point,PixelSize); }
        void Home()
        {
            var area = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
            Place(new Drawing.Point(area.Right-PixelSize-26,area.Bottom-PixelSize-16));
        }
        void HidePet() { if(floatingActive) CancelConversation(); life.InterruptInitiative(DateTime.UtcNow); userHidden=true; destination=null; pet.Hide(); }
        void ShowPet() { userHidden=false; if (!pet.IsVisible) pet.Show(); Place(Desktop.Position(handle)); }
        void ChangeMode(Mode mode)
        {
            if (settings.Mode!=mode) life.RecordMode(mode,DateTime.UtcNow);
            settings.Mode=mode;
            destination=null;
            life.ModeChanged();
            if (mode != Mode.Descanso) ShowPet();
            Save();
        }
        void Save(bool clearAssociationBackups=false)
        {
            var p = Desktop.Position(handle);
            settings.X=p.X; settings.Y=p.Y;
            store.Save(settings,clearAssociationBackups);
        }
        void Tick(object sender,EventArgs e)
        {
            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(5,now-last);
            last = now;
            DialogueTick(now);
            ExpireVision();
            PersonalTick(now);
            if(!floatingValidation && !personalValidation)DesktopTick(now);
            UpdateSpeechMouth();
            var cursor = Forms.Cursor.Position;
            var position = Desktop.Position(handle);
            int size = PixelSize;
            double x = (cursor.X-position.X-size*.5)/Math.Max(1,size*1.2);
            double y = (cursor.Y-position.Y-size*.5)/Math.Max(1,size*1.2);
            bool near = x*x+y*y < 1;
            if (now-lastLife>=.45)
            {
                double idle=Desktop.Idle();
                bool autoHide=settings.Mode==Mode.Descanso && settings.AutoHide && idle>35;
                if (!userHidden && autoHide && pet.IsVisible) pet.Hide();
                if (!userHidden && !autoHide && !pet.IsVisible) ShowPet();
                life.Tick(now-lastLife>5?0:now-lastLife,idle,near,pet.IsVisible,DateTime.UtcNow);
                lastLife=now;
                if (life.MoveRequested && !dragging)
                    destination=Desktop.Clamp(new Drawing.Point(position.X+(position.X>cursor.X?46:-46),position.Y-24),size);
            }
            bool quiet = settings.Mode == Mode.Concentrado || settings.Mode == Mode.Descanso ||
                life.Current == Expression.Descanso || life.Current == Expression.Sueño;
            bool galleryVisible = gallery != null && tabs.SelectedIndex == DiagnosticTab;
            bool appearanceVisible = appearancePreviews.Count != 0 && tabs.SelectedIndex == AppearanceTab;
            bool conversationVisible=conversationFace!=null && panel!=null && tabs.SelectedIndex==0;
            bool showing = pet.IsVisible || galleryVisible || appearanceVisible || conversationVisible || LipLabVisible;
            bool agentsVisible=panel!=null && tabs.SelectedIndex==AgentsTab && runAgents;
            double period = showing && speaking && !lips.Paused?25:agentsVisible?40:showing ?
                (galleryVisible || appearanceVisible || LipLabVisible || !quiet ? 40 : settings.Mode == Mode.Concentrado ? 83 : 140) : 750;
            if (timer.Interval.TotalMilliseconds != period) timer.Interval = TimeSpan.FromMilliseconds(period);
            if (showing)
            {
                if (near || dragging) destination=null;
                if (destination.HasValue)
                {
                    var target=destination.Value;
                    var step=Math.Max(1,(int)(dt*72));
                    Place(new Drawing.Point(position.X+Math.Sign(target.X-position.X)*Math.Min(step,Math.Abs(target.X-position.X)),
                        position.Y+Math.Sign(target.Y-position.Y)*Math.Min(step,Math.Abs(target.Y-position.Y))));
                    if (target == position) destination=null;
                }
                if (now >= previewUntil)
                {
                    if (demoIndex >= 0) NextPreview();
                    else preview=null;
                }
                double gaze=speaking?.48:1;
                expressions.Tick(Math.Min(.15,dt),preview ?? DialogueExpression(),gaze*Math.Max(-1,Math.Min(1,x)),gaze*Math.Max(-1,Math.Min(1,y)),near && !quiet && !preview.HasValue,quiet && !preview.HasValue);
                if (pet.IsVisible) face.InvalidateVisual();
                if (galleryVisible) gallery.InvalidateVisual();
                if (appearanceVisible)
                    foreach (var sample in appearancePreviews) sample.InvalidateVisual();
                if(conversationVisible) conversationFace.InvalidateVisual();
                if(LipLabVisible) { classicLipPreview.InvalidateVisual(); minimalLipPreview.InvalidateVisual(); }
            }
            if (now >= nextSave) { nextSave=now+30; Save(); }
            if (now >= nextSample)
            {
                nextSample=now+1;
                using (var process = Process.GetCurrentProcess())
                {
                    double total=process.TotalProcessorTime.TotalSeconds;
                    if (cpuClock>0) currentCpu=100*(total-cpuTime)/(now-cpuClock)/Environment.ProcessorCount;
                    cpuTime=total; cpuClock=now; currentRam=(int)(process.WorkingSet64/1048576);
                }
                if (diagnostics != null) diagnostics.Update();
            }
            if (testFolder != null) ValidateTick(now);
            AdvanceAgents(now,dt);
        }
        void RenderTray()
        {
            using (var bitmap = new Drawing.Bitmap(32,32))
            using (var g = Drawing.Graphics.FromImage(bitmap))
            using (var blue = new Drawing.SolidBrush(Drawing.Color.FromArgb(14,41,64)))
            using (var cyan = new Drawing.SolidBrush(Drawing.Color.FromArgb(109,225,255)))
            using (var smile = new Drawing.Pen(cyan,1.5f))
            {
                g.SmoothingMode=Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(blue,0,1,32,30);
                g.FillEllipse(cyan,7,9,7,9);
                g.FillEllipse(cyan,18,9,7,9);
                g.DrawArc(smile,11,16,10,7,14,152);
                var raw=bitmap.GetHicon();
                using (var borrowed=Drawing.Icon.FromHandle(raw)) icon=(Drawing.Icon)borrowed.Clone();
                Desktop.DestroyIcon(raw);
            }
            var menu=new Forms.ContextMenuStrip();
            AddTray(menu,"Mostrar Bali",ShowPet);
            AddTray(menu,"Ocultar Bali",HidePet);
            AddTray(menu,"Conversar",()=>OpenPanel());
            menu.Items.Add(new Forms.ToolStripSeparator());
            foreach (Mode mode in Enum.GetValues(typeof(Mode))) { var captured=mode; AddTray(menu,"Modo "+mode.ToString().ToLowerInvariant(),()=>ChangeMode(captured)); }
            menu.Items.Add(new Forms.ToolStripSeparator());
            var clicks=new Forms.ToolStripMenuItem("Ignorar clics");
            clicks.Click += (s,e)=>{ settings.Pass=!settings.Pass; Apply(); Save(); };
            menu.Opening += (s,e)=>clicks.Checked=settings.Pass;
            menu.Items.Add(clicks);
            AddTray(menu,"Configuración",()=>OpenPanel(1));
            AddTray(menu,"Demostración de expresiones",()=>OpenPanel(DiagnosticTab));
            AddTray(menu,"Laboratorio de agentes",()=>OpenPanel(AgentsTab));
            AddTray(menu,"Inteligencia",()=>OpenPanel(IntelligenceTab));
            AddTray(menu,"Agenda",()=>OpenPanel(AgendaTab));
            AddTray(menu,"Sistema",()=>OpenPanel(AgendaTab+5));
            AddTray(menu,"Visión",()=>{RememberVisualForeground();OpenPanel(VisionTab);});
            AddTray(menu,"Leer ventana actual",ReadVisualFromTray);
            menu.Opening+=(s,e)=>RememberVisualForeground();
            AddTray(menu,"Control de escritorio",()=>OpenPanel(ComputerTab));
            AddTray(menu,"Detener control o dictado",()=>{StopComputer(false);CancelConversation();});
            AddTray(menu,"Demostración de voz",()=>OpenPanel(LipTab));
            AddTray(menu,"Hablar con Bali / cancelar turno",()=>{ if(!pet.IsVisible) ShowPet(); ToggleFloatingVoice(); });
            var manualVoice=new Forms.ToolStripMenuItem("Iniciar escucha manual");
            manualVoice.Click+=(s,e)=>{if(!pet.IsVisible)ShowPet();ToggleManualVoice();};
            menu.Opening+=(s,e)=>manualVoice.Text=manualVoiceEnd?"Terminar escucha manual":"Iniciar escucha manual";
            menu.Items.Add(manualVoice);
            var reaction=new Forms.ToolStripMenuItem("Valorar iniciativa");
            foreach(var item in FeedbackActions)
            {
                var captured=item;
                reaction.DropDownItems.Add(item.Key,null,(s,e)=>SubmitFeedback(captured.Value));
            }
            menu.Opening+=(s,e)=>reaction.Enabled=life.PendingInitiative!=null;
            menu.Items.Add(reaction);
            AddTray(menu,"Salir",()=>{ life.InterruptInitiative(DateTime.UtcNow); Save(); Shutdown(); });
            tray=new Forms.NotifyIcon { Icon=icon, Text="Bali Desktop", ContextMenuStrip=menu, Visible=true };
            tray.DoubleClick += (s,e)=>OpenPanel();
        }
        static void AddTray(Forms.ContextMenuStrip menu,string caption,Action action) { menu.Items.Add(caption,null,(s,e)=>action()); }
        protected override void OnExit(ExitEventArgs e)
        {
            closing=true;
            if(catalogWork!=null)catalogWork.Cancel();
            timer.Stop();
            CancelConversation();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            if (handle != IntPtr.Zero && WinAlive) Save();
            if (tray != null) { tray.Visible=false; tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
            if (icon != null) icon.Dispose();
            if (speechOutput != null) speechOutput.Dispose();
            if(turn!=null) turn.Cancel();
            if(deepSeek!=null) deepSeek.Dispose();
            base.OnExit(e);
        }
        bool WinAlive { get { return pet != null && PresentationSource.FromVisual(pet)!=null; } }
    }
}
