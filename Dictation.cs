using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;

namespace BaliDesktop
{
    sealed class DictationDraft
    {
        public string Text="";
        public int SelectionStart,SelectionLength;
        public bool Add(string value)
        {
            if(String.IsNullOrWhiteSpace(value))return false;
            if(SelectionLength>0)return ReplaceSelection(value.Trim());
            string separator=Text.Length==0 || Char.IsWhiteSpace(Text[Text.Length-1])?"":" ";
            if(Text.Length+separator.Length+value.Trim().Length>6000)return false;
            Text+=separator+value.Trim();return true;
        }
        public bool Punctuation(string spoken)
        {
            string value=spoken=="punto y aparte"?".\n\n":spoken=="coma"?",":spoken=="punto"?".":spoken=="dos puntos"?":":null;
            if(value==null || Text.Length==0 || Text.Length+value.Length>6000)return false;
            Text=Text.TrimEnd()+value;return true;
        }
        public bool SelectLast(bool paragraph)
        {
            var match=Regex.Match(Text,paragraph?@"[^\r\n]+(?=\s*$)":@"[\p{L}\p{N}]+(?=[^\p{L}\p{N}]*$)");
            if(!match.Success)return false;
            SelectionStart=match.Index;SelectionLength=match.Length;return true;
        }
        public bool ReplaceSelection(string value)
        {
            if(value==null || SelectionStart<0 || SelectionLength<0 || SelectionStart+SelectionLength>Text.Length ||
                Text.Length-SelectionLength+value.Length>6000)return false;
            Text=Text.Substring(0,SelectionStart)+value+Text.Substring(SelectionStart+SelectionLength);
            SelectionStart+=value.Length;SelectionLength=0;return true;
        }
        public string Selected
        {get{return SelectionLength<=0?"":Text.Substring(SelectionStart,SelectionLength);}}
    }
    sealed class DictationTarget
    {
        public VisualWindow Window;
        public string Runtime, Original;
        public int NativeHandle;
        public bool UsesNative;
    }
    static class DictationWriter
    {
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr handle,StringBuilder name,int size);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageTimeout")] static extern IntPtr NativeRead(IntPtr hwnd,uint message,IntPtr wParam,StringBuilder value,uint flags,uint timeout,out IntPtr result);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageTimeout")] static extern IntPtr NativeWrite(IntPtr hwnd,uint message,IntPtr wParam,string value,uint flags,uint timeout,out IntPtr result);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageTimeout")] static extern IntPtr NativeSelect(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);
        public static bool TrustedEditor(VisualWindow target)
        {
            if(target==null || !target.App.Equals("Notepad",StringComparison.OrdinalIgnoreCase))return false;
            try
            {
                using(var p=Process.GetProcessById(target.ProcessId))
                {
                    var path=p.MainModule.FileName;
                    string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    string system=Environment.SystemDirectory;
                    return path.Equals(System.IO.Path.Combine(system,"notepad.exe"),StringComparison.OrdinalIgnoreCase) ||
                        path.Equals(System.IO.Path.Combine(windows,"notepad.exe"),StringComparison.OrdinalIgnoreCase) ||
                        Regex.IsMatch(path,@"(?i)^[A-Z]:\\Program Files\\WindowsApps\\Microsoft\.WindowsNotepad_[\d.]+_[a-z0-9]+__8wekyb3d8bbwe\\Notepad\\Notepad\.exe$");
                }
            }
            catch(Exception){return false;}
        }
        static AutomationElement Field(VisualWindow target,string[] excludes)
        {
            if(target==null || !VisualWindows.Interactive() || VisualWindows.GetForegroundWindow()!=target.Handle)return null;
            var active=VisualWindows.Inspect(target.Handle);
            if(!target.Same(active) || VisualWindows.Excluded(active,excludes) || ComputerActions.Restricted(active))return null;
            var field=AutomationElement.FocusedElement;
            if(field==null || field.Current.ProcessId!=target.ProcessId || field.Current.IsPassword ||
                !field.Current.IsEnabled || field.Current.IsOffscreen ||
                (field.Current.ControlType!=ControlType.Edit && field.Current.ControlType!=ControlType.Document))return null;
            var walker=TreeWalker.RawViewWalker;
            var parent=field;
            for(int i=0;i<25 && parent!=null;i++,parent=walker.GetParent(parent))
                if(new IntPtr(parent.Current.NativeWindowHandle)==target.Handle)return field;
            return null;
        }
        static string NativeValue(int native)
        {
            if(native==0)return null;
            var handle=new IntPtr(native);
            var name=new StringBuilder(64);GetClassName(handle,name,name.Capacity);
            if(!new[]{"Edit","RichEditD2DPT","RichEdit50W"}.Contains(name.ToString(),StringComparer.OrdinalIgnoreCase))return null;
            IntPtr count;
            if(NativeSelect(handle,0xE,IntPtr.Zero,IntPtr.Zero,2,250,out count)==IntPtr.Zero || count.ToInt64()<0 || count.ToInt64()>24000)return null;
            var text=new StringBuilder((int)count+2);
            if(NativeRead(handle,0xD,new IntPtr(text.Capacity),text,2,250,out count)==IntPtr.Zero)return null;
            return text.ToString();
        }
        public static DictationTarget Prepare(VisualWindow window,string[] exclusions,bool synthetic=false)
        {
            if(!synthetic && !TrustedEditor(window))return null;
            var field=Field(window,exclusions);
            if(field==null)return null;
            object pattern;
            string value=null;
            bool native=false;
            if(field.TryGetCurrentPattern(ValuePattern.Pattern,out pattern))
            {
                var edit=(ValuePattern)pattern;
                if(edit.Current.IsReadOnly)return null;
                value=edit.Current.Value;
            }
            else {value=NativeValue(field.Current.NativeWindowHandle);native=true;}
            if(value==null || value.Length>24000)return null;
            return new DictationTarget{Window=window,Runtime=String.Join(".",field.GetRuntimeId()),Original=value,
                NativeHandle=field.Current.NativeWindowHandle,UsesNative=native};
        }
        public static ComputerResult Insert(DictationTarget target,string draft,string[] exclusions,CancellationToken cancel,bool synthetic=false)
        {
            if(target==null || String.IsNullOrWhiteSpace(draft) || draft.Length>6000 || draft.IndexOf('\0')>=0)
                return ComputerResult.Block("El dictado está vacío o excede el límite.");
            var current=Prepare(target.Window,exclusions,synthetic);
            if(current==null || current.Runtime!=target.Runtime || current.NativeHandle!=target.NativeHandle ||
                current.UsesNative!=target.UsesNative || current.Original!=target.Original)
                return ComputerResult.Block("Cambió la ventana, el campo o su texto. Conservé el borrador en Bali.");
            var focused=Field(target.Window,exclusions);
            if(focused==null || String.Join(".",focused.GetRuntimeId())!=target.Runtime)
                return ComputerResult.Block("El campo dejó de estar enfocado.");
            cancel.ThrowIfCancellationRequested();
            string appended=(target.Original.Length==0 || Char.IsWhiteSpace(target.Original.Last())?"":" ")+draft;
            string final=target.Original+appended;
            if(target.UsesNative)
            {
                var h=new IntPtr(target.NativeHandle);
                IntPtr result;
                if(NativeSelect(h,0xB1,new IntPtr(target.Original.Length),new IntPtr(target.Original.Length),2,250,out result)==IntPtr.Zero)
                    return new ComputerResult(ComputerStatus.Failed,"Windows no confirmó la posición de inserción.");
                cancel.ThrowIfCancellationRequested();
                if(Field(target.Window,exclusions)==null || NativeValue(target.NativeHandle)!=target.Original)
                    return ComputerResult.Block("Cambió el campo antes de insertar.");
                NativeWrite(h,0xC2,new IntPtr(1),appended,2,400,out result);
            }
            else
            {
                object pattern;
                if(!focused.TryGetCurrentPattern(ValuePattern.Pattern,out pattern))return ComputerResult.Block("El campo ya no admite escritura.");
                var value=(ValuePattern)pattern;
                if(value.Current.IsReadOnly || value.Current.Value!=target.Original)return ComputerResult.Block("Cambió el contenido del campo.");
                value.SetValue(final);
            }
            var after=Prepare(target.Window,exclusions,synthetic);
            return after!=null && after.Runtime==target.Runtime && after.Original.Replace("\r\n","\n")==final.Replace("\r\n","\n")?
                new ComputerResult(ComputerStatus.Verified,"Dictado insertado y verificado. El documento sigue abierto."):
                new ComputerResult(ComputerStatus.Requested,"La escritura fue solicitada. No pude verificarla y no la repetí.");
        }
    }
}
