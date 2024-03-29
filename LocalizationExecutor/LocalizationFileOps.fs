﻿module LocalizationExecutor.LocalizationFileOps
open System
open System.IO
open FCommon.Extensions
open LocalizationExecutor.LocalizationCodeGen


type FileInfo = {
    ///Name of the static class constructed to enclose corresponding localized string objects.
    className: string
    ///Optional string prefix to prepend to the IDs of localized string objects.
    /// See LGenCtx.lskeyprefix
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

let exportDir csets req ssht =
    Directory.EnumerateFiles(ssht.csvDir)
    |> Seq.fold (fun (csets, fis, lctxs) p ->
        let basename = Path.GetFileNameWithoutExtension p
        match req.perFileInfo.TryFind basename with
        | Some fi ->
            let lctx = { req.ctx with nestedClassName = fi.className;
                                    lskeyprefix = match fi.referencePrefix with | None -> "" | Some pref -> $"{pref}." }
            let csets, lctx = exportFile csets lctx p fi.className req.outDir
            csets, fi::fis, lctx::lctxs
        | None -> (csets, fis, lctxs)) (csets, [], [])

let generateCode req ssht =
    let csets = req.ctx.locales |> List.map (fun _ -> Set.empty)
    let csets, fis, ctxs = exportDir csets req ssht
    let lsGenerated =
        List.zip fis ctxs
        |> List.fold (fun acc (fi: FileInfo, x: LGenCtx) ->
                        match fi.referencePrefix with
                        | None -> acc
                        | Some prefix ->
                            (x.lsGenerated
                            |> List.map (fun (key, fn) -> ($"{prefix}.{key}", $"{fi.className}.{fn}")))
                            :: acc
            ) []
        |> Seq.concat
        |> Seq.map (fun (key, ls) -> $"{{ \"{key}\", {ls} }},")
    req.ctx.lsclass |> Option.map (fun (_, staticCls) ->
        List.concat [
            [
                Word $"private static readonly Dictionary<string, {staticCls}> _allDataMap = new Dictionary<string, {staticCls}>() {{"
                Indent
            ]
            lsGenerated |> List.ofSeq |> List.collect (fun ls -> [
                Newline
                Word ls
            ])
            [
                Dedent
                Newline
                Word "};"
            ]
        ]
        |> generateClass req.ctx
        |> (fun pieces -> File.WriteAllText(Path.Join(req.outDir, req.topFileName), (render pieces 0)))
        ) |> ignore
    List.zip req.ctx.locales csets
    |> Array.ofList

let explainCsets csets =
    csets
    |> Array.map (fun (loc, cset) -> $"\n~~~~~~\nCSet for locale {loc}:\n\n{cset |> Set.toArray |> System.String}")
    |> String.concat "\n"

let generateAll ssht =
   ssht.batches |> List.iter (fun req ->
        Console.WriteLine $"Performing localization analysis for {req.name}"
        generateCode req ssht
        |> explainCsets
        |> Console.WriteLine
   )