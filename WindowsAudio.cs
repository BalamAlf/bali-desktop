using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace BaliDesktop
{
    sealed class InputDevice
    {
        public readonly string Name;
        public readonly uint Id;
        public InputDevice(string name,uint id) { Name=name; Id=id; }
        public override string ToString() { return Name; }
    }

    sealed class MicrophoneCapture : IDisposable
    {
        public const int Seconds=20;
        const int Capacity=16000*2*Seconds;
        [StructLayout(LayoutKind.Sequential,Pack=2)]
        struct Format
        {
            public ushort Tag,Channels;
            public uint Rate,BytesPerSecond;
            public ushort Align,Bits,Extra;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct Header
        {
            public IntPtr Buffer;
            public uint Length,Recorded;
            public IntPtr User;
            public uint Flags,Loops;
            public IntPtr Next,Reserved;
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct Caps
        {
            public ushort Manufacturer,Product;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name;
            public uint Formats;
            public ushort Channels,Reserved;
        }
        [DllImport("winmm.dll")] static extern uint waveInGetNumDevs();
        [DllImport("winmm.dll",CharSet=CharSet.Unicode)] static extern uint waveInGetDevCaps(UIntPtr id,out Caps caps,uint size);
        [DllImport("winmm.dll")] static extern uint waveInOpen(out IntPtr handle,uint id,ref Format format,IntPtr callback,IntPtr user,uint flags);
        [DllImport("winmm.dll")] static extern uint waveInPrepareHeader(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")] static extern uint waveInAddBuffer(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")] static extern uint waveInStart(IntPtr handle);
        [DllImport("winmm.dll")] static extern uint waveInReset(IntPtr handle);
        [DllImport("winmm.dll")] static extern uint waveInUnprepareHeader(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")] static extern uint waveInClose(IntPtr handle);
        static readonly uint headerSize=(uint)Marshal.SizeOf(typeof(Header));
        IntPtr device,header,buffer;
        bool prepared;
        sealed class Block
        {
            public IntPtr Header,Data;
            public bool Prepared;
        }
        readonly Queue<Block> blocks=new Queue<Block>();
        MemoryStream stream;
        byte[] chunk;
        public static InputDevice[] Devices()
        {
            var devices=new List<InputDevice> { new InputDevice("Predeterminado de Windows",uint.MaxValue) };
            for(uint i=0;i<waveInGetNumDevs();i++)
            {
                Caps caps;
                if(waveInGetDevCaps(new UIntPtr(i),out caps,(uint)Marshal.SizeOf(typeof(Caps)))==0)
                    devices.Add(new InputDevice(caps.Name,i));
            }
            return devices.ToArray();
        }
        static void Check(uint error)
        {
            if(error!=0) throw new InvalidOperationException("Windows no pudo abrir o grabar con el micrófono (código "+error+").");
        }
        public MicrophoneCapture(uint id,bool monitor=false)
        {
            var format=new Format { Tag=1,Channels=1,Rate=16000,BytesPerSecond=32000,Align=2,Bits=16 };
            try
            {
                Check(waveInOpen(out device,id,ref format,IntPtr.Zero,IntPtr.Zero,0));
                if(monitor)
                {
                    chunk=new byte[2560]; // 80 ms of 16 kHz mono PCM.
                    stream=new MemoryStream(Capacity);
                    for(int i=0;i<8;i++)
                    {
                        var block=new Block();
                        blocks.Enqueue(block);
                        block.Data=Marshal.AllocHGlobal(chunk.Length);
                        block.Header=Marshal.AllocHGlobal((int)headerSize);
                        Marshal.StructureToPtr(new Header { Buffer=block.Data,Length=(uint)chunk.Length },block.Header,false);
                        Check(waveInPrepareHeader(device,block.Header,headerSize));
                        block.Prepared=true;
                        Check(waveInAddBuffer(device,block.Header,headerSize));
                    }
                    Check(waveInStart(device));
                    return;
                }
                buffer=Marshal.AllocHGlobal(Capacity);
                header=Marshal.AllocHGlobal((int)headerSize);
                Marshal.StructureToPtr(new Header { Buffer=buffer,Length=Capacity },header,false);
                Check(waveInPrepareHeader(device,header,headerSize)); prepared=true;
                Check(waveInAddBuffer(device,header,headerSize)); Check(waveInStart(device));
            }
            catch { Dispose(); throw; }
        }
        public int Pump(Action<byte[],int> observe)
        {
            if(stream==null) return 0;
            return Drain(observe,true);
        }
        int Drain(Action<byte[],int> observe,bool requeue)
        {
            int total=0;
            int count=blocks.Count;
            while(count-->0 && blocks.Count>0)
            {
                var block=blocks.Peek();
                var sample=(Header)Marshal.PtrToStructure(block.Header,typeof(Header));
                if((sample.Flags&1)==0) break;
                int length=Math.Min(Math.Min((int)sample.Recorded,chunk.Length),Capacity-(int)stream.Length)&~1;
                if(length>0)
                {
                    Marshal.Copy(block.Data,chunk,0,length);
                    stream.Write(chunk,0,length);
                    if(observe!=null) observe(chunk,length);
                    total+=length;
                }
                blocks.Enqueue(blocks.Dequeue());
                if(requeue) Check(waveInAddBuffer(device,block.Header,headerSize));
            }
            return total;
        }
        public byte[] Finish()
        {
            Check(waveInReset(device));
            if(stream!=null)
            {
                Drain(null,false);
                return stream.ToArray();
            }
            var result=(Header)Marshal.PtrToStructure(header,typeof(Header));
            var pcm=new byte[Math.Min((int)result.Recorded,Capacity)&~1];
            Marshal.Copy(buffer,pcm,0,pcm.Length);
            return pcm;
        }
        public void Dispose()
        {
            if(device!=IntPtr.Zero)
            {
                waveInReset(device);
                foreach(var block in blocks)
                    if(block.Prepared) waveInUnprepareHeader(device,block.Header,headerSize);
                if(prepared) waveInUnprepareHeader(device,header,headerSize);
                waveInClose(device); device=IntPtr.Zero;
            }
            if(header!=IntPtr.Zero) { Marshal.FreeHGlobal(header); header=IntPtr.Zero; }
            if(buffer!=IntPtr.Zero) { Marshal.FreeHGlobal(buffer); buffer=IntPtr.Zero; }
            foreach(var block in blocks)
            {
                if(block.Header!=IntPtr.Zero) Marshal.FreeHGlobal(block.Header);
                if(block.Data!=IntPtr.Zero) Marshal.FreeHGlobal(block.Data);
            }
            blocks.Clear();
            if(stream!=null) { Array.Clear(stream.GetBuffer(),0,(int)stream.Length); stream.Dispose(); stream=null; }
            if(chunk!=null) { Array.Clear(chunk,0,chunk.Length); chunk=null; }
        }
    }

    interface ILocalSpeechOutput : IDisposable
    {
        string[] Names { get; }
        string Speak(string text,Settings settings,SpeechSignals signals);
        bool PauseSpeech(bool paused);
        void StopSpeech();
    }

    sealed class Voice : ILocalSpeechOutput
    {
        bool inspected;
        RecognizerInfo recognizer;
        SpeechSynthesizer synth;
        readonly Dispatcher dispatcher=Dispatcher.CurrentDispatcher;
        static readonly string[] NoVoices=new string[0];
        public Voice() {}
        public string[] DesktopVocabulary=new string[0];
        public bool CanListen { get { Inspect(); return recognizer!=null; } }
        public string Status { get { Inspect(); return recognizer==null?"Reconocimiento de Windows en español no disponible":"Reconocimiento local "+recognizer.Culture.Name; } }
        void Inspect()
        {
            if(inspected) return;
            inspected=true;
            try { recognizer=SpeechRecognitionEngine.InstalledRecognizers().Where(r=>r.Culture.TwoLetterISOLanguageName=="es")
                .OrderByDescending(r=>r.Culture.Name=="es-MX").FirstOrDefault(); }
            catch(Exception) { recognizer=null; }
        }
        static string[] VoiceNames(SpeechSynthesizer source)
        {
            return source.GetInstalledVoices().Where(v=>v.Enabled)
                .OrderByDescending(v=>v.VoiceInfo.Culture.Name=="es-MX")
                .ThenByDescending(v=>v.VoiceInfo.Culture.Name=="es-419" || v.VoiceInfo.Culture.TwoLetterISOLanguageName=="es" && v.VoiceInfo.Culture.Name!="es-ES")
                .ThenByDescending(v=>v.VoiceInfo.Culture.TwoLetterISOLanguageName=="es")
                .ThenByDescending(v=>v.VoiceInfo.Gender==VoiceGender.Female).Select(v=>v.VoiceInfo.Name).ToArray();
        }
        public string[] Names
        {
            get { try { using(var probe=new SpeechSynthesizer()) return VoiceNames(probe); } catch(Exception) { return NoVoices; } }
        }
        public string Speak(string text,Settings settings,SpeechSignals signals)
        {
            StopSpeech();
            try
            {
                var next=new SpeechSynthesizer();
                synth=next;
                var names=VoiceNames(next);
                if(names.Length==0) { StopSpeech(); return "No hay síntesis local disponible."; }
                next.SelectVoice(names.Contains(settings.VoiceName)?settings.VoiceName:names[0]);
                next.Rate=settings.Rate; next.Volume=settings.Volume;
                next.SetOutputToDefaultAudioDevice();
                Action<Action> onCurrent=action=>dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=>
                {
                    if(synth==next) action();
                }));
                next.SpeakStarted+=(s,e)=>onCurrent(()=>signals.Started());
                next.VisemeReached+=(s,e)=>
                {
                    double observed=LipSync.Now;
                    onCurrent(()=>signals.Viseme(e.Viseme,e.NextViseme,e.AudioPosition.TotalSeconds,e.Duration.TotalSeconds,observed));
                };
                next.PhonemeReached+=(s,e)=>
                {
                    double observed=LipSync.Now;
                    onCurrent(()=>signals.Phoneme(e.Phoneme,e.AudioPosition.TotalSeconds,e.Duration.TotalSeconds,observed));
                };
                next.SpeakProgress+=(s,e)=>onCurrent(()=>signals.Progress(e.AudioPosition.TotalSeconds));
                next.SpeakCompleted+=(s,e)=>dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=>
                {
                    if(synth!=next) return;
                    synth=null; next.Dispose();
                    signals.Completed(e.Cancelled,e.Error!=null);
                }));
                next.SpeakAsync(text);
                return "Reproduciendo con Windows.";
            }
            catch(Exception) { StopSpeech(); return "Windows no pudo reproducir la voz."; }
        }
        public bool PauseSpeech(bool paused)
        {
            if(synth==null) return false;
            try
            {
                if(paused) synth.Pause();
                else synth.Resume();
                return true;
            }
            catch(InvalidOperationException) { return false; }
        }
        public void StopSpeech()
        {
            var old=synth; synth=null;
            if(old==null) return;
            try { old.SpeakAsyncCancelAll(); } catch(InvalidOperationException) { } finally { old.Dispose(); }
        }
        public async Task<string> Transcribe(byte[] pcm,CancellationToken cancel,bool strict=false,SpeechResult evidence=null,bool desktopApproval=false,bool dictation=false)
        {
            if(!CanListen || pcm.Length<6400) return null;
            var selected=recognizer;
            return await Task.Run(async () =>
            {
                cancel.ThrowIfCancellationRequested();
                using(var audio=new MemoryStream(pcm,false))
                using(var engine=new SpeechRecognitionEngine(selected))
                {
                    var phrases=desktopApproval?SpokenQuestion.DesktopAnswers:dictation?
                        new[]{"termina el dictado","cancela el dictado","corrige la última palabra","agrega un punto y aparte","selecciona el último párrafo","copia el borrador","pega en el borrador"}:
                        LocalConversationProvider.Commands.Concat(LocalConversationProvider.ContextCommands)
                        .Concat(LocalConversationProvider.ControlCommands)
                        .Concat(TechnicalSpeech.Phrases).Concat(DesktopVocabulary).Distinct(StringComparer.OrdinalIgnoreCase)
                        .Concat(new[]{"Bali cuánta energía tienes","cuánta energía tienes","cuánta energía tienes Bali",
                            "Bali abre calculadora","abre calculadora","Bali abre explorador","abre explorador",
                            "hola","Bali hola","qué tengo hoy","Bali qué tengo hoy","hola qué tengo hoy","hola y qué tengo hoy",
                            "Bali qué pendientes tengo hoy","qué pendientes tengo hoy","Bali muéstrame mis recordatorios",
                            "Bali recuérdame en cinco minutos beber agua","recuérdame en cinco minutos beber agua",
                            "Bali cancela mi último recordatorio","confirma","cancela","sí","sí es correcto","no","no es eso","olvídalo",
                            "qué tengo abierto","qué estoy viendo","explícame este error","lee lo que aparece en esta ventana",
                            "cómo puedo solucionarlo","revisa nuevamente","lee la ventana","lee la pantalla","mira la pantalla",
                            "qué hay en la pantalla","revisa la ventana","qué ventanas tengo abiertas","minimiza esta ventana","maximiza esta ventana",
                            "minimiza esta aplicación","regresa a la ventana anterior","cambia a la ventana anterior","desplázate hacia abajo","escribe lo siguiente"}).ToArray();
                    var choices=new Choices(phrases);
                    engine.LoadGrammar(new Grammar(new GrammarBuilder(choices) { Culture=selected.Culture }) { Priority=5 });
                    try { engine.LoadGrammar(new DictationGrammar()); }
                    catch(Exception) { if(evidence!=null)evidence.Rejection="Windows no pudo cargar el dictado libre."; }
                    engine.InitialSilenceTimeout=TimeSpan.FromSeconds(4);
                    engine.BabbleTimeout=TimeSpan.FromSeconds(6);
                    engine.SetInputToAudioStream(audio,new SpeechAudioFormatInfo(16000,AudioBitsPerSample.Sixteen,AudioChannel.Mono));
                    var done=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var transcript=new List<string>();
                    engine.SpeechRecognitionRejected+=(sender,args)=>{lock(done){if(evidence!=null)evidence.Rejected++;}};
                    engine.SpeechRecognized+=(sender,args)=>
                    {
                        if(args.Result!=null && args.Result.Confidence>=(strict && args.Result.Grammar is DictationGrammar?.65:.48))
                        {
                            lock(done)
                            {
                                transcript.Add(args.Result.Text);
                                if(evidence!=null)evidence.Confidence=Math.Min(evidence.Confidence??1,args.Result.Confidence);
                            }
                        }
                        else if(evidence!=null) lock(done) evidence.Rejected++;
                    };
                    engine.RecognizeCompleted+=(sender,args)=>
                    {
                        if(args.Error!=null) done.TrySetException(args.Error);
                        else { lock(done) done.TrySetResult(transcript.Count==0?null:String.Join(" ",transcript)); }
                    };
                    engine.RecognizeAsync(RecognizeMode.Multiple);
                    using(cancel.Register(()=>{ try { engine.RecognizeAsyncCancel(); } catch(InvalidOperationException) {} }))
                    {
                        var result=await done.Task.ConfigureAwait(false);
                        cancel.ThrowIfCancellationRequested();
                        return result;
                    }
                }
            },cancel).ConfigureAwait(false);
        }
        public void Dispose() { StopSpeech(); }
    }
}
