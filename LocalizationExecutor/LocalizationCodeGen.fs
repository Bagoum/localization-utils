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
    ///A C# attribute to attach to non-nested classes, eg "LocalizationStringsRepo".
    classAttribute : string Option
    namespace_: string
    outputHeader: string
} with
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

type GenOutput = {
    ///Errors output by the generation process
    errors: string list
    lsGenerated: (string * string) list
    csets: char Set list
} with
    static member Empty (ctx: LGenCtx) = { errors = []; lsGenerated = []; csets = ctx.locales |> List.map (fun _ -> Set.empty) }
    member this.AddError str = { this with errors = str::this.errors }
    member this.AddErrors strs = { this with errors = List.append this.errors strs }


let generateArgs nargs =
    [0..(nargs - 1)]
    |> List.map argString
    |> String.concat ", "
let generateParams (ctx:LGenCtx) nargs =
    [0..(nargs - 1)]
    |> List.map (fun i -> $"{ctx.objectType} {argString i}")
    |> String.concat ", "


let generateCaseBody locale nargs (ctx: LGenCtx) (localized: ParseSequence) =
    let strings = localized |> ctx.AccCSharpify
    if strings.Length = 1
    then
        Set.ofSeq strings[0],
        if nargs = 0 then [Word strings[0]] else [
            $"{ctx.renderFunc}({locale}, {strings[0]}, " |> Word
            generateArgs nargs |> Word
            Word(")")
        ]
    else
        let cset =
            strings
            |> Seq.map Set.ofSeq
            |> Set.unionMany
        let pieces =
            strings
            |> List.collect (fun word -> [
                    Newline
                    Word word
                    Word ","
                ])
        cset, List.concat [
            [
                $"{ctx.renderFunc}({locale}, new[] {{" |> Word
                Indent
            ]
            pieces
            [
                Dedent
                Newline
                if nargs > 0 then Word "}, " else Word "}"
                generateArgs nargs |> Word
                Word(")")
            ]
        ]

let generateSwitchCase locale nargs (ctx: LGenCtx) caseString localized =
    generateCaseBody locale nargs ctx localized
    |> MapSnd (fun body -> List.concat [
        [
            Newline
            Word caseString
            Word " => "
        ]
        body
        [
            Word ","
        ]
    ])
   
let generateSwitch nargs ctx (localizeds: ((ParseSequence * State) * string) option list) =
    localizeds
    |> List.mapi (fun i -> Option.map (fun ((parsed, _), locale) ->
            if i = 0
            then generateSwitchCase "null" nargs ctx "_" parsed
            else generateSwitchCase locale nargs ctx locale parsed))
    |> unzipNullable
    |> MapSnd (fun cases -> List.concat [
        [
            $"%s{ctx.localeSwitch} switch {{" |> Word
            Indent
        ]
        //default case should be at the end for switch expression
        List.append (List.skip 1 cases) [cases[0]] |> List.choose id |> List.concat
        [
            Dedent
            Newline
            Word "}"
        ]
    ])

let generateLS key (cls:string) nargs ctx (localizeds: ((ParseSequence * State) * string) option list) =
    let csets, cases =
        localizeds
        |> List.map (Option.map (fun (localized, locale) ->
            let ncset, case = generateCaseBody locale nargs ctx (fst localized)
            (ncset, (locale, case)))
        )
        |> unzipNullable
    csets, List.concat [
        [
            Word $"new {cls}("
            Indent
        ]
        (Option.map snd cases[0] |> Option.defaultValue [Word "null"]) //default case, locale def not required
        cases
        |> List.skip 1
        |> List.choose id
        |> List.collect (fun (locale, strs) -> List.concat [
            [
                Word ","
                Newline
                Word $"({locale}, "
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
    
        

let generateRow (generated: GenOutput) (ctx: LGenCtx) (row: Row) =
    let err_localizeds =
        row.locales
        |> List.map stringParser
    if List.length err_localizeds <> List.length ctx.locales
        then failwith "Incorrect number of locales provided"
    let errs, mlocalizeds =
        List.foldBack (fun (x, locale) (errs, acc) ->
                     match x with
                    //If the parse is empty, then skip the language for this row
                     | OK parsed -> if List.length (fst parsed) = 0
                                    then (errs, None::acc)
                                    else (errs, Some (parsed, locale)::acc)
                     | Failed msgs -> (List.append errs msgs, None::acc))
            (List.zip err_localizeds ctx.locales) (generated.errors, [])

    let generated = { generated with errors = errs }
    let nsuccessful = List.choose id mlocalizeds |> List.length
    //No strings, don't generate an entry
    if nsuccessful = 0 then generated, []
    else
    let nargs =
        (mlocalizeds
        |> Seq.choose (Option.map (fun ((_, state: State), _) -> state.highestArg))
        |> Seq.max) + 1
    if nargs > 0 && nsuccessful <> List.length ctx.locales
    then Console.WriteLine $"Row {row.key} uses function strings, but does not provide translations for one or more languages."
    let objName = row.key.Replace('.', '_')
    let fullKey = ctx.lskeyprefix + row.key
    let finalizeCsets ncsets =
        List.zip ncsets generated.csets
        |> List.map (fun (nc, c) -> Option.map (Set.union c) nc |> Option.defaultValue c)
    match nargs, ctx.lsclass with
    //Zero-arg: generate a LocalizedString on the backend, with no suffix (?).
    | 0, Some (instCls, staticCls) ->
        let csets, ls = generateLS fullKey instCls nargs ctx mlocalizeds
        { generated with csets = finalizeCsets csets; lsGenerated = (row.key, objName)::generated.lsGenerated }, List.concat [
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
        let csets, switch = generateSwitch nargs ctx mlocalizeds
        if nargs = 0 then
            //Zero-arg, no method-to-LS: generate a string on the backend.
            { generated with csets = finalizeCsets csets }, List.concat [
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
                       mlocalizeds
                       |> generateLS fullKey instCls nargs ctx 
                       |> (fun (csets, ls) ->
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
            { generated with csets = finalizeCsets csets }, List.concat [
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
 
let generateRows (ctx: LGenCtx) rows =
    let generated, genRows =
        rows
        |> Seq.filter (fun (row: Row) -> System.String.IsNullOrWhiteSpace(row.key) |> not)
        |> Seq.fold (fun (gen, acc) row ->
                let gen, genRow = generateRow gen ctx row
                (gen, genRow::acc)
        ) (GenOutput.Empty ctx, [])
    generated, genRows |> List.rev |> List.concat
    
let generateClass ctx attribute inner =
    List.concat [
        [
            Word ctx.outputHeader
            Newline
            Newline
            $"namespace {ctx.namespace_} {{" |> Word
            Newline
        ]
        attribute
            |> Option.map (fun attr -> [
                Word $"[{attr}]"
                Newline
            ])
            |> Option.defaultValue []
        [
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
    generateClass ctx Option.None (List.concat [
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

let generateCSV ctx (path: string) =
    let generated, genRows =
        ctx.loadRows path 
        |> generateRows ctx
    generated, generateNestedClass ctx genRows

let exportCSV ctx path outpath =
    let generated, gen = generateCSV ctx path
    File.WriteAllText(outpath, (render gen 0))
    generated
   
let exportFile ctx (path: string) filename outdir =
    $"{filename}.cs"
    |> fun x -> Path.Join(outdir, x)
    |> exportCSV ctx path
  
