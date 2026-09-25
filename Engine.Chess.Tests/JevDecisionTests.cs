using System.Net;
using System.Text;
using System.Text.Json;
using Engine.Chess.Board;
using Engine.Jev.Host;

namespace Engine.Chess.Tests;

public sealed class JevDecisionTests {
    [Fact]
    public async Task SendsLegalChoicesAndAcceptsLegalMove() {
        var handler = new StubHandler("""
            {"model":"jev-1","answers":{"move":{"type":"choice","choice":"e2e4","confidence":0.72,"probabilities":{"e2e4":0.72}}},"usage":{"input_tokens":120,"output_tokens":3}}
            """);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.typesafe.ai/") };
        var service = new JevDecisionService(http, "test-key");

        JevMoveDecision result = await service.ChooseAsync(new Position());

        Assert.Equal("e2e4", result.Uci);
        Assert.Equal(0.72, result.Confidence);
        Assert.Equal(120, result.InputTokens);
        Assert.Equal("Bearer test-key", handler.Authorization);
        using JsonDocument request = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("jev-latest", request.RootElement.GetProperty("model").GetString());
        Assert.Contains("rnbqkbnr", request.RootElement.GetProperty("state").GetProperty("board").GetString());
        JsonElement choices = request.RootElement.GetProperty("questions").GetProperty("move")
            .GetProperty("criteria");
        Assert.Equal(20, choices.EnumerateObject().Count());
        Assert.Equal("e4", choices.GetProperty("e2e4").GetString());
    }

    [Fact]
    public async Task RejectsMoveOutsideLegalChoices() {
        var handler = new StubHandler("""
            {"model":"jev-1","answers":{"move":{"type":"choice","choice":"e2e5","confidence":0.8,"probabilities":{}}},"usage":{"input_tokens":1,"output_tokens":1}}
            """);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.typesafe.ai/") };
        var service = new JevDecisionService(http, "test-key");

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ChooseAsync(new Position()));
    }

    private sealed class StubHandler(string responseBody) : HttpMessageHandler {
        public string? Authorization { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
