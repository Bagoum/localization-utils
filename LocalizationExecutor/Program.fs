module Actor.Main
open System
open System.IO
open LocalizationExecutor.LocalizationFileOps
open LocalizationExecutor.LocalizerDMK


[<EntryPoint>]
let main argv =
    generateAll dmkSpreadsheet
    
    0 // return an integer exit code