using System.Net;
using Keen.VRage.Core.Platform.Http;
using Keen.VRage.Library.Filesystem;

namespace LinuxCompat.Platform;

internal sealed class LinuxHttpClient : IPlatformHttp
{
    private static readonly HttpClient Client = new();

    public Keen.VRage.Library.Threading.Task<(HttpStatusCode, string?)> SendRequestAsync(
        string url,
        IPlatformHttp.Data[] parameters,
        IPlatformHttp.Method method,
        object taskLifetime)
    {
        return SendRequestCoreAsync(url, parameters, method);
    }

    public Keen.VRage.Library.Threading.Task<(HttpStatusCode, string?)> SendFormattedCrashReportAsync(
        string url,
        string jsonBody,
        List<Tuple<string, string>> logs,
        List<Tuple<string, byte[]>> additionalFiles,
        IPlatformHttp.Method method,
        object taskLifetime)
    {
        return SendCrashReportCoreAsync(url, jsonBody, logs, additionalFiles, method);
    }

    public Keen.VRage.Library.Threading.Task<HttpStatusCode> DownloadAsync(
        string url,
        FileHandleWritable file,
        IPlatformHttp.ProgressDelegate? progressCallback,
        object taskLifetime)
    {
        return DownloadCoreAsync(url, file, progressCallback);
    }

    private static async System.Threading.Tasks.Task<(HttpStatusCode, string?)> SendRequestCoreAsync(
        string url,
        IPlatformHttp.Data[] parameters,
        IPlatformHttp.Method method)
    {
        using HttpRequestMessage request = new(ToHttpMethod(method), url);
        foreach (IPlatformHttp.Data parameter in parameters)
        {
            if (parameter.Type == IPlatformHttp.DataType.HttpHeader)
                request.Headers.TryAddWithoutValidation(parameter.Name, parameter.Value?.ToString());
            else if (parameter.Type == IPlatformHttp.DataType.RequestBody)
                request.Content = new StringContent(parameter.Value?.ToString() ?? string.Empty);
        }

        using HttpResponseMessage response = await Client.SendAsync(request).ConfigureAwait(false);
        return (response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private static async System.Threading.Tasks.Task<(HttpStatusCode, string?)> SendCrashReportCoreAsync(
        string url,
        string jsonBody,
        List<Tuple<string, string>> logs,
        List<Tuple<string, byte[]>> additionalFiles,
        IPlatformHttp.Method method)
    {
        using MultipartFormDataContent content = new();
        content.Add(new StringContent(jsonBody), "report");
        foreach (Tuple<string, string> log in logs)
            content.Add(new StringContent(log.Item2), log.Item1, log.Item1);
        foreach (Tuple<string, byte[]> file in additionalFiles)
            content.Add(new ByteArrayContent(file.Item2), file.Item1, file.Item1);

        using HttpRequestMessage request = new(ToHttpMethod(method), url) { Content = content };
        using HttpResponseMessage response = await Client.SendAsync(request).ConfigureAwait(false);
        return (response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private static async System.Threading.Tasks.Task<HttpStatusCode> DownloadCoreAsync(
        string url,
        FileHandleWritable file,
        IPlatformHttp.ProgressDelegate? progressCallback)
    {
        using HttpResponseMessage response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return response.StatusCode;

        file.CreateDirectories();
        await using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using Stream destination = file.Open(FileMode.Create, FileAccess.Write, FileShare.Read);
        await source.CopyToAsync(destination).ConfigureAwait(false);
        progressCallback?.Invoke(destination.Length, response.Content.Headers.ContentLength ?? destination.Length);
        return response.StatusCode;
    }

    private static HttpMethod ToHttpMethod(IPlatformHttp.Method method) => method switch
    {
        IPlatformHttp.Method.Get => HttpMethod.Get,
        IPlatformHttp.Method.Post => HttpMethod.Post,
        IPlatformHttp.Method.Put => HttpMethod.Put,
        IPlatformHttp.Method.Delete => HttpMethod.Delete,
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };
}
