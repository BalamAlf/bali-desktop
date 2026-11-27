using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BaliDesktop
{
    sealed class Store
    {
        public readonly string Folder;
        string FileName { get { return Path.Combine(Folder,"estado.json"); } }
        string Backup { get { return FileName+".bak"; } }
        string Legacy { get { return Path.Combine(Folder,"estado.1.0.json"); } }
        string AdaptiveBackup { get { return Path.Combine(Folder,"estado.1.1.json"); } }
        string CognitiveBackup { get { return Path.Combine(Folder,"estado.1.2.json"); } }
        bool recovered;
        string loadedPath;
        public Store(string folder) { Folder = folder; }
        public Settings Load()
        {
            foreach (string path in new[] { FileName, Backup, CognitiveBackup, AdaptiveBackup, Legacy })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    using (var stream = File.OpenRead(path))
                    {
                        var value = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                        value.Memory = value.Memory ?? new Memory();
                        value.Memory.Counts = value.Memory.Counts ?? new System.Collections.Generic.Dictionary<string,int>();
                        if (value.Schema==0 || value.Memory.Adaptive==null) value.Memory.Adaptive=new AdaptiveProfile();
                        if (value.Schema<2 || value.Memory.Cognitive==null) value.Memory.Cognitive=new CognitiveMemory();
                        var cognition=value.Memory.Cognitive;
                        value.Intelligence=value.Intelligence??new IntelligenceSettings();
                        value.Intelligence.Normalize();
                        if (cognition.Associations==null || cognition.Recent==null || cognition.Results==null ||
                            cognition.Results.Length!=Enum.GetValues(typeof(Outcome)).Length)
                            throw new InvalidDataException("Memoria asociativa incompleta.");
                        var p=value.Memory.Adaptive;
                        if (p.Hours==null || p.Hours.Length!=24 || p.Hours.Any(h=>h==null || h.Modes==null || h.Modes.Length!=4) ||
                            p.Traits==null || p.History==null || p.Episodes==null || p.TraitMilestone==null || p.TraitMilestone.Length!=5)
                            throw new InvalidDataException("Estructura adaptativa incompleta.");
                        value.Size = Math.Max(110,Math.Min(400,value.Size));
                        value.Opacity = Math.Max(.35,Math.Min(1,value.Opacity));
                        if (value.Schema==0)
                        {
                            p.OriginalUnknown=true;
                        }
                        recovered=path!=FileName;
                        loadedPath=path;
                        if (recovered) Log("Perfil recuperado desde "+Path.GetFileName(path)+".");
                        return value;
                    }
                }
                catch (Exception e) { Log(e.Message); }
            }
            recovered=File.Exists(FileName);
            loadedPath=null;
            return new Settings();
        }
        public void Save(Settings value,bool clearAssociationBackups=false)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                if (loadedPath!=null && value.Schema==0 && !File.Exists(Legacy)) File.Copy(loadedPath,Legacy);
                if (loadedPath!=null && value.Schema==1 && !File.Exists(AdaptiveBackup)) File.Copy(loadedPath,AdaptiveBackup);
                if (loadedPath!=null && value.Schema==2 && !File.Exists(CognitiveBackup)) File.Copy(loadedPath,CognitiveBackup);
                string temp = FileName+".tmp";
                using (var stream = File.Create(temp))
                {
                    value.Schema=3;
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream,value);
                    stream.Flush(true);
                }
                if (File.Exists(FileName))
                    File.Replace(temp,FileName,clearAssociationBackups?null:recovered?FileName+".corrupt":Backup);
                else File.Move(temp,FileName);
                if (clearAssociationBackups) { File.Delete(Backup); File.Delete(FileName+".corrupt"); }
                recovered=false;
            }
            catch (Exception e) { Log(e.Message); }
        }
        public void Reset()
        {
            foreach (string path in new[] { FileName,FileName+".tmp",Backup,Legacy,AdaptiveBackup,CognitiveBackup,FileName+".corrupt" }) File.Delete(path);
            recovered=false;
            loadedPath=null;
        }
        public void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string file = Path.Combine(Folder,"bali.log");
                if (File.Exists(file) && new FileInfo(file).Length > 100000) File.Delete(file);
                File.AppendAllText(file,DateTime.Now.ToString("s")+" "+message+Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    static class Desktop
    {
        [StructLayout(LayoutKind.Sequential)] struct IdleInfo { public uint Size, Time; }
        [DllImport("user32")] static extern bool GetLastInputInfo(ref IdleInfo info);
        [DllImport("user32")] static extern int GetWindowLong(IntPtr window,int index);
        [DllImport("user32")] static extern int SetWindowLong(IntPtr window,int index,int value);
        [DllImport("user32")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int cx,int cy,uint flags);
        [DllImport("user32")] static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
        [DllImport("gdi32")] public static extern bool DeleteObject(IntPtr handle);
        [DllImport("user32")] public static extern bool DestroyIcon(IntPtr handle);
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
        public static double Idle()
        {
            var info = new IdleInfo { Size=8 };
            return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount-info.Time)/1000.0 : 0;
        }
        public static void Styles(IntPtr window,bool pass)
        {
            const int transparent = 0x20;
            int styles = GetWindowLong(window,-20) | 0x80 | 0x08000000;
            SetWindowLong(window,-20,pass ? styles|transparent : styles&~transparent);
        }
        public static double Scale(IntPtr window)
        {
            uint dpi = GetDpiForWindow(window);
            return dpi == 0 ? 1 : dpi/96.0;
        }
        public static System.Drawing.Point Position(IntPtr window)
        {
            Rect rect;
            GetWindowRect(window,out rect);
            return new System.Drawing.Point(rect.Left,rect.Top);
        }
        public static System.Drawing.Point Clamp(System.Drawing.Point point, int size)
        {
            var area = Forms.Screen.FromPoint(point).WorkingArea;
            return new System.Drawing.Point(Math.Max(area.Left,Math.Min(area.Right-size,point.X)),
                Math.Max(area.Top,Math.Min(area.Bottom-size,point.Y)));
        }
        public static void Place(IntPtr window,System.Drawing.Point point,int size)
        {
            point = Clamp(point,size);
            SetWindowPos(window,IntPtr.Zero,point.X,point.Y,0,0,0x15);
        }
    }

}
