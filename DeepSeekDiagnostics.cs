using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace BaliDesktop
{
    static class DeepSeekDiagnostics
    {
        const string Host="api.deepseek.com";
        public static int Run(string file)
        {
            using(var output=new StreamWriter(file))
            {
                var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BaliDesktop");
                return Probe(output,new CredentialVault(folder)).GetAwaiter().GetResult();
            }
        }
        public static async Task<string> Inspect(CredentialVault vault)
        {
            using(var output=new StringWriter())
            {
                await Probe(output,vault).ConfigureAwait(false);
                return output.ToString().TrimEnd();
            }
        }
        static async Task<int> Probe(TextWriter output,CredentialVault vault)
        {
            output.WriteLine("Destino: GET https://api.deepseek.com/models (sin credencial ni inferencia).");
            output.WriteLine("Framework objetivo: "+(AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName??"sin declarar"));
            output.WriteLine("TLS de System.Net: "+ServicePointManager.SecurityProtocol);
            try
            {
                var dns=Dns.GetHostAddressesAsync(Host);
                if(await Task.WhenAny(dns,Task.Delay(8000)).ConfigureAwait(false)==dns)
                    output.WriteLine("DNS: "+String.Join(", ",(await dns.ConfigureAwait(false)).Select(ip=>ip.ToString())));
                else output.WriteLine("DNS: tiempo de espera agotado.");
            }
            catch(Exception e) { output.WriteLine("DNS: "+Chain(e)); }
            try
            {
                using(var socket=new TcpClient())
                {
                    var connecting=socket.ConnectAsync(Host,443);
                    if(await Task.WhenAny(connecting,Task.Delay(8000)).ConfigureAwait(false)!=connecting)
                        throw new TimeoutException();
                    await connecting.ConfigureAwait(false);
                    output.WriteLine("TCP 443: conectado.");
                    using(var ssl=new SslStream(socket.GetStream()))
                    using(var limit=new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                    using(limit.Token.Register(()=>socket.Close()))
                    {
                        await ssl.AuthenticateAsClientAsync(Host,null,SslProtocols.None,false).ConfigureAwait(false);
                        output.WriteLine("TLS directo con validación de certificado: "+ssl.SslProtocol);
                    }
                }
            }
            catch(Exception e) { output.WriteLine("TCP/TLS directo: "+Chain(e)); }
            int result=1;
            try
            {
                using(var client=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false,UseCookies=false }))
                using(var request=new HttpRequestMessage(HttpMethod.Get,"https://api.deepseek.com/models"))
                using(var limit=new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,limit.Token).ConfigureAwait(false))
                {
                    output.WriteLine("HttpClient: HTTP "+(int)response.StatusCode+" (sin encabezado Authorization).");
                    result=0;
                }
            }
            catch(Exception e) { output.WriteLine("HttpClient: "+Chain(e)); }
            try
            {
                output.WriteLine(vault.Exists && !String.IsNullOrWhiteSpace(vault.Read())?
                    "DPAPI CurrentUser: credencial recuperada en memoria y descartada sin enviarla.":"DPAPI CurrentUser: no hay credencial legible.");
            }
            catch(Exception e) { output.WriteLine("DPAPI: "+Chain(e)); }
            return result;
        }
        internal static string Chain(Exception error)
        {
            var parts=new System.Collections.Generic.List<string>();
            for(var current=error;current!=null && parts.Count<8;current=current.InnerException)
            {
                var web=current as WebException;
                var socket=current as SocketException;
                parts.Add(current.GetType().Name+" 0x"+current.HResult.ToString("X8")+
                    (web==null?"":" "+web.Status)+(socket==null?"":" "+socket.SocketErrorCode));
            }
            return String.Join(" → ",parts);
        }
    }
}
