/*
 * Script to export a spreadsheet into a zip composed of several CSV files on Google Drive.
 * From https://github.com/Bagoum/localization-utils
 */

function SheetToString(sheet) {
    var data = sheet.getDataRange().getValues();
    if (data.length == 0)
        return null;
    return data.map(row =>
        row.map(x => {
            var s = x.toString();
            //Commas and unclosed quotes are problematic
            return (s.indexOf(",") > -1 || s.indexOf("\"") > -1) ?
                "\"" + s.replace(/"/g, "\"\"") + "\"" :
                s;
        }).join(",")).join("\n");
}

function SaveSpreadsheetAsZip(id) {
    var time = new Date().getTime().toString();
    var folder = DriveApp.createFolder("_csvfolder_" + time);
    var blobs = [];
    SpreadsheetApp.openById(id).getSheets().forEach(sheet => {
        var sheetData = SheetToString(sheet);
        if (sheetData != null) {
            blobs.push(folder.createFile(sheet.getName() + ".csv", sheetData, MimeType.CSV).getBlob());
        }
    });
    var zipBlob = Utilities.zip(blobs, "zipped.zip");
    var zipId = folder.createFile(zipBlob).getId();
    return folder.getId() + "::" + zipId;
}
