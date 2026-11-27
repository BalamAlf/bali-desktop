using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace BaliDesktop
{
    sealed class VisualAnalysis
    {
        readonly IVisualOcr ocr;
        public VisualAnalysis(IVisualOcr provider){ocr=provider;}
        public async Task<VisualContext> Observe(VisualSettings config,VisualWindow expected,string scope,
            Rectangle area,bool forceCapture,bool preview,CancellationToken token)
        {
            if(!config.Enabled || !config.Identify || !VisualWindows.Interactive())
                throw new InvalidOperationException("Activa Visión e identificación de ventanas en el escritorio interactivo.");
            var clock=Stopwatch.StartNew();
            var currently=expected==null?null:VisualWindows.Inspect(expected.Handle);
            if(expected!=null && (!expected.Same(currently) || VisualWindows.Excluded(currently,config.Exclusions)))
                throw new InvalidOperationException("La ventana cambió o está protegida o excluida.");
            if(scope=="ventana" && currently==null)throw new InvalidOperationException("Selecciona una ventana visible.");
            var result=new VisualContext{Window=currently,Scope=scope,WindowTime=clock.Elapsed,Method="Identificación de Windows"};
            AccessibleVisual accessible=null;
            if(currently!=null && config.Accessible)
            {
                clock.Restart();
                try{accessible=VisualAccessibility.Read(currently,token);}
                catch(OperationCanceledException){throw;}
                catch(Exception){result.Limitation="La aplicación no expuso su contenido accesible.";}
                result.AccessibleTime=clock.Elapsed;
                if(accessible!=null)
                {
                    result.Protected=accessible.Password || VisualText.HasSensitiveContent(accessible.Text);
                    if(result.Protected)return result;
                    result.Text=VisualText.LocalDisplay(accessible.Text);
                    result.Controls=accessible.Controls.Select(VisualText.LocalDisplay).Where(x=>x.Length>0).ToArray();
                    result.Elements=accessible.Elements;
                    result.Method="Windows UI Automation";
                    if(accessible.Incomplete)result.Limitation="La lectura accesible fue parcial.";
                }
            }
            bool captureAllowed=scope=="ventana"?config.WindowCapture:scope=="monitor"?config.MonitorCapture:config.RegionCapture;
            bool requested=forceCapture || config.Ocr && String.IsNullOrWhiteSpace(result.Text);
            if(forceCapture && !captureAllowed)throw new InvalidOperationException("Falta el permiso de captura para este alcance.");
            if(requested && captureAllowed)
            {
                if(!config.Accessible || currently!=null && (accessible==null || accessible.Incomplete))
                    throw new InvalidOperationException("No pude verificar que el área esté libre de campos protegidos.");
                VisualWindow[] verified=null;
                if(currently==null)
                {
                    var visible=VisualWindows.List().Where(w=>w.Bounds.IntersectsWith(area)).Take(13).ToArray();
                    if(visible.Length>12)throw new InvalidOperationException("Hay demasiadas ventanas en el área para verificar su privacidad.");
                    foreach(var window in visible)
                    {
                        token.ThrowIfCancellationRequested();
                        if(VisualWindows.Excluded(window,config.Exclusions))
                            throw new InvalidOperationException("El área incluye una ventana excluida.");
                        var check=VisualAccessibility.Read(window,token);
                        if(check.Password || check.Incomplete || VisualText.HasSensitiveContent(check.Text))
                            throw new InvalidOperationException("El área contiene campos protegidos o no pude verificarla.");
                    }
                    verified=visible;
                    VerifyArea(area,verified);
                }
                clock.Restart();
                using(var bitmap=VisualWindows.Capture(currently==null?area:currently.Bounds,currently,token,!preview))
                {
                    if(verified!=null)VerifyArea(area,verified);
                    result.CaptureTime=clock.Elapsed;
                    if(preview)result.Preview=Preview(bitmap);
                    result.Method=(accessible==null?"":result.Method+" + ")+"captura visible";
                    if(config.Ocr)
                    {
                        var languages=ocr.Languages();
                        var language=String.IsNullOrWhiteSpace(config.Language)?languages.FirstOrDefault(x=>x.StartsWith("es",StringComparison.OrdinalIgnoreCase))??
                            languages.FirstOrDefault():config.Language;
                        if(language!=null)
                        {
                            clock.Restart();
                            try
                            {
                                string raw;
                                var windowsOcr=ocr as WindowsVisualOcr;
                                if(windowsOcr!=null)
                                {
                                    var words=await windowsOcr.ReadLayout(bitmap,language,token).ConfigureAwait(false);
                                    raw=words.Text;
                                    var origin=currently==null?area:currently.Bounds;
                                    var lines=words.Lines.Where(line=>!line.Bounds.IsEmpty && !VisualText.HasSensitiveContent(line.Name)).Take(80).ToArray();
                                    foreach(var line in lines)line.Bounds.Offset(origin.X,origin.Y);
                                    result.Elements=result.Elements.Concat(lines).ToArray();
                                }
                                else raw=await ocr.Read(bitmap,language,token).ConfigureAwait(false);
                                if(VisualText.HasSensitiveContent(raw))
                                {result.Protected=true;result.Preview=null;result.Text="";result.Controls=new string[0];result.Elements=new VisualElement[0];return result;}
                                var text=VisualText.LocalDisplay(raw);
                                if(!String.IsNullOrWhiteSpace(text))result.Text=text;
                                else result.Limitation="El OCR no encontró texto legible.";
                                result.Method+=" + OCR "+language;
                            }
                            catch(OperationCanceledException){throw;}
                            catch(Exception){result.Limitation="Windows OCR no pudo leer esta imagen.";}
                            result.OcrTime=clock.Elapsed;
                        }
                        else result.Limitation="No hay idiomas de Windows OCR disponibles.";
                    }
                }
            }
            if(expected!=null && !expected.Same(VisualWindows.Inspect(expected.Handle)))
                throw new InvalidOperationException("La ventana cambió durante el análisis.");
            result.Observed=DateTime.UtcNow;
            return result;
        }
        static void VerifyArea(Rectangle area,VisualWindow[] windows)
        {
            var current=VisualWindows.List().Where(w=>w.Bounds.IntersectsWith(area)).Take(13).ToArray();
            if(current.Length!=windows.Length || !current.Zip(windows,(a,b)=>a.Same(b) && a.Bounds==b.Bounds && a.Title==b.Title).All(x=>x))
                throw new InvalidOperationException("Las ventanas del área cambiaron durante la verificación.");
        }
        static BitmapSource Preview(Bitmap bitmap)
        {
            using(var memory=new MemoryStream())
            {
                bitmap.Save(memory,ImageFormat.Png);memory.Position=0;
                var image=new BitmapImage();
                image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=memory;
                image.DecodePixelWidth=Math.Min(820,bitmap.Width);image.EndInit();image.Freeze();
                Array.Clear(memory.GetBuffer(),0,(int)memory.Length);
                return image;
            }
        }
    }
}
