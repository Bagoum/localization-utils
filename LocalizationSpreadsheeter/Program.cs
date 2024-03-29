﻿using System;
using System.IO;
using System.Threading.Tasks;
using LocalizationExecutor;
using static ActorCS.Helpers;
using static LocalizationExecutor.LocalizationFileOps;
using static LocalizationExecutor.LocalizerDMK;
using File = System.IO.File;

namespace ActorCS {


static class Program {
    /// <summary>
    /// Deployment ID of the AppsScript project containing the code in "exportAsCSVAppsScript.js".
    /// You will need to update this before being able to batch-download sheets.
    /// </summary>
    public const string SCRIPT_ID = "AKfycbzhEpwRwps69J4Q-vECvH6H4X3MVm4TvNnHeztVcrSwk6gPzFlSypmc5yij7yU9w2LY";
    /// <summary>
    /// File containing Google Cloud Platform OAuth client information.
    /// You will need to add this before being able to batch-download sheets.
    /// </summary>
    public const string GCP_CLIENT_AUTH = "C://Workspace/dev/LocalizationUtils/Secrets/gdrive.json";

    public static async Task Download(string spreadsheetId, string csvDir) {
        var key = await AuthorizeOAuth(GCP_CLIENT_AUTH);
        var script = MakeAppScript(key, "Localization CSV Generator");
        Console.WriteLine("Running CSV export script");
        var export = await script.RunScript<string>("SaveSpreadsheetAsZip", SCRIPT_ID, spreadsheetId);
        var parts = export.Split("::");
        var folderId = parts[0];
        var zipId = parts[1];
        Console.WriteLine($"Created folder {folderId} and zip {zipId}");
        var drive = MakeGDrive(key, "Localization CSV Exporter");
        var zip = drive.DownloadFile(zipId);
        await drive.Files.Delete(folderId).ExecuteAsync();
        //Note: this command will clear the directory before adding the download files to it.
        if (!Directory.Exists(csvDir))
            Directory.CreateDirectory(csvDir);
        new DirectoryInfo(csvDir).Delete(true);
        System.IO.Compression.ZipFile.ExtractToDirectory(zip, csvDir);
        File.Delete(zip);
    }

    public static async Task DownloadAndGenerate(SpreadsheetCtx spreadsheet) {
        await Download(spreadsheet.spreadsheetId, spreadsheet.csvDir);
        generateAll(spreadsheet);
    }
    
    static async Task Main(string[] args) {
        await DownloadAndGenerate(dmkSpreadsheet);
    }
}
}