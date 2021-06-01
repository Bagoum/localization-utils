module LocalizationExecutor.LocalizationFileOps
open System
open System.IO
open FCommon.Extensions
open LocalizationExecutor.LocalizationCodeGen


type FileInfo = {
    baseFileName: string
    referencePrefix: string option
} with
    static member New baseFileName referencePrefix = {
        baseFileName = baseFileName
        referencePrefix = referencePrefix
    }

type LReqCtx = {
    name : string
    outDir : string
    ctx : LGenCtx
    perFileInfo : Map<string, FileInfo>
    topFileName: string
}

type SpreadsheetCtx = {
    spreadsheetId : string
    csvDir : string
    reqs : LReqCtx list
}

let exportDir csets req ssht =
    Directory.EnumerateFiles(ssht.csvDir)
    |> Seq.fold (fun (csets, fis, lctxs) p ->
        let basename = Path.GetFileNameWithoutExtension p
        match req.perFileInfo.TryFind basename with
        | Some fi ->
            let lctx = { req.ctx with nestedClassName = fi.baseFileName;
                                    lskeyprefix = match fi.referencePrefix with | None -> "" | Some pref -> $"{pref}." }
            let csets, lctx = exportFile csets lctx p req.outDir
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
                            |> List.map (fun (key, fn) -> ($"{prefix}.{key}", $"{fi.baseFileName}.{fn}")))
                            :: acc
            ) []
        |> Seq.concat
        |> Seq.map (fun (key, ls) -> $"{{ \"{key}\", {ls} }},")
    req.ctx.lsclass |> Option.map (fun cls ->
        List.concat [
            [
                Word $"private static readonly Dictionary<string, {cls}> _allDataMap = new Dictionary<string, {cls}>() {{"
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
   ssht.reqs |> List.iter (fun req ->
        Console.WriteLine $"Performing localization analysis for {req.name}"
        generateCode req ssht
        |> explainCsets
        |> Console.WriteLine
   )