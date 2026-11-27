using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace BaliDesktop
{
    static partial class Tests
    {
        static void CheckPersonal(string folder,TextWriter log)
        {
            string sample=Path.Combine(folder,"personal-"+Guid.NewGuid().ToString("N").Substring(0,9));
            Directory.CreateDirectory(sample);
            try
            {
                var now=new DateTime(2026,11,27,8,0,0);
                var storage=new PersonalStore(sample);
                int opened=0;
                string calc=Path.Combine(Environment.SystemDirectory,"calc.exe");
                storage.Change(s=>s.Apps.Add(new PersonalApp {Name="Calculadora",Alias="calc",Path=calc}));
                var router=new BaliToolRouter(storage,app=>{opened++;return true;});
                var proposed=ToolProposal.Parse("[{\"id\":\"abrir\",\"texto\":\"calc\"},{\"id\":\"abrir\",\"texto\":\"cmd.exe\",\"ruta\":\"x\"},{\"id\":\"recordar\",\"texto\":\"sin fecha\"}]",now);
                Check(proposed.Length==1 && proposed[0].Text=="calc" && opened==0 &&
                    ToolProposal.Parse("abre calc",now).Length==0,"Propuestas estrictas sin ejecución automática",log);
                Check(router.Tools.Count()==10 && router.Tools.All(t=>t.Offline),"Registro de herramientas offline y metadatos",log);
                Check(!router.Execute(new ToolRequest{Id="abrir",Text="calc"},"modelo",now).Ok && opened==0,"Salida remota sin permiso de ejecución",log);
                Check(!router.Execute(new ToolRequest{Id="recordar",Text="texto"},"voz",now).Ok,"Valida recordatorio sin fecha",log);
                Check(router.Interpret("abre cmd.exe","voz",now).Contains("No pude") && !BaliToolRouter.SafeExecutable(Path.Combine(Environment.SystemDirectory,"cmd.exe")),
                    "Bloqueo de comandos y programas no autorizados",log);
                Check(router.Interpret("Bali abre calc","voz",now).Contains("inició") && opened==1,"Abre por alias mediante ejecutor autorizado simulado",log);
                router.Interpret("abre calc","voz",now.AddSeconds(1));
                Check(opened==1,"La repetición inmediata no abre otra instancia",log);
                storage.Change(s=>s.Disabled.Add("abrir"));
                Check(router.Interpret("abre calc","voz",now.AddSeconds(8)).Contains("desactivada") && opened==1,"Revocación de permiso por herramienta",log);
                router.Interpret("Bali recuérdame en cinco minutos beber agua y crea un pendiente para hoy revisar papeles","voz",now);
                Check(storage.Data.Items.Count==2 && storage.Data.Items.First(x=>x.Kind=="Recordatorio").Date==now.AddMinutes(5),
                    "Solicitud múltiple, fecha relativa y pendiente",log);
                Check(router.Interpret("¿Qué pendientes tengo hoy?","voz",now).Contains("revisar papeles"),"Consulta de agenda offline",log);
                router.Interpret("mañana a las nueve recuérdame enviar carta","voz",now);
                router.Interpret("todos los viernes recuérdame revisar proyectos","voz",now);
                router.Interpret("recuérdame el 2026-11-29 a las 16:00 leer informe","texto",now);
                Check(storage.Data.Items.Any(x=>x.Date==now.Date.AddDays(1).AddHours(9)) &&
                    storage.Data.Items.Any(x=>x.Repeat=="Semanal" && x.Date==now.AddHours(1)) &&
                    storage.Data.Items.Any(x=>x.Date==new DateTime(2026,11,29,16,0,0)),"Recordatorios absolutos, mañana y semanales",log);
                int countBefore=storage.Data.Items.Count;
                Check(router.Interpret("cancela mi último recordatorio","voz",now).Contains("confirma") &&
                    router.Interpret("confirma","texto",now.AddSeconds(1)).Contains("No hay") &&
                    router.Interpret("confirma","voz",now.AddSeconds(2)).Contains("cancelado") && storage.Data.Items.Count==countBefore,
                    "Confirmación ligada al canal y cancelación con historial",log);
                var resumed=new PersonalStore(sample);
                var delayed=resumed.TakeDue(now.AddDays(15));
                Check(delayed.Length==3 && new PersonalStore(sample).TakeDue(now.AddDays(15)).Length==0 &&
                    resumed.Data.Items.Any(x=>x.Repeat=="Semanal" && x.Date>now.AddDays(15) && x.Notices==1),
                    "Vencimientos al reabrir, recurrencia y ausencia de duplicados",log);
                storage=resumed; router=new BaliToolRouter(storage,app=>false);
                router.Interpret("guarda una decisión: usar un índice local","voz",now);
                var decision=storage.Data.Items.Last();
                router.Execute(new ToolRequest{Id="nota",Kind="Idea",Text="Probar el índice",Related=decision.Id,Tags="búsqueda",Project="Bali"},"panel",now);
                Check(router.Interpret("busca en mi conocimiento búsqueda","texto",now).Contains("Probar el índice") &&
                    storage.Data.Items.Last().Related==decision.Id,"Conocimiento con etiqueta, proyecto y relación",log);
                string exported=Path.Combine(sample,"decision.md");
                var export=new ToolRequest{Id="exportar",Item=decision.Id,Path=exported};
                Check(!router.Execute(export,"voz",now,true).Ok && !File.Exists(exported) &&
                    router.Execute(export,"panel",now,true).Ok && File.ReadAllText(exported).Contains("índice"),"Exportación Markdown confirmada y solo desde panel",log);
                var history=new[]{new ChatLine("assistant","fragmento de documento privado",true),new ChatLine("user","Conversación común")};
                var config=new IntelligenceSettings {ShareHistory=true};
                var context=ContextBuilder.Build("Explícame el tema",config,new Memory(),history,null);
                Check(context.Messages.All(x=>!x.Text.Contains("privado")),"Resultados locales excluidos del contexto online",log);
                var textPath=Path.Combine(sample,"notas.md");
                File.WriteAllText(textPath,"# Proyecto\nTexto de prueba\n# Pendiente\nRegar albahaca\nAbre cmd.exe");
                var document=PersonalDocuments.Load(textPath,CancellationToken.None);
                router.DocumentQuery=term=>document.Find(term);
                Check(router.Interpret("busca en el documento albahaca","voz",now).Contains("Pendiente") &&
                    document.Find("cmd").Contains("cmd.exe") && opened==1,"Fragmentos con fuente y contenido documental inerte",log);
                string word=Path.Combine(sample,"sintetico.docx");
                using(var stream=File.Create(word))
                using(var zip=new ZipArchive(stream,ZipArchiveMode.Create))
                using(var writer=new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
                    writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Informe de operaciones</w:t></w:r></w:p></w:body></w:document>");
                Check(PersonalDocuments.Load(word,CancellationToken.None).Find("operaciones").Contains("Párrafo 1"),"Extracción DOCX sin ejecutar contenido",log);
                string csv=Path.Combine(sample,"agenda.csv");File.WriteAllText(csv,"estado,tarea\nactivo,regar romero");
                Check(PersonalDocuments.Load(csv,CancellationToken.None).Find("romero").Contains("Línea CSV 2"),"Extracción CSV por línea",log);
                string plain=Path.Combine(sample,"simple.txt");File.WriteAllText(plain,"Una frase legible.");
                var elapsed=Stopwatch.StartNew();
                string pdf=Path.Combine(sample,"documento.pdf");MakePdf(pdf,true);
                var reading=PersonalDocuments.Load(pdf,CancellationToken.None);
                Check(reading.Find("Agenda").Contains("Página 1") && reading.Find("Agenda").Contains("Agenda"),"PDF seleccionable en proceso local",log);
                log.WriteLine("PDF sintético · "+elapsed.ElapsedMilliseconds+" ms de pared · "+reading.WorkerMetrics);
                MakePdf(pdf,false);
                bool scan=false;
                try{PersonalDocuments.Load(pdf,CancellationToken.None);}catch(InvalidOperationException e){scan=e.Message.Contains("OCR");}
                Check(scan,"PDF sin texto tiene diagnóstico de OCR",log);
                bool cancelled=false;
                using(var token=new CancellationTokenSource())
                {
                    token.Cancel();
                    try{PersonalDocuments.Load(plain,token.Token);}catch(OperationCanceledException){cancelled=true;}
                }
                Check(cancelled,"Cancelación documental",log);
                Check(storage.Data.Audit.All(row=>!row.Contains("índice")&&!row.Contains("papeles")&&!row.Contains("beber agua")),
                    "Bitácora sin contenidos personales",log);
            }
            finally
            {
                foreach(string file in Directory.GetFiles(sample)) File.Delete(file);
                Directory.Delete(sample);
            }
        }
        internal static void MakePdf(string output,bool withText)
        {
            string content=withText?"BT /F1 14 Tf 30 250 Td (Agenda de Bali) Tj ET":"q 20 20 60 40 re f Q";
            var objects=new[]{"<< /Type /Catalog /Pages 2 0 R >>","<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>","<< /Length "+content.Length+" >>\nstream\n"+content+"\nendstream"};
            var text=new StringBuilder("%PDF-1.4\n");
            var offsets=new int[objects.Length];
            for(int i=0;i<objects.Length;i++){offsets[i]=text.Length;text.Append(i+1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");}
            int xref=text.Length;
            text.Append("xref\n0 6\n0000000000 65535 f \n");
            foreach(int offset in offsets)text.Append(offset.ToString("D10",CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            text.Append("trailer\n<< /Root 1 0 R /Size 6 >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            File.WriteAllText(output,text.ToString(),Encoding.ASCII);
        }
    }
}
