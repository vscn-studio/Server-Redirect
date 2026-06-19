# Server Redirect

Vintage Story mod by VSCN-Studio.

This mod lets server operators maintain a named redirect list. Players press `Alt+R` to open the redirect window, then click a target name to switch to that server in the current client session.

The mod must be installed on both the server and clients because the redirect window and hotkey are client-side.

## Player use

Press `Alt+R` in game to open the redirect window. Click a configured target name to redirect to that server.

## Admin commands

Requires the `controlserver` privilege.

```text
/serverredirect add <host:port> <password> <name>
/serverredirect del <name>
/serverredirect del <host:port>
/serverredirect list
```

Examples:

```text
/serverredirect add hub.example.com:42420 - Hub
/serverredirect add survival.example.com:42420 secret Survival
/serverredirect del Survival
/serverredirect del hub.example.com:42420
/serverredirect list
```

`host:port` may also be a `vintagestoryjoin://` link. The mod strips that prefix before saving the redirect target. Use `-` as the password for servers without a password. Passwords cannot contain spaces.

## Config

The server config file is `ModConfig/serverredirect.json`.

```json
{
  "entries": [
    {
      "host": "survival.example.com:42420",
      "password": "secret",
      "name": "Survival"
    }
  ]
}
```

The redirect flow uses this mod's client channel and a clean client-session teardown before reconnecting.

## Local build

Install the .NET SDK used by the project, then set `VINTAGE_STORY` to a Vintage Story install or server directory that contains `VintagestoryAPI.dll`, `VintagestoryLib.dll`, and `Lib/protobuf-net.dll`.

```powershell
$env:VINTAGE_STORY = "E:\vintagestory\vs_client_windows_1.22.3"
dotnet build
dotnet publish -c Release -o "Releases\serverredirect"
Compress-Archive -Path "Releases\serverredirect\*" -DestinationPath "Releases\serverredirect-1.0.0.zip" -Force
```

## GitHub Actions

Two manual workflows are included:

- `Build Package`: enter a mod version and Vintage Story version, then download the generated `serverredirect-<version>.zip` artifact.
- `Publish Release`: enter a mod version and Vintage Story version, then create a GitHub Release tagged `v<version>` with the packaged zip attached.

Both workflows download the Vintage Story server package for the selected Vintage Story version and use its DLLs as compile references. The workflow version input also updates `modinfo.json` inside the packaged zip.
