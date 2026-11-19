using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>
/// Servico 'accounts' falso rodando em Kestrel real (porta livre em 127.0.0.1). Um servidor de
/// verdade e necessario porque a propagacao do <c>traceparent</c> e o span de saida sao feitos
/// pela pilha HTTP do .NET (SocketsHttpHandler); um handler falso no HttpClient pularia isso.
/// Responde 200 para qualquer conta/cartao e registra os headers W3C recebidos.
/// </summary>
public sealed class StubAccountsServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public StubAccountsServer()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        _app = builder.Build();
        _app.MapGet("/accounts/{id}", (HttpRequest request) => Record(request));
        _app.MapGet("/credit-cards/{id}", (HttpRequest request) => Record(request));
        _app.StartAsync().GetAwaiter().GetResult();

        BaseUrl = _app.Urls.Single();
    }

    public string BaseUrl { get; }

    /// <summary>Valores do header <c>traceparent</c> recebidos, na ordem de chegada.</summary>
    public ConcurrentQueue<string> ReceivedTraceParents { get; } = new();

    private IResult Record(HttpRequest request)
    {
        ReceivedTraceParents.Enqueue(request.Headers["traceparent"].ToString());
        return Results.Ok();
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
