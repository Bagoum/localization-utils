module LocalizationExecutor.LocalizationCodeGen
open System
open System.IO
open FCommon.Types
open FCommon.Functions
open LocalizationExecutor.LocalizationParser

type LRenderFragment =
    | Newline
    | Indent //Place at the end of the previous line, before the newline
    | Dedent
    | Word of string

let render fragments indent =
    let rec inner fragments indent =
        match fragments with
        | [] -> Seq.empty
        | f::fs ->
            match f with
            | Newline -> seq {
                    yield "\n"
                    yield (List.replicate indent "\t" |> String.concat "")
                    yield! inner fs indent;
                }
            | Indent -> inner fs (indent + 1)
            | Dedent -> inner fs (indent - 1)
            | Word s -> seq {
                    yield s
                    yield! inner fs indent;
                }
    (inner fragments indent)
    |> String.concat ""

type Row = {
    key: string
    locales: string list
}
let argString i = $"arg{i}"

type LGenCtx = {
    ///Function that gets all rows from a CSV path. Implementation depends on CSV structure.
    /// See LocalizerBase.fs for an example.
    loadRows: string -> Row seq
    ///The statically referencable object name which can be used to switch by locale.
    localeSwitch: string
    ///The type name of parameters to localization functions (eg. in C# this should be "object").
    objectType: string
    ///Locale keys (first is default). Must match the output of loadRows.
    locales: string list 
    ///If present, zero-arg functions will be saved as LocalizedString instead (preferred).
    ///The tuple should contain the instantiated class and the static class. They may be the same.
    /// In the case of DMK, the instantiated class is LText and the static class is LString.
    lsclass: (string * string) option
    ///If this and lsclass are present, multi-arg functions will have a second form
    /// which returns a LocalizedString. This requires resolving all languages,
    /// but is useful for UI cases where the language can change on the screen.
    methodToLsSuffix : string option
    ///The prefix to use for generating the ID of the localized string object. For example, if the key in
    /// the excel sheet for the row is "cat", and lskeyprefix = "animals", then the final
    /// ID for the localized string object will be "animals.cat".
    lskeyprefix : string
    ///A function overloaded with two signatures that formats the string based on locale:
    /// Render(string locale, string[] fmtStrings, params object[] fmtArgs)
    /// Render(string locale, string fmtString, params object[] fmtArgs)
    ///Cf. Suzunoya.BagoumLib.Culture.LocalizationRendering
    renderFunc: string
    funcStandardizer: string -> string
    className: string
    nestedClassName: string
    namespace_: string
    outputHeader: string
    ///Errors output by the generation process
    errors: string list
    lsGenerated: (string * string) list
} with
    member this.AddError str = { this with errors = str::this.errors }
    member this.AddErrors strs = { this with errors = List.append this.errors strs }
    member private this.CSharpifyNoQuotes pu =
        match pu with 
        | String s -> s
        | StandardFormat s -> $"{{{s}}}"
        | Argument i -> argString i
        | ConjFormat c ->
            c.args
            |> List.map (List.map this.CSharpify >> String.concat "")
            |> String.concat ", "
            |> sprintf "%s(%s)" (this.funcStandardizer c.func)
    member private this.CSharpify (pu:ParseUnit) =    
        if requiresQuotes pu
        then $"\"{this.CSharpifyNoQuotes pu}\""
        else this.CSharpifyNoQuotes pu
    
    member this.AccCSharpify (units:ParseUnit list) =
        let commit s acc = $"\"{s}\""::acc
        let last, acc =
            List.foldBack (fun u (last, acc) ->
                let csu = this.CSharpifyNoQuotes u
                match last with
                | Some s -> if requiresQuotes u
                            then (csu + s |> Some, acc)
                            else (None, csu::(commit s acc))
                | _ -> if requiresQuotes u
                        then (Some csu, acc)
                        else (None, csu::acc)
            ) units (None, [])
        match last with
        | Some s -> commit s acc
        | _ -> acc

let generateArgs nargs =
    [0..(nargs - 1)]
    |> List.map argString
    |> String.concat ", "
let generateParams (ctx:LGenCtx) nargs =
    [0..(nargs - 1)]
    |> List.map (fun i -> $"{ctx.objectType} {argString i}")
    |> String.concat ", "


let generateCaseBody locale cset nargs (ctx: LGenCtx) (localized: ParseSequence * State) =
    let seq = localized |> fst |> ctx.AccCSharpify
    if seq.Length = 1
    then
        updateCharset cset seq[0], ctx,
        if nargs = 0 then [Word seq[0]] else [
            $"{ctx.renderFunc}({locale}, {seq[0]}, " |> Word
            generateArgs nargs |> Word
            Word(")")
        ]
    else
        let cset, pieces = List.foldBack (fun word (cset, acc) ->
                        updateCharset cset word, [
                            Newline
                            Word word
                            Word ","
                        ]::acc) seq (cset, List.empty)
        cset, ctx, List.concat [
            [
                $"{ctx.renderFunc}({locale}, new[] {{" |> Word
                Indent
            ]
            List.concat pieces
            [
                Dedent
                Newline
                if nargs > 0 then Word "}, " else Word "}"
                generateArgs nargs |> Word
                Word(")")
            ]
        ]

let generateSwitchCaseOrDefault locale cset nargs (ctx: LGenCtx) caseString localized =
    let cset, ctx, body = generateCaseBody locale cset nargs ctx localized
    cset, ctx, List.concat [
        [
            Newline
            Word caseString
            Word " => "
        ]
        body
        [
            Word ","
        ]
    ]
    
    
    
let generateSwitchCase cset nargs (ctx: LGenCtx) lang localized =
    generateSwitchCaseOrDefault lang cset nargs ctx lang localized
    
let generateSwitchDefault cset nargs (ctx: LGenCtx) localized =
    generateSwitchCaseOrDefault "null" cset nargs ctx "_" localized
   
let generateSwitch nargs ctx (localizeds: ((ParseSequence * State) * string * char Set) list) =
    let default_localized::localizeds = localizeds
    let (dflt_parse, _, dflt_cset) = default_localized
    let dflt_cset, ctx, default_case = generateSwitchDefault dflt_cset nargs ctx dflt_parse
    let csets, ctx, cases =
        localizeds
        |> List.fold (fun (csets, ctx, acc) (localized, lang, cset) ->
            let cset, ctx, case = generateSwitchCase cset nargs ctx lang localized
            cset::csets, ctx, case::acc
            ) ([], ctx, [])
    dflt_cset::csets, ctx, List.concat [
        [
            $"%s{ctx.localeSwitch} switch {{" |> Word
            Indent
        ]
        (default_case::cases) |> List.rev |> List.concat
        [
            Dedent
            Newline
            Word "}"
        ]
    ]

let generateLS key (cls:string) nargs ctx (localizeds: ((ParseSequence * State) * string * char Set) list) =
    let default_localized::localizeds = localizeds
    let (dflt_parse, dflt_locale, dflt_cset) = default_localized
    let dflt_cset, ctx, default_case = generateCaseBody dflt_locale dflt_cset nargs ctx dflt_parse
    let csets, ctx, cases =
        localizeds
        |> List.rev
        |> List.fold (fun (csets, ctx, acc) (localized, locale, cset) ->
            let cset, ctx, case = generateCaseBody locale cset nargs ctx localized
            cset::csets, ctx, (locale, case)::acc
            ) ([], ctx, [])
    dflt_cset::csets, ctx, List.concat [
        [
            Word $"new {cls}("
            Indent
        ]
        default_case
        cases
        |> List.collect (fun (loc, strs) -> List.concat [
            [
                Word ","
                Newline
                Word $"({loc}, "
            ]
            strs
            [
                Word ")"
            ]
        ])
        [
            Word ")"
            Newline
            Word $"{{ ID = \"{key}\" }}"
            Dedent
        ]
    ]
    
        

let generateRow (csets: char Set list) (ctx: LGenCtx) (row: Row) =
    let err_localizeds =
        row.locales
        |> List.map stringParser
    if List.length err_localizeds <> List.length ctx.locales
        then failwith "Incorrect number of locales provided"
    let remix_csets, ctx, localizeds =
        List.foldBack(fun x (rcsets, ctx: LGenCtx, localizeds) ->
                    match x with
                    //If the parse is empty, then skip the language for this row
                    | OK parsed, lang, cset -> if List.length (fst parsed) = 0
                                                then (Some cset::rcsets, ctx, localizeds)
                                                else (None::rcsets, ctx, (parsed, lang, cset)::localizeds)
                    | Failed msg, _, cset -> (Some cset::rcsets, ctx.AddErrors msg, localizeds)
        ) (List.zip3 err_localizeds ctx.locales csets) ([], ctx, [])
    //No strings, don't generate an entry
    if List.length localizeds = 0 then csets, ctx, []
    else
    let nargs =
        (localizeds
        |> Seq.map (fun ((_, state: State), _, _) -> state.highestArg)
        |> Seq.max) + 1
    if nargs > 0 && List.length localizeds <> List.length ctx.locales
    then Console.WriteLine $"Row {row.key} uses function strings, but does not provide translations for one or more languages."
    let objName = row.key.Replace('.', '_')
    let fullKey = ctx.lskeyprefix + row.key
    match nargs, ctx.lsclass with
    //Zero-arg: generate a LocalizedString on the backend, with no suffix (?).
    | 0, Some (instCls, staticCls) ->
        let csets, ctx, ls =
            localizeds
            |> generateLS fullKey instCls nargs ctx
        mixBack remix_csets csets, { ctx with lsGenerated = (row.key, objName)::ctx.lsGenerated }, List.concat [
            [
                Newline
                Word $"public static readonly {staticCls} {objName} = "
            ]
            ls
            [
                Word ";"
                Newline
            ]
        ]
    | _, _ ->
        let csets, ctx, switch =
            localizeds
            |> generateSwitch nargs ctx
        if nargs = 0 then
            //Zero-arg, no method-to-LS: generate a string on the backend.
            mixBack remix_csets csets, ctx, List.concat [
                [
                    Newline
                    Word $"public static string {objName} => "
                ]
                switch
                [
                    Word ";"
                    Newline
                ]
            ]
        else
            //Multi-arg: generate a string function, and if method-to-LS is present, a LocalizedString function with suffix.
            let prms = generateParams ctx nargs
            let ls_copy = //cset/ctx output is not important
                match ctx.lsclass, ctx.methodToLsSuffix with
                | Some (instCls, staticCls), Some suffix ->
                       localizeds
                       |> generateLS fullKey instCls nargs ctx 
                       |> (fun (csets, ctx, ls) ->
                           List.concat [
                               [
                                   Newline
                                   Word $"public static {staticCls} {objName}{suffix}({prms}) => "
                               ]
                               ls
                               [
                                   Word ";"
                                   Newline
                               ]
                           ])
                | _, _ -> []
            mixBack remix_csets csets, ctx, List.concat [
                [
                    Newline
                    Word $"public static string {objName}({prms}) => "
                ]
                switch
                [
                    Word ";"
                    Newline
                ]
                ls_copy
            ]
 
let generateRows csets (ctx: LGenCtx) rows =
    let csets, ctx, genRows =
        rows
        |> Seq.filter (fun (row: Row) -> System.String.IsNullOrWhiteSpace(row.key) |> not)
        |> Seq.fold (fun (csets, ctx, acc) row ->
                let csets, ctx, genRow = generateRow csets ctx row
                (csets, ctx, genRow::acc)
        ) (csets, ctx, [])
    csets, ctx, genRows |> List.rev |> List.concat
    
let generateClass ctx inner =
    List.concat [
        [
            Word ctx.outputHeader
            Newline
            Newline
            $"namespace {ctx.namespace_} {{" |> Word
            Newline
            $"public static partial class {ctx.className} {{" |> Word
            Indent
            Newline
        ]
        inner
        [
            Dedent
            Newline
            Word "}"
            Newline
            Word "}"
            Newline
        ]
    ]

let generateNestedClass ctx inner =
    generateClass ctx (List.concat [
        [
            $"public static partial class {ctx.nestedClassName} {{" |> Word
            Indent
            Newline
        ]
        inner
        [
            Dedent
            Newline
            Word "}"
        ]
        
    ])

let generateCSV csets ctx (path: string) =
    let csets, ctx, genRows =
        ctx.loadRows path 
        |> generateRows csets ctx
    csets, ctx, generateNestedClass ctx genRows

let exportCSV csets ctx path out =
    let csets, ctx, gen = generateCSV csets ctx path
    File.WriteAllText(out, (render gen 0))
    csets, ctx
   
let exportFile csets ctx (path: string) filename outdir =
    let parts = Path.GetFileName(path).Split(".")
    //parts
    //|> Array.take (parts.Length - 1)
    //|> String.concat "."
    filename
    |> sprintf "%s.cs"
    |> fun x -> Path.Join(outdir, x)
    |> exportCSV csets ctx path
  
