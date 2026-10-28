using System.Text.Json.Serialization;
using MyTelU_Launcher.Services;

MyTelU_Launcher.Core.Helpers.Json.Options.TypeInfoResolver = AuthenticationJsonContext.Default;
if (args.Length != 1 || (args[0] != "--interactive" && args[0] != "--silent"))
    return 2;

var service = new BrowserLoginService();
bool success = args[0] == "--interactive"
    ? await service.StartLoginAsync()
    : await service.TrySilentLoginAsync();
return success ? 0 : 1;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class AuthenticationJsonContext : JsonSerializerContext;
