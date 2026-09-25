using System.Net;
using System.Text.Json;
using Engine.Chess.Board;
using Engine.Jev.Host;

string root = Path.GetFullPath(Environment.GetEnvironmentVariable("CHESS_UI_ROOT") ??
    Path.Combine("Engine.UI", "bin", "Release", "net10.0", "publish", "wwwroot"));
if (!File.Exists(Path.Combine(root, "index.html"))) {
    Console.Error.WriteLine($"Published UI not found at {root}. Run the publish command in the README first.");
    return 1;
}

string? apiKey = Environment.GetEnvironmentVariable("JEV_API_KEY");
string model = Environment.GetEnvironmentVariable("JEV_MODEL") ?? "jev-latest";
using var http = new HttpClient { BaseAddress = new Uri("https://api.typesafe.ai/"),
    Timeout = TimeSpan.FromSeconds(20) };
JevDecisionService? jev = string.IsNullOrWhiteSpace(apiKey) ? null : new(http, apiKey, model);

using var listener = new HttpListener();
listener.Prefixes.Add("http://127.0.0.1:5208/");
listener.Start();
Console.WriteLine("Chess UI: http://127.0.0.1:5208/");
Console.WriteLine(jev is null ? "Jev unavailable: set JEV_API_KEY before starting." : $"Jev enabled ({model}).");

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping.Cancel(); listener.Stop(); };
while (!stopping.IsCancellationRequested) {
    HttpListenerContext context;
    try { context = await listener.GetContextAsync(); }
    catch (HttpListenerException) when (stopping.IsCancellationRequested) { break; }
    _ = Task.Run(() => HandleAsync(context, root, jev));
}
return 0;

static async Task HandleAsync(HttpListenerContext context, string root, JevDecisionService? jev) {
    HttpListenerRequest request = context.Request;
    HttpListenerResponse response = context.Response;
    try {
        string path = request.Url?.AbsolutePath ?? "/";
        if (path == "/api/jev/status" && request.HttpMethod == "GET") {
            await WriteJsonAsync(response, 200, new { available = jev is not null });
        } else if (path == "/api/jev/move" && request.HttpMethod == "POST") {
            if (jev is null) { await WriteJsonAsync(response, 503, new { error = "Jev is not configured." }); return; }
            if (request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true ||
                (request.Headers["Origin"] is string origin && origin != "http://127.0.0.1:5208")) {
                await WriteJsonAsync(response, 403, new { error = "Only same-origin JSON requests are allowed." }); return;
            }
            if (request.ContentLength64 is < 1 or > 4096) {
                await WriteJsonAsync(response, 400, new { error = "Expected a small JSON request." }); return;
            }
            using JsonDocument body = await JsonDocument.ParseAsync(request.InputStream);
            if (!body.RootElement.TryGetProperty("fen", out JsonElement fenValue) ||
                fenValue.GetString() is not string fen) {
                await WriteJsonAsync(response, 400, new { error = "Missing FEN." }); return;
            }
            var position = new Position(fen);
            JevMoveDecision decision = await jev.ChooseAsync(position);
            await WriteJsonAsync(response, 200, decision);
        } else if (request.HttpMethod == "GET") {
            string relative = path == "/" ? "index.html" : Uri.UnescapeDataString(path.TrimStart('/'));
            string file = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(file)) {
                await WriteJsonAsync(response, 404, new { error = "File not found." }); return;
            }
            response.ContentType = Path.GetExtension(file).ToLowerInvariant() switch {
                ".html" => "text/html; charset=utf-8", ".js" => "application/javascript",
                ".css" => "text/css", ".json" => "application/json",
                ".wasm" => "application/wasm", ".dll" => "application/octet-stream",
                ".png" => "image/png", ".svg" => "image/svg+xml",
                ".dat" => "application/octet-stream", _ => "application/octet-stream",
            };
            await using var stream = File.OpenRead(file);
            response.ContentLength64 = stream.Length;
            await stream.CopyToAsync(response.OutputStream);
        } else {
            await WriteJsonAsync(response, 405, new { error = "Method not allowed." });
        }
    } catch (Exception ex) {
        try { await WriteJsonAsync(response, 502, new { error = ex is HttpRequestException
            ? ex.Message : "Jev could not choose a move. Try again." }); }
        catch (HttpListenerException) { }
        Console.Error.WriteLine(ex);
    } finally {
        response.Close();
    }
}

static async Task WriteJsonAsync(HttpListenerResponse response, int status, object value) {
    byte[] data = JsonSerializer.SerializeToUtf8Bytes(value,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
    response.StatusCode = status;
    response.ContentType = "application/json; charset=utf-8";
    response.ContentLength64 = data.Length;
    await response.OutputStream.WriteAsync(data);
}
