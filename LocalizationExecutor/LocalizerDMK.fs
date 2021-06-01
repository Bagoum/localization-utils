module LocalizationExecutor.LocalizerDMK
open LocalizationExecutor.LocalizationFileOps
open LocalizationExecutor.LocalizerBase


let private PROJECT_DIR = "C://Workspace/unity/Danmokou/"

let lctx = { defaultLCtx with
                namespace_ = "Danmokou.Core"
                outputHeader = defaultLCtx.outputHeader + "\nusing Danmokou.Core;\nusing static Danmokou.Core.LocalizationRendering;"
           }

let dmkCoreReq : LReqCtx = {
    name = "DMK Core"
    topFileName = "_StringRepository.cs"
    outDir = PROJECT_DIR + "Assets/Danmokou/Plugins/Danmokou/Core/Localization/Generated/";
    ctx = lctx
    perFileInfo = [
        ("TestContent1", FileInfo.New "TestContent1" None)
        ("Generic", FileInfo.New "Generic" (Some "generic"))
        ("UI", FileInfo.New "UI" None)
        ("Tutorial", FileInfo.New "Tutorial" None)
        ("CustomDifficulty", FileInfo.New "CDifficulty" None)
        ("PlayerMetadata", FileInfo.New "Players" (Some "players"))
        ("Dialogue.Metadata", FileInfo.New "DialogueMetadata" (Some "dialogue"))
        ("Other.Bosses.Metadata", FileInfo.New "BossMetadata" (Some "boss"))
        ("Other.Bosses.Cards", FileInfo.New "BossCards" (Some "boss"))
    ] |> Map.ofList
}

let dmkSimpReq : LReqCtx = {
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
    spreadsheetId = "1Oglvu_08k3i7EDxQGrogGXUncs0q-hs_lb834cCvcq0"
    csvDir = "C://Workspace/tmp/csv/"
    reqs = [ dmkCoreReq; dmkSimpReq ]
}