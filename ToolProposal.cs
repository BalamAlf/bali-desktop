using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace BaliDesktop
{
    static class ToolProposal
    {
        public static ChatContext Context(string question,IntelligenceSettings options)
        {
            if(String.IsNullOrWhiteSpace(question)||MemoryBook.LooksSensitive(question)) throw new InvalidOperationException("Escribe una petición sin datos sensibles.");
            var payload=new ChatContext();
            payload.Messages.Add(new ChatLine("system","Devuelve exclusivamente un arreglo JSON de hasta cuatro objetos. Campos permitidos: id, texto, fecha. IDs: recordar, pendiente, nota, abrir. Fecha local opcional AAAA-MM-DD HH:mm y obligatoria para recordar. Para abrir usa únicamente el nombre descriptivo, jamás rutas ni argumentos. Si no puedes interpretar, devuelve []. Esto solo propone acciones para revisión humana."));
            payload.Messages.Add(new ChatLine("user","Hora local "+DateTime.Now.ToString("yyyy-MM-dd HH:mm")+" | Petición: "+question));
            payload.Budget=8+payload.Messages.Sum(line=>ChatContext.Measure(line));
            if(payload.Budget>options.Context) throw new InvalidOperationException("La petición supera el límite.");
            payload.Categories.Add("Petición explícita de propuesta");
            return payload;
        }
        public static ToolRequest[] Parse(string json,DateTime now)
        {
            if(String.IsNullOrEmpty(json)||json.Length>4800) return new ToolRequest[0];
            try
            {
                var entries=new JavaScriptSerializer{MaxJsonLength=4800,RecursionLimit=8}.DeserializeObject(json) as object[];
                if(entries==null || entries.Length>4) return new ToolRequest[0];
                var result=new List<ToolRequest>();
                foreach(object entry in entries)
                {
                    var fields=entry as IDictionary<string,object>;
                    if(fields==null || fields.Keys.Any(k=>k!="id" && k!="texto" && k!="fecha") ||
                        !fields.ContainsKey("id") || !fields.ContainsKey("texto")) continue;
                    string id=fields["id"] as string, text=fields["texto"] as string;
                    if(!new[]{"recordar","pendiente","nota","abrir"}.Contains(id) ||
                        String.IsNullOrWhiteSpace(text)||text.Length>450||MemoryBook.LooksSensitive(text) ||
                        id=="abrir" && text.IndexOfAny(new[]{'\\','/',':','"',';'})>=0)continue;
                    DateTime? date=null;
                    if(fields.ContainsKey("fecha") && fields["fecha"]!=null)
                    {
                        DateTime parsed;
                        if(!DateTime.TryParseExact(fields["fecha"] as string,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed) ||
                            parsed<=now || parsed>now.AddYears(5))continue;
                        date=parsed;
                    }
                    if(id=="recordar" && !date.HasValue)continue;
                    if(id=="abrir" && date.HasValue)continue;
                    result.Add(new ToolRequest{Id=id,Text=text,Date=date});
                }
                return result.ToArray();
            }
            catch(ArgumentException){return new ToolRequest[0];}
            catch(InvalidOperationException){return new ToolRequest[0];}
        }
    }
}
