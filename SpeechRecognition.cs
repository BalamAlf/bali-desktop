using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BaliDesktop
{
    sealed class SpeechResult
    {
        public string Text, Provider, Model, Rejection;
        public double? Confidence;
        public int Rejected;
        public bool ConfirmedDesktopOpening;
        public double CaptureSeconds, DecodeSeconds, CpuSeconds;
        public long PeakBytes;
        public bool HasText { get { return !String.IsNullOrWhiteSpace(Text); } }
        public bool NeedsReview { get { return Rejected>0 || !Confidence.HasValue; } }
        public bool CanConfirm
        {
            get { return HasText && !NeedsReview && Text.Trim(' ','.','!','¡').Equals("confirma",StringComparison.OrdinalIgnoreCase); }
        }
    }
    interface ISpeechRecognizer
    {
        string Name {get;}
        bool Available {get;}
        Task<SpeechResult> Recognize(byte[] pcm,bool strict,CancellationToken cancel);
    }
    sealed class WindowsRecognizer : ISpeechRecognizer
    {
        readonly Voice voice;
        public WindowsRecognizer(Voice voice){this.voice=voice;}
        public string Name {get{return "Windows Speech";}}
        public bool Available {get{return voice.CanListen;}}
        public async Task<SpeechResult> Recognize(byte[] pcm,bool strict,CancellationToken cancel)
        {return await Recognize(pcm,strict,cancel,false).ConfigureAwait(false);}
        public async Task<SpeechResult> Recognize(byte[] pcm,bool strict,CancellationToken cancel,bool desktopApproval)
        {return await Recognize(pcm,strict,cancel,desktopApproval,false).ConfigureAwait(false);}
        public Task<SpeechResult> Dictate(byte[] pcm,CancellationToken cancel)
        {return Recognize(pcm,true,cancel,false,true);}
        async Task<SpeechResult> Recognize(byte[] pcm,bool strict,CancellationToken cancel,bool desktopApproval,bool dictation)
        {
            var clock=Stopwatch.StartNew();
            var result=new SpeechResult{Provider=Name,Model=voice.Status,CaptureSeconds=pcm.Length/32000.0};
            result.Text=await voice.Transcribe(pcm,cancel,strict,result,desktopApproval,dictation).ConfigureAwait(false);
            result.DecodeSeconds=clock.Elapsed.TotalSeconds;
            if(String.IsNullOrWhiteSpace(result.Text) && result.Rejection==null)result.Rejection="Windows no devolvió palabras con la confianza requerida.";
            return result;
        }
    }
    sealed class WhisperRecognizer : ISpeechRecognizer
    {
        readonly string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"stt");
        readonly string variant;
        readonly bool technicalPrompt;
        public WhisperRecognizer(bool useBase=false):this(useBase?"base":"tiny"){}
        internal WhisperRecognizer(string model,bool prompt=false)
        {
            if(model!="tiny" && model!="base" && model!="small")throw new ArgumentOutOfRangeException("model");
            variant=model;technicalPrompt=prompt;
        }
        string Binary{get{return Path.Combine(folder,"whisper-cli.exe");}}
        string ModelFile{get{return "ggml-"+variant+"-q5_1.bin";}}
        string Model{get{return Path.Combine(folder,ModelFile);}}
        public string Name{get{return (variant=="small"?"Whisper Small":variant=="base"?"Whisper Base":"Whisper Tiny (experimental)")+
            (technicalPrompt?" con contexto técnico":"");}}
        public bool Available{get{return File.Exists(Binary)&&File.Exists(Model);}}
        public async Task<SpeechResult> Recognize(byte[] pcm,bool strict,CancellationToken cancel)
        {return await Decode(pcm,cancel,false).ConfigureAwait(false);}
        public Task<SpeechResult> Dictate(byte[] pcm,CancellationToken cancel){return Decode(pcm,cancel,true);}
        async Task<SpeechResult> Decode(byte[] pcm,CancellationToken cancel,bool dictation)
        {
            if(!Available)throw new InvalidOperationException("No está disponible "+Name+".");
            var clock=Stopwatch.StartNew();
            var result=new SpeechResult{Provider=Name,Model=variant+" multilingüe q5_1 · es",CaptureSeconds=pcm.Length/32000.0};
            cancel.ThrowIfCancellationRequested();
            using(var p=Process.Start(new ProcessStartInfo
            {
                FileName=Binary,WorkingDirectory=folder,
                // An explicit output base keeps the console callback enabled for stdin input.
                // With no output format selected the CLI writes only to the redirected streams.
                Arguments="-m "+ModelFile+" -f - -of bali-transcript -l es -t 4 -ng -nt -nf"+
                    (technicalPrompt?" --prompt \"Conversación en español de México. Términos: VPN, API, SQL, Microsoft Word, Brave.\"":""),
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,
                RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8
            }))
            using(cancel.Register(()=>{try{if(!p.HasExited)p.Kill();}catch(Exception){}}))
            {
                var output=p.StandardOutput.ReadToEndAsync();
                var diagnostics=p.StandardError.ReadToEndAsync();
                using(var wav=new MemoryStream())
                {
                    WriteWave(wav,pcm);
                    wav.Position=0;
                    try{await wav.CopyToAsync(p.StandardInput.BaseStream,81920,cancel).ConfigureAwait(false);}
                    catch(IOException) { cancel.ThrowIfCancellationRequested(); throw; }
                    finally
                    {
                        Array.Clear(wav.GetBuffer(),0,(int)wav.Length);
                        p.StandardInput.BaseStream.Dispose();
                    }
                }
                while(!p.HasExited)
                {
                    p.Refresh();
                    try{result.PeakBytes=Math.Max(result.PeakBytes,p.WorkingSet64);}catch(InvalidOperationException){}
                    await Task.Delay(40,cancel).ConfigureAwait(false);
                }
                string raw=await output.ConfigureAwait(false);
                string debug=await diagnostics.ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
                if(p.ExitCode!=0 || debug.Contains("failed to read audio file"))
                    throw new InvalidOperationException("Whisper no completó la decodificación.");
                try{result.CpuSeconds=p.TotalProcessorTime.TotalSeconds;}catch(InvalidOperationException){}
                var timing=Regex.Match(debug,@"load time\s*=\s*(\d+(?:\.\d+)?)\s*ms");
                if(timing.Success)result.Model+=" · carga "+timing.Groups[1].Value+" ms";
                result.Text=Clean(raw,dictation);
                if(result.Text==null) result.Rejection="Whisper no devolvió una frase utilizable.";
            }
            result.DecodeSeconds=clock.Elapsed.TotalSeconds;
            return result;
        }
        internal static string Clean(string input,bool dictation=false)
        {
            var text=Regex.Replace(input??"",@"\[[^\]\r\n]{1,70}\]|\([^\)\r\n]{1,70}\)"," ");
            text=Regex.Replace(text,@"\s+"," ").Trim();
            string word=text.Trim('¡','!','¿','?','.');
            bool knownWord=SpokenQuestion.Answer(word)!=SpokenReply.Other || word.Equals("hola",StringComparison.OrdinalIgnoreCase);
            if(text.Length>1000 || !knownWord && !VoiceSilenceDetector.IsQuestion(text) && !(dictation && Regex.IsMatch(word,@"^[\p{L}\p{N}]+$")) ||
                Regex.IsMatch(text,@"(?i)subt[ií]tulos|amara\.org|gracias por ver|suscr[ií]bete"))return null;
            return text;
        }
        internal static void WriteWave(Stream stream,byte[] pcm)
        {
            using(var writer=new BinaryWriter(stream,Encoding.ASCII,true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+pcm.Length);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);
                writer.Write((short)1);writer.Write((short)1);writer.Write(16000);writer.Write(32000);
                writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(pcm.Length);writer.Write(pcm);
            }
        }
    }
    static class SpeechAudio
    {
        public static byte[] Prepare(byte[] pcm,double? onset,bool enhance,double voiceRms=0)
        {
            int offset=onset.HasValue?(int)(Math.Max(0,onset.Value-.50)*32000)&~1:0;
            if(offset>=pcm.Length-1)offset=0;
            if(offset==0 && !enhance)return pcm;
            var output=new byte[pcm.Length-offset];
            Buffer.BlockCopy(pcm,offset,output,0,output.Length);
            if(!enhance)return output;
            double previous=0,filtered=0,peak=0;
            for(int i=0;i+1<output.Length;i+=2)
            {
                double sample=unchecked((short)(output[i]|output[i+1]<<8));
                filtered=.974*(filtered+sample-previous);
                previous=sample;peak=Math.Max(peak,Math.Abs(filtered));
                Put(output,i,filtered);
            }
            double gain=peak>400 && peak<12000?Math.Min(2.25,12000/peak):1;
            if(voiceRms>0 && voiceRms<.035 && peak>0)
                gain=Math.Max(gain,Math.Min(8,Math.Min(.06/voiceRms,24000/peak)));
            if(gain>1)
                for(int i=0;i+1<output.Length;i+=2)Put(output,i,unchecked((short)(output[i]|output[i+1]<<8))*gain);
            return output;
        }
        static void Put(byte[] data,int index,double value)
        {
            short sample=(short)Math.Max(-32760,Math.Min(32760,value));
            data[index]=(byte)sample;data[index+1]=(byte)(sample>>8);
        }
    }
}
