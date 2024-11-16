using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Script.v1;
using Google.Apis.Script.v1.Data;

namespace ActorCS {
public static class Helpers {
    private static readonly Random rand = new();
    private const string randChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";

    public static async Task<UserCredential> AuthorizeOAuth(string secretsFile, CancellationToken? cT = null) {
        //Note: If you change the required permissions, you need to delete the token in
        // Roaming/Google.Apis.Auth and recertify
        using (var sstream = new FileStream(secretsFile, FileMode.Open, FileAccess.Read)) {
            return await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.Load(sstream).Secrets,
                new[] {DriveService.Scope.Drive, ScriptService.Scope.Spreadsheets },
                "actorCS", cT ?? CancellationToken.None
            );
        }
    }

    public static ScriptService MakeAppScript(UserCredential key, string appName) => new(
        new BaseClientService.Initializer() {
            ApplicationName = appName,
            HttpClientInitializer = key
        });
    public static DriveService MakeGDrive(UserCredential key, string appName) => new(
        new BaseClientService.Initializer() {
            ApplicationName = appName,
            HttpClientInitializer = key
        });

    public static string RandString(int len) {
        var sb = new StringBuilder();
        for (int ii = 0; ii < len; ++ii) {
            sb.Append(randChars[rand.Next(randChars.Length)]);
        }
        return sb.ToString();
    }

    public static async Task<T> RunScript<T>(this ScriptService s, string fn, string scriptId, params object[] prms) {
        var result = await s.Scripts.Run(new ExecutionRequest() {
            DevMode = true,
            Function = fn,
            Parameters = prms
        }, scriptId).ExecuteAsync();
        Console.WriteLine($"Script execution complete with status {result.Error?.Message ?? "OK"}");
        return (T) result.Response["result"];
    }
    public static string DownloadFile(this DriveService d, string id, string? outFile=null) {
        var f = d.Files.Get(id);
        using (var strm = new FileStream(outFile ??= RandString(9), FileMode.Create)) {
            var status = f.DownloadWithStatus(strm);
            Console.WriteLine($"Download complete with status {status.Status} ({status.Exception}");
        }
        return outFile;
    }
}
}