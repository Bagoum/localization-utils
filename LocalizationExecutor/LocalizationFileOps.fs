module LocalizationExecutor.LocalizationFileOps
open System
open System.IO
open FCommon.Extensions
open LocalizationExecutor.LocalizationCodeGen


type FileInfo = {
    ///Name of the static class constructed to enclose corresponding localized string objects.
    className: string
    ///Optional string prefix to prepend to the IDs of localized string objects.
    ///For example, if the key in the excel sheet for the row is "cat", and lskeyprefix = "animals", then the final
    /// ID for the localized string object will be "animals.cat".
    referencePrefix: string option
} with
    static member New baseFileName referencePrefix = {
        className = baseFileName
        referencePrefix = referencePrefix
    }

type FileBatch = {
    name : string
    outDir : string
    ctx : LGenCtx
    perFileInfo : Map<string, FileInfo>
    topFileName: string
}

type SpreadsheetCtx = {
    spreadsheetId : string
    csvDir : string
    batches : FileBatch list
}

let exportDir req ssht =
    Directory.EnumerateFiles(ssht.csvDir)
    |> Seq.choose (fun p ->
        let basename = Path.GetFileNameWithoutExtension p
        match req.perFileInfo.TryFind basename with
        | Some fi ->
            let lctx = { req.ctx with nestedClassName = fi.className;
                                    lskeyprefix = match fi.referencePrefix with | None -> "" | Some pref -> $"{pref}." }
            Some (exportFile lctx p fi.className req.outDir, fi)
        | None -> None)

let generateCode req ssht =
    let exported = exportDir req ssht |> List.ofSeq
    let lsGenerated =
        exported
        |> List.choose (fun (generated, fi) ->
                            fi.referencePrefix |> Option.map (fun prefix ->
                                generated.lsGenerated
                                |> List.map (fun (key, fn) -> ($"{prefix}.{key}", $"{fi.className}.{fn}"))
            ))
        |> List.concat
        |> List.map (fun (key, ls) -> $"{{ \"{key}\", {ls} }},")
    req.ctx.lsclass |> Option.map (fun (_, staticCls) ->
        List.concat [
            [
                Word $"private static readonly Dictionary<string, {staticCls}> _allDataMap = new Dictionary<string, {staticCls}>() {{"
                Indent
            ]
            lsGenerated |> List.collect (fun ls -> [
                Newline
                Word ls
            ])
            [
                Dedent
                Newline
                Word "};"
            ]
        ]
        |> generateClass req.ctx req.ctx.classAttribute
        |> (fun pieces -> File.WriteAllText(Path.Join(req.outDir, req.topFileName), (render pieces 0)))
        ) |> ignore
    Seq.zip req.ctx.locales (seq {
        for i in 0..(req.ctx.locales.Length-1) -> Seq.map (fun exp -> (fst exp).csets[i]) exported |> Set.unionMany
    })
    |> Array.ofSeq

let explainCsets csets =
    csets
    |> Array.map (fun (loc, cset) -> $"\n~~~~~~\nCSet for locale {loc}:\n\n{cset |> Set.toArray |> String}")
    |> String.concat "\n"

let generateAll ssht =
   ssht.batches |> List.iter (fun req ->
        Console.WriteLine $"Performing localization analysis for {req.name}"
        generateCode req ssht
        |> explainCsets
        |> Console.WriteLine
   )