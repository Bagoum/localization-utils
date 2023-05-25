module LocalizationExecutor.LocalizerPolyglot
open LocalizationExecutor.LocalizationCodeGen
open LocalizationExecutor.LocalizationFileOps
open LocalizationExecutor.LocalizerBase
open LocalizerDMK
open FSharp.Data


//Localizer for the Polyglot resource:
// https://docs.google.com/spreadsheets/d/17f0dQawb-s_Fd7DHgmVvJoEGDMH_yoSd8EYigrb0zmM
//Not used in DMK but provided here as an example

type PolyglotCSVRow = CsvProvider<"./CSV/StructurePolyglot.csv">
let polyglotCSVRowToRow (row: PolyglotCSVRow.Row) : Row =
    {
        key = row.``STRING ID``.ToLower().Replace(" ", "_")
        locales = [
            row.ENGLISH
            row.``JAPANESE / 日本語``
            row.``FRENCH / FRANÇAIS``
            row.``SPANISH / ESPAÑOL``
            row.``PORTUGUESE / PORTUGUÊS (BR)``
            row.``GERMAN / DEUTSCH``
            row.``SIMPLIFIED CHINESE / 简体中文``
        ]
    }

let polyglotLctx =
    { lctx with
        loadRows = fun path ->
                    (PolyglotCSVRow.Load path).Rows
                    |> Seq.map polyglotCSVRowToRow
        locales = ["null"; "Locales.JP"; "Locales.FR"; "Locales.ES"; "Locales.PT"; "Locales.DE"; "Locales.ZH"]
        className = "LocalizedStringsP"
}

let polyglotBatch : FileBatch = {
    name = "Polyglot Localization"
    topFileName = "_StringRepositoryPolyglot.cs"
    outDir = dmkCoreBatch.outDir
    ctx = polyglotLctx
    perFileInfo = [
        ("Master", FileInfo.New "Polyglot" (Some "polyglot"))
    ] |> Map.ofList
}


let polyglotSpreadsheet = {
    spreadsheetId = "1pGswHVZJMrOR9nehly7EPQgOYf3pvQw11TXQY3CP_DI"
    csvDir = "C://Workspace/tmp/csv/"
    batches = [ polyglotBatch ]
}