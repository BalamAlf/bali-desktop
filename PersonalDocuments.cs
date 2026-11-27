using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

namespace BaliDesktop
{
    sealed class Reading
    {
        public string Name;
        public string WorkerMetrics;
        public readonly List<KeyValuePair<string,string>> Parts=new List<KeyValuePair<string,string>>();
        public void Add(string source,string text)
        {
            if(String.IsNullOrWhiteSpace(text) || Parts.Count>=300) return;
            Parts.Add(new KeyValuePair<string,string>(source,text.Substring(0,Math.Min(text.Length,3000)).Trim()));
        }
        public string Find(string question)
        {
            string[] keys=Regex.Matches(question??"",@"[\p{L}\p{N}]{3,}").Cast<Match>().Select(m=>m.Value)
                .Where(x=>!new[]{"qué","dice","para","sobre","cuáles","del","las","los"}.Contains(x,StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(14).ToArray();
            var result=Parts.Select(p=>new {Part=p,Hits=keys.Count(k=>p.Value.IndexOf(k,StringComparison.OrdinalIgnoreCase)>=0)})
                .Where(p=>keys.Length==0 || p.Hits>0).OrderByDescending(p=>p.Hits).Take(4).ToArray();
            return result.Length==0?"No encontré fragmentos coincidentes.":String.Join("\n",result.Select(p=>
                p.Part.Key+" · "+p.Part.Value.Substring(0,Math.Min(340,p.Part.Value.Length))));
        }
    }
    static class PersonalDocuments
    {
        const int Limit=12*1024*1024;
        public static Reading Load(string path,CancellationToken cancel)
        {
            var fi=new FileInfo(path);
            if(!fi.Exists || fi.Length>Limit) throw new InvalidOperationException("El archivo debe existir y pesar como máximo 12 MiB.");
            var result=new Reading {Name=fi.Name};
            string ext=fi.Extension.ToLowerInvariant();
            if(ext==".pdf") return ReadPdf(fi,result,cancel);
            if(ext==".docx")
            {
                using(var input=File.OpenRead(fi.FullName))
                using(var archive=new ZipArchive(input,ZipArchiveMode.Read))
                {
                    var part=archive.GetEntry("word/document.xml");
                    if(part==null || part.Length>Limit) throw new InvalidOperationException("DOCX inválido o demasiado grande.");
                    using(var source=part.Open())
                    using(var xml=XmlReader.Create(source,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=Limit }))
                    {
                        int section=0;
                        const string word="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                        while(!xml.EOF && result.Parts.Count<300)
                        {
                            cancel.ThrowIfCancellationRequested();
                            if(xml.NodeType!=XmlNodeType.Element || xml.LocalName!="p" || xml.NamespaceURI!=word){xml.Read();continue;}
                            var text=new StringBuilder();
                            using(var inner=xml.ReadSubtree())
                            {
                                while(!inner.EOF)
                                {
                                    cancel.ThrowIfCancellationRequested();
                                    if(inner.NodeType==XmlNodeType.Element && inner.LocalName=="t" && inner.NamespaceURI==word)
                                    { string chunk=inner.ReadElementContentAsString(); if(text.Length<3000) text.Append(chunk,0,Math.Min(3000-text.Length,chunk.Length)); }
                                    else inner.Read();
                                }
                            }
                            result.Add("Párrafo "+(++section),text.ToString());
                            xml.Read();
                        }
                    }
                }
            }
            else if(ext==".txt" || ext==".md" || ext==".csv")
            {
                using(var input=new StreamReader(fi.FullName,Encoding.UTF8,true))
                {
                    string line; int number=0;
                    string section=null;
                    var group=new StringBuilder(); int first=1;
                    while((line=input.ReadLine())!=null && number<4000 && result.Parts.Count<300)
                    {
                        cancel.ThrowIfCancellationRequested();
                        number++;
                        if(ext==".csv") { result.Add("Línea CSV "+number,line); continue; }
                        if(ext==".md" && line.StartsWith("#"))
                        {
                            result.Add(section??("Línea "+first),group.ToString()); group.Clear();
                            section=line.Substring(0,Math.Min(100,line.Length)).Trim('#',' ');
                            first=number;
                        }
                        if(group.Length+Math.Min(line.Length,750)>950)
                        {result.Add(section??("Línea "+first),group.ToString());group.Clear();first=number;}
                        group.Append(line,0,Math.Min(line.Length,750)).AppendLine();
                    }
                    result.Add(section??("Línea "+first),group.ToString());
                }
            }
            else throw new InvalidOperationException("Selecciona TXT, Markdown, PDF, DOCX o CSV.");
            if(result.Parts.Count==0) throw new InvalidOperationException("No se encontró texto legible.");
            return result;
        }
        static Reading ReadPdf(FileInfo file,Reading result,CancellationToken cancel)
        {
            string library=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"UglyToad.PdfPig.dll");
            if(!File.Exists(library)) throw new InvalidOperationException("El lector local de PDF no está disponible.");
            using(var process=Process.Start(new ProcessStartInfo
            {
                FileName=Assembly.GetExecutingAssembly().Location,
                Arguments="--pdf-text \""+file.FullName+"\"",
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,StandardOutputEncoding=Encoding.UTF8
            }))
            using(cancel.Register(()=>{try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}))
            {
                var read=process.StandardOutput.ReadToEndAsync();
                if(!process.WaitForExit(12000))
                {
                    try {process.Kill();} catch(InvalidOperationException){}
                    throw new InvalidOperationException("El PDF superó el tiempo de lectura.");
                }
                cancel.ThrowIfCancellationRequested();
                var response=read.GetAwaiter().GetResult();
                if(process.ExitCode!=0) throw new InvalidOperationException("No se pudo extraer texto del PDF local ("+response.Trim()+").");
                foreach(string line in response.Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries))
                {
                    if(line.StartsWith("M\t")) {result.WorkerMetrics=line.Substring(2).Trim();continue;}
                    int split=line.IndexOf('\t');
                    if(split<=0) continue;
                    result.Add("Página "+line.Substring(0,split),Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(split+1).Trim())));
                }
            }
            if(result.Parts.Count==0) throw new InvalidOperationException("El PDF no contiene texto seleccionable. El OCR no está habilitado.");
            return result;
        }
        public static int PdfWorker(string path)
        {
            try
            {
                var elapsed=Stopwatch.StartNew();
                var info=new FileInfo(path);
                if(!info.Exists || info.Length>Limit || !info.Extension.Equals(".pdf",StringComparison.OrdinalIgnoreCase)) return 1;
                var assembly=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"UglyToad.PdfPig.dll"));
                var type=assembly.GetType("UglyToad.PdfPig.PdfDocument",true);
                var open=type.GetMethods().First(m=>m.Name=="Open" && m.GetParameters().Length==2 && m.GetParameters()[0].ParameterType==typeof(string));
                using(var document=(IDisposable)open.Invoke(null,new object[]{info.FullName,null}))
                {
                    int pages=(int)type.GetProperty("NumberOfPages").GetValue(document);
                    var getPage=type.GetMethods().Single(m=>m.Name=="GetPage" && !m.IsGenericMethod);
                    var layout=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"UglyToad.PdfPig.DocumentLayoutAnalysis.dll"))
                        .GetType("UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor");
                    var extractor=layout==null?null:layout.GetMethods().FirstOrDefault(m=>m.Name=="GetText" &&
                        m.GetParameters().Length==2 && m.GetParameters()[1].ParameterType==typeof(bool));
                    for(int i=1;i<=Math.Min(pages,30);i++)
                    {
                        var page=getPage.Invoke(document,new object[]{i});
                        string value=(string)(extractor==null?page.GetType().GetProperty("Text").GetValue(page):extractor.Invoke(null,new object[]{page,false}));
                        if(!String.IsNullOrWhiteSpace(value))
                            Console.WriteLine(i+"\t"+Convert.ToBase64String(Encoding.UTF8.GetBytes(value.Substring(0,Math.Min(value.Length,3000)))));
                    }
                }
                using(var process=Process.GetCurrentProcess())
                    Console.WriteLine(String.Format("M\t{0:0.00} s · CPU {1:0.00}% · RAM {2:0.0} MiB",
                        elapsed.Elapsed.TotalSeconds,100*process.TotalProcessorTime.TotalSeconds/Math.Max(.01,elapsed.Elapsed.TotalSeconds)/Environment.ProcessorCount,
                        process.WorkingSet64/1048576.0));
                return 0;
            }
            catch(Exception e){Console.WriteLine((e.InnerException??e).GetType().Name);return 1;}
        }
    }
}
