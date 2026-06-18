# StatusPoker

A [Deadworks](https://deadworks.net) server plugin that sends a POST request to a configurable endpoint every 10 seconds. Useful as a heartbeat, status reporting, or webhook integration for your Deadlock dedicated server.

## How it works

On plugin load, StatusPoker starts a background timer that fires every 10 seconds and sends a JSON POST request:

```
POST https://postman-echo.com/post
Content-Type: application/json

{"hello":"world"}
```

The timer runs independently of the game loop, so it fires even when no players are connected.

## Configuration

### Changing the endpoint URL

Edit `StatusPoker.cs` and replace the URL in the `SendPoke` method:

```csharp
var response = await Http.PostAsJsonAsync(
    "https://postman-echo.com/post",  // <-- change this URL
    new { hello = "world" });
```

### Changing the request body

Modify the anonymous object passed to `PostAsJsonAsync`:

```csharp
var response = await Http.PostAsJsonAsync(
    "https://your-endpoint.com/status",
    new { server = "my-server", status = "online", map = Server.MapName });
```

### Changing the interval

Adjust the `TimeSpan.FromSeconds(10)` value in `OnLoad`:

```csharp
_pokerTimer = new System.Threading.Timer(
    _ => SendPoke(),
    null,
    TimeSpan.Zero,              // initial delay (Zero = fire immediately)
    TimeSpan.FromSeconds(30));  // interval between requests
```

## Build

Requires .NET 10 and a Deadlock installation with Deadworks.

Set the `DEADLOCK_GAME_DIR` environment variable to your Deadlock install path, or pass it at build time:

```bash
dotnet build -p:DeadlockDir="F:\SteamLibrary\steamapps\common\Deadlock"
```

The build automatically deploys the plugin DLL to the server's `managed/plugins/` directory.
