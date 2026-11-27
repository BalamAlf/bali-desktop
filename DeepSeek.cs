using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BaliDesktop
{
    [DataContract]
    public sealed class RequestUsage
    {
        [DataMember] public DateTime Date;
        [DataMember] public string Model, Status;
        [DataMember] public int? Input, Output, CacheHit, CacheMiss, Reasoning, Total;
        [DataMember] public long Milliseconds;
        [DataMember] public decimal Reserved;
        [DataMember] public decimal? Estimated;
        public decimal Budgeted { get { return Estimated??Reserved; } }
    }

    static class DeepSeekPrices
    {
        // USD per million tokens at peak, verified 2026-11-26.
        public static decimal Cost(string model,int hit,int miss,int output)
        {
            bool pro=model=="deepseek-v4-pro";
            return (hit*(pro?.044m:.006m)+miss*(pro?1.32m:.30m)+output*(pro?3.96m:1.20m))/1000000m;
        }
    }

    sealed class UsageLedger
    {
        readonly string path;
        public List<RequestUsage> Rows { get; private set; }
        public bool Available { get; private set; }
        public UsageLedger(string folder)
        {
            path=Path.Combine(folder,"consumo-deepseek.json");
            Rows=new List<RequestUsage>();
            try
            {
                if(File.Exists(path)) Rows=LocalJson.Read<List<RequestUsage>>(path)??Rows;
                Available=Rows.All(x=>x!=null && x.Reserved>=0 && (!x.Estimated.HasValue || x.Estimated>=0));
            }
            catch(Exception) { Available=false; }
            if(!Available) Rows.Clear();
        }
        public bool SameMonth(DateTime left,DateTime right)
        {
            var a=left.ToLocalTime(); var b=right.ToLocalTime();
            return a.Year==b.Year && a.Month==b.Month;
        }
        public int Today(DateTime utc) { return Rows.Count(x=>x.Date.ToLocalTime().Date==utc.ToLocalTime().Date); }
        public decimal Month(DateTime utc) { return Rows.Where(x=>SameMonth(x.Date,utc)).Sum(x=>x.Budgeted); }
        public RequestUsage Reserve(IntelligenceSettings options,int inputBound,int maxOutput,DateTime utc)
        {
            if(!Available) throw new ConversationError("Registro de consumo no disponible.");
            if(Today(utc)>=options.Daily) throw new ConversationError("Límite diario alcanzado.");
            decimal estimate=DeepSeekPrices.Cost(options.Model,0,inputBound,maxOutput);
            if(Month(utc)+estimate>options.Monthly) throw new ConversationError("Presupuesto mensual local insuficiente.");
            var row=new RequestUsage { Date=utc,Model=options.Model,Status="Pendiente / consumo desconocido",Reserved=estimate };
            var next=Rows.Where(x=>x.Date>=utc.AddDays(-62)).ToList();
            next.Add(row);
            try { LocalJson.Write(path,next); }
            catch(Exception) { throw new ConversationError("No se pudo guardar la reserva de consumo."); }
            Rows=next;
            return row;
        }
        public void Commit(RequestUsage row)
        {
            if(row.Input.HasValue && row.Output.HasValue && (!row.CacheHit.HasValue || row.CacheHit<=row.Input))
            {
                int hit=row.CacheHit??0;
                row.Estimated=DeepSeekPrices.Cost(row.Model,hit,row.CacheMiss??(row.Input.Value-hit),row.Output.Value);
            }
            try { LocalJson.Write(path,Rows); }
            catch(Exception) { Available=false; }
        }
        public string Report()
        {
            if(!Available) return "Registro de consumo no disponible. Las consultas online quedan bloqueadas.";
            var now=DateTime.UtcNow;
            var month=Rows.Where(x=>SameMonth(x.Date,now)).ToArray();
            var day=month.Where(x=>x.Date.ToLocalTime().Date==now.ToLocalTime().Date).ToArray();
            var latest=Rows.LastOrDefault();
            Func<RequestUsage[],string> totals=list=>
            {
                var known=list.Where(x=>x.Input.HasValue && x.Output.HasValue).ToArray();
                return known.Length==0?"sin tokens informados":
                    String.Format("{0:N0} entrada · {1:N0} salida · {2} caché · {3} razonamiento",
                        known.Sum(x=>(long)x.Input.Value),known.Sum(x=>(long)x.Output.Value),
                        known.Any(x=>x.CacheHit.HasValue)?known.Sum(x=>(long)(x.CacheHit??0)).ToString("N0"):"sin dato",
                        known.Any(x=>x.Reasoning.HasValue)?known.Sum(x=>(long)(x.Reasoning??0)).ToString("N0"):"sin dato");
            };
            return String.Format("Hoy: {0} peticiones · {1}\nMes: {2} peticiones · {3}\nEstimado a tarifa pico USD {4:0.0000} · reservas sin dato USD {5:0.0000}{6}",
                day.Length,totals(day),month.Length,totals(month),month.Sum(x=>x.Estimated??0),
                month.Where(x=>!x.Estimated.HasValue).Sum(x=>x.Reserved),latest==null?"":"\n"+
                String.Join("\n",Rows.Skip(Math.Max(0,Rows.Count-5)).Reverse().Select(x=>x.Date.ToLocalTime().ToString("g")+
                    " · "+x.Model+" · "+x.Status+" · "+x.Milliseconds+" ms")));
        }
    }

    sealed class ConversationError : Exception { public ConversationError(string message):base(message) {} }
    sealed class ProviderUsage
    {
        public int? Input, Output, Hit, Miss, Reasoning, Total;
    }
    interface IStreamingConversationProvider
    {
        Task<ProviderUsage> StreamAsync(string key,IntelligenceSettings settings,IList<ChatLine> messages,
            Action<string> chunk,Action<ProviderUsage> usage,CancellationToken cancel);
    }

    sealed class DeepSeekProvider : IStreamingConversationProvider, IDisposable
    {
        static readonly Uri Endpoint=new Uri("https://api.deepseek.com/chat/completions");
        readonly HttpClient client;
        public DeepSeekProvider() : this(new HttpClientHandler { AllowAutoRedirect=false, UseCookies=false }) {}
        internal DeepSeekProvider(HttpMessageHandler handler)
        {
            client=new HttpClient(handler) { Timeout=System.Threading.Timeout.InfiniteTimeSpan };
        }
        public async Task<ProviderUsage> StreamAsync(string key,IntelligenceSettings settings,IList<ChatLine> messages,
            Action<string> chunk,Action<ProviderUsage> usage,CancellationToken cancel)
        {
            var payload=new Dictionary<string,object> {
                { "model",settings.Model },{ "messages",messages.Select(x=>new { role=x.Role,content=x.Text }).ToArray() },
                { "stream",true },{ "stream_options",new { include_usage=true } },{ "max_tokens",settings.Output },
                { "thinking",new { type="disabled" } }
            };
            if(settings.Model=="deepseek-v4-pro") payload.Add("temperature",settings.Temperature);
            using(var request=new HttpRequestMessage(HttpMethod.Post,Endpoint))
            {
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                request.Content=new StringContent(new JavaScriptSerializer().Serialize(payload),Encoding.UTF8,"application/json");
                HttpResponseMessage response;
                try { response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel).ConfigureAwait(false); }
                catch(HttpRequestException ex) { cancel.ThrowIfCancellationRequested(); throw TransportError(ex); }
                using(response)
                {
                    if(!response.IsSuccessStatusCode) throw new ConversationError(HttpError((int)response.StatusCode));
                    var media=response.Content.Headers.ContentType;
                    if(media==null || !String.Equals(media.MediaType,"text/event-stream",StringComparison.OrdinalIgnoreCase))
                        throw new ConversationError("DeepSeek no devolvió un flujo compatible.");
                    try
                    {
                        using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using(cancel.Register(()=>stream.Dispose()))
                            return await ReadEvents(stream,chunk,usage,cancel).ConfigureAwait(false);
                    }
                    catch(IOException ex)
                    {
                        cancel.ThrowIfCancellationRequested();
                        throw new ConversationError("El flujo se cortó después de recibir HTTP "+(int)response.StatusCode+" · "+DeepSeekDiagnostics.Chain(ex));
                    }
                }
            }
        }

        internal static string HttpError(int code)
        {
            switch(code)
            {
                case 401: return "DeepSeek rechazó la credencial (401).";
                case 402: return "Saldo insuficiente en DeepSeek (402).";
                case 403: return "DeepSeek denegó el acceso (403). Revisa permisos y restricciones de la cuenta o red.";
                case 404: return "DeepSeek no encontró la ruta o el recurso solicitado (404).";
                case 429: return "DeepSeek alcanzó su límite de solicitudes (429).";
                case 408: case 504: return "DeepSeek agotó el tiempo de espera ("+code+").";
                case 500: case 502: case 503: return "DeepSeek está temporalmente indisponible ("+code+").";
                default: return "DeepSeek devolvió un error HTTP "+code+".";
            }
        }
        internal static ConversationError TransportError(HttpRequestException error)
        {
            string reason=null;
            for(Exception current=error;current!=null;current=current.InnerException)
            {
                var web=current as WebException;
                if(web!=null)
                {
                    var http=web.Response as HttpWebResponse;
                    if(http!=null) return new ConversationError(HttpError((int)http.StatusCode));
                    switch(web.Status)
                    {
                        case WebExceptionStatus.SecureChannelFailure: reason="Falló la negociación TLS"; break;
                        case WebExceptionStatus.TrustFailure: reason="Windows no confía en el certificado HTTPS"; break;
                        case WebExceptionStatus.NameResolutionFailure: reason="No se resolvió el DNS de DeepSeek"; break;
                        case WebExceptionStatus.ProxyNameResolutionFailure: reason="No se resolvió el DNS del proxy"; break;
                        case WebExceptionStatus.ConnectFailure: reason="No se estableció la conexión TCP"; break;
                        case WebExceptionStatus.Timeout: reason="Tiempo de espera de red agotado"; break;
                        case WebExceptionStatus.ReceiveFailure: reason="Se interrumpió la recepción"; break;
                    }
                }
                if(reason!=null) continue;
                var socket=current as SocketException;
                if(socket!=null)
                {
                    switch(socket.SocketErrorCode)
                    {
                        case SocketError.HostNotFound: case SocketError.NoData: case SocketError.TryAgain:
                            reason="No se resolvió el DNS de DeepSeek"; break;
                        case SocketError.TimedOut: reason="Tiempo de espera de red agotado"; break;
                        case SocketError.ConnectionRefused: reason="La conexión TCP fue rechazada"; break;
                        default: reason="Falló la conexión de red"; break;
                    }
                }
                else if(current is AuthenticationException) reason="Falló la autenticación TLS del servidor";
            }
            return new ConversationError((reason??"Falló el transporte HTTPS")+" · sin respuesta HTTP · "+DeepSeekDiagnostics.Chain(error));
        }
        static IDictionary<string,object> Child(IDictionary<string,object> row,string key)
        {
            object value; return row!=null && row.TryGetValue(key,out value)?value as IDictionary<string,object>:null;
        }
        static int? Number(IDictionary<string,object> row,string key)
        {
            object value;
            if(row==null || !row.TryGetValue(key,out value) || !(value is int) || (int)value<0 || (int)value>2000000) return null;
            return (int)value;
        }
        internal static async Task<ProviderUsage> ReadEvents(Stream stream,Action<string> chunk,Action<ProviderUsage> usage,CancellationToken cancel)
        {
            var json=new JavaScriptSerializer { MaxJsonLength=262144,RecursionLimit=32 };
            var eventData=new StringBuilder();
            ProviderUsage final=null;
            int read=0, visible=0;
            using(var reader=new StreamReader(stream,Encoding.UTF8,false,4096,true))
            {
                while(true)
                {
                    cancel.ThrowIfCancellationRequested();
                    string line=await reader.ReadLineAsync().ConfigureAwait(false);
                    cancel.ThrowIfCancellationRequested();
                    if(line==null) break;
                    read+=line.Length+1;
                    if(read>2000000 || line.Length>262144) throw new ConversationError("El flujo supera el límite de seguridad local.");
                    if(line.StartsWith("data:",StringComparison.Ordinal))
                    {
                        if(eventData.Length!=0) eventData.Append('\n');
                        eventData.Append(line.Substring(5).TrimStart(' '));
                        continue;
                    }
                    if(line.Length!=0 || eventData.Length==0) continue;
                    string data=eventData.ToString(); eventData.Clear();
                    if(data=="[DONE]") return final;
                    IDictionary<string,object> node;
                    try { node=json.DeserializeObject(data) as IDictionary<string,object>; }
                    catch(Exception) { throw new ConversationError("Flujo de DeepSeek no válido."); }
                    object value;
                    if(node==null || node.ContainsKey("error")) throw new ConversationError("DeepSeek interrumpió el flujo.");
                    var report=Child(node,"usage");
                    if(report!=null)
                    {
                        final=new ProviderUsage { Input=Number(report,"prompt_tokens"),Output=Number(report,"completion_tokens"),
                            Hit=Number(report,"prompt_cache_hit_tokens")??Number(Child(report,"prompt_tokens_details"),"cached_tokens"),
                            Miss=Number(report,"prompt_cache_miss_tokens"),Reasoning=Number(Child(report,"completion_tokens_details"),"reasoning_tokens"),
                            Total=Number(report,"total_tokens") };
                        usage(final);
                    }
                    if(!node.TryGetValue("choices",out value)) continue;
                    var choices=value as object[];
                    if(choices==null || choices.Length==0) continue;
                    var delta=Child(choices[0] as IDictionary<string,object>,"delta");
                    if(delta==null || !delta.TryGetValue("content",out value)) continue;
                    var text=value as string;
                    if(String.IsNullOrEmpty(text) || visible>=6000) continue;
                    if(text.Length>6000-visible) text=text.Substring(0,6000-visible);
                    visible+=text.Length;
                    chunk(text);
                }
            }
            throw new ConversationError("DeepSeek cerró el flujo antes de completarlo.");
        }
        public void Dispose() { client.Dispose(); }
    }

    sealed class ConversationService
    {
        readonly UsageLedger ledger;
        readonly IStreamingConversationProvider provider;
        int active;
        public ConversationService(UsageLedger ledger,IStreamingConversationProvider provider)
        { this.ledger=ledger; this.provider=provider; }
        public async Task Query(string key,IntelligenceSettings settings,ChatContext context,bool diagnostic,
            Action<string> chunk,CancellationToken cancel)
        {
            if(!settings.Enabled || String.IsNullOrEmpty(key)) throw new ConversationError("DeepSeek está desactivado o falta la credencial.");
            if(!diagnostic && !settings.Verified) throw new ConversationError("Autoriza primero la prueba controlada en Inteligencia.");
            if(Interlocked.CompareExchange(ref active,1,0)!=0) throw new ConversationError("Ya hay un turno en curso.");
            RequestUsage row=null;
            var clock=Stopwatch.StartNew();
            ProviderUsage seen=null;
            try
            {
                cancel.ThrowIfCancellationRequested();
                row=ledger.Reserve(settings,context.Budget,settings.Output,DateTime.UtcNow);
                using(var limit=CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    limit.CancelAfter(TimeSpan.FromSeconds(settings.Timeout));
                    try
                    {
                        seen=await provider.StreamAsync(key,settings,context.Messages,chunk,report=>seen=report,limit.Token)??seen;
                        row.Status="Completada";
                    }
                    catch(Exception)
                    {
                        if(!limit.IsCancellationRequested) throw;
                        throw new ConversationError(cancel.IsCancellationRequested?"Solicitud cancelada.":"Tiempo de espera agotado.");
                    }
                }
            }
            catch(ConversationError ex) { if(row!=null) row.Status=ex.Message; throw; }
            catch(OperationCanceledException)
            { if(row!=null) row.Status="Solicitud cancelada."; throw new ConversationError("Solicitud cancelada."); }
            catch(Exception)
            {
                if(row!=null) row.Status="Error de transporte o respuesta";
                throw new ConversationError("Conexión interrumpida. Los comandos locales siguen disponibles.");
            }
            finally
            {
                try
                {
                    if(row!=null)
                    {
                        row.Milliseconds=clock.ElapsedMilliseconds;
                        if(seen!=null) { row.Input=seen.Input; row.Output=seen.Output; row.CacheHit=seen.Hit; row.CacheMiss=seen.Miss;
                            row.Reasoning=seen.Reasoning; row.Total=seen.Total; }
                        ledger.Commit(row);
                    }
                }
                finally { Volatile.Write(ref active,0); }
            }
        }
    }
}
