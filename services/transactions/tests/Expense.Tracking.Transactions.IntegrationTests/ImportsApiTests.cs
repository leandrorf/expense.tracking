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

    // ---------- IMP-5: confirmar ----------

    /// <summary>Envia o extrato OFX sintetico (5 linhas: 4 gastos e 1 salario) e devolve o lote.</summary>
    private async Task<(HttpClient Client, Guid AccountId, JsonElement Batch)> PreviewBankStatementAsync(Guid? owner = null)
    {
        var client = ClientFor(owner ?? Guid.NewGuid());
        var accountId = Guid.NewGuid();
        var response = await client.PostAsync("/imports",
            Upload(Sample("extrato-conta-ofx1.ofx"), "extrato.ofx", accountId, paymentMethod: "Pix"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (client, accountId, await Json(response));
    }

    private static object AllWithCategory(JsonElement batch, string category = "Importados") => new
    {
        lines = batch.GetProperty("lines").EnumerateArray()
            .Select(l => new { id = l.GetProperty("id").GetGuid(), category })
            .ToArray(),
    };

    private async Task<List<JsonElement>> TransactionsOfAsync(HttpClient client, Guid accountId) =>
        (await client.GetFromJsonAsync<JsonElement>($"/transactions?accountId={accountId}"))!.EnumerateArray().ToList();

    [Fact]
    public async Task Confirm_CreatesTheTransactionsAndOneEventEach()
    {
        var (client, accountId, batch) = await PreviewBankStatementAsync();
        var id = batch.GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync($"/imports/{id}/confirm", AllWithCategory(batch, "Mercado"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var confirmed = await Json(response);
        Assert.Equal("Confirmed", confirmed.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, confirmed.GetProperty("confirmedAtUtc").ValueKind);
        Assert.Equal(5, confirmed.GetProperty("summary").GetProperty("imported").GetInt32());

        var created = await TransactionsOfAsync(client, accountId);
        Assert.Equal(5, created.Count);
        Assert.All(created, t => Assert.Equal("Mercado", t.GetProperty("category").GetString()));
        Assert.Equal(1, created.Count(t => t.GetProperty("type").GetString() == "Income"));
        Assert.Contains(created, t => t.GetProperty("description").GetString() == "PADARIA SÃO JOÃO"
            && t.GetProperty("amount").GetDecimal() == 18.50m && t.GetProperty("occurredOn").GetString() == "2026-10-02");

        var lineTransactionIds = confirmed.GetProperty("lines").EnumerateArray()
            .Select(l => l.GetProperty("transactionId").GetGuid()).ToHashSet();
        Assert.Equal(created.Select(t => t.GetProperty("id").GetGuid()).ToHashSet(), lineTransactionIds);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();
        var events = db.OutboxEvents.Where(e => lineTransactionIds.Contains(e.AggregateId)).ToList();
        Assert.Equal(5, events.Count);
        Assert.All(events, e => Assert.Equal("TransactionRegistered", e.EventType));
        Assert.All(db.Transactions.Where(t => lineTransactionIds.Contains(t.Id)), t => Assert.Equal(id, t.ImportBatchId));
    }

    [Fact]
    public async Task Confirm_IncludedLineWithoutCategory_CreatesNothingAndPointsTheLines()
    {
        var (client, accountId, batch) = await PreviewBankStatementAsync();
        var lines = batch.GetProperty("lines").EnumerateArray().ToList();
        var body = new { lines = new[] { new { id = lines[0].GetProperty("id").GetGuid(), category = "Padaria" } } };

        var response = await client.PostAsJsonAsync($"/imports/{batch.GetProperty("id").GetGuid()}/confirm", body);

        await AssertProblem(response, HttpStatusCode.BadRequest, "import.categories_required");
        var problem = await Json(response);
        Assert.Equal([2, 3, 4, 5], problem.GetProperty("lines").EnumerateArray().Select(n => n.GetInt32()));
        Assert.Empty(await TransactionsOfAsync(client, accountId));

        var stillPending = await Json(await client.GetAsync($"/imports/{batch.GetProperty("id").GetGuid()}"));
        Assert.Equal("Pending", stillPending.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Confirm_Twice_DoesNotDuplicate_AndAnswersTheSame()
    {
        var (client, accountId, batch) = await PreviewBankStatementAsync();
        var url = $"/imports/{batch.GetProperty("id").GetGuid()}/confirm";

        var first = await (await client.PostAsJsonAsync(url, AllWithCategory(batch))).Content.ReadAsStringAsync();
        var second = await client.PostAsJsonAsync(url, AllWithCategory(batch, "Outra"));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(first, await second.Content.ReadAsStringAsync());
        Assert.Equal(5, (await TransactionsOfAsync(client, accountId)).Count);
    }

    // Confirmacoes em paralelo: o EF InMemory nao aplica token de concorrencia entre contextos, entao
    // a corrida e provada no ConfirmImportHandlerTests (nova tentativa) e ao vivo no MySQL (PR da IMP-5).

    [Fact]
    public async Task Confirm_KeyImportedByAnotherBatchAfterThePreview_ComesBackAsDuplicate()
    {
        var owner = Guid.NewGuid();
        var (client, accountId, batch) = await PreviewBankStatementAsync(owner);
        // Outra importacao lancou o Pix (FITID 202610050001) depois desta previa.
        await SeedImportedTransactionAsync(owner, accountId, externalId: "202610050001");

        var confirmed = await Json(await client.PostAsJsonAsync(
            $"/imports/{batch.GetProperty("id").GetGuid()}/confirm", AllWithCategory(batch)));

        var lines = confirmed.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal("Duplicate", lines[1].GetProperty("status").GetString());
        Assert.Equal(4, confirmed.GetProperty("summary").GetProperty("imported").GetInt32());
        Assert.Equal(1, confirmed.GetProperty("summary").GetProperty("duplicate").GetInt32());
        Assert.Equal(5, (await TransactionsOfAsync(client, accountId)).Count); // 4 novos + o que ja existia
    }

    [Fact]
    public async Task Confirm_CardStatement_PaymentStaysOutAndPurchasesUseTheCard()
    {
        var client = ClientFor(Guid.NewGuid());
        var accountId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var batch = await Json(await client.PostAsync("/imports",
            Upload(Sample("fatura-cartao-ofx2.ofx"), "fatura.ofx", accountId, creditCardId: cardId)));

        var response = await client.PostAsJsonAsync($"/imports/{batch.GetProperty("id").GetGuid()}/confirm", AllWithCategory(batch, "Trabalho"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await TransactionsOfAsync(client, accountId);
        Assert.Equal(3, created.Count);
        Assert.All(created, t => Assert.Equal(("CreditCard", cardId, "Expense"),
            (t.GetProperty("paymentMethod").GetString(), t.GetProperty("creditCardId").GetGuid(), t.GetProperty("type").GetString())));
    }

    [Fact]
    public async Task Confirm_IncludingADuplicateLine_IsRejected()
    {
        var owner = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await SeedImportedTransactionAsync(owner, accountId, externalId: "202610050001");
        var client = ClientFor(owner);
        var batch = await Json(await client.PostAsync("/imports",
            Upload(Sample("extrato-conta-ofx1.ofx"), "extrato.ofx", accountId, paymentMethod: "Pix")));
        var duplicate = batch.GetProperty("lines")[1].GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync($"/imports/{batch.GetProperty("id").GetGuid()}/confirm",
            new { lines = new[] { new { id = duplicate, included = true, category = "X" } } });

        await AssertProblem(response, HttpStatusCode.BadRequest, "import.line_not_includable");
    }

    [Fact]
    public async Task Confirm_AnotherUsersBatch_IsNotFound_AndReadScopeIsForbidden()
    {
        var (_, _, batch) = await PreviewBankStatementAsync();
        var url = $"/imports/{batch.GetProperty("id").GetGuid()}/confirm";

        var other = await ClientFor(Guid.NewGuid()).PostAsJsonAsync(url, AllWithCategory(batch));
        var readOnly = await ClientFor(Guid.NewGuid(), scope: "expenses.read").PostAsJsonAsync(url, AllWithCategory(batch));

        await AssertProblem(other, HttpStatusCode.NotFound, "import.not_found");
        Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);
    }

    [Fact]
    public async Task Confirm_AccountRemovedAfterThePreview_IsNotFound()
    {
        var (client, accountId, batch) = await PreviewBankStatementAsync();
        _factory.AccountsClient.InvalidAccounts.Add(accountId);

        var response = await client.PostAsJsonAsync($"/imports/{batch.GetProperty("id").GetGuid()}/confirm", AllWithCategory(batch));

        await AssertProblem(response, HttpStatusCode.NotFound, "account.not_found");
    }

    // ---------- IMP-9: desfazer ----------

    private async Task<(HttpClient Client, Guid AccountId, Guid BatchId, List<Guid> Created)> ConfirmedBankStatementAsync(Guid? owner = null)
    {
        var (client, accountId, batch) = await PreviewBankStatementAsync(owner);
        var id = batch.GetProperty("id").GetGuid();
        var confirmed = await Json(await client.PostAsJsonAsync($"/imports/{id}/confirm", AllWithCategory(batch)));
        var created = confirmed.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("transactionId").GetGuid()).ToList();
        return (client, accountId, id, created);
    }

    [Fact]
    public async Task Undo_RemovesTheBatchTransactionsWithOneDeletedEventEach_AndKeepsTheOthers()
    {
        var owner = Guid.NewGuid();
        var (client, accountId, id, created) = await ConfirmedBankStatementAsync(owner);
        // Lancamento da mesma conta que nao veio do lote: fica.
        await SeedImportedTransactionAsync(owner, accountId, externalId: "outro-lote");

        var response = await client.PostAsync($"/imports/{id}/undo", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var undone = await Json(response);
        Assert.Equal("Undone", undone.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, undone.GetProperty("undoneAtUtc").ValueKind);

        var left = await TransactionsOfAsync(client, accountId);
        Assert.Equal(["Importado antes"], left.Select(t => t.GetProperty("description").GetString()));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();
        var deleted = db.OutboxEvents.Where(e => created.Contains(e.AggregateId) && e.EventType == "TransactionDeleted").ToList();
        Assert.Equal(created.Order(), deleted.Select(e => e.AggregateId).Order());
    }

    [Fact]
    public async Task Undo_ThenTheSameFileAgain_ComesBackAsNewLines()
    {
        var (client, accountId, id, _) = await ConfirmedBankStatementAsync();
        await client.PostAsync($"/imports/{id}/undo", null);

        var again = await Json(await client.PostAsync("/imports",
            Upload(Sample("extrato-conta-ofx1.ofx"), "extrato.ofx", accountId, paymentMethod: "Pix")));

        Assert.Equal(5, again.GetProperty("summary").GetProperty("new").GetInt32());
        Assert.Equal(0, again.GetProperty("summary").GetProperty("duplicate").GetInt32());
    }

    [Fact]
    public async Task Undo_Twice_DeletesOnceAndAnswersTheSame()
    {
        var (client, _, id, created) = await ConfirmedBankStatementAsync();

        var first = await (await client.PostAsync($"/imports/{id}/undo", null)).Content.ReadAsStringAsync();
        var second = await client.PostAsync($"/imports/{id}/undo", null);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(first, await second.Content.ReadAsStringAsync());
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();
        Assert.Equal(created.Count, db.OutboxEvents.Count(e => created.Contains(e.AggregateId) && e.EventType == "TransactionDeleted"));
    }

    [Fact]
    public async Task Undo_TransactionAlreadyDeletedByHand_IsSkipped()
    {
        var (client, accountId, id, created) = await ConfirmedBankStatementAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/transactions/{created[0]}")).StatusCode);

        var response = await client.PostAsync($"/imports/{id}/undo", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await TransactionsOfAsync(client, accountId));
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();
        // Um evento por lancamento: o excluido a mao nao ganha um segundo TransactionDeleted.
        Assert.Equal(created.Count, db.OutboxEvents.Count(e => created.Contains(e.AggregateId) && e.EventType == "TransactionDeleted"));
    }

    [Fact]
    public async Task Undo_PendingBatch_IsConflict()
    {
        var (client, _, batch) = await PreviewBankStatementAsync();

        var response = await client.PostAsync($"/imports/{batch.GetProperty("id").GetGuid()}/undo", null);

        await AssertProblem(response, HttpStatusCode.Conflict, "import.not_confirmed");
    }

    [Fact]
    public async Task Undo_AnotherUsersBatch_IsNotFound_AndReadScopeIsForbidden()
    {
        var (client, accountId, id, _) = await ConfirmedBankStatementAsync();

        var other = await ClientFor(Guid.NewGuid()).PostAsync($"/imports/{id}/undo", null);
        var readOnly = await ClientFor(Guid.NewGuid(), scope: "expenses.read").PostAsync($"/imports/{id}/undo", null);

        await AssertProblem(other, HttpStatusCode.NotFound, "import.not_found");
        Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);
        Assert.Equal(5, (await TransactionsOfAsync(client, accountId)).Count);
    }

    // ---------- IMP-6: sugestoes pelo historico ----------

    private static byte[] OfxWithMemos(params string[] memos) => Encoding.Latin1.GetBytes(string.Join("\r\n",
    [
        "OFXHEADER:100", "DATA:OFXSGML", "VERSION:102", "CHARSET:1252", "",
        "<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><CURDEF>BRL<BANKTRANLIST>",
        .. memos.Select((m, i) => $"<STMTTRN><DTPOSTED>20261102<TRNAMT>-{i + 1}.00<FITID>H-{i}<MEMO>{m}</STMTTRN>"),
        "</BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>",
    ]));

    [Fact]
    public async Task Upload_SuggestsTheMostRecentCategoryOfTheSameDescription_FromAnyAccountOfTheOwner()
    {
        var owner = Guid.NewGuid();
        var otherAccount = Guid.NewGuid();
        await SeedTransactionAsync(owner, otherAccount, "UBER *TRIP", "Viagem", DateTimeOffset.UtcNow.AddDays(-30));
        await SeedTransactionAsync(owner, otherAccount, "UBER *TRIP", "Transporte", DateTimeOffset.UtcNow.AddDays(-1));
        // Historico de outro usuario nunca entra.
        await SeedTransactionAsync(Guid.NewGuid(), Guid.NewGuid(), "PADARIA", "Alheia", DateTimeOffset.UtcNow);

        var batch = await Json(await ClientFor(owner).PostAsync("/imports",
            Upload(OfxWithMemos("Uber   *Trip", "PADARIA"), "extrato.ofx", Guid.NewGuid(), paymentMethod: "Pix")));

        var lines = batch.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal("Transporte", lines[0].GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, lines[1].GetProperty("category").ValueKind);
    }

    [Fact]
    public async Task Upload_SuggestsTheCategoryOfTheLastChange_SoRecategorizingTeachesTheImport()
    {
        // LNC-3 (ADR-0007, decisao 8): o lancamento antigo, recategorizado agora, vence o mais novo.
        var (owner, accountId) = (Guid.NewGuid(), Guid.NewGuid());
        var old = await SeedTransactionAsync(owner, accountId, "UBER *TRIP", "Viagem", DateTimeOffset.UtcNow.AddDays(-30));
        await SeedTransactionAsync(owner, accountId, "UBER *TRIP", "Transporte", DateTimeOffset.UtcNow.AddDays(-1));
        var client = ClientFor(owner);

        var edited = await client.PutAsJsonAsync($"/transactions/{old}", new
        {
            accountId, type = "Expense", paymentMethod = "Pix", description = "UBER *TRIP", category = "Mobilidade",
            amount = 10m, currency = "BRL", occurredOn = "2026-10-05", version = 1,
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        var batch = await Json(await client.PostAsync("/imports",
            Upload(OfxWithMemos("UBER *TRIP"), "extrato.ofx", Guid.NewGuid(), paymentMethod: "Pix")));
        Assert.Equal("Mobilidade", batch.GetProperty("lines")[0].GetProperty("category").GetString());
    }

    [Fact]
    public async Task Upload_BankStatement_MemoHintsSuggestTheMethod_OtherwiseTheBatchDefault()
    {
        var batch = await Json(await ClientFor(Guid.NewGuid()).PostAsync("/imports",
            Upload(Sample("extrato-conta-ofx1.ofx"), "extrato.ofx", Guid.NewGuid(), paymentMethod: "DebitCard")));

        var methods = batch.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("paymentMethod").GetString());
        // Padaria, "PIX ENVIADO", salario, "PAG BOLETO", padaria.
        Assert.Equal(["DebitCard", "Pix", "DebitCard", "Boleto", "DebitCard"], methods);
    }

    [Fact]
    public async Task Confirm_SuggestedCategoryIsKept_WhenTheUserDoesNotChangeIt()
    {
        var owner = Guid.NewGuid();
        await SeedTransactionAsync(owner, Guid.NewGuid(), "PADARIA", "Padaria", DateTimeOffset.UtcNow);
        var client = ClientFor(owner);
        var accountId = Guid.NewGuid();
        var batch = await Json(await client.PostAsync("/imports",
            Upload(OfxWithMemos("Padaria"), "extrato.ofx", accountId, paymentMethod: "Pix")));

        // Confirma sem decisao nenhuma: vale o que a previa sugeriu.
        var response = await client.PostAsJsonAsync($"/imports/{batch.GetProperty("id").GetGuid()}/confirm", new { lines = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Padaria", (await TransactionsOfAsync(client, accountId)).Single().GetProperty("category").GetString());
    }

    private async Task<Guid> SeedTransactionAsync(Guid owner, Guid accountId, string description, string category, DateTimeOffset createdAtUtc)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransactionsDbContext>();
        var transaction = Transaction.Create(owner, accountId, TransactionType.Expense, PaymentMethod.Pix, null,
            description, category, 10m, "BRL", new DateOnly(2026, 10, 5), null, createdAtUtc).Value;
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.Id;
    }

    // ---------- IMP-11: mapeamentos de CSV salvos ----------

    private static object SaveBody(string name, int amountColumn = 2, string[]? headers = null) => new
    {
        name,
        mapping = new { dateColumn = 0, descriptionColumn = 1, amountColumn, hasHeader = true, decimalSeparator = ",", invertSign = true },
        headers = headers ?? ["Data", "Descricao", "Valor"],
    };

    [Fact]
    public async Task CsvMappings_SaveAndList_WithMappingAndHeaders()
    {
        var client = ClientFor(Guid.NewGuid());

        var created = await client.PostAsJsonAsync("/imports/csv/mappings", SaveBody("Banco X - fatura"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await Json(created);
        Assert.Equal($"/imports/csv/mappings/{saved.GetProperty("id").GetGuid()}", created.Headers.Location!.OriginalString);

        var list = (await client.GetFromJsonAsync<JsonElement>("/imports/csv/mappings"))!.EnumerateArray().ToList();
        var item = Assert.Single(list);
        Assert.Equal("Banco X - fatura", item.GetProperty("name").GetString());
        Assert.True(item.GetProperty("mapping").GetProperty("invertSign").GetBoolean());
        Assert.Equal(",", item.GetProperty("mapping").GetProperty("decimalSeparator").GetString());
        Assert.Equal(["Data", "Descricao", "Valor"], item.GetProperty("headers").EnumerateArray().Select(h => h.GetString()));
    }

    [Fact]
    public async Task CsvMappings_SameNameIgnoringCase_ReplacesInsteadOfDuplicating()
    {
        var client = ClientFor(Guid.NewGuid());
        var first = await Json(await client.PostAsJsonAsync("/imports/csv/mappings", SaveBody("Banco X")));

        var again = await client.PostAsJsonAsync("/imports/csv/mappings", SaveBody("  banco   x ", amountColumn: 3, headers: ["D", "H", "X", "V"]));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var list = (await client.GetFromJsonAsync<JsonElement>("/imports/csv/mappings"))!.EnumerateArray().ToList();
        var item = Assert.Single(list);
        Assert.Equal(first.GetProperty("id").GetGuid(), item.GetProperty("id").GetGuid());
        Assert.Equal("Banco X", item.GetProperty("name").GetString()); // o nome original fica
        Assert.Equal(3, item.GetProperty("mapping").GetProperty("amountColumn").GetInt32());
    }

    [Fact]
    public async Task CsvMappings_InvalidMappingOrName_AreRejected()
    {
        var client = ClientFor(Guid.NewGuid());

        var noName = await client.PostAsJsonAsync("/imports/csv/mappings", SaveBody(" "));
        var badMapping = await client.PostAsJsonAsync("/imports/csv/mappings", new
        {
            name = "X",
            mapping = new { dateColumn = 0, descriptionColumn = 0, amountColumn = 2, hasHeader = true },
        });
        var noMapping = await client.PostAsJsonAsync("/imports/csv/mappings", new { name = "X" });

        await AssertProblem(noName, HttpStatusCode.BadRequest, "import.mapping_name_required");
        await AssertProblem(badMapping, HttpStatusCode.BadRequest, "import.mapping_invalid");
        await AssertProblem(noMapping, HttpStatusCode.BadRequest, "import.mapping_invalid");
    }

    [Fact]
    public async Task CsvMappings_AreScopedToTheOwner_AndDeleteOnlyOwn()
    {
        var mine = ClientFor(Guid.NewGuid());
        var other = ClientFor(Guid.NewGuid());
        var saved = await Json(await mine.PostAsJsonAsync("/imports/csv/mappings", SaveBody("Meu banco")));
        var url = $"/imports/csv/mappings/{saved.GetProperty("id").GetGuid()}";

        Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/imports/csv/mappings"))!.EnumerateArray());
        await AssertProblem(await other.DeleteAsync(url), HttpStatusCode.NotFound, "import.mapping_not_found");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ClientFor(Guid.NewGuid(), scope: "expenses.read").PostAsJsonAsync("/imports/csv/mappings", SaveBody("X"))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await mine.DeleteAsync(url)).StatusCode);
        Assert.Empty((await mine.GetFromJsonAsync<JsonElement>("/imports/csv/mappings"))!.EnumerateArray());
    }

    [Fact]
    public async Task CsvMappings_LimitPerUser_IsEnforced()
    {
        var client = ClientFor(Guid.NewGuid());
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/imports/csv/mappings", SaveBody($"Banco {i}"))).StatusCode);
        }

        await AssertProblem(await client.PostAsJsonAsync("/imports/csv/mappings", SaveBody("Banco 50")),
            HttpStatusCode.Conflict, "import.mappings_limit");
        // Substituir um existente continua valendo no limite.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/imports/csv/mappings", SaveBody("Banco 7"))).StatusCode);
    }

    private async Task<int> CountBatchesAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<TransactionsDbContext>().ImportBatches.Count();
    }

    // ---------- RCN-3 (ADR-0011, decisoes 2 e 3) ----------

    [Fact]
    public async Task Upload_CardStatement_BringsTheStatedTotal_AndDuplicatesPointToTheirTransactions()
    {
        var client = ClientFor(Guid.NewGuid());
        var (accountId, cardId) = (Guid.NewGuid(), Guid.NewGuid());
        var first = await Json(await client.PostAsync("/imports",
            Upload(Sample("fatura-cartao-ofx2.ofx"), "fatura.ofx", accountId, creditCardId: cardId)));
        Assert.Equal((572.30m, "2026-10-06", "2026-11-05"), (first.GetProperty("statedTotal").GetDecimal(),
            first.GetProperty("statementStart").GetString(), first.GetProperty("statementEnd").GetString()));
        var confirmed = await Json(await client.PostAsJsonAsync(
            $"/imports/{first.GetProperty("id").GetGuid()}/confirm", AllWithCategory(first)));
        var launched = confirmed.GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("transactionId").ValueKind == JsonValueKind.String)
            .Select(l => l.GetProperty("transactionId").GetGuid()).Order().ToList();

        // O mesmo arquivo de novo: cada linha ja lancada diz qual lancamento repete.
        var again = await Json(await client.PostAsync("/imports",
            Upload(Sample("fatura-cartao-ofx2.ofx"), "fatura.ofx", accountId, creditCardId: cardId)));
        var duplicates = again.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("status").GetString() == "Duplicate").ToList();

        Assert.Equal(3, launched.Count);
        Assert.Equal(launched, duplicates.Select(l => l.GetProperty("duplicateOf").GetGuid()).Order().ToList());
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
