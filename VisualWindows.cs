using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using Forms=System.Windows.Forms;

namespace BaliDesktop
{
    sealed class VisualWindow
    {
        public IntPtr Handle;
        public int ProcessId;
        public long ProcessStart;
        public string App, Title, Monitor;
        public Rectangle Bounds;
        public bool Visible;
        public bool Same(VisualWindow other)
        {return other!=null && Handle==other.Handle && ProcessId==other.ProcessId && ProcessStart==other.ProcessStart;}
        public override string ToString(){return App+" · "+Title;}
    }
    static class VisualWindows
    {
        [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int L,T,R,B;}
        delegate bool EnumCallback(IntPtr hwnd,IntPtr state);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback,IntPtr state);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder value,int max);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder value,int max);
        [DllImport("user32.dll")] static extern bool GetWindowDisplayAffinity(IntPtr hwnd,out uint affinity);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd,IntPtr target,uint flags);
        [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint desired);
        [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder text,uint bytes,out uint count);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int id,out int value,int size);
        [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int FrameBounds(IntPtr hwnd,int id,out NativeRect rect,int size);
        static readonly int own=Process.GetCurrentProcess().Id;
        public static bool OwnForeground()
        {uint id;GetWindowThreadProcessId(GetForegroundWindow(),out id);return id==own;}
        public static bool Interactive()
        {
            var desktop=OpenInputDesktop(0,false,1);
            if(desktop==IntPtr.Zero)return false;
            try
            {
                var text=new StringBuilder(128);uint bytes;
                return GetUserObjectInformation(desktop,2,text,256,out bytes) && text.ToString().Equals("Default",StringComparison.OrdinalIgnoreCase);
            }
            finally{CloseDesktop(desktop);}
        }
        public static VisualWindow Inspect(IntPtr handle,bool minimized=false)
        {
            if(handle==IntPtr.Zero || !IsWindowVisible(handle) || !minimized && IsIconic(handle))return null;
            var kind=new StringBuilder(48);
            GetClassName(handle,kind,kind.Capacity);
            if(new[]{"Shell_TrayWnd","Shell_SecondaryTrayWnd","Progman","WorkerW","#32768","NotifyIconOverflowWindow"}.Contains(kind.ToString()))return null;
            int cloaked;
            if(DwmGetWindowAttribute(handle,14,out cloaked,4)==0 && cloaked!=0)return null;
            uint id, affinity;
            GetWindowThreadProcessId(handle,out id);
            if(id==0 || id==own || GetWindowDisplayAffinity(handle,out affinity) && affinity!=0)return null;
            NativeRect rect;
            if(FrameBounds(handle,9,out rect,16)!=0 && !GetWindowRect(handle,out rect))return null;
            if(rect.R-rect.L<50 || rect.B-rect.T<35)return null;
            var title=new StringBuilder(241);
            GetWindowText(handle,title,title.Capacity);
            if(title.Length==0)return null;
            try
            {
                using(var process=Process.GetProcessById((int)id))
                {
                    var bounds=Rectangle.FromLTRB(rect.L,rect.T,rect.R,rect.B);
                    return new VisualWindow{Handle=handle,ProcessId=(int)id,ProcessStart=process.StartTime.Ticks,
                        App=process.ProcessName,Title=title.ToString(),Bounds=bounds,Monitor=Forms.Screen.FromRectangle(bounds).DeviceName,Visible=true};
                }
            }
            catch(Exception){return null;}
        }
        public static bool Excluded(VisualWindow window,IEnumerable<string> exclude)
        {
            return window==null || Regex.IsMatch(window.App+" "+window.Title,
                @"(?i)1password|bitwarden|keepass|lastpass|dashlane|password|contraseñ|credenciales|credential|authenticator|autenticaci[oó]n|sign in|iniciar sesi[oó]n|banca|bank|wallet|privad[oa]|inc[oó]gnito|consent|logonui|lockapp") ||
                exclude.Any(value=>!String.IsNullOrWhiteSpace(value) &&
                    (window.App.Equals(value,StringComparison.OrdinalIgnoreCase) ||
                    window.Title.IndexOf(value,StringComparison.OrdinalIgnoreCase)>=0));
        }
        public static VisualWindow[] List(bool minimized=false)
        {
            if(!Interactive())return new VisualWindow[0];
            var windows=new List<VisualWindow>();
            EnumWindows((hwnd,_)=>{var candidate=Inspect(hwnd,minimized);if(candidate!=null)windows.Add(candidate);return windows.Count<128;},IntPtr.Zero);
            return windows.ToArray();
        }
        public static Bitmap Capture(Rectangle bounds,VisualWindow expected,CancellationToken cancel,bool requireForeground=true)
        {
            cancel.ThrowIfCancellationRequested();
            if(!Interactive())throw new InvalidOperationException("El escritorio interactivo no está disponible.");
            if(expected!=null)
            {
                var active=Inspect(requireForeground?GetForegroundWindow():expected.Handle);
                if(!expected.Same(active) || active.Bounds!=bounds)
                    throw new InvalidOperationException("Para capturar la ventana, mantenla visible y en primer plano.");
            }
            var monitors=Forms.Screen.AllScreens;
            if(bounds.Width<30 || bounds.Height<25 || bounds.Width>4096 || bounds.Height>4096 ||
                (long)bounds.Width*bounds.Height>8294400 || !monitors.Any(x=>x.Bounds.Contains(bounds)))
                throw new InvalidOperationException("El área debe estar completa en un monitor y dentro del límite de resolución.");
            var bitmap=new Bitmap(bounds.Width,bounds.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                using(var graphics=Graphics.FromImage(bitmap))
                {
                    if(expected==null)
                        graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size,CopyPixelOperation.SourceCopy);
                    else
                    {
                        var dc=graphics.GetHdc();
                        bool rendered;
                        try{rendered=PrintWindow(expected.Handle,dc,2);}
                        finally{graphics.ReleaseHdc(dc);}
                        if(!rendered)throw new InvalidOperationException("La aplicación protegió la imagen o no admite captura de ventana.");
                    }
                }
                cancel.ThrowIfCancellationRequested();
                if(!Interactive() || expected!=null && !expected.Same(Inspect(requireForeground?GetForegroundWindow():expected.Handle)))
                    throw new InvalidOperationException("Cambió el escritorio o la ventana durante la captura.");
                return bitmap;
            }
            catch{bitmap.Dispose();throw;}
        }
    }
    sealed class AccessibleVisual
    {
        public string Text;
        public string[] Controls;
        public bool Password, Incomplete;
        public VisualElement[] Elements;
    }
    static class VisualAccessibility
    {
        public static AccessibleVisual Read(VisualWindow target,CancellationToken token)
        {
            var lines=new List<string>();
            var controls=new List<string>();
            var elements=new List<VisualElement>();
            var seen=new HashSet<string>(StringComparer.Ordinal);
            var timer=Stopwatch.StartNew();
            var result=new AccessibleVisual();
            var root=AutomationElement.FromHandle(target.Handle);
            if(root.Current.ProcessId!=target.ProcessId)throw new InvalidOperationException("Cambió la ventana.");
            var stack=new Stack<AutomationElement>();stack.Push(root);
            var walker=TreeWalker.ControlViewWalker;
            int visited=0,characters=0;
            while(stack.Count>0 && visited++<280 && timer.Elapsed.TotalSeconds<2.5)
            {
                token.ThrowIfCancellationRequested();
                var item=stack.Pop();
                try
                {
                    var current=item.Current;
                    if(current.IsOffscreen)continue;
                    if(current.IsPassword){result.Password=true;continue;}
                    var bounds=current.BoundingRectangle;
                    if(bounds.IsEmpty || bounds.Width<=0 || bounds.Height<=0 ||
                        !new System.Windows.Rect(target.Bounds.X,target.Bounds.Y,target.Bounds.Width,target.Bounds.Height).IntersectsWith(bounds))continue;
                    object pattern;
                    string name=current.Name??"";
                    if(elements.Count<100 && name.Length>0 && !VisualText.HasSensitiveContent(name))
                    {
                        string kind=current.ControlType.ProgrammaticName.Replace("ControlType.","");
                        if(kind!="Window" && kind!="Pane")
                        {
                            string state=current.IsEnabled?"":"deshabilitado";
                            if(item.TryGetCurrentPattern(SelectionItemPattern.Pattern,out pattern) &&
                                ((SelectionItemPattern)pattern).Current.IsSelected)state="seleccionado";
                            if(item.TryGetCurrentPattern(TogglePattern.Pattern,out pattern))
                                state=((TogglePattern)pattern).Current.ToggleState.ToString();
                            elements.Add(new VisualElement{Name=name.Substring(0,Math.Min(120,name.Length)),Kind=kind,Bounds=bounds,
                                AutomationId=current.AutomationId,Runtime=String.Join(".",item.GetRuntimeId()),Enabled=current.IsEnabled,
                                State=state,Source="UI Automation"});
                        }
                    }
                    if(item.TryGetCurrentPattern(TextPattern.Pattern,out pattern))
                    {
                        foreach(var range in ((TextPattern)pattern).GetVisibleRanges().Take(8))
                            Add(range.GetText(Math.Min(2000,8000-characters)),lines,seen,ref characters);
                    }
                    else if(current.ControlType==ControlType.Text)
                        Add(name,lines,seen,ref characters);
                    else if(current.ControlType==ControlType.Button || current.ControlType==ControlType.CheckBox ||
                        current.ControlType==ControlType.RadioButton || current.ControlType==ControlType.MenuItem ||
                        current.ControlType==ControlType.ListItem || current.ControlType==ControlType.DataItem)
                    {
                        if(name.Length>0 && controls.Count<60)
                        {
                            string state=current.IsEnabled?"":" (deshabilitado)";
                            if(item.TryGetCurrentPattern(SelectionItemPattern.Pattern,out pattern) &&
                                ((SelectionItemPattern)pattern).Current.IsSelected)state+=" (seleccionado)";
                            if(item.TryGetCurrentPattern(TogglePattern.Pattern,out pattern))
                                state+=" ("+((TogglePattern)pattern).Current.ToggleState.ToString()+")";
                            controls.Add(name.Substring(0,Math.Min(120,name.Length))+state);
                        }
                    }
                    var child=walker.GetFirstChild(item);
                    var siblings=new List<AutomationElement>();
                    while(child!=null && siblings.Count<65)
                    {
                        siblings.Add(child);
                        child=walker.GetNextSibling(child);
                    }
                    if(child!=null)result.Incomplete=true;
                    for(int i=siblings.Count-1;i>=0;i--)stack.Push(siblings[i]);
                }
                catch(ElementNotAvailableException){result.Incomplete=true;}
            }
            result.Text=String.Join("\n",lines);
            result.Controls=controls.Distinct().ToArray();
            result.Elements=elements.ToArray();
            result.Incomplete|=stack.Count>0 || characters>=8000;
            return result;
        }
        static void Add(string value,List<string> lines,HashSet<string> seen,ref int characters)
        {
            if(String.IsNullOrWhiteSpace(value) || characters>=8000)return;
            value=value.Trim();
            if(!seen.Add(value))return;
            if(value.Length>8000-characters)value=value.Substring(0,8000-characters);
            characters+=value.Length;lines.Add(value);
        }
    }
}
