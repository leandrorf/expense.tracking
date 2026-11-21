using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Expense.Tracking.Transactions.Application.Abstractions;
using Expense.Tracking.Transactions.Domain.Imports;
using Expense.Tracking.Transactions.Domain.Transactions;
using Expense.Tracking.Transactions.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Expense.Tracking.Transactions.IntegrationTests;

/// <summary>IMP-4: envio do arquivo e previa (POST /imports, POST /imports/csv/proposal, GET /imports/{id}).</summary>
public sealed class ImportsApiTests(ImportsApiTests.Factory factory) : IClassFixture<ImportsApiTests.Factory>
{
    /// <summary>Marcador escrito nos arquivos para provar que o conteudo nao vaza para log nem resposta.</summary>
    private const string Secret = "SEGREDO-DO-EXTRATO-7731";

    private readonly Factory _factory = factory;

    public sealed class Factory : TransactionsApiFactory
    {
        public CapturingLoggerProvider Logs { get; } = new();

        protected override void ConfigureTestServices(IServiceCollection services) =>
            services.AddSingleton<ILoggerProvider>(Logs);
    }

    private HttpClient ClientFor(Guid userId, string? scope = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, userId.ToString());
        if (scope is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeHeader, scope);
        }

        return client;
    }

    private static byte[] Sample(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Imports", "Samples", name));

    private static MultipartFormDataContent Upload(byte[] content, string fileName, Guid? accountId,
        Guid? creditCardId = null, string? paymentMethod = null, string? mapping = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (accountId is not null)
        {
            form.Add(new StringContent(accountId.ToString()!), "accountId");
        }

        if (creditCardId is not null)
        {
            form.Add(new StringContent(creditCardId.ToString()!), "creditCardId");
        }

        if (paymentMethod is not null)
        {
            form.Add(new StringContent(paymentMethod), "paymentMethod");
        }

        if (mapping is not null)
        {
            form.Add(new StringContent(mapping), "mapping");
        }

        return form;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(status == response.StatusCode, $"{(int)response.StatusCode}: {body}");
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Upload_Ofx1BankStatement_ReturnsPendingBatchWithLines()
    {
        var client = ClientFor(Guid.NewGuid());
        var accountId = Guid.NewGuid();

        var response = await client.PostAsync("/imports",
            Upload(Sample("extrato-conta-ofx1.ofx"), "extrato.ofx", accountId, paymentMethod: "Pix"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var batch = await Json(response);
        Assert.Equal($"/imports/{batch.GetProperty("id").GetGuid()}", response.Headers.Location!.OriginalString);
        Assert.Equal("Pending", batch.GetProperty("status").GetString());
        Assert.Equal("Ofx", batch.GetProperty("format").GetString());
        Assert.Equal("BankAccount", batch.GetProperty("kind").GetString());
        Assert.Equal("Pix", batch.GetProperty("defaultPaymentMethod").GetString());
        Assert.Equal(accountId, batch.GetProperty("accountId").GetGuid());

        var summary = batch.GetProperty("summary");
        Assert.Equal(5, summary.GetProperty("total").GetInt32());
        Assert.Equal(5, summary.GetProperty("new").GetInt32());
        Assert.Equal(5, summary.GetProperty("included").GetInt32());

        var lines = batch.GetProperty("lines").EnumerateArray().ToList();
        var bakery = lines[0];
        Assert.Equal("New", bakery.GetProperty("status").GetString());
        Assert.True(bakery.GetProperty("included").GetBoolean());
        Assert.Equal("Expense", bakery.GetProperty("type").GetString());
        Assert.Equal(18.50m, bakery.GetProperty("amount").GetDecimal());
        Assert.Equal("2026-10-02", bakery.GetProperty("occurredOn").GetString());
        Assert.Equal("Pix", bakery.GetProperty("paymentMethod").GetString());
        Assert.Equal(JsonValueKind.Null, bakery.GetProperty("category").ValueKind);
        Assert.False(bakery.TryGetProperty("externalId", out _));

        var salary = lines[2];
        Assert.Equal("Income", salary.GetProperty("type").GetString());
        Assert.True(salary.GetProperty("included").GetBoolean());
    }

    [Fact]
    public async Task Upload_Ofx2CardStatement_UsesCreditCardAndLeavesThePaymentOut()
    {
        var client = ClientFor(Guid.NewGuid());
        var cardId = Guid.NewGuid();

        var response = await client.PostAsync("/imports",
            Upload(Sample("fatura-cartao-ofx2.ofx"), "fatura.ofx", Guid.NewGuid(), creditCardId: cardId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var batch = await Json(response);
        Assert.Equal("CreditCard", batch.GetProperty("kind").GetString());
        Assert.Equal(cardId, batch.GetProperty("creditCardId").GetGuid());

        var lines = batch.GetProperty("lines").EnumerateArray().ToList();
        Assert.All(lines, l => Assert.Equal("CreditCard", l.GetProperty("paymentMethod").GetString()));
        var payment = Assert.Single(lines, l => l.GetProperty("type").GetString() == "Income");
        Assert.Equal("ExcludedByDefault", payment.GetProperty("status").GetString());
        Assert.False(payment.GetProperty("included").GetBoolean());
        Assert.Equal(3, batch.GetProperty("summary").GetProperty("included").GetInt32());
    }

    [Fact]
    public async Task Upload_LinesAlreadyLaunchedInTheAccount_AreDuplicatesAndNotIncluded()
    {
        var owner = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await SeedImportedTransactionAsync(owner, accountId, externalId: "202610050001");
        // A mesma chave em outra conta nao conta como duplicada.
        await SeedImportedTransactionAsync(owner, Guid.NewGuid(), externalId: "202610020001");

        var response = await ClientFor(owner).PostAsync("/imports",
            Upload(Sample("extrato-conta-ofx1.ofx"), "extrato.ofx", accountId, paymentMethod: "Pix"));

        var batch = await Json(response);
        var lines = batch.GetProperty("lines").EnumerateArray().ToList();
        var pix = lines[1];
        Assert.Equal("Duplicate", pix.GetProperty("status").GetString());
        Assert.False(pix.GetProperty("included").GetBoolean());
        Assert.Equal("New", lines[0].GetProperty("status").GetString());
        Assert.Equal(1, batch.GetProperty("summary").GetProperty("duplicate").GetInt32());
    }

    [Fact]
    public async Task Upload_CsvWithMapping_MarksInvalidLinesAndKeepsTheRest()
    {
        var mapping = """{"dateColumn":0,"descriptionColumn":1,"amountColumn":2,"hasHeader":true,"decimalSeparator":","}""";

        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(Sample("extrato-conta.csv"), "extrato.csv", Guid.NewGuid(), paymentMethod: "DebitCard", mapping: mapping));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var batch = await Json(response);
        Assert.Equal("Csv", batch.GetProperty("format").GetString());
        var summary = batch.GetProperty("summary");
        Assert.Equal(6, summary.GetProperty("total").GetInt32());
        Assert.Equal(2, summary.GetProperty("invalid").GetInt32());
        Assert.Equal(4, summary.GetProperty("new").GetInt32());

        var invalid = batch.GetProperty("lines").EnumerateArray().First(l => l.GetProperty("status").GetString() == "Invalid");
        Assert.Equal(2, invalid.GetProperty("number").GetInt32());
        Assert.False(string.IsNullOrEmpty(invalid.GetProperty("problem").GetString()));
    }

    [Fact]
    public async Task Upload_CsvWithCard_IsACardStatement()
    {
        var mapping = """{"dateColumn":0,"descriptionColumn":1,"amountColumn":2,"hasHeader":true,"decimalSeparator":".","invertSign":true}""";

        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(Sample("fatura-cartao.csv"), "fatura.csv", Guid.NewGuid(), creditCardId: Guid.NewGuid(), mapping: mapping));

        var batch = await Json(response);
        Assert.Equal("CreditCard", batch.GetProperty("kind").GetString());
        Assert.Equal(1, batch.GetProperty("summary").GetProperty("excludedByDefault").GetInt32());
    }

    [Fact]
    public async Task Upload_CsvWithoutMapping_AsksForIt()
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(Sample("extrato-conta.csv"), "extrato.csv", Guid.NewGuid(), paymentMethod: "Pix"));

        await AssertProblem(response, HttpStatusCode.BadRequest, "import.mapping_required");
    }

    [Fact]
    public async Task Upload_CsvWithMalformedMappingJson_IsRejected()
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(Sample("extrato-conta.csv"), "extrato.csv", Guid.NewGuid(), paymentMethod: "Pix", mapping: "{nao e json"));

        await AssertProblem(response, HttpStatusCode.BadRequest, "import.mapping_invalid");
    }

    [Fact]
    public async Task Proposal_ForCsv_ReturnsMappingAndPreviewWithoutStoringAnything()
    {
        var before = await CountBatchesAsync();

        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports/csv/proposal",
            Upload(Sample("extrato-conta.csv"), "extrato.csv", accountId: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proposal = await Json(response);
        var mapping = proposal.GetProperty("mapping");
        Assert.Equal(0, mapping.GetProperty("dateColumn").GetInt32());
        Assert.Equal(2, mapping.GetProperty("amountColumn").GetInt32());
        Assert.Equal(",", mapping.GetProperty("decimalSeparator").GetString());
        Assert.Equal("Data", proposal.GetProperty("headers")[0].GetString());
        Assert.Equal(5, proposal.GetProperty("preview").GetArrayLength());

        Assert.Equal(before, await CountBatchesAsync());
    }

    [Fact]
    public async Task Proposal_ForOfx_IsRejected()
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports/csv/proposal",
            Upload(Sample("extrato-conta-ofx1.ofx"), "extrato.ofx", accountId: null));

        await AssertProblem(response, HttpStatusCode.BadRequest, "import.format_invalid");
    }

    [Fact]
    public async Task Upload_CardStatementWithoutCard_IsRejected()
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(Sample("fatura-cartao-ofx2.ofx"), "fatura.ofx", Guid.NewGuid(), paymentMethod: "Pix"));

        await AssertProblem(response, HttpStatusCode.BadRequest, "import.card_required");
    }

    [Theory]
    [InlineData(true, null, "import.card_not_allowed")]
    [InlineData(false, null, "import.payment_method_required")]
    [InlineData(false, "CreditCard", "import.payment_method_invalid")]
    [InlineData(false, "Cheque", "transaction.payment_method_invalid")]
    public async Task Upload_BankStatementWithWrongCardOrMethod_IsRejected(bool withCard, string? method, string code)
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(Sample("extrato-conta-ofx1.ofx"), "extrato.ofx", Guid.NewGuid(),
                creditCardId: withCard ? Guid.NewGuid() : null, paymentMethod: method));

        await AssertProblem(response, HttpStatusCode.BadRequest, code);
    }

    [Fact]
    public async Task Upload_AccountOfAnotherUser_ReturnsNotFound()
    {
        var accountId = Guid.NewGuid();
        _factory.AccountsClient.InvalidAccounts.Add(accountId);

        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(Sample("extrato-conta-ofx1.ofx"), "extrato.ofx", accountId, paymentMethod: "Pix"));

        await AssertProblem(response, HttpStatusCode.NotFound, "account.not_found");
    }

    [Fact]
    public async Task Upload_CardOfAnotherUser_ReturnsNotFound()
    {
        var cardId = Guid.NewGuid();
        _factory.AccountsClient.InvalidCards.Add(cardId);

        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(Sample("fatura-cartao-ofx2.ofx"), "fatura.ofx", Guid.NewGuid(), creditCardId: cardId));

        await AssertProblem(response, HttpStatusCode.NotFound, "card.not_found");
    }

    [Fact]
    public async Task Upload_WithoutAccount_IsRejected()
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(Sample("extrato-conta-ofx1.ofx"), "extrato.ofx", accountId: null, paymentMethod: "Pix"));

        await AssertProblem(response, HttpStatusCode.BadRequest, "import.account_required");
    }

    [Fact]
    public async Task Upload_FileAbove5Mb_IsRejectedWithStableCode()
    {
        var big = new byte[ImportBatch.MaxFileBytes + 1];
        Array.Fill(big, (byte)'a');

        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(big, "extrato.ofx", Guid.NewGuid(), paymentMethod: "Pix"));

        await AssertProblem(response, HttpStatusCode.BadRequest, "import.file_too_large");
    }

    [Fact]
    public async Task Upload_MoreThan5000Lines_IsRejectedWithStableCode()
    {
        var csv = new StringBuilder("Data;Descricao;Valor\n");
        for (var i = 0; i < ImportBatch.MaxLines + 1; i++)
        {
            csv.Append("01/10/2026;Linha ").Append(i).Append(";-1,00\n");
        }

        var mapping = """{"dateColumn":0,"descriptionColumn":1,"amountColumn":2,"hasHeader":true}""";
        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(Encoding.UTF8.GetBytes(csv.ToString()), "grande.csv", Guid.NewGuid(), paymentMethod: "Pix", mapping: mapping));

        await AssertProblem(response, HttpStatusCode.BadRequest, "import.too_many_lines");
    }

    [Theory]
    [InlineData("extrato.pdf")]
    [InlineData("extrato.xlsx")]
    [InlineData("extrato")]
    public async Task Upload_FormatNotAccepted_IsRejected(string fileName)
    {
        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(Sample("extrato-conta-ofx1.ofx"), fileName, Guid.NewGuid(), paymentMethod: "Pix"));

        await AssertProblem(response, HttpStatusCode.BadRequest, "import.format_invalid");
    }

    [Fact]
    public async Task Upload_ContentThatIsNotOfx_IsRejectedWithoutLeakingContentToResponseOrLogs()
    {
        var content = Encoding.UTF8.GetBytes($"<html><body>{Secret} 123.456.789-00</body></html>");

        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(content, "extrato.ofx", Guid.NewGuid(), paymentMethod: "Pix"));

        await AssertProblem(response, HttpStatusCode.BadRequest, "import.format_invalid");
        Assert.DoesNotContain(Secret, await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(_factory.Logs.Messages, m => m.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upload_ValidFile_DoesNotWriteItsContentToLogs()
    {
        var ofx = Encoding.Latin1.GetString(Sample("extrato-conta-ofx1.ofx")).Replace("PADARIA", Secret);

        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(Encoding.Latin1.GetBytes(ofx), "extrato.ofx", Guid.NewGuid(), paymentMethod: "Pix"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotEmpty(_factory.Logs.Messages); // o log da request foi capturado; so nao tem o conteudo
        Assert.DoesNotContain(_factory.Logs.Messages, m => m.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upload_WithoutFile_IsRejected()
    {
        var form = new MultipartFormDataContent { { new StringContent(Guid.NewGuid().ToString()), "accountId" } };

        var response = await ClientFor(Guid.NewGuid()).PostAsync("/imports", form);

        await AssertProblem(response, HttpStatusCode.BadRequest, "import.file_required");
    }

    [Fact]
    public async Task Upload_WithReadOnlyScope_IsForbidden()
    {
        var response = await ClientFor(Guid.NewGuid(), scope: "expenses.read").PostAsync("/imports",
            Upload(Sample("extrato-conta-ofx1.ofx"), "extrato.ofx", Guid.NewGuid(), paymentMethod: "Pix"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WithoutToken_IsUnauthorized()
    {
        var response = await _factory.CreateClient().PostAsync("/imports",
            Upload(Sample("extrato-conta-ofx1.ofx"), "extrato.ofx", Guid.NewGuid(), paymentMethod: "Pix"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_OwnBatch_ReturnsItAndAnotherUsersBatchIsNotFound()
    {
        var owner = Guid.NewGuid();
        var created = await Json(await ClientFor(owner).PostAsync("/imports",
            Upload(Sample("extrato-conta-ofx1.ofx"), "extrato.ofx", Guid.NewGuid(), paymentMethod: "Pix")));
        var id = created.GetProperty("id").GetGuid();

        var own = await ClientFor(owner, scope: "expenses.read").GetAsync($"/imports/{id}");
        var other = await ClientFor(Guid.NewGuid()).GetAsync($"/imports/{id}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(5, (await Json(own)).GetProperty("lines").GetArrayLength());
        await AssertProblem(other, HttpStatusCode.NotFound, "import.not_found");
    }

    [Fact]
    public async Task ExpiredPendingBatch_IsNotFound_AndCleanupDeletesIt()
    {
        var owner = Guid.NewGuid();
        var batch = ImportBatch.Create(owner, Guid.NewGuid(), null, PaymentMethod.Pix, ImportFormat.Ofx,
            new ParsedStatement(StatementKind.BankAccount, "BRL",
                [StatementLine.Valid(1, new DateOnly(2026, 1, 1), -1m, "X", "F1")]),
            new HashSet<string>(), DateTimeOffset.UtcNow - ImportBatch.PendingLifetime - TimeSpan.FromMinutes(1)).Value;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var batches = scope.ServiceProvider.GetRequiredService<IImportBatchRepository>();
            await batches.AddAsync(batch, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var response = await ClientFor(owner).GetAsync($"/imports/{batch.Id}");
        await AssertProblem(response, HttpStatusCode.NotFound, "import.not_found");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var deleted = await scope.ServiceProvider.GetRequiredService<IImportBatchRepository>()
                .DeleteExpiredAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.True(deleted >= 1);
            Assert.False(scope.ServiceProvider.GetRequiredService<TransactionsDbContext>().ImportBatches.Any(b => b.Id == batch.Id));
        }
    }

    private async Task<int> CountBatchesAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<TransactionsDbContext>().ImportBatches.Count();
    }

    private async Task SeedImportedTransactionAsync(Guid owner, Guid accountId, string externalId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();
        var transaction = Transaction.Create(owner, accountId, TransactionType.Expense, PaymentMethod.Pix, null,
            "Importado antes", "Mercado", 120m, "BRL", new DateOnly(2026, 10, 5), null, DateTimeOffset.UtcNow).Value;
        db.Transactions.Add(transaction);
        db.Entry(transaction).Property(t => t.ExternalId).CurrentValue = externalId;
        await db.SaveChangesAsync();
    }
}

/// <summary>Guarda as mensagens de log da aplicacao para provar que o conteudo do arquivo nao vaza.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            messages.Enqueue(formatter(state, exception) + " " + exception);
    }
}
