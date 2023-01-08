module LocalizationExecutor.LocalizerDMK
open LocalizationExecutor.LocalizationFileOps
open LocalizationExecutor.LocalizerBase


let private PROJECT_DIR = "C://Workspace/unity/Danmokou/"

let lctx = { defaultLCtx with
                namespace_ = "Danmokou.Core"
                outputHeader = defaultLCtx.outputHeader + "\nusing Danmokou.Core;\nusing static BagoumLib.Culture.LocalizationRendering;\nusing static Danmokou.Core.LocalizationRendering;"
           }

let dmkCoreBatch : FileBatch = {
    name = "DMK Core"
    topFileName = "_StringRepository.cs"
    outDir = PROJECT_DIR + "Assets/Danmokou/Plugins/Danmokou/Core/Localization/Generated/";
    ctx = lctx
    perFileInfo = [
        ("TestContent1", FileInfo.New "TestContent1" None)
        ("Generic", FileInfo.New "Generic" (Some "generic"))
        ("Controls", FileInfo.New "Controls" None)
        ("UI", FileInfo.New "UI" None)
        ("Tutorial", FileInfo.New "Tutorial" None)
        ("CustomDifficulty", FileInfo.New "CDifficulty" None)
        ("PlayerMetadata", FileInfo.New "Players" (Some "players"))
        ("Dialogue.Metadata", FileInfo.New "DialogueMetadata" (Some "dialogue"))
        ("Other.Bosses.Metadata", FileInfo.New "BossMetadata" (Some "boss"))
        ("Other.Bosses.Cards", FileInfo.New "BossCards" (Some "boss"))
    ] |> Map.ofList
}

let dmkSimpBatch : FileBatch = {
    name = "DMK SiMP"
    topFileName = "_StringRepository.cs"
    outDir = PROJECT_DIR + "Assets/SiMP/Plugins/Danmokou/Localization/Generated/";
    ctx = { lctx with namespace_ = "SiMP.Localization" } 
    perFileInfo = [
        ("SiMP.UI", FileInfo.New "SiMPUI" (Some "simp.ui"))
        ("SiMP.MusicRoom", FileInfo.New "SiMPMusic" (Some "simp.music"))
        ("SiMP.Bosses.Metadata", FileInfo.New "SiMPBossMetadata" (Some "simp.boss"))
        ("SiMP.Bosses.Cards", FileInfo.New "SiMPBossCards" (Some "simp.boss"))
        ("SiMP.Achievements", FileInfo.New "SiMPAchievements" (Some "simp.acv"))
    ] |> Map.ofList
}


let dmkSpreadsheet = {
    spreadsheetId = "1iRlNA8DNFSPBEdcRsbc_e74G0sB-yIFmvsSKRGUKX9w"
    csvDir = "C://Workspace/tmp/csv/"
    batches = [ dmkCoreBatch; dmkSimpBatch ]
}