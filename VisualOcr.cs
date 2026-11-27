using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace BaliDesktop
{
    interface IVisualOcr
    {
        string[] Languages();
        Task<string> Read(Bitmap image,string language,CancellationToken cancel);
    }
    sealed class VisualOcrResult
    {
        public string Text;
        public VisualElement[] Lines;
    }
    sealed class WindowsVisualOcr : IVisualOcr
    {
        public string[] Languages()
        {
            try {return OcrEngine.AvailableRecognizerLanguages.Select(x=>x.LanguageTag).ToArray();}
            catch(Exception){return new string[0];}
        }
        public async Task<string> Read(Bitmap image,string language,CancellationToken cancel)
        {return (await ReadLayout(image,language,cancel).ConfigureAwait(false)).Text;}
        public async Task<VisualOcrResult> ReadLayout(Bitmap image,string language,CancellationToken cancel)
        {
            var available=Languages();
            if(available.Length==0)throw new InvalidOperationException("Windows OCR no está disponible para esta sesión.");
            string selected=available.FirstOrDefault(x=>x.Equals(language,StringComparison.OrdinalIgnoreCase));
            if(selected==null)throw new InvalidOperationException("El idioma OCR elegido no está instalado.");
            var engine=OcrEngine.TryCreateFromLanguage(new Language(selected));
            if(engine==null)throw new InvalidOperationException("No pude iniciar Windows OCR.");
            using(var encoded=new MemoryStream())
            using(var stream=new InMemoryRandomAccessStream())
            {
                image.Save(encoded,ImageFormat.Png);
                var bytes=encoded.ToArray();
                try
                {
                    using(var writer=new DataWriter(stream))
                    {
                        writer.WriteBytes(bytes);
                        await Wait(writer.StoreAsync(),cancel).ConfigureAwait(false);
                        writer.DetachStream();
                    }
                }
                finally {Array.Clear(bytes,0,bytes.Length);}
                stream.Seek(0);
                var decoder=await Wait(BitmapDecoder.CreateAsync(stream),cancel).ConfigureAwait(false);
                using(var bitmap=await Wait(decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied),cancel).ConfigureAwait(false))
                {
                    var result=await Wait(engine.RecognizeAsync(bitmap),cancel).ConfigureAwait(false);
                    var lines=result.Lines.Select(line=>{
                        var bounds=System.Windows.Rect.Empty;
                        foreach(var word in line.Words)
                        {
                            var rect=word.BoundingRect;
                            bounds.Union(new System.Windows.Rect(rect.X,rect.Y,rect.Width,rect.Height));
                        }
                        return new VisualElement{Name=line.Text,Kind="Línea",Source="OCR · posición aproximada",Bounds=bounds,Enabled=true};
                    }).ToArray();
                    return new VisualOcrResult{Text=String.Join("\n",lines.Select(line=>line.Name)),Lines=lines};
                }
            }
        }
        static async Task<T> Wait<T>(IAsyncOperation<T> operation,CancellationToken cancel)
        {
            using(cancel.Register(()=>{try{operation.Cancel();}catch(Exception){}}))
            {
                while(operation.Status==AsyncStatus.Started)await Task.Delay(12,cancel).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
                return operation.GetResults();
            }
        }
    }
}
