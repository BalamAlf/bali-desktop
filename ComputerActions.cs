using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using Forms=System.Windows.Forms;

namespace BaliDesktop
{
    enum ComputerVerb { None, Activate, Previous, Minimize, Maximize, Restore, Left, Right, ScrollDown, ScrollUp, Select, Expand, Collapse, Invoke, Dictate }
    enum ComputerStatus { Verified, Requested, Blocked, Cancelled, Failed }
    sealed class ComputerRequest
    {
        public ComputerVerb Verb;
        public string Target;
        public static ComputerRequest Parse(string phrase)
        {
            string text=VisualSpeech.Normalize(phrase);
            string plain=DesktopCatalog.Normalize(text);
            if(Regex.IsMatch(plain,@"\b(?:enviar|envia|compra|paga|elimina|borra|ejecuta|instala|publica|contrasena|permisos|desactiva)\b"))
                return new ComputerRequest();
            if(Regex.IsMatch(plain,@"^(?:cambia(?: a)?|regresa a|vuelve a) la (?:ventana|aplicacion) anterior$"))
                return new ComputerRequest{Verb=ComputerVerb.Previous};
            if(Regex.IsMatch(plain,@"^(?:escribe lo siguiente|inicia (?:el )?dictado|modo dictado)$"))
                return new ComputerRequest{Verb=ComputerVerb.Dictate};
            if(Regex.IsMatch(plain,@"^abre (?:la |el )?(?:(?:primera|segunda|tercera) (?:opcion|pestana)|pestana .+)$"))
                return new ComputerRequest{Verb=ComputerVerb.Select,Target=text.Substring(5)};
            if(Regex.IsMatch(plain,@"^(?:desplazate|desplaza|desplazamiento|baja|sube)(?: (?:hacia )?(arriba|abajo))?$"))
                return new ComputerRequest{Verb=plain.Contains("arriba") || plain=="sube"?ComputerVerb.ScrollUp:ComputerVerb.ScrollDown};
            if(Regex.IsMatch(plain,@"^(?:organiza|coloca|mueve) (?:esta|la) ventana a la (?:izquierda|derecha)$"))
                return new ComputerRequest{Verb=plain.EndsWith("izquierda")?ComputerVerb.Left:ComputerVerb.Right};
            var action=Regex.Match(text,@"^(minimiza|minimise|maximiza|restaura|cambia a|activa|selecciona|expande|contrae|pulsa|presiona)\s+(.+)$");
            if(!action.Success)return new ComputerRequest();
            ComputerVerb verb;
            switch(action.Groups[1].Value)
            {
                case "minimiza":case "minimise":verb=ComputerVerb.Minimize;break;
                case "maximiza":verb=ComputerVerb.Maximize;break;
                case "restaura":verb=ComputerVerb.Restore;break;
                case "cambia a":case "activa":verb=ComputerVerb.Activate;break;
                case "selecciona":verb=ComputerVerb.Select;break;
                case "expande":verb=ComputerVerb.Expand;break;
                case "contrae":verb=ComputerVerb.Collapse;break;
                default:verb=ComputerVerb.Invoke;break;
            }
            return new ComputerRequest{Verb=verb,Target=action.Groups[2].Value};
        }
        public static bool ClearLowRisk(ComputerRequest action,string text)
        {
            if(String.IsNullOrWhiteSpace(text) || Regex.IsMatch(text,@"\.\.\.|…|[\[\]]"))return false;
            var plain=DesktopCatalog.Normalize(VisualSpeech.Normalize(text));
            if(action.Verb==ComputerVerb.Minimize || action.Verb==ComputerVerb.Maximize || action.Verb==ComputerVerb.Restore)
                return Regex.IsMatch(plain,@"^(?:minimiza|maximiza|restaura) (?:esta|la) (?:ventana|aplicacion)(?: actual)?$");
            return action.Verb==ComputerVerb.Previous ||
                (action.Verb==ComputerVerb.ScrollDown || action.Verb==ComputerVerb.ScrollUp) && Regex.IsMatch(plain,@"^(?:desplazate|desplaza) (?:hacia )?(?:arriba|abajo)$");
        }
    }
    sealed class ComputerResult
    {
        public readonly ComputerStatus Status;
        public readonly string Message;
        public ComputerResult(ComputerStatus status,string message){Status=status;Message=message;}
        public static ComputerResult Block(string why){return new ComputerResult(ComputerStatus.Blocked,why);}
    }
    static class ComputerActions
    {
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hwnd,int command);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
        public static bool Restricted(VisualWindow window)
        {
            return window==null || Regex.IsMatch(window.App+" "+window.Title,@"(?i)consent|credential|securityhealth|msmpeng|windows defender|seguridad de windows|administrador:\s|administrator:\s|control de cuentas|uac|logonui|lockapp");
        }
        static bool Matches(VisualWindow expected,string[] exclusions,bool foreground)
        {
            if(!VisualWindows.Interactive())return false;
            var current=VisualWindows.Inspect(expected.Handle,true);
            return expected.Same(current) && !VisualWindows.Excluded(current,exclusions) && !Restricted(current) &&
                (!foreground || VisualWindows.GetForegroundWindow()==expected.Handle);
        }
        public static ComputerResult Window(VisualWindow expected,ComputerVerb verb,string[] exclusions,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(expected==null || !Matches(expected,exclusions,false))return ComputerResult.Block("La ventana ya no es válida o está protegida.");
            var handle=expected.Handle;
            bool wasMinimized=IsIconic(handle);
            bool sent;
            Rectangle desired=Rectangle.Empty;
            if(verb==ComputerVerb.Activate || verb==ComputerVerb.Previous)
            {
                if(IsIconic(handle))ShowWindowAsync(handle,9);
                sent=SetForegroundWindow(handle);
            }
            else if(verb==ComputerVerb.Left || verb==ComputerVerb.Right)
            {
                if(IsZoomed(handle) || IsIconic(handle))
                    return ComputerResult.Block("Restaura primero la ventana para organizarla.");
                var area=Forms.Screen.FromRectangle(expected.Bounds).WorkingArea;
                desired=new Rectangle(verb==ComputerVerb.Left?area.Left:area.Left+area.Width/2,area.Top,area.Width/2,area.Height);
                token.ThrowIfCancellationRequested();
                sent=SetWindowPos(handle,IntPtr.Zero,desired.X,desired.Y,desired.Width,desired.Height,0x14);
            }
            else if(verb==ComputerVerb.Minimize || verb==ComputerVerb.Maximize || verb==ComputerVerb.Restore)
                sent=ShowWindowAsync(handle,verb==ComputerVerb.Minimize?6:verb==ComputerVerb.Maximize?3:9);
            else return ComputerResult.Block("Operación de ventana no admitida.");
            for(int i=0;i<10;i++)
            {
                if(token.IsCancellationRequested)return new ComputerResult(ComputerStatus.Requested,"Windows recibió una solicitud antes de cancelar. No se repitió.");
                if(!Matches(expected,exclusions,false))break;
                bool verified=verb==ComputerVerb.Minimize?IsIconic(handle):verb==ComputerVerb.Maximize?IsZoomed(handle):
                    verb==ComputerVerb.Restore?!IsIconic(handle) && (wasMinimized || !IsZoomed(handle)):
                    desired==Rectangle.Empty?VisualWindows.GetForegroundWindow()==handle:Near(VisualWindows.Inspect(handle,true).Bounds,desired);
                if(verified)return new ComputerResult(ComputerStatus.Verified,"Verifiqué el cambio de ventana.");
                token.WaitHandle.WaitOne(40);
            }
            return new ComputerResult(sent?ComputerStatus.Requested:ComputerStatus.Failed,
                sent?"Solicité el cambio, pero Windows no permitió verificarlo.":"Windows rechazó o bloqueó el cambio.");
        }
        static bool Near(Rectangle actual,Rectangle desired)
        {return Math.Abs(actual.X-desired.X)<18 && Math.Abs(actual.Y-desired.Y)<18 && Math.Abs(actual.Width-desired.Width)<25 && Math.Abs(actual.Height-desired.Height)<25;}
        static AutomationElement[] Elements(VisualWindow target,CancellationToken cancel)
        {
            var result=new List<AutomationElement>();
            var walker=TreeWalker.ControlViewWalker;
            var pending=new Queue<AutomationElement>();pending.Enqueue(AutomationElement.FromHandle(target.Handle));
            var clock=System.Diagnostics.Stopwatch.StartNew();
            while(pending.Count>0 && result.Count<280 && clock.ElapsedMilliseconds<1800)
            {
                cancel.ThrowIfCancellationRequested();
                var node=pending.Dequeue();
                result.Add(node);
                var child=walker.GetFirstChild(node);
                int siblings=0;
                while(child!=null && ++siblings<75){pending.Enqueue(child);child=walker.GetNextSibling(child);}
            }
            return result.ToArray();
        }
        public static ComputerResult Control(VisualWindow window,VisualElement expected,ComputerVerb verb,string[] exclusions,CancellationToken cancel)
        {
            if(window==null || !Matches(window,exclusions,true))return ComputerResult.Block("La ventana cambió o está protegida.");
            var elements=Elements(window,cancel);
            if(elements.Any(e=>e.Current.ProcessId==window.ProcessId && !e.Current.IsOffscreen && e.Current.IsPassword))
                return ComputerResult.Block("La ventana contiene campos protegidos.");
            if(verb==ComputerVerb.ScrollDown || verb==ComputerVerb.ScrollUp)
                return Scroll(window,elements,verb==ComputerVerb.ScrollDown,exclusions,cancel);
            if(expected==null || expected.Source!="UI Automation")return ComputerResult.Block("El control debe identificarse con UI Automation.");
            var matched=elements.Where(e=>e.Current.ProcessId==window.ProcessId && !e.Current.IsOffscreen && !e.Current.IsPassword && e.Current.IsEnabled &&
                String.Join(".",e.GetRuntimeId())==expected.Runtime && e.Current.AutomationId==expected.AutomationId && e.Current.Name==expected.Name &&
                e.Current.BoundingRectangle.IntersectsWith(expected.Bounds)).ToArray();
            if(matched.Length!=1 || !Matches(window,exclusions,true))return ComputerResult.Block("El control desapareció, cambió o ya no es único.");
            var item=matched[0];
            object value;
            cancel.ThrowIfCancellationRequested();
            if(verb==ComputerVerb.Select && item.Current.ControlType==ControlType.TabItem && item.TryGetCurrentPattern(SelectionItemPattern.Pattern,out value))
            {
                var tab=(SelectionItemPattern)value;
                if(!tab.Current.IsSelected)tab.Select();
                return Outcome(tab.Current.IsSelected && Matches(window,exclusions,true),"Verifiqué la pestaña seleccionada.");
            }
            if((verb==ComputerVerb.Expand || verb==ComputerVerb.Collapse) &&
                (item.Current.ControlType==ControlType.MenuItem || item.Current.ControlType==ControlType.TreeItem) &&
                item.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out value))
            {
                var menu=(ExpandCollapsePattern)value;
                var expectedState=verb==ComputerVerb.Expand?ExpandCollapseState.Expanded:ExpandCollapseState.Collapsed;
                if(menu.Current.ExpandCollapseState!=expectedState)
                {if(verb==ComputerVerb.Expand)menu.Expand();else menu.Collapse();}
                return Outcome(menu.Current.ExpandCollapseState==expectedState && Matches(window,exclusions,true),"Verifiqué el estado del menú.");
            }
            return ComputerResult.Block("Solo se admiten pestañas, expansión de menús y desplazamiento verificables. La invocación genérica queda bloqueada.");
        }
        static ComputerResult Scroll(VisualWindow window,AutomationElement[] elements,bool down,string[] exclusions,CancellationToken token)
        {
            var candidates=new List<ScrollPattern>();
            foreach(var item in elements)
            {
                object value;
                if(item.Current.ProcessId==window.ProcessId && !item.Current.IsOffscreen && item.TryGetCurrentPattern(ScrollPattern.Pattern,out value) &&
                    ((ScrollPattern)value).Current.VerticallyScrollable)candidates.Add((ScrollPattern)value);
            }
            if(candidates.Count!=1 || !Matches(window,exclusions,true))
                return ComputerResult.Block(candidates.Count==0?"No encontré un área desplazable verificable.":"Hay varias áreas desplazables. Deja únicamente la que quieres usar.");
            var control=candidates[0];double before=control.Current.VerticalScrollPercent;
            if(down && before>=99.9 || !down && before<=.1)return new ComputerResult(ComputerStatus.Verified,"El área ya está en el extremo.");
            token.ThrowIfCancellationRequested();
            control.Scroll(ScrollAmount.NoAmount,down?ScrollAmount.LargeIncrement:ScrollAmount.LargeDecrement);
            return Outcome(Math.Abs(before-control.Current.VerticalScrollPercent)>.01 && Matches(window,exclusions,true),"Verifiqué el desplazamiento.");
        }
        static ComputerResult Outcome(bool verified,string message)
        {return new ComputerResult(verified?ComputerStatus.Verified:ComputerStatus.Requested,verified?message:"Solicité el cambio. No pude comprobar el resultado y no lo repetí.");}
    }
}
